using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CodexHomeMover
{
    internal static class UiSnapshot
    {
        [STAThread]
        private static int Main(string[] arguments)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception error = e.ExceptionObject as Exception;
                if (error != null)
                {
                    Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message);
                    Console.Error.WriteLine(error.StackTrace);
                }
            };
            if (arguments.Length < 1 || arguments.Length > 2)
            {
                Console.Error.WriteLine("Expected an output PNG path and optional zoom percent.");
                return 2;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string outputPath = Path.GetFullPath(arguments[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            if (arguments.Length == 2 && string.Equals(arguments[1], "success", StringComparison.OrdinalIgnoreCase))
            {
                using (MigrationSuccessDialog dialog = new MigrationSuccessDialog(
                    LoadMascot(), @"D:\CodexData\.codex"))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-32000, -32000);
                    dialog.ShowInTaskbar = false;
                    dialog.Show();
                    Application.DoEvents();
                    using (Bitmap image = new Bitmap(dialog.Width, dialog.Height,
                        PixelFormat.Format32bppArgb))
                    {
                        dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                        image.Save(outputPath, ImageFormat.Png);
                    }
                    dialog.Hide();
                }
                Console.WriteLine("Saved success-dialog snapshot: " + outputPath);
                return 0;
            }

            using (MainForm form = new MainForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                StopStorageScanner(form);
                if (arguments.Length == 2)
                {
                    int zoomPercent;
                    if (!int.TryParse(arguments[1], out zoomPercent))
                    {
                        Console.Error.WriteLine("Invalid zoom percent.");
                        return 2;
                    }
                    MethodInfo applyZoom = typeof(MainForm).GetMethod("ApplyZoom",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    applyZoom.Invoke(form, new object[] { zoomPercent / 100F, true });
                    Application.DoEvents();
                }
                ApplyDemoState(form);
                DateTime waitUntil = DateTime.UtcNow.AddMilliseconds(450);
                while (DateTime.UtcNow < waitUntil)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(15);
                }
                ApplyDemoState(form);
                Application.DoEvents();
                ApplyDemoState(form);
                using (Bitmap image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.ClientSize));
                    image.Save(outputPath, ImageFormat.Png);
                }
                form.Hide();
            }

            Console.WriteLine("Saved UI snapshot: " + outputPath);
            return 0;
        }

        private static void StopStorageScanner(MainForm form)
        {
            FieldInfo timerField = typeof(MainForm).GetField("storageScanTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Timer timer = timerField == null ? null : timerField.GetValue(form) as Timer;
            if (timer != null)
            {
                timer.Stop();
            }
        }

        private static void ApplyDemoState(MainForm form)
        {
            SetText(form, "sourcePathLabel", "当前 Codex 数据目录 · 已自动找到");
            SetText(form, "destinationPathLabel", "推荐的新位置 · D: 为 NTFS 固定磁盘，可用 480 GB");
            SetText(form, "sourceText", @"C:\Users\DemoUser\.codex");
            SetText(form, "destinationText", @"D:\CodexData\.codex");
            SetText(form, "stageLabel", "准备就绪");
            SetText(form, "percentLabel", "0%");
            SetText(form, "detailLabel", "先点击“预检”，不会修改或移动任何文件。");
            SetText(form, "countersLabel", "");
            SetText(form, "storageTitleLabel", "Codex 磁盘占用 · C: 盘");
            SetText(form, "storageValueLabel", "28.3 GB");
            SetText(form, "storageFileLabel", "39,748 文件");
            SetText(form, "storageDriveLabel", "占该盘 14.2% · 剩余 120 GB");
            SetText(form, "logBox", "");
            SetVisible(form, "reuseDestinationCheck", false);
            SetVisible(form, "rollbackButton", false);
            SetVisible(form, "cleanupButton", false);
            SetVisible(form, "cancelButton", false);
            SetVisible(form, "preflightButton", true);
            SetVisible(form, "migrateButton", true);

            FieldInfo meterField = typeof(MainForm).GetField("storageMeter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FluentProgressBar meter = meterField == null ? null : meterField.GetValue(form) as FluentProgressBar;
            if (meter != null)
            {
                meter.Value = 14;
            }
            FieldInfo progressField = typeof(MainForm).GetField("progressBar",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MigrationAnimationControl progress = progressField == null
                ? null
                : progressField.GetValue(form) as MigrationAnimationControl;
            if (progress != null)
            {
                progress.Value = 0;
                progress.Active = false;
            }
        }

        private static void SetText(MainForm form, string fieldName, string text)
        {
            FieldInfo field = typeof(MainForm).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Control control = field == null ? null : field.GetValue(form) as Control;
            if (control != null)
            {
                control.Text = text;
            }
        }

        private static void SetVisible(MainForm form, string fieldName, bool visible)
        {
            FieldInfo field = typeof(MainForm).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Control control = field == null ? null : field.GetValue(form) as Control;
            if (control != null)
            {
                control.Visible = visible;
            }
        }

        private static Image LoadMascot()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "CodexHomeMover.Mascot.png"))
            {
                if (stream == null)
                {
                    return null;
                }
                using (Image image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
        }
    }
}
