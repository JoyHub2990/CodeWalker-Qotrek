using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeWalker.Utils
{
    public class ReleaseInfo
    {
        public string TagName;
        public string Name;
        public string Body;
        public string HtmlUrl;
        public string DownloadUrl;
        public long Size;
        public bool IsNewer;
    }

    public static class UpdateManager
    {
        public const string CurrentVersion = "v0.2.2-patch";
        public const string GitHubRepo = "Qotrek/CodeWalker";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";

        private static bool hasCheckedStartup = false;

        public static void CheckOnStartup(Form owner)
        {
            if (hasCheckedStartup) return;
            hasCheckedStartup = true;

            Task.Run(async () =>
            {
                // Wait 2 seconds after startup so UI is fully responsive
                await Task.Delay(2000);
                CheckForUpdatesAsync(true, owner);
            });
        }

        public static async void CheckForUpdatesAsync(bool silent, Form owner)
        {
            try
            {
                var release = await FetchLatestReleaseAsync();
                if (release == null)
                {
                    if (!silent)
                    {
                        ShowMessage(owner, "Could not find any release information on GitHub.", "Update Check", MessageBoxIcon.Warning);
                    }
                    return;
                }

                if (release.IsNewer)
                {
                    if (silent)
                    {
                        if (owner != null && owner.IsHandleCreated)
                        {
                            owner.BeginInvoke(new Action(() =>
                            {
                                var res = MessageBox.Show(owner,
                                    string.Format("A new version of CodeWalker ({0}) is available!\n\nWould you like to review and install the update?", release.TagName),
                                    "CodeWalker Update Available",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Information);

                                if (res == DialogResult.Yes)
                                {
                                    ShowUpdateDialog(owner, release);
                                }
                            }));
                        }
                    }
                    else
                    {
                        ShowUpdateDialog(owner, release);
                    }
                }
                else
                {
                    if (!silent)
                    {
                        ShowMessage(owner,
                            string.Format("You are running the latest version of CodeWalker ({0}).", CurrentVersion),
                            "CodeWalker is Up to Date",
                            MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    ShowMessage(owner, "Failed to check for updates:\n\n" + ex.Message, "Update Check Error", MessageBoxIcon.Error);
                }
            }
        }

        public static async Task<ReleaseInfo> FetchLatestReleaseAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                    var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
                    request.UserAgent = "CodeWalker-AutoUpdater";
                    request.Accept = "application/vnd.github.v3+json";
                    request.Timeout = 10000;

                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();
                        return ParseReleaseJson(json);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Update check failed: " + ex.Message);
                    throw;
                }
            });
        }

        private static ReleaseInfo ParseReleaseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var info = new ReleaseInfo();
            info.TagName = ExtractJsonString(json, "tag_name");
            info.Name = ExtractJsonString(json, "name");
            info.HtmlUrl = ExtractJsonString(json, "html_url");
            info.Body = UnescapeJsonString(ExtractJsonString(json, "body"));

            // Look for .zip asset
            var zipMatch = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.zip)\"");
            if (zipMatch.Success)
            {
                info.DownloadUrl = zipMatch.Groups[1].Value;
            }

            info.IsNewer = IsNewerVersion(CurrentVersion, info.TagName);
            return info;
        }

        public static bool IsNewerVersion(string currentVersion, string remoteTag)
        {
            if (string.IsNullOrEmpty(remoteTag)) return false;
            if (string.Equals(currentVersion?.Trim(), remoteTag?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;

            int[] current = ParseVersionNumbers(currentVersion);
            int[] remote = ParseVersionNumbers(remoteTag);

            int maxLen = Math.Max(current.Length, remote.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int c = i < current.Length ? current[i] : 0;
                int r = i < remote.Length ? remote[i] : 0;
                if (r > c) return true;
                if (r < c) return false;
            }

            return false;
        }

        private static int[] ParseVersionNumbers(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return new int[0];
            var matches = Regex.Matches(tag, @"\d+");
            var list = new List<int>();
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Value, out int n))
                {
                    list.Add(n);
                }
            }
            return list.ToArray();
        }

        private static string ExtractJsonString(string json, string key)
        {
            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "";
        }

        private static string UnescapeJsonString(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\r\\n", "\r\n")
                       .Replace("\\n", "\r\n")
                       .Replace("\\r", "\r\n")
                       .Replace("\\\"", "\"")
                       .Replace("\\\\", "\\")
                       .Replace("\\t", "\t");
        }

        private static void ShowUpdateDialog(Form owner, ReleaseInfo release)
        {
            if (owner != null && owner.InvokeRequired)
            {
                owner.BeginInvoke(new Action(() => ShowUpdateDialog(owner, release)));
                return;
            }

            using (var dialog = new Forms.UpdateDialog(release))
            {
                dialog.ShowDialog(owner);
            }
        }

        private static void ShowMessage(Form owner, string message, string title, MessageBoxIcon icon)
        {
            if (owner != null && owner.InvokeRequired)
            {
                owner.BeginInvoke(new Action(() => ShowMessage(owner, message, title, icon)));
                return;
            }

            MessageBox.Show(owner, message, title, MessageBoxButtons.OK, icon);
        }
    }
}
