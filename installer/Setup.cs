// =====================================================================
//  Nook Setup – installs Nook for the current user, no admin rights.
//
//  - copies Nook.exe to %LOCALAPPDATA%\Programs\Nook
//  - Start menu shortcut (and optionally a desktop shortcut)
//  - optional "Start with Windows" (HKCU Run + behavior.autostart in config.json)
//  - entry under Settings > Apps, uninstall included
//
//  Nook.exe is embedded as a resource. Build: installer\build-installer.bat
//  or the GitHub Action. C# 5, .NET Framework 4.x, WinForms.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NookSetup
{
    static class Program
    {
        public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nook");
        public static readonly string StartMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nook.lnk");
        public static readonly string DesktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nook.lnk");
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Nook";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool De = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

        public static string T(string en, string de) { return De ? de : en; }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = false, silent = false;
            string dir = null;
            foreach (var a in args)
            {
                string x = a.ToLowerInvariant();
                if (x == "/uninstall" || x == "--uninstall") uninstall = true;
                else if (x == "/s" || x == "/silent" || x == "--silent") silent = true;
                else if (x.StartsWith("/dir:")) dir = a.Substring(5).Trim('"');
            }
            try
            {
                if (uninstall) Uninstaller.Run(dir ?? InstallDir, silent);
                else if (silent) Installer.Install(true, false, true, false);
                else Application.Run(new SetupForm());
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show(ex.Message, "Nook Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Install / uninstall
    // ------------------------------------------------------------------
    static class Installer
    {
        public static string Install(bool autostart, bool desktop, bool keepAutostartIfUnset, bool launch)
        {
            string dir = Program.InstallDir;
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "Nook.exe");

            NookProcess.Stop();

            // Nook.exe from the embedded resource
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("Nook.exe"))
            {
                if (src == null) throw new Exception("Nook.exe is missing in this setup.");
                CopyWithRetry(src, exe);
            }
            if (File.Exists(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.standard.json")))
            {
                try { File.Copy(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.standard.json"), Path.Combine(dir, "config.standard.json"), true); } catch { }
            }

            // Uninstaller = copy of this setup
            string uninst = Path.Combine(dir, "Uninstall Nook.exe");
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninst), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninst, true);

            // Shortcuts
            Shortcut.Create(Program.StartMenuLnk, exe, dir, "Nook");
            if (desktop) Shortcut.Create(Program.DesktopLnk, exe, dir, "Nook");

            // Autostart: registry and Nook's own setting stay in sync
            if (!(keepAutostartIfUnset && !autostart))
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Program.RunKey))
                {
                    if (autostart) k.SetValue("Nook", "\"" + exe + "\"");
                    else if (k.GetValue("Nook") != null) k.DeleteValue("Nook");
                }
                ConfigFile.SetAutostart(Path.Combine(dir, "config.json"), autostart);
            }

            // Settings > Apps
            using (var k = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
            {
                k.SetValue("DisplayName", "Nook");
                k.SetValue("DisplayVersion", SetupVersion.Value);
                k.SetValue("Publisher", "Nook");
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /silent");
                k.SetValue("URLInfoAbout", "https://github.com/NixFabo/Nook");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024 + 64), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }

            if (launch) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true });
            return exe;
        }

        static void CopyWithRetry(Stream src, string target)
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    using (var f = File.Create(target)) src.CopyTo(f);
                    return;
                }
                catch (IOException)
                {
                    if (i >= 20) throw;
                    Thread.Sleep(250);
                    src.Position = 0;
                }
            }
        }
    }

    static class Uninstaller
    {
        public static void Run(string dir, bool silent)
        {
            string self = Assembly.GetExecutingAssembly().Location;
            // Running from inside the install folder: continue from a temp copy so the folder can be removed
            if (Path.GetFullPath(self).StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "NookUninstall.exe");
                File.Copy(self, tmp, true);
                Process.Start(tmp, "/uninstall /dir:\"" + dir + "\"" + (silent ? " /silent" : ""));
                return;
            }

            bool deleteData = false;
            if (!silent)
            {
                var r = MessageBox.Show(
                    Program.T("Remove Nook?\n\nYes: also delete your settings and data\nNo: keep settings (config.json, data folder)",
                              "Nook entfernen?\n\nJa: auch Einstellungen und Daten löschen\nNein: Einstellungen behalten (config.json, Ordner data)"),
                    "Nook", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                deleteData = r == DialogResult.Yes;
            }

            NookProcess.Stop();
            Thread.Sleep(300);
            TryDel(Program.StartMenuLnk);
            TryDel(Program.DesktopLnk);
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Program.RunKey, true))
                    if (k != null && k.GetValue("Nook") != null) k.DeleteValue("Nook");
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }

            if (Directory.Exists(dir))
            {
                if (deleteData) { try { Directory.Delete(dir, true); } catch { } }
                else
                {
                    foreach (var f in new[] { "Nook.exe", "Uninstall Nook.exe", "config.standard.json" }) TryDel(Path.Combine(dir, f));
                }
            }
            if (!silent)
                MessageBox.Show(Program.T("Nook has been removed.", "Nook wurde entfernt."), "Nook", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // remove this temp copy shortly after exit
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + self + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
        }

        static void TryDel(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }

    // Quits a running Nook cleanly (it restores the Windows taskbar on quit)
    static class NookProcess
    {
        public static void Stop()
        {
            foreach (var ch in new[] { "", "_Test" })
            {
                try
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting("Nook_Quit_v1" + ch, out ev)) { ev.Set(); ev.Dispose(); }
                }
                catch { }
            }
            var until = DateTime.Now.AddSeconds(6);
            while (DateTime.Now < until)
            {
                if (Process.GetProcessesByName("Nook").Length == 0) return;
                Thread.Sleep(150);
            }
            foreach (var p in Process.GetProcessesByName("Nook"))
            {
                try
                {
                    string path = p.MainModule.FileName;
                    if (path.StartsWith(Program.InstallDir, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
            }
        }
    }

    // Writes behavior.autostart into Nook's config.json (creates a minimal one if missing)
    static class ConfigFile
    {
        public static void SetAutostart(string path, bool on)
        {
            try
            {
                var js = new JavaScriptSerializer();
                Dictionary<string, object> root = null;
                if (File.Exists(path))
                {
                    try { root = js.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>; } catch { }
                    if (root == null) return;   // don't touch a file we can't read
                }
                if (root == null) root = new Dictionary<string, object>();
                object b;
                var beh = root.TryGetValue("behavior", out b) ? b as Dictionary<string, object> : null;
                if (beh == null) { beh = new Dictionary<string, object>(); root["behavior"] = beh; }
                beh["autostart"] = on;
                File.WriteAllText(path, js.Serialize(root), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    //  .lnk via IShellLink (no extra references)
    // ------------------------------------------------------------------
    static class Shortcut
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        public static void Create(string lnk, string target, string workDir, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(target);
            link.SetWorkingDirectory(workDir);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(lnk, true);
            Marshal.ReleaseComObject(link);
        }
    }

    // ------------------------------------------------------------------
    //  Window
    // ------------------------------------------------------------------
    class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(14, 14, 16), Surface = Color.FromArgb(28, 28, 31), Text_ = Color.FromArgb(242, 242, 240),
                              Muted = Color.FromArgb(150, 150, 156), Accent = Color.FromArgb(212, 178, 102);
        readonly CheckBox cbAuto, cbDesktop, cbLaunch;
        readonly Button btnInstall, btnCancel;
        readonly Label status;

        public SetupForm()
        {
            bool installed = File.Exists(Path.Combine(Program.InstallDir, "Nook.exe"));
            Text = "Nook Setup";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 330);
            BackColor = Bg; ForeColor = Text_;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            var title = new Label { Text = "Nook", Font = new Font("Segoe UI Semibold", 20f), AutoSize = true, Location = new Point(28, 22), ForeColor = Text_ };
            var ver = new Label { Text = "v" + SetupVersion.Value, AutoSize = true, Location = new Point(110, 36), ForeColor = Accent };
            var sub = new Label
            {
                Text = installed
                    ? Program.T("Nook is already installed. Setup will update it – your settings stay.", "Nook ist schon installiert. Setup aktualisiert es – deine Einstellungen bleiben.")
                    : Program.T("A minimal taskbar replacement for Windows 11.\nInstalled for your user only – no administrator rights needed.", "Schlanker Taskleisten-Ersatz für Windows 11.\nWird nur für deinen Benutzer installiert – ohne Administratorrechte."),
                Location = new Point(30, 72), Size = new Size(430, 46), ForeColor = Muted
            };
            var path = new Label { Text = Program.T("Folder: ", "Ordner: ") + Program.InstallDir, Location = new Point(30, 124), Size = new Size(430, 20), ForeColor = Muted, AutoEllipsis = true };

            cbAuto = Check(Program.T("Start Nook with Windows", "Nook mit Windows starten"), 156, true);
            cbDesktop = Check(Program.T("Desktop shortcut", "Verknüpfung auf dem Desktop"), 184, false);
            cbLaunch = Check(Program.T("Start Nook after setup", "Nook nach der Installation starten"), 212, true);
            var startNote = new Label { Text = Program.T("A Start menu shortcut is always created.", "Eine Verknüpfung im Startmenü wird immer angelegt."), Location = new Point(30, 242), Size = new Size(430, 20), ForeColor = Muted, Font = new Font("Segoe UI", 8.5f) };

            status = new Label { Location = new Point(30, 284), Size = new Size(220, 24), ForeColor = Accent };
            btnCancel = Btn(Program.T("Cancel", "Abbrechen"), 268, false);
            btnInstall = Btn(installed ? Program.T("Update", "Aktualisieren") : Program.T("Install", "Installieren"), 372, true);
            btnCancel.Click += (s, e) => Close();
            btnInstall.Click += (s, e) => DoInstall();
            AcceptButton = btnInstall; CancelButton = btnCancel;

            Controls.AddRange(new Control[] { title, ver, sub, path, cbAuto, cbDesktop, cbLaunch, startNote, status, btnCancel, btnInstall });
        }

        CheckBox Check(string text, int y, bool on)
        {
            return new CheckBox { Text = text, Checked = on, Location = new Point(30, y), AutoSize = true, ForeColor = Text_, FlatStyle = FlatStyle.System };
        }

        Button Btn(string text, int x, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, 280), Size = new Size(94, 32), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.FromArgb(24, 20, 12) : Text_, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        void DoInstall()
        {
            btnInstall.Enabled = btnCancel.Enabled = false;
            status.Text = Program.T("Installing …", "Installiere …");
            Refresh();
            try
            {
                Installer.Install(cbAuto.Checked, cbDesktop.Checked, false, cbLaunch.Checked);
                status.Text = Program.T("Done.", "Fertig.");
                MessageBox.Show(this,
                    Program.T("Nook is installed.\n\nFind it in the Start menu. Remove it any time under Settings > Apps.",
                              "Nook ist installiert.\n\nDu findest es im Startmenü. Entfernen jederzeit unter Einstellungen > Apps."),
                    "Nook", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                status.Text = "";
                MessageBox.Show(this, Program.T("Setup failed:\n", "Installation fehlgeschlagen:\n") + ex.Message, "Nook Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInstall.Enabled = btnCancel.Enabled = true;
            }
        }
    }
}
