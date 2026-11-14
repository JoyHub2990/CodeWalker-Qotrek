using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Windows.Forms;
using CodeWalker.Utils;

namespace CodeWalker.Forms
{
    public class UpdateDialog : Form
    {
        private readonly ReleaseInfo release;
        private WebClient webClient;
        private string tempDir;
        private string tempZipPath;
        private string tempExtractDir;
        private bool isDownloading = false;

        private Panel headerPanel;
        private Label titleLabel;
        private Label versionLabel;
        private TextBox notesTextBox;
        private Label notesHeaderLabel;
        private ProgressBar progressBar;
        private Label statusLabel;
        private Button updateButton;
        private Button githubButton;
        private Button cancelButton;

        public UpdateDialog(ReleaseInfo release)
        {
            this.release = release;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "CodeWalker Update Available";
            this.ClientSize = new Size(580, 480);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
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

            // Header panel
            headerPanel = new Panel()
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = SystemColors.Window,
                Padding = new Padding(15, 10, 15, 10)
            };

            titleLabel = new Label()
            {
                Text = "A new version of CodeWalker is available!",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = SystemColors.HotTrack,
                Location = new Point(15, 10),
                AutoSize = true
            };

            versionLabel = new Label()
            {
                Text = string.Format("Latest: {0}   |   Current: {1}", release.TagName, UpdateManager.CurrentVersion),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = SystemColors.ControlDarkDark,
                Location = new Point(16, 35),
                AutoSize = true
            };

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(versionLabel);

            // Notes header
            notesHeaderLabel = new Label()
            {
                Text = "Release Notes:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(15, 75),
                AutoSize = true
            };

            // Notes TextBox
            notesTextBox = new TextBox()
            {
                Location = new Point(15, 95),
                Size = new Size(550, 260),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Font = new Font("Consolas", 9f, FontStyle.Regular),
                Text = string.IsNullOrEmpty(release.Body) ? "No release notes provided." : release.Body
            };

            // Status label & progress bar
            statusLabel = new Label()
            {
                Text = "Ready to download and install.",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(15, 365),
                Size = new Size(550, 20),
                AutoEllipsis = true
            };

            progressBar = new ProgressBar()
            {
                Location = new Point(15, 390),
                Size = new Size(550, 20),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            // Buttons
            updateButton = new Button()
            {
                Text = "Download & Install",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Size = new Size(150, 32),
                Location = new Point(15, 430),
                UseVisualStyleBackColor = true
            };
            updateButton.Click += UpdateButton_Click;

            githubButton = new Button()
            {
                Text = "View on GitHub",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Size = new Size(120, 32),
                Location = new Point(175, 430),
                UseVisualStyleBackColor = true
            };
            githubButton.Click += GithubButton_Click;

            cancelButton = new Button()
            {
                Text = "Close",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Size = new Size(90, 32),
                Location = new Point(475, 430),
                UseVisualStyleBackColor = true
            };
            cancelButton.Click += CancelButton_Click;

            this.Controls.Add(headerPanel);
            this.Controls.Add(notesHeaderLabel);
            this.Controls.Add(notesTextBox);
            this.Controls.Add(statusLabel);
            this.Controls.Add(progressBar);
            this.Controls.Add(updateButton);
            this.Controls.Add(githubButton);
            this.Controls.Add(cancelButton);

            this.FormClosing += UpdateDialog_FormClosing;
        }

        private void GithubButton_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(release.HtmlUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            if (isDownloading)
            {
                var res = MessageBox.Show(this, "Are you sure you want to cancel the update download?", "Cancel Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res != DialogResult.Yes) return;

                if (webClient != null && webClient.IsBusy)
                {
                    webClient.CancelAsync();
                }
            }
            this.Close();
        }

        private void UpdateDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isDownloading)
            {
                if (webClient != null && webClient.IsBusy)
                {
                    webClient.CancelAsync();
                }
            }
        }

        private void UpdateButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(release.DownloadUrl))
            {
                MessageBox.Show(this, "No .zip asset was found in this release. Please download the update manually from GitHub.", "Update Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartDownload();
        }

        private void StartDownload()
        {
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "CodeWalker_Update");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                tempZipPath = Path.Combine(tempDir, "update.zip");
                tempExtractDir = Path.Combine(tempDir, "extracted");

                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { }
                }
                if (Directory.Exists(tempExtractDir))
                {
                    try { Directory.Delete(tempExtractDir, true); } catch { }
                }

                isDownloading = true;
                updateButton.Enabled = false;
                githubButton.Enabled = false;
                cancelButton.Text = "Cancel";

                statusLabel.Text = "Connecting to download server...";
                progressBar.Value = 0;

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                webClient = new WebClient();
                webClient.Headers.Add("User-Agent", "CodeWalker-AutoUpdater");
                webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
                webClient.DownloadFileCompleted += WebClient_DownloadFileCompleted;

                webClient.DownloadFileAsync(new Uri(release.DownloadUrl), tempZipPath);
            }
            catch (Exception ex)
            {
                isDownloading = false;
                updateButton.Enabled = true;
                githubButton.Enabled = true;
                cancelButton.Text = "Close";
                MessageBox.Show(this, "Failed to start download:\n\n" + ex.Message, "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            progressBar.Value = e.ProgressPercentage;
            double mbRecv = e.BytesReceived / 1048576.0;
            double mbTotal = e.TotalBytesToReceive / 1048576.0;
            statusLabel.Text = string.Format("Downloading update: {0}% ({1:F1} MB / {2:F1} MB)", e.ProgressPercentage, mbRecv, mbTotal);
        }

        private void WebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            isDownloading = false;

            if (e.Cancelled)
            {
                statusLabel.Text = "Download cancelled.";
                progressBar.Value = 0;
                updateButton.Enabled = true;
                githubButton.Enabled = true;
                cancelButton.Text = "Close";
                return;
            }

            if (e.Error != null)
            {
                statusLabel.Text = "Download failed: " + e.Error.Message;
                updateButton.Enabled = true;
                githubButton.Enabled = true;
                cancelButton.Text = "Close";
                MessageBox.Show(this, "Failed to download update:\n\n" + e.Error.Message, "Download Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Download succeeded, extract and launch updater
            statusLabel.Text = "Extracting update package...";
            progressBar.Value = 100;

            try
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, true);
                }
                Directory.CreateDirectory(tempExtractDir);

                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);

                statusLabel.Text = "Starting updater helper...";

                // Find CodeWalker.Updater.exe
                string updaterExeName = "CodeWalker.Updater.exe";
                string extractedUpdater = Path.Combine(tempExtractDir, updaterExeName);
                string localUpdater = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, updaterExeName);
                string tempUpdater = Path.Combine(tempDir, updaterExeName);

                if (File.Exists(extractedUpdater))
                {
                    File.Copy(extractedUpdater, tempUpdater, true);
                }
                else if (File.Exists(localUpdater))
                {
                    File.Copy(localUpdater, tempUpdater, true);
                }
                else
                {
                    throw new FileNotFoundException("Could not find CodeWalker.Updater.exe in the update package or installation folder.");
                }

                string targetDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                int currentPid = Process.GetCurrentProcess().Id;
                string currentExeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);

                var psi = new ProcessStartInfo()
                {
                    FileName = tempUpdater,
                    Arguments = string.Format("\"{0}\" \"{1}\" {2} \"{3}\"", targetDir, tempExtractDir, currentPid, currentExeName),
                    UseShellExecute = true,
                    Verb = "runas" // Request UAC elevation for C:\Program Files
                };

                Process.Start(psi);

                // Exit CodeWalker
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                updateButton.Enabled = true;
                githubButton.Enabled = true;
                cancelButton.Text = "Close";
                statusLabel.Text = "Extraction error: " + ex.Message;
                MessageBox.Show(this, "Failed to prepare update:\n\n" + ex.Message, "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
