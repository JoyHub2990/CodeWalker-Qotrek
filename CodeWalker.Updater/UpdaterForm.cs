using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeWalker.Updater
{
    public class UpdaterForm : Form
    {
        private readonly string targetDir;
        private readonly string sourceDir;
        private readonly int waitPid;
        private readonly string restartExe;

        private Label titleLabel;
        private Label statusLabel;
        private ProgressBar progressBar;

        public UpdaterForm(string targetDir, string sourceDir, int waitPid, string restartExe)
        {
            this.targetDir = targetDir;
            this.sourceDir = sourceDir;
            this.waitPid = waitPid;
            this.restartExe = restartExe;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "CodeWalker Updater";
            this.ClientSize = new Size(460, 160);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowIcon = true;

            try
            {
                if (File.Exists("CW.ico"))
                {
                    this.Icon = new Icon("CW.ico");
                }
            }
            catch { }

            titleLabel = new Label()
            {
                Text = "Updating CodeWalker...",
                Font = new Font("Segoe UI", 11.25f, FontStyle.Bold),
                Location = new Point(20, 20),
                Size = new Size(420, 25),
                AutoEllipsis = true
            };

            progressBar = new ProgressBar()
            {
                Location = new Point(20, 55),
                Size = new Size(420, 23),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            statusLabel = new Label()
            {
                Text = "Initializing update...",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(20, 90),
                Size = new Size(420, 45),
                AutoEllipsis = true
            };

            this.Controls.Add(titleLabel);
            this.Controls.Add(progressBar);
            this.Controls.Add(statusLabel);

            this.Shown += UpdaterForm_Shown;
        }

        private async void UpdaterForm_Shown(object sender, EventArgs e)
        {
            await Task.Run(() => PerformUpdate());
        }

        private void SetStatus(string text, int progress = -1)
        {
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => SetStatus(text, progress)));
                }
                catch { }
                return;
            }

            statusLabel.Text = text;
            if (progress >= 0 && progress <= 100)
            {
                progressBar.Value = progress;
            }
        }

        private void PerformUpdate()
        {
            try
            {
                // 1. Wait for parent process to exit
                if (waitPid > 0)
                {
                    SetStatus("Waiting for CodeWalker to close...", 10);
                    try
                    {
                        var proc = Process.GetProcessById(waitPid);
                        if (!proc.WaitForExit(8000))
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Process already terminated
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error waiting for process: " + ex.Message);
                    }
                }

                // Give the OS 500ms to release remaining file handles
                Thread.Sleep(500);

                if (!Directory.Exists(sourceDir))
                {
                    throw new DirectoryNotFoundException("Update source folder not found: " + sourceDir);
                }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // 2. Collect all files to copy
                var allFiles = new List<string>();
                CollectFiles(sourceDir, allFiles);

                SetStatus("Copying update files...", 20);

                int total = allFiles.Count;
                int current = 0;

                foreach (var file in allFiles)
                {
                    string relativePath = GetRelativePath(sourceDir, file);
                    string destFile = Path.Combine(targetDir, relativePath);
                    string destDir = Path.GetDirectoryName(destFile);

                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Retry copy in case of momentary locks
                    bool copied = false;
                    Exception lastEx = null;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            File.Copy(file, destFile, true);
                            copied = true;
                            break;
                        }
                        catch (IOException ioex)
                        {
                            lastEx = ioex;
                            Thread.Sleep(300);
                        }
                        catch (UnauthorizedAccessException uex)
                        {
                            lastEx = uex;
                            Thread.Sleep(300);
                        }
                    }

                    if (!copied && lastEx != null)
                    {
                        throw new IOException("Failed to update file '" + destFile + "': " + lastEx.Message, lastEx);
                    }

                    current++;
                    int pct = 20 + (int)((float)current / Math.Max(1, total) * 70);
                    SetStatus("Updated: " + relativePath, pct);
                }

                SetStatus("Cleaning up temporary files...", 95);
                try
                {
                    Directory.Delete(sourceDir, true);
                }
                catch { }

                SetStatus("Restarting CodeWalker...", 100);
                Thread.Sleep(500);

                // 3. Restart application
                string exeToLaunch = restartExe;
                if (string.IsNullOrEmpty(exeToLaunch))
                {
                    exeToLaunch = "CodeWalker.exe";
                }

                string fullRestartPath = Path.Combine(targetDir, exeToLaunch);
                if (!File.Exists(fullRestartPath))
                {
                    // Fallback search
                    if (File.Exists(Path.Combine(targetDir, "CodeWalker RPF Explorer.exe")))
                    {
                        fullRestartPath = Path.Combine(targetDir, "CodeWalker RPF Explorer.exe");
                    }
                    else if (File.Exists(Path.Combine(targetDir, "CodeWalker.exe")))
                    {
                        fullRestartPath = Path.Combine(targetDir, "CodeWalker.exe");
                    }
                }

                if (File.Exists(fullRestartPath))
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = fullRestartPath,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                }

                // 4. Exit updater
                if (InvokeRequired)
                {
                    Invoke(new Action(() => Application.Exit()));
                }
                else
                {
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() =>
                    {
                        MessageBox.Show(this, "An error occurred while updating CodeWalker:\n\n" + ex.Message, "CodeWalker Updater Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Application.Exit();
                    }));
                }
                else
                {
                    MessageBox.Show("An error occurred while updating CodeWalker:\n\n" + ex.Message, "CodeWalker Updater Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                }
            }
        }

        private static void CollectFiles(string dir, List<string> files)
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                files.Add(f);
            }
            foreach (var d in Directory.GetDirectories(dir))
            {
                CollectFiles(d, files);
            }
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            if (!rootPath.EndsWith("\\") && !rootPath.EndsWith("/"))
            {
                rootPath += "\\";
            }
            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(rootPath.Length);
            }
            return Path.GetFileName(fullPath);
        }
    }
}
