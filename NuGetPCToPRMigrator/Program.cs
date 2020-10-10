using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using HWND = System.IntPtr;

namespace Ceridian
{
    public static class Program
    {
        private const string DEVENV = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe";
        private const string DEFAULT_BROWSER = "chrome.exe";

        private delegate bool EnumWindowsProc(HWND hWnd, int lParam);

        [DllImport("USER32.DLL")]
        private static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);

        [DllImport("USER32.DLL")]
        private static extern int GetWindowText(HWND hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("USER32.DLL")]
        private static extern int GetWindowTextLength(HWND hWnd);

        [DllImport("USER32.DLL")]
        private static extern bool IsWindowVisible(HWND hWnd);

        [DllImport("USER32.DLL")]
        private static extern HWND GetShellWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(HWND hWnd);

        public static HWND GetDialogHandle()
        {
            HWND shellWindow = GetShellWindow();
            HWND handle = HWND.Zero;

            EnumWindows(delegate (HWND hWnd, int lParam)
            {
                const string TITLE_PREFIX = "Migrate NuGet format to PackageReference - ";

                if (hWnd == shellWindow || !IsWindowVisible(hWnd))
                {
                    return true;
                }

                int length = GetWindowTextLength(hWnd);
                if (length < TITLE_PREFIX.Length + 1)
                {
                    return true;
                }

                var builder = new StringBuilder(length);
                GetWindowText(hWnd, builder, length + 1);

                var title = builder.ToString();
                if (title.StartsWith(TITLE_PREFIX))
                {
                    handle = hWnd;
                    return false;
                }
                return true;
            }, 0);

            return handle;
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine($"Usage: {Process.GetCurrentProcess().ProcessName} SlnFile1 SlnFile2 ...");
                return;
            }

            foreach (var slnFilePath in args.Select(Path.GetFullPath))
            {
                Process devenv = null;
                var dte = RunningVSInstanceFinder.Find(slnFilePath);
                if (dte == null)
                {
                    devenv = Process.Start(DEVENV, slnFilePath);
                    do
                    {
                        Thread.Sleep(1000);
                        dte = RunningVSInstanceFinder.Find(slnFilePath);
                    }
                    while (dte == null);
                }
                MigrateSolution(dte, slnFilePath);
                if (devenv != null)
                {
                    devenv.Kill();
                }
            }
        }

        private static T ExecuteWithRetry<T>(Func<T> func)
        {
            int retryCount = 30;
            for (; ; )
            {
                try
                {
                    return func();
                }
                catch
                {
                    --retryCount;
                    if (retryCount == 0)
                    {
                        throw;
                    }
                    Thread.Sleep(1000);
                }
            }
        }

        private static string StartProjectMigration(EnvDTE80.DTE2 dte, int i, int total, string solutionName, string projectFilePath)
        {
            Console.WriteLine($"[{i}/{total}] {projectFilePath}");
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            var projectItem = dte.ToolWindows.SolutionExplorer.GetItem($@"{solutionName}\{projectName}");
            projectItem.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
            dte.ExecuteCommand("ClassViewContextMenus.ClassViewProject.Migratepackages.configtoPackageReference");
            return projectName;
        }
        private static T WaitForResult<T>(Func<T> func, Predicate<T> isReady)
        {
            T res;
            while (!isReady(res = func()))
            {
                Thread.Sleep(1000);
            }
            return res;
        }

        private static object InitializeNuGetPackageManager(EnvDTE80.DTE2 dte)
        {
            dte.ExecuteCommand("View.PackageManagerConsole");
            return null;
        }

        private static string TryReadAllText(string filePath)
        {
            try
            {
                return File.ReadAllText(filePath);
            }
            catch
            {
                return null;
            }
        }

        private static string Save(EnvDTE.Project project)
        {
            project.Save();
            return null;
        }

        private static Func<string, string> GetHtmlReportFilePathFunc(string slnFilePath)
        {
            var migrationBackupFolderPath = $@"{slnFilePath}\..\MigrationBackup";
            HashSet<string> done;
            if (Directory.Exists(migrationBackupFolderPath))
            {
                done = Directory
                    .EnumerateDirectories(migrationBackupFolderPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return projectName => Directory.Exists(migrationBackupFolderPath) ? Directory
                .EnumerateDirectories(migrationBackupFolderPath)
                .Where(path => !done.Contains(path) && File.Exists($@"{path}\{projectName}\NuGetUpgradeLog.html"))
                .Select(path => $@"{path}\{projectName}\NuGetUpgradeLog.html")
                .FirstOrDefault() : null;
        }

        private static void MigrateSolution(EnvDTE80.DTE2 dte, string slnFilePath)
        {
            var getHTMLReportFilePath = GetHtmlReportFilePathFunc(slnFilePath);

            var solutionName = Path.GetFileNameWithoutExtension(slnFilePath);

            ExecuteWithRetry(() => InitializeNuGetPackageManager(dte));
            var projects = ExecuteWithRetry(() => dte.Solution.Projects.Cast<EnvDTE.Project>().Where(IsClassLibraryWithPackagesConfig).ToList());
            int i = 0;
            foreach (var project in projects)
            {
                ++i;
                var projectFullName = ExecuteWithRetry(() => project.FullName);
                var projectName = ExecuteWithRetry(() => StartProjectMigration(dte, i, projects.Count, solutionName, projectFullName));
                var handle = WaitForResult(GetDialogHandle, h => h != HWND.Zero);

                SetForegroundWindow(handle);
                SendKeys.SendWait("{ENTER}");

                int j = 0;
                var packagesConfigFilePath = $@"{projectFullName}\..\packages.config";
                string htmlReportFilePath = WaitForResult(() => getHTMLReportFilePath(projectName), path => path != null || ++j >= 60 && !File.Exists(packagesConfigFilePath));

                if (htmlReportFilePath != null)
                {
                    string htmlReport = WaitForResult(() => TryReadAllText(htmlReportFilePath), content => content != null);
                    Process.Start("taskkill", "/f /im " + DEFAULT_BROWSER);
                    if (htmlReport.Contains("No issues were found."))
                    {
                        Directory.Delete($@"{htmlReportFilePath}\..\..", true);
                    }
                }
                ExecuteWithRetry(() => Save(project));
            }
        }

        private static bool IsWebApplication(EnvDTE.Project project) =>
            project.Properties.Cast<EnvDTE.Property>().Any(p => p.Name == "WebApplication.UseIISExpress");

        private static bool IsClassLibraryWithPackagesConfig(EnvDTE.Project p) =>
            p.Kind == "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}" &&
            File.Exists($@"{p.FullName}\..\packages.config") &&
            !IsWebApplication(p);
    }
}