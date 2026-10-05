// Деинсталлятор MonitorTray (запускается из «Параметры → Приложения»)
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

using System.Reflection;
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
[assembly: AssemblyTitle("MonitorTray Uninstaller")]
[assembly: AssemblyDescription("Uninstaller for MonitorTray")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("MonitorTray project")]
[assembly: AssemblyProduct("MonitorTray")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MonitorTray contributors (MIT)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.4.1.0")]
[assembly: AssemblyFileVersion("1.4.1.0")]

namespace MonitorTraySetup
{
    internal static class Uninstall
    {
        [DllImport("kernel32.dll")]
        static extern bool SetDefaultDllDirectories(uint flags);

        [STAThread]
        static void Main()
        {
            // библиотеки — только из System32 (защита от подложенных рядом DLL)
            try { SetDefaultDllDirectories(0x800 /*LOAD_LIBRARY_SEARCH_SYSTEM32*/); } catch { }
            bool ru;
            try { ru = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"; }
            catch { ru = false; }

            // Удаляем только папку установки. Раньше удалялась папка, где лежит Uninstall.exe, —
            // запущенный, скажем, с рабочего стола, он стёр бы весь рабочий стол.
            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "MonitorTray");
            DialogResult r = MessageBox.Show(
                ru ? "Удалить MonitorTray с этого компьютера?" : "Remove MonitorTray from this computer?",
                ru ? "Удаление MonitorTray" : "Remove MonitorTray",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            foreach (Process p in Process.GetProcessesByName("MonitorTray"))
            {
                try { p.Kill(); } catch { }
            }
            Thread.Sleep(800);

            try
            {
                Registry.CurrentUser.DeleteSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorTray", false);
            }
            catch { }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                k.DeleteValue("MonitorTray", false);

            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MonitorTray.lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MonitorTray.lnk"));
            TryDelete(Path.Combine(installDir, "MonitorTray.exe"));

            // папку установки (вместе с самим деинсталлятором) удаляет отложенная команда после выхода
            if (Directory.Exists(installDir))
                Process.Start("cmd.exe", "/c timeout /t 2 >nul & rd /s /q \"" + installDir + "\"");
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
