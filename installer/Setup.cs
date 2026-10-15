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
using System.Drawing.Drawing2D;
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
                    "Remove Nook?\n\nYes: also delete your settings and data\nNo: keep settings (config.json, data folder)",
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
                MessageBox.Show("Nook has been removed.", "Nook", MessageBoxButtons.OK, MessageBoxIcon.Information);

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
        static readonly Color Bg = Color.FromArgb(14, 14, 16), Surface = Color.FromArgb(36, 36, 40), Fg = Color.FromArgb(242, 242, 240),
                              Muted = Color.FromArgb(150, 150, 156), Accent = Color.FromArgb(212, 178, 102);
        readonly Toggle cbAuto, cbDesktop, cbLaunch;
        readonly Button btnInstall, btnCancel;
        readonly Label status;
        readonly float k;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // dark title bar (Windows 10 20H1+ / Windows 11)
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }
        }

        // pixels at 100 % -> pixels at the current display scaling
        int P(int v) { return (int)Math.Round(v * k); }

        public SetupForm()
        {
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            bool installed = File.Exists(Path.Combine(Program.InstallDir, "Nook.exe"));

            Text = "Nook Setup";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg; ForeColor = Fg;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            int pad = P(32), textW = P(420);
            var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Location = new Point(pad, P(28)) };

            // Header: logo, name, version
            var head = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, P(14)) };
            var logo = Logo(P(44));
            if (logo != null) head.Controls.Add(new PictureBox { Image = logo, Size = new Size(P(44), P(44)), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, P(2), P(14), 0) });
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            titles.Controls.Add(new Label { Text = "Nook", Font = new Font("Segoe UI Semibold", 17f), AutoSize = true, ForeColor = Fg, Margin = new Padding(0) });
            titles.Controls.Add(new Label { Text = "Version " + SetupVersion.Value, Font = new Font("Segoe UI", 9f), AutoSize = true, ForeColor = Accent, Margin = new Padding(P(2), 0, 0, 0) });
            head.Controls.Add(titles);
            col.Controls.Add(head);

            col.Controls.Add(Text_(installed
                ? "Nook is already installed. Setup will update it, your settings stay."
                : "A minimal taskbar replacement for Windows 11. Installed for your user only, no administrator rights needed.",
                Muted, 10f, textW, P(18)));

            // Options card
            var card = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Surface, Padding = new Padding(P(18), P(14), P(18), P(8)), Margin = new Padding(0, 0, 0, P(12)),
                MinimumSize = new Size(textW, 0)
            };
            cbAuto = Check("Start Nook with Windows", true);
            cbDesktop = Check("Create a desktop shortcut", false);
            cbLaunch = Check("Start Nook when setup is done", true);
            card.Controls.Add(cbAuto); card.Controls.Add(cbDesktop); card.Controls.Add(cbLaunch);
            col.Controls.Add(card);

            col.Controls.Add(Text_("Installs to " + Program.InstallDir + ". A Start menu shortcut is always created.", Muted, 8.5f, textW, P(22)));

            // Bottom row: status left, buttons right
            var bottom = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, MinimumSize = new Size(textW, 0), Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status = new Label { AutoSize = true, ForeColor = Accent, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            btnCancel = Btn("Cancel", false);
            btnInstall = Btn(installed ? "Update" : "Install", true);
            bottom.Controls.Add(status, 0, 0);
            bottom.Controls.Add(btnCancel, 1, 0);
            bottom.Controls.Add(btnInstall, 2, 0);
            col.Controls.Add(bottom);

            Controls.Add(col);
            // Window = content + even margin on all sides
            var pref = col.GetPreferredSize(Size.Empty);
            ClientSize = new Size(pref.Width + 2 * pad, pref.Height + P(28) + pad);
            btnCancel.Click += (s, e) => Close();
            btnInstall.Click += (s, e) => DoInstall();
            AcceptButton = btnInstall; CancelButton = btnCancel;
        }

        Label Text_(string text, Color color, float size, int width, int bottom)
        {
            return new Label
            {
                Text = text, ForeColor = color, Font = new Font("Segoe UI", size), AutoSize = true,
                MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, bottom)
            };
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        // Logo in the requested size, taken from this exe's own icon (nook.ico)
        static Image Logo(int size)
        {
            try
            {
                var h = new IntPtr[1]; var ids = new int[1];
                if (PrivateExtractIcons(Assembly.GetExecutingAssembly().Location, 0, size, size, h, ids, 1, 0) == 0 || h[0] == IntPtr.Zero) return null;
                try { using (var ic = Icon.FromHandle(h[0])) return ic.ToBitmap(); }
                finally { DestroyIcon(h[0]); }
            }
            catch { return null; }
        }

        Toggle Check(string text, bool on)
        {
            return new Toggle(text, on, k, Font) { ForeColor = Fg, BackColor = Surface, Margin = new Padding(0, 0, 0, P(10)) };
        }

        Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                Padding = new Padding(P(14), P(5), P(14), P(5)), Margin = new Padding(P(8), 0, 0, 0),
                BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.FromArgb(24, 20, 12) : Fg, Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 10f)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(226, 196, 128) : Color.FromArgb(50, 50, 55);
            return b;
        }

        void DoInstall()
        {
            btnInstall.Enabled = btnCancel.Enabled = false;
            status.Text = "Installing …";
            Refresh();
            try
            {
                Installer.Install(cbAuto.Checked, cbDesktop.Checked, false, cbLaunch.Checked);
                status.Text = "Done.";
                MessageBox.Show(this,
                    "Nook is installed.\n\nYou'll find it in the Start menu. Remove it any time under Settings > Apps.",
                    "Nook", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                status.Text = "";
                MessageBox.Show(this, "Setup failed:\n" + ex.Message, "Nook Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInstall.Enabled = btnCancel.Enabled = true;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Modern switch instead of the system checkbox (looks the same at every DPI)
    // ------------------------------------------------------------------
    class Toggle : Control
    {
        static readonly Color On = Color.FromArgb(212, 178, 102), Off = Color.FromArgb(70, 70, 76), OffHover = Color.FromArgb(88, 88, 95),
                              KnobOn = Color.FromArgb(24, 20, 12), KnobOff = Color.FromArgb(220, 220, 224);
        bool on, hover;
        readonly float k;
        readonly Size textSize;

        public bool Checked { get { return on; } set { on = value; Invalidate(); } }

        public Toggle(string text, bool initial, float scale, Font font)
        {
            k = scale; on = initial; Text = text; Font = font;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Cursor = Cursors.Hand;
            textSize = TextRenderer.MeasureText(text, font);
            Size = new Size(S(40) + S(12) + textSize.Width + S(4), Math.Max(S(28), textSize.Height + S(6)));
        }

        int S(int v) { return (int)Math.Round(v * k); }

        void Flip() { on = !on; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Focus(); Flip(); } base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Flip(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        static GraphicsPath Pill(RectangleF r)
        {
            var p = new GraphicsPath();
            float d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = S(40), h = S(22), y = (Height - h) / 2f;
            var track = new RectangleF(1, y, w, h);
            using (var path = Pill(track))
            using (var br = new SolidBrush(on ? On : (hover ? OffHover : Off)))
                g.FillPath(br, path);
            if (Focused)
            {
                var f = RectangleF.Inflate(track, S(2) * 0.5f, S(2) * 0.5f);
                using (var path = Pill(f))
                using (var pen = new Pen(Color.FromArgb(160, On), Math.Max(1f, k)))
                    g.DrawPath(pen, path);
            }
            float d = h - S(6);
            float kx = on ? track.Right - S(3) - d : track.X + S(3);
            using (var kb = new SolidBrush(on ? KnobOn : KnobOff))
                g.FillEllipse(kb, kx, y + S(3), d, d);
            TextRenderer.DrawText(g, Text, Font, new Point((int)w + S(12), (Height - textSize.Height) / 2), ForeColor, TextFormatFlags.NoPadding);
        }
    }
}
