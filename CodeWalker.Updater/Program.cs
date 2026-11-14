using System;
using System.IO;
using System.Windows.Forms;

namespace CodeWalker.Updater
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args == null || args.Length < 3)
            {
                MessageBox.Show("CodeWalker Updater is an internal component executed by CodeWalker to apply updates.\n\nUsage: CodeWalker.Updater.exe <targetDir> <sourceDir> <waitPid> [restartExe]",
                    "CodeWalker Updater", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string targetDir = args[0];
            string sourceDir = args[1];
            int waitPid = 0;
            int.TryParse(args[2], out waitPid);
            string restartExe = args.Length > 3 ? args[3] : "CodeWalker.exe";

            Application.Run(new UpdaterForm(targetDir, sourceDir, waitPid, restartExe));
        }
    }
}
