// Деинсталлятор MonitorTray (запускается из «Параметры → Приложения»)
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

using System.Reflection;
[assembly: AssemblyTitle("MonitorTray Uninstaller")]
[assembly: AssemblyDescription("Uninstaller for MonitorTray")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("MonitorTray project")]
[assembly: AssemblyProduct("MonitorTray")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MonitorTray contributors (MIT)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace MonitorTraySetup
{
    internal static class Uninstall
    {
        [STAThread]
        static void Main()
        {
            bool ru;
            try { ru = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"; }
            catch { ru = false; }

            string dir = AppDomain.CurrentDomain.BaseDirectory;
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
            TryDelete(Path.Combine(dir, "MonitorTray.exe"));

            // сам деинсталлятор и его папку удаляет отложенная команда после выхода
            Process.Start("cmd.exe", "/c timeout /t 2 >nul & rd /s /q \"" + dir + "\"");
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
