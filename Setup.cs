// Установщик MonitorTray — всё в одном файле:
// MonitorTray.exe и Uninstall.exe зашиты внутрь как ресурсы.
// Устанавливает в %LOCALAPPDATA%\Programs\MonitorTray (без прав администратора),
// создаёт ярлыки, автозапуск (по выбору) и запись в «Приложения и возможности».
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("MonitorTray Setup")]
[assembly: AssemblyDescription("Installer for MonitorTray - monitor power control from the Windows tray")]
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
    internal static class Program
    {
        public static bool Ru()
        {
            try { return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"; }
            catch { return false; }
        }

        public static string DestDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "MonitorTray");
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (mode == "silent")
            {
                Install(true, true, true, false);
                return;
            }
            if (mode == "update")
            {
                // автообновление из программы: тихо, с прежними настройками автозапуска и ярлыка
                bool auto = false;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    auto = k != null && k.GetValue("MonitorTray") != null;
                bool desk = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "MonitorTray.lnk"));
                Install(auto, desk, true, false);
                return;
            }
            Application.Run(new SetupForm());
        }

        public static void Install(bool autostart, bool desktopShortcut, bool startNow, bool showDone)
        {
            string dir = DestDir();
            string exe = Path.Combine(dir, "MonitorTray.exe");
            Directory.CreateDirectory(dir);

            foreach (Process p in Process.GetProcessesByName("MonitorTray"))
            {
                try { p.Kill(); p.WaitForExit(3000); }
                catch { }
            }

            Extract("MonitorTray.exe", exe);
            Extract("Uninstall.exe", Path.Combine(dir, "Uninstall.exe"));

            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "MonitorTray.lnk"), exe);
            string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "MonitorTray.lnk");
            if (desktopShortcut) CreateShortcut(desk, exe);
            else { try { File.Delete(desk); } catch { } }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (autostart) k.SetValue("MonitorTray", "\"" + exe + "\"");
                else k.DeleteValue("MonitorTray", false);
            }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorTray"))
            {
                k.SetValue("DisplayName", "MonitorTray");
                k.SetValue("DisplayVersion", "1.3");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", Path.Combine(dir, "Uninstall.exe"));
                k.SetValue("InstallLocation", dir);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            if (startNow) Process.Start(exe);
            if (showDone)
            {
                DialogResult r = MessageBox.Show(
                    Ru() ? "Готово! Программа установлена.\n\nЗапустить MonitorTray сейчас?"
                         : "Done! MonitorTray has been installed.\n\nStart MonitorTray now?",
                    Ru() ? "Установка MonitorTray" : "MonitorTray Setup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (r == DialogResult.Yes) Process.Start(exe);
            }
        }

        static void Extract(string name, string to)
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (st == null) throw new Exception("Не найден встроенный файл " + name);
                using (FileStream fs = File.Create(to))
                {
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
        }

        static void CreateShortcut(string lnkPath, string target)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { lnkPath });
                Type sct = sc.GetType();
                sct.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                sct.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                    new object[] { Path.GetDirectoryName(target) });
                sct.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                sct.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }
    }

    internal class SetupForm : Form
    {
        CheckBox _auto;
        CheckBox _desk;

        public SetupForm()
        {
            bool ru = Program.Ru();
            Text = ru ? "Установка MonitorTray" : "MonitorTray Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 215);
            try
            {
                foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                {
                    if (n.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(n))
                        {
                            if (st != null) { Icon = new Icon(st); break; }
                        }
                    }
                }
            }
            catch { }

            Label l1 = new Label();
            l1.Text = ru ? "MonitorTray — управление мониторами из трея" : "MonitorTray — monitor control from the tray";
            l1.Font = new Font(l1.Font, FontStyle.Bold);
            l1.Location = new Point(16, 16);
            l1.AutoSize = true;

            Label l2 = new Label();
            l2.Text = ru ? "Установка в папку пользователя, права администратора не нужны:"
                         : "Installs into your user folder, no administrator rights required:";
            l2.Location = new Point(16, 46);
            l2.AutoSize = true;

            Label l3 = new Label();
            l3.Text = Program.DestDir();
            l3.ForeColor = Color.DimGray;
            l3.Location = new Point(16, 68);
            l3.AutoSize = true;

            _auto = new CheckBox();
            _auto.Text = ru ? "Запускать автоматически при входе в Windows" : "Start automatically when you sign in to Windows";
            _auto.Checked = true;
            _auto.Location = new Point(18, 105);
            _auto.AutoSize = true;

            _desk = new CheckBox();
            _desk.Text = ru ? "Ярлык на рабочем столе" : "Desktop shortcut";
            _desk.Checked = true;
            _desk.Location = new Point(18, 131);
            _desk.AutoSize = true;

            Button ok = new Button();
            ok.Text = ru ? "Установить" : "Install";
            ok.Size = new Size(110, 30);
            ok.Location = new Point(226, 170);
            ok.Click += delegate
            {
                try
                {
                    Program.Install(_auto.Checked, _desk.Checked, false, true);
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show((ru ? "Ошибка установки: " : "Installation failed: ") + ex.Message,
                        ru ? "Установка MonitorTray" : "MonitorTray Setup",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            Button cancel = new Button();
            cancel.Text = ru ? "Отмена" : "Cancel";
            cancel.Size = new Size(85, 30);
            cancel.Location = new Point(348, 170);
            cancel.Click += delegate { Close(); };

            Controls.Add(l1);
            Controls.Add(l2);
            Controls.Add(l3);
            Controls.Add(_auto);
            Controls.Add(_desk);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
