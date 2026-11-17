using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RogueCapriSetup
{
    // Rogue-Capri-Setup.exe: installs Rogue Capri 1.0 from an embedded zip.
    //   (no args)        GUI installer
    //   /S [/D=path]     silent install (drives the smoke tests)
    //   --uninstall [/S] remove shortcuts/registry; asks about data unless /S
    internal static class Program
    {
        internal const string AppName = "Rogue Capri";
        internal const string ExeName = "RogueCapri.exe";
        internal const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RogueCapri";

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = false;
            string dest = null;
            bool uninstall = false;
            // /D= stays the last NSIS-style argument; any following non-switch token
            // is a continuation of the path (paths with spaces may be split by the caller).
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "/S", StringComparison.OrdinalIgnoreCase)) silent = true;
                else if (arg == "--uninstall") uninstall = true;
                else if (arg.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                {
                    dest = arg.Substring(3);
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("/") && !args[i + 1].StartsWith("--")) { dest += " " + args[i + 1]; i++; }
                }
                else if (string.Equals(arg, "/D", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { dest = args[++i]; }
            }
            // Install defaults to E:\Rogue Capri; uninstall always defaults to the exe's own folder.
            if (!uninstall && dest == null) dest = @"E:\Rogue Capri";
            if (uninstall) return UninstallMode(dest, silent);
            if (silent) return InstallMode(dest, silent, true);
            using (var form = new SetupForm(dest))
            {
                DialogResult result = form.ShowDialog();
                return result == DialogResult.OK ? 0 : 1;
            }
        }

        private static int InstallMode(string dest, bool silent, bool desktopShortcut)
        {
            string error = Installer.Install(dest, desktopShortcut, silent ? (Action<string>)Console.WriteLine : null);
            if (error != null)
            {
                Console.Error.WriteLine("Rogue Capri setup failed: " + error);
                return 1;
            }
            return 0;
        }

        private static int UninstallMode(string dest, bool silent)
        {
            string error = Installer.RequestUninstall(dest, silent);
            if (error != null)
            {
                Console.Error.WriteLine("Rogue Capri uninstall failed: " + error);
                return 1;
            }
            return 0;
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox pathBox;
        private readonly CheckBox desktopBox;
        private readonly ProgressBar progress;
        private readonly Button installButton, cancelButton;
        private readonly TextBox logBox;

        internal SetupForm(string dest)
        {
            Text = "Rogue Capri Setup 1.0";
            Icon = LoadIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = false;
            ClientSize = new Size(560, 420);

            var title = new Label { Text = "Rogue Capri 1.0", Font = new Font("Segoe UI", 16, FontStyle.Bold), Left = 20, Top = 16, Width = 520, Height = 30 };
            var info = new Label { Text = "Standalone installation (no separate .NET needed). User data stays under the install folder.", Left = 20, Top = 52, Width = 520, Height = 36 };

            pathBox = new TextBox { Text = dest, Left = 20, Top = 100, Width = 420, Height = 24 };
            var browse = new Button { Text = "Browse...", Left = 455, Top = 98, Width = 85, Height = 28 };
            browse.Click += (_, _) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Choose the Rogue Capri folder" })
                    if (dlg.ShowDialog() == DialogResult.OK) pathBox.Text = Path.Combine(dlg.SelectedPath, "Rogue Capri");
            };

            desktopBox = new CheckBox { Text = "Create a desktop shortcut", Checked = true, Left = 20, Top = 136, Width = 300, Height = 24 };

            progress = new ProgressBar { Left = 20, Top = 180, Width = 520, Height = 18, Minimum = 0, Maximum = 100, Value = 0 };

            logBox = new TextBox { Left = 20, Top = 210, Width = 520, Height = 140, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };

            installButton = new Button { Text = "Install", Left = 320, Top = 368, Width = 110, Height = 32 };
            cancelButton = new Button { Text = "Cancel", Left = 440, Top = 368, Width = 100, Height = 32 };

            Controls.Add(title); Controls.Add(info); Controls.Add(pathBox); Controls.Add(browse);
            Controls.Add(desktopBox); Controls.Add(progress); Controls.Add(logBox);
            Controls.Add(installButton); Controls.Add(cancelButton);

            cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
            installButton.Click += (_, _) =>
            {
                installButton.Enabled = false; cancelButton.Enabled = false;
                string dest2 = pathBox.Text.Trim();
                new Thread(() =>
                {
                    string error = Installer.Install(dest2, desktopBox.Checked, line => Invoke(new Action(() => logBox.AppendText(line + "\r\n"))), v => Invoke(new Action(() => progress.Value = v)));
                    Invoke(new Action(() =>
                    {
                        if (error != null) { MessageBox.Show(this, error, "Rogue Capri Setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                        else { MessageBox.Show(this, "Rogue Capri 1.0 installed to " + dest2, "Rogue Capri Setup", MessageBoxButtons.OK, MessageBoxIcon.Information); DialogResult = DialogResult.OK; }
                    }));
                }) { IsBackground = true }.Start();
            };
        }

        private static Icon LoadIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }
    }
    internal static class Installer
    {
        private static readonly string[] LayoutDirs = { "Data", "Config", "Logs", "Cache", "Updates", "Backup" };

        internal static string DefaultTarget() { return @"E:\Rogue Capri"; }

        private static string DriveOf(string path)
        {
            try { return Path.GetPathRoot(Path.GetFullPath(path)); }
            catch (Exception) { return null; }
        }

        internal static string Install(string dest, bool desktopShortcut, Action<string> log, Action<int> progress = null)
        {
            if (string.IsNullOrWhiteSpace(dest)) return "Choose an install folder.";
            string full;
            try { full = Path.GetFullPath(dest); }
            catch (Exception error) { return "That folder path is not valid: " + error.Message; }
            string root = DriveOf(full);
            if (root == null || !new DirectoryInfo(root).Exists)
                return "Drive " + (root ?? "?") + " is not available. Rogue Capri installs on E: by default.";

            try
            {
                log?.Invoke("Extracting Rogue Capri 1.0 to " + full + " ...");
                Directory.CreateDirectory(full);
                using (Stream stream = typeof(Installer).Assembly.GetManifestResourceStream("RogueCapriSetup.app.zip"))
                {
                    if (stream == null) return "The app package is missing in this setup file (re-run ci/build-rogue.ps1).";
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        int total = zip.Entries.Count, done = 0;
                        foreach (ZipArchiveEntry entry in zip.Entries)
                        {
                            if (entry.FullName.Length == 0) continue;
                            string target = Path.GetFullPath(Path.Combine(full, entry.FullName));
                            if (!target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(target);
                            }
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                bool dataFile = IsDataFile(entry.FullName);
                                if (!(dataFile && File.Exists(target)))
                                {
                                    using (Stream from = entry.Open())
                                    using (Stream to = File.Create(target)) from.CopyTo(to);
                                }
                            }
                            done++;
                            progress?.Invoke(total == 0 ? 100 : done * 100 / total);
                        }
                    }
                }

                // Layout: data/config/logs live next to the exe and are never overwritten on update.
                foreach (string part in LayoutDirs) Directory.CreateDirectory(Path.Combine(full, part));
                File.Copy(Application.ExecutablePath, Path.Combine(full, "Uninstall.exe"), true);

                string exe = Path.Combine(full, Program.ExeName);
                if (!File.Exists(exe)) return "Extraction finished without " + Program.ExeName + ".";

                CreateShortcuts(full, exe, desktopShortcut);
                WriteUninstallEntry(full, exe);
                log?.Invoke("Done.");
                return null;
            }
            catch (Exception error) { return error.Message; }
        }

        private static bool IsDataFile(string entryName)
        {
            string name = entryName.Replace('\\', '/');
            return name.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || name.EndsWith("/companion-memory.json", StringComparison.OrdinalIgnoreCase);
        }

        private static void CreateShortcuts(string dir, string exe, bool desktopShortcut)
        {
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, Program.AppName + ".lnk"), exe, dir);
            if (desktopShortcut)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk"), exe, dir);
        }

        private static void CreateShortcut(string link, string exe, string dir)
        {
            var shell = (Type) Type.GetTypeFromProgID("WScript.Shell");
            if (shell == null) return;
            object com = Activator.CreateInstance(shell);
            try
            {
                dynamic shortcut = com.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, com, new object[] { link });
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = dir;
                shortcut.Save();
            }
            catch (Exception) { }
            finally { Marshal.FinalReleaseComObject(com); }
        }

        private static void WriteUninstallEntry(string dir, string exe)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.RegKey))
            {
                if (key == null) return;
                key.SetValue("DisplayName", Program.AppName);
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "Capri Desktop");
                key.SetValue("InstallLocation", dir);
                key.SetValue("DisplayIcon", exe);
                key.SetValue("UninstallString", "\"" + Path.Combine(dir, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + Path.Combine(dir, "Uninstall.exe") + "\" --uninstall /S");
            }
        }

        internal static string RequestUninstall(string dest, bool silent)
        {
            string dir = null;
            try
            {
                string self = Path.GetDirectoryName(Application.ExecutablePath);
                // An explicit /D= wins, but only if it really is an install folder
                // (contains our Uninstall.exe). Anything else is refused.
                if (!string.IsNullOrWhiteSpace(dest) && Directory.Exists(dest) && File.Exists(Path.Combine(dest, "Uninstall.exe"))) dir = Path.GetFullPath(dest);
                else dir = self;
            }
            catch (Exception error) { return error.Message; }
            if (dir == null) return "Install folder not found.";

            bool keepData = true;
            if (!silent)
            {
                DialogResult answer = MessageBox.Show("Remove Rogue Capri? Your data (" + Path.Combine(dir, "Data") + ") is kept unless you say no.", "Rogue Capri Uninstall", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return null;
                keepData = answer == DialogResult.Yes;
            }

            RemoveShortcuts();
            Registry.CurrentUser.DeleteSubKeyTree(Program.RegKey, false);
            try
            {
                if (keepData)
                {
                    foreach (string file in Directory.EnumerateFiles(dir))
                    {
                        string name = Path.GetFileName(file);
                        if (string.Equals(name, "Uninstall.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        File.Delete(file);
                    }
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        string name = new DirectoryInfo(sub).Name;
                        if (string.Equals(name, "Data", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Backup", StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.Delete(sub, true);
                    }
                }
                else Directory.Delete(dir, true);
            }
            catch (Exception error) { return error.Message; }

            // A running exe cannot delete itself; finish the job from a temp script.
            // keepData=true: only the uninstaller exe itself may be removed.
            try
            {
                string bat = Path.Combine(Path.GetTempPath(), "rogue-cleanup-" + DateTime.Now.Ticks + ".bat");
                string body = keepData
                    ? "@echo off\r\nset n=0\r\n:retry\r\ndel /f /q \"" + Path.Combine(dir, "Uninstall.exe") + "\" >nul 2>nul\r\nif not exist \"" + Path.Combine(dir, "Uninstall.exe") + "\" goto done\r\nset /a n+=1\r\nif %n% LSS 60 goto wait\r\ngoto done\r\n:wait\r\ntimeout /t 1 /nobreak >nul\r\ngoto retry\r\n:done\r\ndel \"%~f0\"\r\n"
                    : "@echo off\r\nset n=0\r\n:retry\r\nrmdir /s /q \"" + dir + "\"\r\nif not exist \"" + dir + "\" goto done\r\nset /a n+=1\r\nif %n% LSS 30 goto wait\r\ngoto done\r\n:wait\r\ntimeout /t 2 /nobreak >nul\r\ngoto retry\r\n:done\r\ndel \"%~f0\"\r\n";
                File.WriteAllText(bat, body);
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"") { UseShellExecute = true });
            }
            catch (Exception) { }
            return null;
        }

        private static void RemoveShortcuts()
        {
            try
            {
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", Program.AppName + ".lnk"));
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk"));
            }
            catch (Exception) { }
        }
    }
}
