using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexHomeMover
{
    internal sealed class MigrationSuccessDialog : Form
    {
        private readonly Color appBackground = Color.FromArgb(23, 22, 20);
        private readonly Color cardBackground = Color.FromArgb(42, 40, 36);
        private readonly Color textPrimary = Color.FromArgb(248, 246, 239);
        private readonly Color textSecondary = Color.FromArgb(197, 205, 212);
        private readonly Color textMuted = Color.FromArgb(145, 139, 126);
        private readonly Color brandYellow = Color.FromArgb(255, 199, 39);
        private readonly Color brandOrange = Color.FromArgb(255, 166, 25);
        private Image mascotImage;

        internal MigrationSuccessDialog(Image mascot, string destinationPath)
        {
            mascotImage = mascot;
            Text = "迁移完成";
            ClientSize = new Size(620, 420);
            BackColor = appBackground;
            ForeColor = textPrimary;
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BuildInterface(destinationPath);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowStyling.Apply(this);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds, Color.FromArgb(30, 28, 24), appBackground, LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(background, bounds);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawConfetti(e.Graphics, 226, 25, brandYellow, 5);
            DrawConfetti(e.Graphics, 573, 31, brandOrange, 4);
            DrawConfetti(e.Graphics, 594, 115, brandYellow, 3);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && mascotImage != null)
            {
                mascotImage.Dispose();
                mascotImage = null;
            }
            base.Dispose(disposing);
        }

        private void BuildInterface(string destinationPath)
        {
            FluentPanel mascotCard = new FluentPanel();
            mascotCard.Location = new Point(24, 28);
            mascotCard.Size = new Size(190, 278);
            mascotCard.FillColor = Color.FromArgb(48, 42, 27);
            mascotCard.GradientColor = Color.FromArgb(72, 57, 22);
            mascotCard.BorderColor = Color.FromArgb(126, 99, 24);
            mascotCard.CornerRadius = 20;
            Controls.Add(mascotCard);

            PictureBox mascot = new PictureBox();
            mascot.Location = new Point(20, 18);
            mascot.Size = new Size(150, 198);
            mascot.SizeMode = PictureBoxSizeMode.Zoom;
            mascot.BackColor = Color.Transparent;
            mascot.Image = mascotImage;
            mascotCard.Controls.Add(mascot);

            Label homeBadge = CreateLabel("安全到家 · 100%", 10F, FontStyle.Bold, brandYellow);
            homeBadge.Location = new Point(16, 227);
            homeBadge.Size = new Size(158, 32);
            homeBadge.TextAlign = ContentAlignment.MiddleCenter;
            mascotCard.Controls.Add(homeBadge);

            Label title = CreateLabel("小鱼搬家成功啦！", 22F, FontStyle.Bold, textPrimary);
            title.Location = new Point(238, 36);
            title.Size = new Size(350, 46);
            Controls.Add(title);

            Label subtitle = CreateLabel(
                "复制、校验与路径切换已完成，C 盘原路径保持不变。",
                10F, FontStyle.Regular, textSecondary);
            subtitle.Location = new Point(240, 84);
            subtitle.Size = new Size(345, 44);
            Controls.Add(subtitle);

            Label copied = CreateLabel("✓  复制及所选校验已完成", 10F, FontStyle.Bold, brandYellow);
            copied.Location = new Point(240, 137);
            copied.Size = new Size(340, 25);
            Controls.Add(copied);

            Label backup = CreateLabel("✓  C 盘安全备份仍然保留", 10F, FontStyle.Regular, textSecondary);
            backup.Location = new Point(240, 166);
            backup.Size = new Size(340, 25);
            Controls.Add(backup);

            FluentPanel destinationCard = new FluentPanel();
            destinationCard.Location = new Point(236, 202);
            destinationCard.Size = new Size(350, 82);
            destinationCard.FillColor = cardBackground;
            destinationCard.GradientColor = Color.FromArgb(48, 45, 39);
            destinationCard.BorderColor = Color.FromArgb(79, 73, 62);
            destinationCard.CornerRadius = 12;
            Controls.Add(destinationCard);

            Label destinationCaption = CreateLabel("小鱼的新家", 8.5F, FontStyle.Bold, textMuted);
            destinationCaption.Location = new Point(16, 11);
            destinationCaption.Size = new Size(310, 21);
            destinationCard.Controls.Add(destinationCaption);

            Label destination = CreateLabel(
                string.IsNullOrWhiteSpace(destinationPath) ? "新磁盘 Codex 数据目录" : destinationPath,
                10F, FontStyle.Regular, textPrimary);
            destination.Location = new Point(16, 37);
            destination.Size = new Size(318, 30);
            destination.AutoEllipsis = true;
            destination.TextAlign = ContentAlignment.MiddleLeft;
            destination.AccessibleDescription = destinationPath;
            destinationCard.Controls.Add(destination);

            FluentPanel nextStepCard = new FluentPanel();
            nextStepCard.Location = new Point(24, 326);
            nextStepCard.Size = new Size(372, 66);
            nextStepCard.FillColor = Color.FromArgb(37, 35, 31);
            nextStepCard.GradientColor = Color.FromArgb(43, 39, 29);
            nextStepCard.BorderColor = Color.FromArgb(105, 83, 24);
            nextStepCard.CornerRadius = 12;
            Controls.Add(nextStepCard);

            Label nextStep = CreateLabel(
                "下一步：打开 Codex 检查旧任务与文件；确认正常后，再释放 C 盘空间。",
                9F, FontStyle.Regular, textSecondary);
            nextStep.Location = new Point(16, 10);
            nextStep.Size = new Size(340, 47);
            nextStepCard.Controls.Add(nextStep);

            FluentButton doneButton = new FluentButton();
            doneButton.Text = "好耶，去验证";
            doneButton.Location = new Point(410, 334);
            doneButton.Size = new Size(176, 50);
            doneButton.NormalColor = brandYellow;
            doneButton.HoverColor = Color.FromArgb(255, 217, 86);
            doneButton.PressedColor = brandOrange;
            doneButton.BorderColor = Color.FromArgb(255, 229, 137);
            doneButton.DisabledColor = Color.FromArgb(82, 70, 36);
            doneButton.ForeColor = Color.FromArgb(35, 29, 10);
            doneButton.Font = new Font("Microsoft YaHei UI", 13.33F, FontStyle.Bold, GraphicsUnit.Pixel);
            doneButton.Icon = FluentIcon.CheckCircle;
            doneButton.CornerRadius = 9;
            doneButton.DialogResult = DialogResult.OK;
            Controls.Add(doneButton);
            AcceptButton = doneButton;
            CancelButton = doneButton;
        }

        private Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Microsoft YaHei UI", size * 1.3333F, style, GraphicsUnit.Pixel);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }

        private static void DrawConfetti(Graphics graphics, int x, int y, Color color, int size)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillEllipse(brush, x, y, size, size);
                graphics.FillEllipse(brush, x + 12, y + 9, Math.Max(2, size - 1), Math.Max(2, size - 1));
            }
        }
    }
}
