using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexHomeMover
{
    internal sealed class MainForm : Form
    {
        private const int BaseClientWidth = 1060;
        private const int BaseClientHeight = 720;
        private const float MinimumZoom = 0.8F;
        private const float MaximumZoom = 2F;
        private readonly Color appBackground = Color.FromArgb(23, 22, 20);
        private readonly Color sidebarBackground = Color.FromArgb(15, 15, 14);
        private readonly Color cardBackground = Color.FromArgb(42, 40, 36);
        private readonly Color cardHighlight = Color.FromArgb(53, 50, 44);
        private readonly Color inputBackground = Color.FromArgb(32, 31, 28);
        private readonly Color border = Color.FromArgb(72, 68, 59);
        private readonly Color textPrimary = Color.FromArgb(248, 246, 239);
        private readonly Color textSecondary = Color.FromArgb(197, 205, 212);
        private readonly Color textMuted = Color.FromArgb(145, 139, 126);
        private readonly Color fluentBlue = Color.FromArgb(255, 199, 39);
        private readonly Color brandYellow = Color.FromArgb(255, 199, 39);
        private readonly Color brandOrange = Color.FromArgb(255, 166, 25);
        private readonly MigrationEngine engine;
        private readonly string logPath;
        private readonly object logLock = new object();
        private readonly Dictionary<Control, Rectangle> baseControlBounds = new Dictionary<Control, Rectangle>();
        private readonly Dictionary<Control, float> baseFontSizes = new Dictionary<Control, float>();

        private Panel viewport;
        private Panel zoomSurface;
        private Label zoomLabel;
        private Label sourcePathLabel;
        private Label destinationPathLabel;
        private TextBox sourceText;
        private TextBox destinationText;
        private CheckBox verifyHashCheck;
        private CheckBox reuseDestinationCheck;
        private Label stageLabel;
        private Label percentLabel;
        private Label detailLabel;
        private Label countersLabel;
        private MigrationAnimationControl progressBar;
        private Label storageTitleLabel;
        private Label storageValueLabel;
        private Label storageFileLabel;
        private Label storageDriveLabel;
        private FluentProgressBar storageMeter;
        private System.Windows.Forms.Timer storageScanTimer;
        private RichTextBox logBox;
        private FluentButton sourceBrowseButton;
        private FluentButton destinationBrowseButton;
        private FluentButton autoRecommendButton;
        private FluentButton preflightButton;
        private FluentButton migrateButton;
        private FluentButton rollbackButton;
        private FluentButton cleanupButton;
        private FluentButton cancelButton;
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool applyingZoom;
        private bool initialZoomApplied;
        private bool zoomLayoutReady;
        private bool storageScanRunning;
        private int storageScanVersion;
        private float zoomFactor = 1F;
        private FormWindowState previousWindowState;

        private sealed class PathRecommendation
        {
            internal string SourcePath;
            internal string DestinationPath;
            internal string SourceReason;
            internal string DestinationReason;
            internal long SourceBytes;
            internal long DestinationFreeBytes;
            internal bool SourceFound;
            internal bool DestinationFound;
        }

        private sealed class StorageUsage
        {
            internal long LogicalBytes;
            internal long AllocatedBytes;
            internal long LargestFile;
            internal long FileCount;
            internal long DriveTotalBytes;
            internal long DriveFreeBytes;
            internal string DriveName;
        }

        public MainForm()
        {
            engine = new MigrationEngine();
            engine.ProgressChanged += EngineProgressChanged;
            engine.LogMessage += AppendLog;

            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexHomeMover", "logs");
            Directory.CreateDirectory(logDirectory);
            logPath = Path.Combine(logDirectory, "latest.log");
            RotateLogIfNeeded(logPath);

            InitializeWindow();
            BuildInterface();
            InitializeStorageScanner();
            LoadDefaults();
            RefreshRecordState();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowStyling.Apply(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && storageScanTimer != null)
            {
                storageScanTimer.Stop();
                storageScanTimer.Dispose();
                storageScanTimer = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (initialZoomApplied)
            {
                return;
            }

            initialZoomApplied = true;
            float dpi;
            using (Graphics graphics = CreateGraphics())
            {
                dpi = graphics.DpiX;
            }
            if (dpi >= 144F)
            {
                ApplyZoom(1.3F, true);
            }
            else if (dpi >= 120F)
            {
                ApplyZoom(1.1F, true);
            }
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (zoomLayoutReady && !applyingZoom && WindowState == FormWindowState.Normal)
            {
                ApplyZoom((float)ClientSize.Width / BaseClientWidth, false);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!zoomLayoutReady || applyingZoom || WindowState == previousWindowState)
            {
                return;
            }

            previousWindowState = WindowState;
            BeginInvoke((Action)delegate
            {
                if (!IsDisposed && zoomLayoutReady && !applyingZoom)
                {
                    ApplyZoom((float)ClientSize.Width / BaseClientWidth, false);
                }
            });
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus))
            {
                ChangeZoom(0.1F);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus))
            {
                ChangeZoom(-0.1F);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0))
            {
                ApplyZoom(1F, true);
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        private void InitializeWindow()
        {
            Text = "Codex 搬家小鱼";
            ClientSize = new Size(BaseClientWidth, BaseClientHeight);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(860, 620);
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = appBackground;
            ForeColor = textPrimary;
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }
        }

        private void BuildInterface()
        {
            SuspendLayout();

            viewport = new Panel();
            viewport.Dock = DockStyle.Fill;
            viewport.AutoScroll = true;
            viewport.BackColor = appBackground;
            Controls.Add(viewport);

            zoomSurface = new Panel();
            zoomSurface.Location = Point.Empty;
            zoomSurface.Size = new Size(BaseClientWidth, BaseClientHeight);
            zoomSurface.BackColor = appBackground;
            viewport.Controls.Add(zoomSurface);

            Panel sidebar = BuildSidebar();
            sidebar.Location = Point.Empty;
            sidebar.Size = new Size(250, BaseClientHeight);
            zoomSurface.Controls.Add(sidebar);

            Panel content = new Panel();
            content.Location = new Point(250, 0);
            content.Size = new Size(810, 720);
            content.BackColor = appBackground;
            zoomSurface.Controls.Add(content);

            Label heading = NewLabel("Codex 搬家小鱼", 22F, FontStyle.Bold, textPrimary);
            heading.Location = new Point(28, 18);
            heading.Size = new Size(420, 42);
            content.Controls.Add(heading);

            Label subheading = NewLabel("完整复制并校验后，通过 Junction 保持原路径不变。", 9.5F,
                FontStyle.Regular, textSecondary);
            subheading.Location = new Point(30, 62);
            subheading.Size = new Size(560, 25);
            content.Controls.Add(subheading);

            Panel brandLine = new Panel();
            brandLine.Location = new Point(30, 89);
            brandLine.Size = new Size(58, 3);
            brandLine.BackColor = brandYellow;
            content.Controls.Add(brandLine);

            FluentPanel zoomPanel = new FluentPanel();
            zoomPanel.Location = new Point(612, 22);
            zoomPanel.Size = new Size(170, 36);
            zoomPanel.FillColor = Color.FromArgb(47, 40, 21);
            zoomPanel.GradientColor = Color.FromArgb(68, 55, 22);
            zoomPanel.BorderColor = Color.FromArgb(125, 101, 31);
            zoomPanel.CornerRadius = 18;

            FluentButton zoomOutButton = NewSecondaryButton(string.Empty);
            zoomOutButton.Location = new Point(4, 4);
            zoomOutButton.Size = new Size(32, 28);
            zoomOutButton.Icon = FluentIcon.ZoomOut;
            ApplyAccentOutline(zoomOutButton);
            zoomOutButton.BorderColor = Color.Transparent;
            zoomOutButton.Click += delegate { ChangeZoom(-0.1F); };
            zoomPanel.Controls.Add(zoomOutButton);

            zoomLabel = NewLabel("显示 100%", 8.3F, FontStyle.Regular, fluentBlue);
            zoomLabel.Location = new Point(39, 5);
            zoomLabel.Size = new Size(92, 26);
            zoomLabel.TextAlign = ContentAlignment.MiddleCenter;
            zoomPanel.Controls.Add(zoomLabel);

            FluentButton zoomInButton = NewSecondaryButton(string.Empty);
            zoomInButton.Location = new Point(134, 4);
            zoomInButton.Size = new Size(32, 28);
            zoomInButton.Icon = FluentIcon.ZoomIn;
            ApplyAccentOutline(zoomInButton);
            zoomInButton.BorderColor = Color.Transparent;
            zoomInButton.Click += delegate { ChangeZoom(0.1F); };
            zoomPanel.Controls.Add(zoomInButton);
            content.Controls.Add(zoomPanel);

            FluentPanel settingsCard = NewCard();
            settingsCard.Location = new Point(28, 108);
            settingsCard.Size = new Size(754, 184);
            content.Controls.Add(settingsCard);

            Label settingsTitle = NewLabel("迁移位置", 11F, FontStyle.Bold, textPrimary);
            settingsTitle.Location = new Point(18, 12);
            settingsTitle.Size = new Size(150, 27);
            settingsCard.Controls.Add(settingsTitle);

            autoRecommendButton = NewAccentButton("自动推荐");
            autoRecommendButton.Location = new Point(628, 10);
            autoRecommendButton.Size = new Size(108, 34);
            autoRecommendButton.Icon = FluentIcon.Sparkles;
            autoRecommendButton.Click += async delegate { await RunAutoRecommendAsync(); };
            settingsCard.Controls.Add(autoRecommendButton);

            sourcePathLabel = NewLabel("当前 Codex 数据目录", 8.5F, FontStyle.Regular, textSecondary);
            sourcePathLabel.Location = new Point(18, 43);
            sourcePathLabel.Size = new Size(520, 22);
            settingsCard.Controls.Add(sourcePathLabel);

            FluentPanel sourceField = NewPathField(out sourceText);
            sourceField.Location = new Point(18, 64);
            sourceField.Size = new Size(600, 40);
            settingsCard.Controls.Add(sourceField);

            sourceBrowseButton = NewOutlineAccentButton("浏览");
            sourceBrowseButton.Location = new Point(628, 64);
            sourceBrowseButton.Size = new Size(108, 40);
            sourceBrowseButton.Icon = FluentIcon.Folder;
            sourceBrowseButton.Click += delegate { BrowseForFolder(sourceText); };
            settingsCard.Controls.Add(sourceBrowseButton);

            destinationPathLabel = NewLabel("新位置（建议选择非 C 盘的 NTFS 磁盘）", 8.5F,
                FontStyle.Regular, textSecondary);
            destinationPathLabel.Location = new Point(18, 108);
            destinationPathLabel.Size = new Size(590, 22);
            settingsCard.Controls.Add(destinationPathLabel);

            FluentPanel destinationField = NewPathField(out destinationText);
            destinationField.Location = new Point(18, 132);
            destinationField.Size = new Size(600, 40);
            settingsCard.Controls.Add(destinationField);
            destinationText.TextChanged += delegate { UpdateReuseDestinationOption(); };

            destinationBrowseButton = NewOutlineAccentButton("浏览");
            destinationBrowseButton.Location = new Point(628, 132);
            destinationBrowseButton.Size = new Size(108, 40);
            destinationBrowseButton.Icon = FluentIcon.Folder;
            destinationBrowseButton.Click += delegate { BrowseForFolder(destinationText); };
            settingsCard.Controls.Add(destinationBrowseButton);

            verifyHashCheck = NewCheckBox("逐文件 SHA-256 校验（推荐）");
            verifyHashCheck.Checked = true;
            verifyHashCheck.Location = new Point(30, 308);
            content.Controls.Add(verifyHashCheck);

            reuseDestinationCheck = NewCheckBox("仅用于失败后续传（目标多余文件会隔离）");
            reuseDestinationCheck.Location = new Point(310, 308);
            content.Controls.Add(reuseDestinationCheck);

            FluentPanel progressCard = NewCard();
            progressCard.Location = new Point(28, 344);
            progressCard.Size = new Size(754, 150);
            content.Controls.Add(progressCard);

            FluentPanel stageDot = new FluentPanel();
            stageDot.Location = new Point(21, 23);
            stageDot.Size = new Size(10, 10);
            stageDot.FillColor = fluentBlue;
            stageDot.BorderColor = Color.Transparent;
            stageDot.CornerRadius = 5;
            progressCard.Controls.Add(stageDot);

            stageLabel = NewLabel("准备就绪", 13F, FontStyle.Bold, textPrimary);
            stageLabel.Location = new Point(40, 14);
            stageLabel.Size = new Size(500, 32);
            progressCard.Controls.Add(stageLabel);

            percentLabel = NewLabel("0%", 20F, FontStyle.Bold, fluentBlue);
            percentLabel.Location = new Point(628, 10);
            percentLabel.Size = new Size(104, 38);
            percentLabel.TextAlign = ContentAlignment.MiddleRight;
            progressCard.Controls.Add(percentLabel);

            progressBar = new MigrationAnimationControl();
            progressBar.Location = new Point(21, 48);
            progressBar.Size = new Size(710, 40);
            progressBar.BackColor = cardBackground;
            progressBar.TrackColor = Color.FromArgb(66, 62, 54);
            progressBar.ProgressColor = brandYellow;
            progressBar.ProgressEndColor = brandOrange;
            progressBar.MascotImage = LoadMascot();
            progressCard.Controls.Add(progressBar);

            detailLabel = NewLabel("先点击“预检”，不会修改或移动任何文件。", 9F,
                FontStyle.Regular, textSecondary);
            detailLabel.Location = new Point(21, 91);
            detailLabel.Size = new Size(710, 23);
            progressCard.Controls.Add(detailLabel);

            countersLabel = NewLabel("", 8.5F, FontStyle.Regular, textMuted);
            countersLabel.Location = new Point(21, 119);
            countersLabel.Size = new Size(710, 22);
            progressCard.Controls.Add(countersLabel);

            preflightButton = NewBrandOutlineButton("预检");
            preflightButton.Location = new Point(28, 512);
            preflightButton.Size = new Size(96, 42);
            preflightButton.Icon = FluentIcon.CheckCircle;
            preflightButton.Click += async delegate { await RunPreflightAsync(); };
            content.Controls.Add(preflightButton);

            migrateButton = NewPrimaryButton("开始迁移");
            migrateButton.Location = new Point(136, 512);
            migrateButton.Size = new Size(126, 42);
            migrateButton.Icon = FluentIcon.ArrowRight;
            migrateButton.Click += async delegate { await RunMigrationAsync(); };
            content.Controls.Add(migrateButton);

            rollbackButton = NewSecondaryButton("迁回 C 盘");
            rollbackButton.Location = new Point(274, 512);
            rollbackButton.Size = new Size(112, 42);
            rollbackButton.Icon = FluentIcon.Undo;
            rollbackButton.Visible = false;
            rollbackButton.Click += async delegate { await RunRollbackAsync(); };
            content.Controls.Add(rollbackButton);

            cleanupButton = NewDangerButton("释放 C 盘空间");
            cleanupButton.Location = new Point(398, 512);
            cleanupButton.Size = new Size(136, 42);
            cleanupButton.Icon = FluentIcon.Trash;
            cleanupButton.Visible = false;
            cleanupButton.Click += async delegate { await RunCleanupAsync(); };
            content.Controls.Add(cleanupButton);

            cancelButton = NewSecondaryButton("取消");
            cancelButton.Location = new Point(546, 512);
            cancelButton.Size = new Size(82, 42);
            cancelButton.Icon = FluentIcon.Close;
            cancelButton.Enabled = false;
            cancelButton.Visible = false;
            cancelButton.Click += delegate
            {
                if (cancellation != null)
                {
                    cancellation.Cancel();
                    AppendLog("已请求取消；程序会在下一个安全检查点停止。");
                }
            };
            content.Controls.Add(cancelButton);

            FluentButton openLogButton = NewSecondaryButton("打开日志");
            openLogButton.Location = new Point(640, 512);
            openLogButton.Size = new Size(102, 42);
            openLogButton.Icon = FluentIcon.Document;
            openLogButton.Click += delegate
            {
                if (File.Exists(logPath))
                {
                    string notepadPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "notepad.exe");
                    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                    startInfo.FileName = notepadPath;
                    startInfo.Arguments = "\"" + logPath + "\"";
                    startInfo.UseShellExecute = false;
                    System.Diagnostics.Process.Start(startInfo);
                }
            };
            content.Controls.Add(openLogButton);

            Label logTitle = NewLabel("操作记录", 9.5F, FontStyle.Bold, textSecondary);
            logTitle.Location = new Point(30, 582);
            logTitle.Size = new Size(150, 26);
            content.Controls.Add(logTitle);

            Label logHint = NewLabel("仅保存在本机", 8F, FontStyle.Regular, textMuted);
            logHint.Location = new Point(628, 584);
            logHint.Size = new Size(154, 22);
            logHint.TextAlign = ContentAlignment.MiddleRight;
            content.Controls.Add(logHint);

            logBox = new RichTextBox();
            logBox.Location = new Point(28, 616);
            logBox.Size = new Size(754, 76);
            logBox.ReadOnly = true;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.BackColor = Color.FromArgb(11, 18, 25);
            logBox.ForeColor = Color.FromArgb(176, 189, 199);
            logBox.Font = new Font("Consolas", 11.33F, FontStyle.Regular, GraphicsUnit.Pixel);
            logBox.DetectUrls = false;
            content.Controls.Add(logBox);

            ResumeLayout(true);
            zoomSurface.PerformLayout();
            CaptureZoomLayout();
        }

        private void CaptureZoomLayout()
        {
            baseControlBounds.Clear();
            baseFontSizes.Clear();
            CaptureChildLayout(zoomSurface);
            zoomFactor = 1F;
            zoomLayoutReady = true;
            previousWindowState = WindowState;
            viewport.AutoScrollMinSize = zoomSurface.Size;
        }

        private void CaptureChildLayout(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                baseControlBounds[control] = control.Bounds;
                if (control.Font != null)
                {
                    baseFontSizes[control] = control.Font.Size;
                }
                CaptureChildLayout(control);
            }
        }

        private void ChangeZoom(float change)
        {
            ApplyZoom(zoomFactor + change, true);
        }

        private void ApplyZoom(float requestedZoom, bool resizeWindow)
        {
            if (!zoomLayoutReady || applyingZoom)
            {
                return;
            }

            float targetZoom = Math.Max(MinimumZoom, Math.Min(MaximumZoom,
                (float)Math.Round(requestedZoom * 10F) / 10F));
            applyingZoom = true;
            try
            {
                if (resizeWindow && WindowState == FormWindowState.Normal)
                {
                    Rectangle workingArea = Screen.FromControl(this).WorkingArea;
                    int centerX = Left + Width / 2;
                    int centerY = Top + Height / 2;
                    int desiredWidth = (int)Math.Round(BaseClientWidth * targetZoom);
                    int desiredHeight = (int)Math.Round(BaseClientHeight * targetZoom);
                    ClientSize = new Size(
                        Math.Min(desiredWidth, Math.Max(800, workingArea.Width - 40)),
                        Math.Min(desiredHeight, Math.Max(560, workingArea.Height - 40)));
                    Location = new Point(
                        Math.Max(workingArea.Left, Math.Min(centerX - Width / 2, workingArea.Right - Width)),
                        Math.Max(workingArea.Top, Math.Min(centerY - Height / 2, workingArea.Bottom - Height)));
                }

                zoomSurface.SuspendLayout();
                foreach (KeyValuePair<Control, Rectangle> item in baseControlBounds)
                {
                    Rectangle original = item.Value;
                    item.Key.Bounds = new Rectangle(
                        (int)Math.Round(original.X * targetZoom),
                        (int)Math.Round(original.Y * targetZoom),
                        Math.Max(1, (int)Math.Round(original.Width * targetZoom)),
                        Math.Max(1, (int)Math.Round(original.Height * targetZoom)));

                    float baseFontSize;
                    if (baseFontSizes.TryGetValue(item.Key, out baseFontSize) && baseFontSize > 0F)
                    {
                        item.Key.Font = new Font(item.Key.Font.FontFamily,
                            Math.Max(7F, baseFontSize * targetZoom), item.Key.Font.Style, GraphicsUnit.Pixel);
                    }
                }

                zoomSurface.Size = new Size(
                    (int)Math.Round(BaseClientWidth * targetZoom),
                    (int)Math.Round(BaseClientHeight * targetZoom));
                zoomSurface.ResumeLayout(true);
                zoomFactor = targetZoom;
                zoomLabel.Text = string.Format("显示 {0}%", (int)Math.Round(zoomFactor * 100F));

                viewport.AutoScrollPosition = Point.Empty;
                viewport.AutoScrollMinSize = zoomSurface.Size;
                int left = Math.Max(0, (viewport.ClientSize.Width - zoomSurface.Width) / 2);
                int top = Math.Max(0, (viewport.ClientSize.Height - zoomSurface.Height) / 2);
                zoomSurface.Location = new Point(left, top);
                viewport.Invalidate(true);
            }
            finally
            {
                applyingZoom = false;
            }
        }

        private Panel BuildSidebar()
        {
            Panel sidebar = new Panel();
            sidebar.Size = new Size(250, 720);
            sidebar.BackColor = sidebarBackground;

            PictureBox mascot = new PictureBox();
            mascot.Location = new Point(37, 16);
            mascot.Size = new Size(176, 176);
            mascot.SizeMode = PictureBoxSizeMode.Zoom;
            mascot.BackColor = Color.Transparent;
            mascot.Image = LoadMascot();
            sidebar.Controls.Add(mascot);

            Label productName = NewLabel("Codex 搬家小鱼", 15F, FontStyle.Bold, textPrimary);
            productName.Location = new Point(16, 196);
            productName.Size = new Size(218, 38);
            productName.TextAlign = ContentAlignment.MiddleCenter;
            sidebar.Controls.Add(productName);

            Label tagline = NewLabel("安全迁移 · 原路径不变", 9.5F, FontStyle.Regular, textSecondary);
            tagline.Location = new Point(16, 236);
            tagline.Size = new Size(218, 26);
            tagline.TextAlign = ContentAlignment.MiddleCenter;
            sidebar.Controls.Add(tagline);

            Panel divider = new Panel();
            divider.Location = new Point(24, 286);
            divider.Size = new Size(202, 1);
            divider.BackColor = Color.FromArgb(52, 49, 43);
            sidebar.Controls.Add(divider);

            AddStep(sidebar, 314, "1", "预检", "空间、进程与权限");
            AddStep(sidebar, 386, "2", "复制与校验", "实时显示文件进度");
            AddStep(sidebar, 458, "3", "安全切换", "Junction 与自动回滚");

            FluentPanel storageCard = new FluentPanel();
            storageCard.Location = new Point(16, 560);
            storageCard.Size = new Size(218, 78);
            storageCard.FillColor = Color.FromArgb(38, 36, 31);
            storageCard.GradientColor = Color.FromArgb(48, 43, 30);
            storageCard.BorderColor = Color.FromArgb(91, 76, 39);
            storageCard.CornerRadius = 10;

            storageTitleLabel = NewLabel("Codex 磁盘占用", 7.4F, FontStyle.Bold, brandYellow);
            storageTitleLabel.Location = new Point(12, 7);
            storageTitleLabel.Size = new Size(194, 18);
            storageCard.Controls.Add(storageTitleLabel);

            storageValueLabel = NewLabel("正在统计…", 11F, FontStyle.Bold, textPrimary);
            storageValueLabel.Location = new Point(12, 24);
            storageValueLabel.Size = new Size(105, 25);
            storageCard.Controls.Add(storageValueLabel);

            storageFileLabel = NewLabel(string.Empty, 7F, FontStyle.Regular, textSecondary);
            storageFileLabel.Location = new Point(112, 28);
            storageFileLabel.Size = new Size(94, 19);
            storageFileLabel.TextAlign = ContentAlignment.MiddleRight;
            storageCard.Controls.Add(storageFileLabel);

            storageMeter = new FluentProgressBar();
            storageMeter.Location = new Point(12, 51);
            storageMeter.Size = new Size(194, 5);
            storageMeter.BackColor = storageCard.FillColor;
            storageMeter.TrackColor = Color.FromArgb(67, 62, 50);
            storageMeter.ProgressColor = brandYellow;
            storageMeter.ProgressEndColor = brandOrange;
            storageCard.Controls.Add(storageMeter);

            storageDriveLabel = NewLabel("后台统计，不影响操作", 6.8F, FontStyle.Regular, textMuted);
            storageDriveLabel.Location = new Point(12, 59);
            storageDriveLabel.Size = new Size(194, 16);
            storageCard.Controls.Add(storageDriveLabel);
            sidebar.Controls.Add(storageCard);

            Label unofficial = NewLabel("社区工具 · 非 OpenAI 官方产品", 7.8F,
                FontStyle.Regular, textMuted);
            unofficial.Location = new Point(10, 682);
            unofficial.Size = new Size(230, 22);
            unofficial.TextAlign = ContentAlignment.MiddleCenter;
            sidebar.Controls.Add(unofficial);

            return sidebar;
        }

        private void AddStep(Panel parent, int top, string number, string title, string description)
        {
            FluentPanel badge = new FluentPanel();
            badge.Location = new Point(30, top);
            badge.Size = new Size(38, 38);
            badge.FillColor = Color.FromArgb(57, 47, 18);
            badge.GradientColor = Color.FromArgb(83, 66, 19);
            badge.BorderColor = Color.FromArgb(137, 106, 22);
            badge.CornerRadius = 9;

            Label badgeText = NewLabel(number, 10F, FontStyle.Bold, fluentBlue);
            badgeText.Dock = DockStyle.Fill;
            badgeText.TextAlign = ContentAlignment.MiddleCenter;
            badge.Controls.Add(badgeText);
            parent.Controls.Add(badge);

            Label titleLabel = NewLabel(title, 10.5F, FontStyle.Bold, textPrimary);
            titleLabel.Location = new Point(82, top - 1);
            titleLabel.Size = new Size(164, 24);
            parent.Controls.Add(titleLabel);

            Label descriptionLabel = NewLabel(description, 8.3F, FontStyle.Regular, textMuted);
            descriptionLabel.Location = new Point(82, top + 24);
            descriptionLabel.Size = new Size(170, 22);
            parent.Controls.Add(descriptionLabel);
        }

        private FluentPanel NewCard()
        {
            FluentPanel panel = new FluentPanel();
            panel.FillColor = cardBackground;
            panel.GradientColor = cardHighlight;
            panel.BorderColor = border;
            panel.CornerRadius = 11;
            return panel;
        }

        private FluentPanel NewPathField(out TextBox textBox)
        {
            FluentPanel field = new FluentPanel();
            field.Size = new Size(600, 40);
            field.FillColor = inputBackground;
            field.GradientColor = Color.FromArgb(39, 37, 33);
            field.BorderColor = border;
            field.CornerRadius = 7;

            textBox = new TextBox();
            textBox.Location = new Point(12, 10);
            textBox.Size = new Size(570, 24);
            textBox.BorderStyle = BorderStyle.None;
            textBox.BackColor = inputBackground;
            textBox.ForeColor = textPrimary;
            textBox.Font = new Font("Segoe UI", 13.33F, FontStyle.Regular, GraphicsUnit.Pixel);
            field.Controls.Add(textBox);
            return field;
        }

        private CheckBox NewCheckBox(string text)
        {
            FluentCheckBox checkBox = new FluentCheckBox();
            checkBox.Text = text;
            checkBox.AutoSize = true;
            checkBox.AccentColor = brandYellow;
            checkBox.BoxColor = inputBackground;
            checkBox.BorderColor = border;
            checkBox.ForeColor = textSecondary;
            checkBox.BackColor = Color.Transparent;
            checkBox.Font = new Font("Microsoft YaHei UI", 11.73F, FontStyle.Regular, GraphicsUnit.Pixel);
            return checkBox;
        }

        private FluentButton NewPrimaryButton(string text)
        {
            FluentButton button = new FluentButton();
            button.Text = text;
            button.NormalColor = brandYellow;
            button.HoverColor = Color.FromArgb(255, 217, 86);
            button.PressedColor = brandOrange;
            button.BorderColor = Color.FromArgb(255, 229, 137);
            button.DisabledColor = Color.FromArgb(82, 70, 36);
            button.ForeColor = Color.FromArgb(35, 29, 10);
            button.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
            return button;
        }

        private FluentButton NewAccentButton(string text)
        {
            FluentButton button = NewPrimaryButton(text);
            button.Font = new Font("Microsoft YaHei UI", 11.73F, FontStyle.Bold, GraphicsUnit.Pixel);
            return button;
        }

        private FluentButton NewSecondaryButton(string text)
        {
            FluentButton button = new FluentButton();
            button.Text = text;
            button.NormalColor = Color.FromArgb(45, 43, 39);
            button.HoverColor = Color.FromArgb(59, 55, 47);
            button.PressedColor = Color.FromArgb(69, 63, 51);
            button.BorderColor = Color.FromArgb(80, 75, 65);
            button.DisabledColor = Color.FromArgb(34, 33, 30);
            button.ForeColor = textPrimary;
            button.Font = new Font("Microsoft YaHei UI", 11.73F, FontStyle.Regular, GraphicsUnit.Pixel);
            return button;
        }

        private FluentButton NewOutlineAccentButton(string text)
        {
            FluentButton button = NewSecondaryButton(text);
            ApplyAccentOutline(button);
            return button;
        }

        private FluentButton NewBrandOutlineButton(string text)
        {
            FluentButton button = NewSecondaryButton(text);
            ApplyAccentOutline(button);
            button.Font = new Font("Microsoft YaHei UI", 11.73F, FontStyle.Bold, GraphicsUnit.Pixel);
            return button;
        }

        private void ApplyAccentOutline(FluentButton button)
        {
            button.NormalColor = Color.FromArgb(45, 42, 34);
            button.HoverColor = Color.FromArgb(65, 56, 31);
            button.PressedColor = Color.FromArgb(81, 66, 27);
            button.BorderColor = Color.FromArgb(143, 111, 25);
            button.DisabledColor = Color.FromArgb(40, 38, 33);
            button.ForeColor = brandYellow;
        }

        private FluentButton NewDangerButton(string text)
        {
            FluentButton button = NewSecondaryButton(text);
            button.ForeColor = Color.FromArgb(255, 153, 164);
            button.BorderColor = Color.FromArgb(99, 55, 60);
            button.HoverColor = Color.FromArgb(72, 43, 47);
            button.PressedColor = Color.FromArgb(89, 44, 50);
            return button;
        }

        private Label NewLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Microsoft YaHei UI", size * 1.3333F, style, GraphicsUnit.Pixel);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }

        private void InitializeStorageScanner()
        {
            storageScanTimer = new System.Windows.Forms.Timer();
            storageScanTimer.Interval = 450;
            storageScanTimer.Tick += async delegate
            {
                storageScanTimer.Stop();
                if (storageScanRunning)
                {
                    return;
                }
                await RefreshStorageUsageAsync(storageScanVersion);
            };
            sourceText.TextChanged += delegate { ScheduleStorageScan(); };
            ScheduleStorageScan();
        }

        private void ScheduleStorageScan()
        {
            storageScanVersion++;
            if (storageScanTimer == null || IsDisposed)
            {
                return;
            }
            storageScanTimer.Stop();
            storageScanTimer.Start();
        }

        private void UpdateReuseDestinationOption()
        {
            if (reuseDestinationCheck == null)
            {
                return;
            }
            bool hasEntries = DirectoryHasEntries(destinationText.Text);
            reuseDestinationCheck.Visible = hasEntries;
            reuseDestinationCheck.Checked = hasEntries;
        }

        private async Task RefreshStorageUsageAsync(int version)
        {
            string path = sourceText.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                storageTitleLabel.Text = "Codex 磁盘占用";
                storageValueLabel.Text = "等待目录";
                storageFileLabel.Text = string.Empty;
                storageDriveLabel.Text = "选择有效的 Codex 数据目录";
                storageMeter.Value = 0;
                return;
            }

            storageScanRunning = true;
            storageValueLabel.Text = "正在统计…";
            storageFileLabel.Text = string.Empty;
            storageDriveLabel.Text = "后台扫描文件，不影响操作";
            storageMeter.Value = 0;
            try
            {
                StorageUsage usage = await Task.Run(delegate { return CalculateStorageUsage(path); });
                if (IsDisposed || version != storageScanVersion)
                {
                    return;
                }

                long usedBytes = usage.AllocatedBytes > 0 ? usage.AllocatedBytes : usage.LogicalBytes;
                storageTitleLabel.Text = string.IsNullOrWhiteSpace(usage.DriveName)
                    ? "Codex 磁盘占用"
                    : string.Format("Codex 磁盘占用 · {0} 盘", usage.DriveName);
                storageValueLabel.Text = FormatBytes(usedBytes);
                storageFileLabel.Text = string.Format("{0:N0} 文件", usage.FileCount);
                if (usage.DriveTotalBytes > 0)
                {
                    double percent = usedBytes * 100.0 / usage.DriveTotalBytes;
                    storageMeter.Value = usedBytes > 0
                        ? Math.Max(1, Math.Min(100, (int)Math.Round(percent)))
                        : 0;
                    storageDriveLabel.Text = string.Format("占该盘 {0:0.0}% · 剩余 {1}",
                        percent, FormatBytes(usage.DriveFreeBytes));
                }
                else
                {
                    storageMeter.Value = 0;
                    storageDriveLabel.Text = "已按实际磁盘占用统计";
                }
            }
            catch (Exception error)
            {
                if (!IsDisposed && version == storageScanVersion)
                {
                    storageValueLabel.Text = "无法统计";
                    storageFileLabel.Text = string.Empty;
                    storageDriveLabel.Text = error.Message;
                    storageMeter.Value = 0;
                }
            }
            finally
            {
                storageScanRunning = false;
                if (!IsDisposed && storageScanTimer != null && version != storageScanVersion)
                {
                    storageScanTimer.Stop();
                    storageScanTimer.Start();
                }
            }
        }

        private void LoadDefaults()
        {
            ApplyRecommendation(FindPathRecommendation(false));
        }

        private async Task RunAutoRecommendAsync()
        {
            if (busy)
            {
                return;
            }

            busy = true;
            SetBusyControls(true);
            stageLabel.ForeColor = textPrimary;
            stageLabel.Text = "正在自动查找";
            detailLabel.Text = "正在识别 Codex 数据并计算可用磁盘空间……";
            countersLabel.Text = string.Empty;
            AppendLog("开始自动查找源目录和推荐目标磁盘。");

            try
            {
                PathRecommendation recommendation = await Task.Run(
                    delegate { return FindPathRecommendation(true); });
                ApplyRecommendation(recommendation);

                stageLabel.Text = recommendation.SourceFound && recommendation.DestinationFound
                    ? "自动推荐完成"
                    : "需要手动选择";
                detailLabel.Text = recommendation.SourceFound && recommendation.DestinationFound
                    ? "已填写推荐路径；点击“预检”前仍可手动修改。"
                    : "没有找到完整的自动推荐结果，请使用“浏览”手动选择。";

                string sourceSize = recommendation.SourceBytes > 0
                    ? FormatBytes(recommendation.SourceBytes)
                    : "未计算";
                string destination = recommendation.DestinationFound
                    ? recommendation.DestinationPath
                    : "未找到空间足够的非 C 盘 NTFS 固定磁盘";
                string message = string.Format(
                    "Codex 数据目录\r\n{0}\r\n{1} · 数据量 {2}\r\n\r\n推荐的新位置\r\n{3}\r\n{4}",
                    recommendation.SourcePath,
                    recommendation.SourceReason,
                    sourceSize,
                    destination,
                    recommendation.DestinationReason);
                MessageBox.Show(this, message, "自动查找与推荐", MessageBoxButtons.OK,
                    recommendation.SourceFound && recommendation.DestinationFound
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Warning);
                AppendLog("自动推荐完成：" + recommendation.DestinationReason);
            }
            catch (Exception error)
            {
                SetFailureDisplay("自动推荐失败：" + error.Message);
                AppendLog("自动推荐失败：" + error);
                MessageBox.Show(this, error.Message, "自动推荐失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
                SetBusyControls(false);
                RefreshRecordState();
            }
        }

        private PathRecommendation FindPathRecommendation(bool calculateSize)
        {
            PathRecommendation result = new PathRecommendation();
            MigrationRecord record = engine.LoadRecord(null);
            if (record != null && Directory.Exists(record.SourcePath))
            {
                result.SourcePath = record.SourcePath;
                result.SourceFound = true;
                result.SourceReason = "来自现有迁移记录";
                result.DestinationPath = record.DestinationPath;
                result.DestinationFound = Directory.Exists(record.DestinationPath);
                result.DestinationReason = "沿用现有迁移记录，避免重复迁移";
                if (calculateSize)
                {
                    long largestFile;
                    result.SourceBytes = CalculateDirectoryBytes(record.SourcePath, out largestFile);
                }
                SetDestinationFreeSpace(result);
                return result;
            }

            string userProfilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            string environmentPath = Environment.GetEnvironmentVariable("CODEX_HOME");
            List<string> candidates = new List<string>();
            AddCandidate(candidates, environmentPath);
            AddCandidate(candidates, userProfilePath);

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    {
                        continue;
                    }
                    AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, "CodexData", ".codex"));
                    AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, ".codex"));
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(environmentPath) && ScoreCodexHome(environmentPath) >= 0)
            {
                result.SourcePath = Path.GetFullPath(environmentPath).TrimEnd('\\');
                result.SourceFound = true;
            }
            else if (ScoreCodexHome(userProfilePath) >= 0)
            {
                result.SourcePath = userProfilePath;
                result.SourceFound = true;
            }
            else
            {
                int bestScore = int.MinValue;
                for (int index = 0; index < candidates.Count; index++)
                {
                    int markerScore = ScoreCodexHome(candidates[index]);
                    if (markerScore < 0)
                    {
                        continue;
                    }
                    int score = markerScore * 1000 - index;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        result.SourcePath = candidates[index];
                        result.SourceFound = true;
                    }
                }
            }

            if (!result.SourceFound)
            {
                result.SourcePath = userProfilePath;
                result.SourceReason = "未找到有效目录，请使用“浏览”确认";
            }
            else if (!string.IsNullOrWhiteSpace(environmentPath) && PathsEqual(result.SourcePath, environmentPath))
            {
                result.SourceReason = "由 CODEX_HOME 环境变量识别";
            }
            else if (PathsEqual(result.SourcePath, userProfilePath))
            {
                result.SourceReason = "由当前 Windows 用户目录识别";
            }
            else
            {
                result.SourceReason = "由常见 Codex 数据标记识别";
            }

            long largestSourceFile = 0;
            if (calculateSize && result.SourceFound)
            {
                result.SourceBytes = CalculateDirectoryBytes(result.SourcePath, out largestSourceFile);
            }

            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            string sourceRoot = Path.GetPathRoot(result.SourcePath);
            long requiredSpace = calculateSize
                ? result.SourceBytes + Math.Max(1024L * 1024L * 1024L, largestSourceFile)
                : 0L;
            DriveInfo recommendedDrive = null;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed ||
                        !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(drive.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(drive.RootDirectory.FullName, sourceRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (calculateSize && drive.AvailableFreeSpace < requiredSpace)
                    {
                        continue;
                    }
                    if (recommendedDrive == null || drive.AvailableFreeSpace > recommendedDrive.AvailableFreeSpace)
                    {
                        recommendedDrive = drive;
                    }
                }
                catch
                {
                }
            }

            if (recommendedDrive != null)
            {
                result.DestinationFound = true;
                result.DestinationPath = Path.Combine(
                    recommendedDrive.RootDirectory.FullName, "CodexData", ".codex");
                result.DestinationFreeBytes = recommendedDrive.AvailableFreeSpace;
                result.DestinationReason = string.Format("{0} 为 NTFS 固定磁盘，可用 {1}",
                    recommendedDrive.Name.TrimEnd('\\'), FormatBytes(recommendedDrive.AvailableFreeSpace));
            }
            else
            {
                result.DestinationReason = calculateSize
                    ? "没有找到空间足够的非 C 盘 NTFS 固定磁盘"
                    : "没有找到可推荐的非 C 盘 NTFS 固定磁盘";
            }
            return result;
        }

        private void ApplyRecommendation(PathRecommendation recommendation)
        {
            sourceText.Text = recommendation.SourcePath ?? string.Empty;
            destinationText.Text = recommendation.DestinationFound
                ? recommendation.DestinationPath
                : string.Empty;
            sourcePathLabel.Text = recommendation.SourceFound
                ? "当前 Codex 数据目录 · 已自动找到"
                : "当前 Codex 数据目录 · 未找到，请浏览";
            destinationPathLabel.Text = recommendation.DestinationFound
                ? string.Format("推荐的新位置 · {0}", recommendation.DestinationReason)
                : "新位置 · 未找到可推荐磁盘，请浏览";
            UpdateReuseDestinationOption();
        }

        private static void AddCandidate(ICollection<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return;
            }
            if (!candidates.Any(item => string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(fullPath);
            }
        }

        private static int ScoreCodexHome(string path)
        {
            if (!Directory.Exists(path))
            {
                return -1;
            }
            int score = 1;
            if (File.Exists(Path.Combine(path, "config.toml"))) score += 5;
            if (File.Exists(Path.Combine(path, "auth.json"))) score += 5;
            if (Directory.Exists(Path.Combine(path, "sessions"))) score += 4;
            try
            {
                if (Directory.EnumerateFiles(path, "state*.sqlite", SearchOption.TopDirectoryOnly).Any())
                {
                    score += 3;
                }
            }
            catch
            {
            }
            return score;
        }

        private static long CalculateDirectoryBytes(string rootPath, out long largestFile)
        {
            StorageUsage usage = CalculateStorageUsage(rootPath);
            largestFile = usage.LargestFile;
            return usage.LogicalBytes;
        }

        private static StorageUsage CalculateStorageUsage(string rootPath)
        {
            StorageUsage usage = new StorageUsage();
            Stack<string> pending = new Stack<string>();
            pending.Push(rootPath);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                try
                {
                    foreach (string filePath in Directory.EnumerateFiles(current))
                    {
                        try
                        {
                            FileInfo file = new FileInfo(filePath);
                            usage.LogicalBytes += file.Length;
                            usage.AllocatedBytes += NativeMethods.GetAllocatedFileSize(filePath, file.Length);
                            usage.LargestFile = Math.Max(usage.LargestFile, file.Length);
                            usage.FileCount++;
                        }
                        catch
                        {
                        }
                    }
                    foreach (string directoryPath in Directory.EnumerateDirectories(current))
                    {
                        try
                        {
                            if ((File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) == 0)
                            {
                                pending.Push(directoryPath);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            try
            {
                string physicalPath = NativeMethods.ResolveFinalPath(rootPath);
                SetStorageDrive(usage, physicalPath);
            }
            catch
            {
                SetStorageDrive(usage, rootPath);
            }
            return usage;
        }

        private static void SetStorageDrive(StorageUsage usage, string path)
        {
            try
            {
                string driveRoot = Path.GetPathRoot(path);
                if (string.IsNullOrWhiteSpace(driveRoot))
                {
                    return;
                }
                DriveInfo drive = new DriveInfo(driveRoot);
                if (!drive.IsReady)
                {
                    return;
                }
                usage.DriveName = drive.Name.TrimEnd('\\');
                usage.DriveTotalBytes = drive.TotalSize;
                usage.DriveFreeBytes = drive.AvailableFreeSpace;
            }
            catch
            {
            }
        }

        private static bool DirectoryHasEntries(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }
            try
            {
                return Directory.EnumerateFileSystemEntries(path).Any();
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(Path.GetFullPath(first).TrimEnd('\\'),
                    Path.GetFullPath(second).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void SetDestinationFreeSpace(PathRecommendation recommendation)
        {
            try
            {
                recommendation.DestinationFreeBytes = new DriveInfo(
                    Path.GetPathRoot(recommendation.DestinationPath)).AvailableFreeSpace;
            }
            catch
            {
            }
        }

        private async Task RunPreflightAsync()
        {
            await RunBusyAsync("预检", delegate(MigrationOptions options, CancellationToken token)
            {
                PreflightResult result = engine.Preflight(options, token);
                AppendLog(string.Format("预检通过：{0} 个文件，{1}，{2} 个 Junction。",
                    result.FileCount, FormatBytes(result.SourceBytes), result.JunctionCount));
                BeginInvoke((Action)delegate
                {
                    string warnings = result.Warnings.Count == 0 ? "无额外警告。" : string.Join("\r\n", result.Warnings);
                    MessageBox.Show(this,
                        string.Format("预检通过\r\n\r\n文件：{0:N0}\r\n数据量：{1}\r\n预计新增空间：{2}\r\n\r\n{3}",
                            result.FileCount, FormatBytes(result.SourceBytes),
                            FormatBytes(result.RequiredAdditionalBytes), warnings),
                        "预检结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            });
        }

        private async Task RunMigrationAsync()
        {
            DialogResult codexClosed = MessageBox.Show(this,
                "开始迁移前，请先完成以下操作：\r\n\r\n" +
                "1. 保存 Codex 和 ChatGPT 中正在进行的工作\r\n" +
                "2. 彻底退出 Codex 和 ChatGPT（包括后台窗口）\r\n" +
                "3. 保持本迁移程序开启\r\n\r\n" +
                "你是否已经关闭 Codex 和 ChatGPT？\r\n\r\n" +
                "选择“否”会安全返回，不会移动任何文件。",
                "开始迁移前确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (codexClosed != DialogResult.Yes)
            {
                AppendLog("用户尚未确认 Codex 已关闭，未开始迁移。");
                return;
            }

            MigrationRecord completedMigration = null;
            await RunBusyAsync("迁移", delegate(MigrationOptions options, CancellationToken token)
            {
                completedMigration = engine.RunMigration(options, token);
            });

            if (completedMigration != null)
            {
                using (MigrationSuccessDialog dialog = new MigrationSuccessDialog(
                    LoadMascot(), completedMigration.DestinationPath))
                {
                    dialog.Icon = Icon;
                    dialog.ShowDialog(this);
                }
            }
        }

        private async Task RunRollbackAsync()
        {
            if (MessageBox.Show(this,
                "迁回前必须彻底退出 Codex/ChatGPT。新磁盘副本会保留，不会直接删除。\r\n\r\n是否继续？",
                "确认迁回 C 盘", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            await RunBusyAsync("迁回", delegate(MigrationOptions options, CancellationToken token)
            {
                engine.Rollback(options, token);
            });
        }

        private async Task RunCleanupAsync()
        {
            DialogResult first = MessageBox.Show(this,
                "只有在你已经重新打开 Codex，并确认任务、图片和文件都正常时，才能释放 C 盘空间。\r\n\r\n" +
                "此操作会永久删除 C 盘安全备份，不会进入回收站。是否继续？",
                "释放 C 盘空间", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (first != DialogResult.Yes)
            {
                return;
            }
            DialogResult second = MessageBox.Show(this,
                "最后确认：Codex 已在新磁盘数据上正常运行，并且你接受删除 C 盘安全备份？",
                "最后确认", MessageBoxButtons.YesNo, MessageBoxIcon.Stop);
            if (second != DialogResult.Yes)
            {
                return;
            }
            await RunBusyAsync("清理", delegate(MigrationOptions options, CancellationToken token)
            {
                engine.DeleteSafetyBackup(options, token);
            });
        }

        private async Task RunBusyAsync(string operationName, Action<MigrationOptions, CancellationToken> action)
        {
            if (busy)
            {
                return;
            }
            busy = true;
            cancellation = new CancellationTokenSource();
            SetBusyControls(true);
            AppendLog(operationName + "开始。");
            MigrationOptions options = ReadOptions();

            try
            {
                await Task.Run(delegate { action(options, cancellation.Token); });
                AppendLog(operationName + "完成。");
            }
            catch (OperationCanceledException)
            {
                SetFailureDisplay("操作已取消；C 盘原目录未切换。已复制到目标的文件可在下次继续复用。");
                AppendLog(operationName + "已安全取消。");
            }
            catch (Exception error)
            {
                SetFailureDisplay(error.Message);
                AppendLog(operationName + "失败：" + error);
                MessageBox.Show(this, error.Message, operationName + "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (cancellation != null)
                {
                    cancellation.Dispose();
                    cancellation = null;
                }
                busy = false;
                SetBusyControls(false);
                RefreshRecordState();
            }
        }

        private MigrationOptions ReadOptions()
        {
            MigrationOptions options = new MigrationOptions();
            options.SourcePath = sourceText.Text;
            options.DestinationPath = destinationText.Text;
            options.VerifySha256 = verifyHashCheck.Checked;
            options.AllowExistingDestination = reuseDestinationCheck.Checked;
            return options;
        }

        private void RefreshRecordState()
        {
            try
            {
                MigrationRecord record = engine.LoadRecord(null);
                bool hasRecord = record != null;
                rollbackButton.Enabled = !busy && hasRecord;
                cleanupButton.Enabled = !busy && hasRecord && !record.BackupDeleted;
                rollbackButton.Visible = !busy && hasRecord;
                cleanupButton.Visible = !busy && hasRecord && !record.BackupDeleted;
                if (hasRecord)
                {
                    sourceText.Text = record.SourcePath;
                    destinationText.Text = record.DestinationPath;
                    if (!busy)
                    {
                        stageLabel.ForeColor = textPrimary;
                        stageLabel.Text = record.BackupDeleted ? "迁移已完成，C 盘空间已释放" : "迁移已完成，等待验证";
                        detailLabel.Text = record.BackupDeleted
                            ? "数据通过原路径 Junction 使用新磁盘目录。"
                            : "确认 Codex 正常后，可释放 C 盘安全备份。";
                        progressBar.Value = 100;
                        progressBar.Active = false;
                        percentLabel.Text = "100%";
                    }
                }
            }
            catch (Exception error)
            {
                AppendLog("读取迁移记录失败：" + error.Message);
            }
        }

        private void EngineProgressChanged(MigrationProgress progress)
        {
            if (IsDisposed)
            {
                return;
            }
            BeginInvoke((Action)delegate
            {
                progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, progress.Percent));
                progressBar.Active = progress.Stage != MigrationStage.Idle &&
                    progress.Stage != MigrationStage.Completed &&
                    progress.Stage != MigrationStage.Failed &&
                    progress.Stage != MigrationStage.WaitingForCodex;
                percentLabel.Text = progress.Percent + "%";
                stageLabel.Text = StageTitle(progress.Stage);
                detailLabel.Text = progress.Message;
                if (progress.TotalBytes > 0)
                {
                    countersLabel.Text = string.Format("{0} / {1}    文件 {2:N0} / {3:N0}",
                        FormatBytes(progress.ProcessedBytes), FormatBytes(progress.TotalBytes),
                        progress.ProcessedFiles, progress.TotalFiles);
                }
                else if (progress.TotalFiles > 0)
                {
                    countersLabel.Text = string.Format("文件 {0:N0} / {1:N0}", progress.ProcessedFiles, progress.TotalFiles);
                }
                else
                {
                    countersLabel.Text = string.Empty;
                }
            });
        }

        private void SetFailureDisplay(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetFailureDisplay(message)));
                return;
            }
            stageLabel.Text = "未完成";
            stageLabel.ForeColor = Color.FromArgb(255, 153, 164);
            detailLabel.Text = message;
            progressBar.Active = false;
        }

        private void SetBusyControls(bool isBusy)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetBusyControls(isBusy)));
                return;
            }

            MigrationRecord record = engine.LoadRecord(null);
            sourceText.Enabled = !isBusy;
            destinationText.Enabled = !isBusy;
            sourceBrowseButton.Enabled = !isBusy;
            destinationBrowseButton.Enabled = !isBusy;
            autoRecommendButton.Enabled = !isBusy;
            verifyHashCheck.Enabled = !isBusy;
            reuseDestinationCheck.Enabled = !isBusy;
            preflightButton.Enabled = !isBusy;
            migrateButton.Enabled = !isBusy;
            rollbackButton.Enabled = !isBusy && record != null;
            cleanupButton.Enabled = !isBusy && record != null && !record.BackupDeleted;
            cancelButton.Enabled = isBusy;
            rollbackButton.Visible = !isBusy && record != null;
            cleanupButton.Visible = !isBusy && record != null && !record.BackupDeleted;
            cancelButton.Visible = isBusy;
            if (!isBusy)
            {
                progressBar.Active = false;
                UpdateReuseDestinationOption();
                ScheduleStorageScan();
            }
            if (isBusy)
            {
                stageLabel.ForeColor = textPrimary;
            }
        }

        private static void RotateLogIfNeeded(string currentLogPath)
        {
            const long MaximumLogBytes = 5L * 1024L * 1024L;
            try
            {
                FileInfo current = new FileInfo(currentLogPath);
                if (!current.Exists || current.Length < MaximumLogBytes)
                {
                    return;
                }

                string previousLogPath = Path.Combine(current.DirectoryName, "previous.log");
                if (File.Exists(previousLogPath))
                {
                    File.Delete(previousLogPath);
                }
                File.Move(currentLogPath, previousLogPath);
            }
            catch
            {
                // Logging must never prevent the recovery tool from opening.
            }
        }

        private void AppendLog(string message)
        {
            string timestamped = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message);
            lock (logLock)
            {
                try
                {
                    File.AppendAllText(logPath, timestamped + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                }
            }
            if (logBox == null || logBox.IsDisposed)
            {
                return;
            }
            if (logBox.InvokeRequired)
            {
                logBox.BeginInvoke((Action)(() => AppendLogToBox(timestamped)));
            }
            else
            {
                AppendLogToBox(timestamped);
            }
        }

        private void AppendLogToBox(string message)
        {
            logBox.AppendText(message + Environment.NewLine);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void BrowseForFolder(TextBox target)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择目录";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(target.Text))
                {
                    dialog.SelectedPath = target.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.SelectedPath;
                    if (target == destinationText)
                    {
                        UpdateReuseDestinationOption();
                    }
                }
            }
        }

        private Image LoadMascot()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexHomeMover.Mascot.png"))
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
            catch
            {
                return null;
            }
        }

        private static string StageTitle(MigrationStage stage)
        {
            switch (stage)
            {
                case MigrationStage.Preflight: return "正在预检";
                case MigrationStage.WaitingForCodex: return "等待 Codex 退出";
                case MigrationStage.Copying: return "正在复制数据";
                case MigrationStage.Reconciling: return "正在核对目标";
                case MigrationStage.Verifying: return "正在校验文件";
                case MigrationStage.CheckingDatabases: return "正在检查数据库";
                case MigrationStage.SecuringPermissions: return "正在设置安全权限";
                case MigrationStage.Switching: return "正在安全切换";
                case MigrationStage.RollingBack: return "正在迁回 C 盘";
                case MigrationStage.CleaningUp: return "正在释放 C 盘空间";
                case MigrationStage.Completed: return "操作完成";
                case MigrationStage.Failed: return "未完成";
                default: return "准备就绪";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return string.Format("{0:0.##} {1}", value, units[unit]);
        }
    }
}
