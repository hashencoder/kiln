using System;
using System.Threading;
using System.Windows.Forms;

namespace WebHut
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Native.DontInheritStdHandles();
            Ui.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Sys.EnableModernTls();

            var problem = K.RootProblem();
            if (problem != null)
            {
                MessageBox.Show(problem, "WebHut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            // One panel per WebHut folder.
            bool created;
            using (var mutex = new Mutex(true, "WebHut-" + K.Root.ToLowerInvariant().GetHashCode().ToString("x"), out created))
            {
                if (!created)
                {
                    MessageBox.Show("WebHut is already running for this folder. Look for its icon in the system tray.", "WebHut",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Log.Info("WebHut " + K.AppVersion + " started in " + K.Root);
                Application.ThreadException += (s, e) => Fatal(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Fatal(e.ExceptionObject as Exception);

                if (!Installer.IsInstalled())
                {
                    using (var setup = new SetupForm())
                        if (setup.ShowDialog() != DialogResult.OK) return 0;
                }
                Application.Run(new MainForm());
            }
            return 0;
        }

        static void Fatal(Exception e)
        {
            if (e == null) return;
            Log.Error("Unexpected error: " + e);
            MessageBox.Show(e.Message + "\n\nDetails were written to logs\\webhut.log.", "WebHut - unexpected error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
