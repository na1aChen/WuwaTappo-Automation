using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WuwaTappoPortable
{
    internal static class CodexPalette
    {
        public static readonly Color Background = Color.FromArgb(11, 13, 16);
        public static readonly Color Black = Color.Black;
        public static readonly Color Surface = Color.FromArgb(20, 23, 28);
        public static readonly Color SurfaceRaised = Color.FromArgb(27, 31, 38);
        public static readonly Color Border = Color.FromArgb(48, 55, 65);
        public static readonly Color Text = Color.FromArgb(242, 245, 247);
        public static readonly Color Muted = Color.FromArgb(145, 155, 168);
        public static readonly Color Accent = Color.FromArgb(93, 218, 184);
        public static readonly Color AccentDark = Color.FromArgb(53, 126, 108);
        public static readonly Color Error = Color.FromArgb(255, 112, 118);
        public static readonly Color Warning = Color.FromArgb(244, 188, 103);
    }

    internal static class CodexWindowTheme
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            IntPtr windowHandle,
            string subApplicationName,
            string subIdList);

        public static void Apply(Control root)
        {
            if (root == null)
                return;
            ApplyToControl(root, true);
            foreach (Control child in root.Controls)
                Apply(child);
        }

        private static void ApplyToControl(Control control, bool form)
        {
            if (!control.IsHandleCreated)
                return;
            try
            {
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
            }
            if (!form)
                return;

            int enabled = 1;
            try
            {
                if (DwmSetWindowAttribute(
                    control.Handle,
                    DwmUseImmersiveDarkMode,
                    ref enabled,
                    sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(
                        control.Handle,
                        DwmUseImmersiveDarkModeBefore20H1,
                        ref enabled,
                        sizeof(int));
                }
            }
            catch
            {
            }
        }
    }

    internal static class CodexFonts
    {
        public static Font Ui(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static Font Mono(float size, FontStyle style)
        {
            string family = "Cascadia Mono";
            try
            {
                if (!FontFamily.Families.Any(delegate(FontFamily item) {
                    return String.Equals(item.Name, "Cascadia Mono", StringComparison.OrdinalIgnoreCase);
                }))
                {
                    family = "Consolas";
                }
            }
            catch
            {
                family = "Consolas";
            }
            return new Font(family, size, style, GraphicsUnit.Point);
        }
    }

    internal class RoundedPanel : Panel
    {
        private int cornerRadius = 14;
        private Color fillColor = CodexPalette.Surface;
        private Color borderColor = CodexPalette.Border;

        public int CornerRadius
        {
            get { return cornerRadius; }
            set { cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        public Color FillColor
        {
            get { return fillColor; }
            set
            {
                fillColor = value;
                if (BackColor != value)
                    base.BackColor = value;
                Invalidate();
            }
        }

        public Color BorderColor
        {
            get { return borderColor; }
            set { borderColor = value; Invalidate(); }
        }

        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = CodexPalette.Background;
            Padding = new Padding(16);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? CodexPalette.Background : Parent.BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundedPath(rect, cornerRadius))
            using (SolidBrush brush = new SolidBrush(fillColor))
            using (Pen pen = new Pen(borderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        internal static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Min(Math.Max(radius * 2, 1), Math.Min(rect.Width, rect.Height));
            if (diameter <= 1)
            {
                path.AddRectangle(rect);
                return path;
            }

            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class CodexTableLayoutPanel : TableLayoutPanel
    {
        public CodexTableLayoutPanel()
        {
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);
            DoubleBuffered = true;
        }
    }

    internal sealed class CodexFlowLayoutPanel : FlowLayoutPanel
    {
        public CodexFlowLayoutPanel()
        {
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);
            DoubleBuffered = true;
        }
    }

    internal sealed class CodexButton : Button
    {
        private bool hovering;

        public Color FillColor = CodexPalette.SurfaceRaised;
        public Color HoverColor = Color.FromArgb(35, 41, 49);
        public Color OutlineColor = CodexPalette.Border;
        public Color TextColor = CodexPalette.Text;

        public CodexButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Height = 40;
            Padding = new Padding(14, 0, 14, 0);
            Font = CodexFonts.Ui(9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovering = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? CodexPalette.Background : Parent.BackColor);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Enabled ? (hovering ? HoverColor : FillColor) : Color.FromArgb(28, 32, 38);
            Color outline = Enabled ? OutlineColor : Color.FromArgb(42, 48, 56);
            using (GraphicsPath path = RoundedPanel.CreateRoundedPath(rect, 9))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(outline))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            Color textColor = Enabled ? TextColor : CodexPalette.Muted;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class CodexMetricCard : RoundedPanel
    {
        private readonly Label captionLabel;
        private readonly Label valueLabel;

        public CodexMetricCard(string caption, int width)
        {
            Width = width;
            Height = 88;
            Margin = new Padding(0, 0, 12, 12);
            Padding = new Padding(14, 12, 14, 10);
            FillColor = CodexPalette.Surface;
            BorderColor = CodexPalette.Border;

            captionLabel = new Label();
            captionLabel.Text = caption.ToUpperInvariant();
            captionLabel.ForeColor = CodexPalette.Muted;
            captionLabel.BackColor = FillColor;
            captionLabel.Font = CodexFonts.Mono(8f, FontStyle.Bold);
            captionLabel.AutoSize = true;
            captionLabel.Location = new Point(14, 11);
            Controls.Add(captionLabel);

            valueLabel = new Label();
            valueLabel.Text = "-";
            valueLabel.ForeColor = CodexPalette.Text;
            valueLabel.BackColor = FillColor;
            valueLabel.Font = CodexFonts.Ui(14f, FontStyle.Bold);
            valueLabel.AutoEllipsis = true;
            valueLabel.AutoSize = false;
            valueLabel.Location = new Point(14, 38);
            valueLabel.Size = new Size(Math.Max(40, width - 28), 32);
            valueLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(valueLabel);
        }

        public void SetValue(string value, Color color)
        {
            valueLabel.Text = value;
            valueLabel.ForeColor = color;
        }
    }

    internal sealed class CodexStatusDot : Control
    {
        private Color dotColor = CodexPalette.Warning;

        public Color DotColor
        {
            get { return dotColor; }
            set { dotColor = value; Invalidate(); }
        }

        public CodexStatusDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(12, 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? CodexPalette.Background : Parent.BackColor);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(55, dotColor)))
            using (SolidBrush core = new SolidBrush(dotColor))
            {
                e.Graphics.FillEllipse(glow, 0, 0, Width, Height);
                e.Graphics.FillEllipse(core, 3, 3, Width - 6, Height - 6);
            }
        }
    }

    internal sealed class CodexHistoryView : ListView
    {
        private static readonly Font HeaderFont =
            CodexFonts.Ui(8.5f, FontStyle.Bold);

        public CodexHistoryView()
        {
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);
            DoubleBuffered = true;
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            OwnerDraw = true;
            MultiSelect = false;
            BorderStyle = BorderStyle.None;
            VirtualMode = true;
            VirtualListSize = 0;
            BackColor = CodexPalette.Black;
            ForeColor = CodexPalette.Text;
            Font = CodexFonts.Ui(9f, FontStyle.Regular);
            Columns.Add("时间", 126, HorizontalAlignment.Left);
            Columns.Add("物品", 360, HorizontalAlignment.Left);
            Columns.Add("位置", 78, HorizontalAlignment.Left);
            Columns.Add("结果", 90, HorizontalAlignment.Left);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Columns.Count != 4)
                return;
            int itemWidth = Math.Max(150, ClientSize.Width - Columns[0].Width -
                Columns[2].Width - Columns[3].Width - 2);
            Columns[1].Width = itemWidth;
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(CodexPalette.SurfaceRaised))
                e.Graphics.FillRectangle(background, e.Bounds);
            using (Pen border = new Pen(CodexPalette.Border))
                e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(
                e.Graphics,
                e.Header.Text,
                HeaderFont,
                e.Bounds,
                CodexPalette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            Color background = e.Item.Selected
                ? Color.FromArgb(35, 50, 46)
                : (e.ItemIndex % 2 == 0
                    ? CodexPalette.Black
                    : Color.FromArgb(8, 9, 11));
            using (SolidBrush brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (Pen separator = new Pen(Color.FromArgb(27, 31, 36)))
                e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            string text = e.SubItem.Text ?? "";
            Color color = e.ColumnIndex == 3 &&
                (text == "已开启" || text == "开箱成功")
                ? CodexPalette.Accent
                : (text == "结果未知" ? CodexPalette.Warning : CodexPalette.Text);
            Rectangle bounds = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                bounds,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class CodexMainForm : Form
    {
        private readonly CodexMetricCard workerCard;
        private readonly CodexMetricCard phaseCard;
        private readonly CodexMetricCard characterCard;
        private readonly CodexMetricCard activityCard;
        private readonly CodexMetricCard chestCard;
        private readonly CodexMetricCard lastOpenCard;
        private Label statusText;
        private CodexStatusDot statusDot;
        private readonly Label messageLabel;
        private readonly Label subMessageLabel;
        private readonly CodexHistoryView historyView;
        private readonly CodexButton startButton;
        private readonly CodexButton stopButton;
        private readonly CodexButton refreshButton;
        private readonly CodexButton galleryButton;
        private readonly CodexButton readmeButton;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private bool busy;
        private string historySignature = "";
        private string statusSignature = "";
        private bool lastRunningState;
        private DateTime lastLivenessCheckUtc = DateTime.MinValue;
        private List<HistoryEntry> historyEntries = new List<HistoryEntry>();

        public CodexMainForm()
        {
            Text = "Wuwa Tappo 自动助手";
            BackColor = CodexPalette.Background;
            ForeColor = CodexPalette.Text;
            Font = CodexFonts.Ui(9.5f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 760);
            MinimumSize = new Size(740, 700);
            MaximizeBox = true;
            DoubleBuffered = true;

            TableLayoutPanel root = new CodexTableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = CodexPalette.Background;
            root.Padding = new Padding(26, 22, 26, 20);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 216f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);

            FlowLayoutPanel metrics = new CodexFlowLayoutPanel();
            metrics.Dock = DockStyle.Fill;
            metrics.AutoScroll = true;
            metrics.WrapContents = true;
            metrics.FlowDirection = FlowDirection.LeftToRight;
            metrics.BackColor = CodexPalette.Background;
            metrics.Padding = new Padding(0, 14, 0, 8);
            root.Controls.Add(metrics, 0, 1);

            workerCard = new CodexMetricCard("工作进程", 170);
            phaseCard = new CodexMetricCard("运行阶段", 190);
            characterCard = new CodexMetricCard("字符数", 180);
            activityCard = new CodexMetricCard("自动计数", 190);
            chestCard = new CodexMetricCard("待开宝箱", 160);
            lastOpenCard = new CodexMetricCard("最近开箱", 220);
            metrics.Controls.Add(workerCard);
            metrics.Controls.Add(phaseCard);
            metrics.Controls.Add(characterCard);
            metrics.Controls.Add(activityCard);
            metrics.Controls.Add(chestCard);
            metrics.Controls.Add(lastOpenCard);

            RoundedPanel messagePanel = new RoundedPanel();
            messagePanel.Dock = DockStyle.Top;
            messagePanel.Height = 92;
            messagePanel.Padding = new Padding(17, 13, 17, 13);
            messagePanel.FillColor = CodexPalette.Black;
            messagePanel.BorderColor = CodexPalette.Border;
            root.Controls.Add(messagePanel, 0, 2);

            Label messageCaption = new Label();
            messageCaption.Text = "状态消息";
            messageCaption.ForeColor = CodexPalette.Muted;
            messageCaption.BackColor = CodexPalette.Black;
            messageCaption.Font = CodexFonts.Mono(8f, FontStyle.Bold);
            messageCaption.AutoSize = true;
            messageCaption.Location = new Point(17, 13);
            messagePanel.Controls.Add(messageCaption);

            messageLabel = new Label();
            messageLabel.Text = "正在读取状态";
            messageLabel.ForeColor = CodexPalette.Text;
            messageLabel.BackColor = CodexPalette.Black;
            messageLabel.Font = CodexFonts.Ui(10.5f, FontStyle.Bold);
            messageLabel.AutoEllipsis = true;
            messageLabel.AutoSize = false;
            messageLabel.Location = new Point(17, 36);
            messageLabel.Height = 23;
            messageLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            messagePanel.Controls.Add(messageLabel);

            subMessageLabel = new Label();
            subMessageLabel.Text = "";
            subMessageLabel.ForeColor = CodexPalette.Muted;
            subMessageLabel.BackColor = CodexPalette.Black;
            subMessageLabel.Font = CodexFonts.Mono(8.5f, FontStyle.Regular);
            subMessageLabel.AutoEllipsis = true;
            subMessageLabel.AutoSize = false;
            subMessageLabel.Location = new Point(17, 61);
            subMessageLabel.Height = 20;
            subMessageLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            messagePanel.Controls.Add(subMessageLabel);

            RoundedPanel historyPanel = new RoundedPanel();
            historyPanel.Dock = DockStyle.Fill;
            historyPanel.MinimumSize = new Size(0, 130);
            historyPanel.Margin = new Padding(0, 8, 0, 0);
            historyPanel.Padding = new Padding(14, 10, 14, 12);
            historyPanel.FillColor = CodexPalette.Black;
            historyPanel.BorderColor = CodexPalette.Border;
            root.Controls.Add(historyPanel, 0, 3);

            TableLayoutPanel historyLayout = new CodexTableLayoutPanel();
            historyLayout.Dock = DockStyle.Fill;
            historyLayout.Margin = new Padding(0);
            historyLayout.Padding = new Padding(0);
            historyLayout.ColumnCount = 1;
            historyLayout.RowCount = 2;
            historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            historyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            historyLayout.BackColor = CodexPalette.Black;
            historyPanel.Controls.Add(historyLayout);

            Label historyCaption = new Label();
            historyCaption.Text = "自动开箱历史  ·  最近 100 条  ·  跨 Worker 与 App 重启保存";
            historyCaption.AutoSize = false;
            historyCaption.Height = 20;
            historyCaption.Dock = DockStyle.Top;
            historyCaption.AutoEllipsis = true;
            historyCaption.ForeColor = CodexPalette.Muted;
            historyCaption.BackColor = CodexPalette.Black;
            historyCaption.Font = CodexFonts.Ui(8.5f, FontStyle.Bold);
            historyCaption.Margin = new Padding(2, 0, 0, 6);
            historyLayout.Controls.Add(historyCaption, 0, 0);

            historyView = new CodexHistoryView();
            historyView.Dock = DockStyle.Fill;
            historyView.Margin = new Padding(0);
            historyView.RetrieveVirtualItem += RetrieveHistoryItem;
            historyLayout.Controls.Add(historyView, 0, 1);

            FlowLayoutPanel actions = new CodexFlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.AutoSize = true;
            actions.WrapContents = true;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.BackColor = CodexPalette.Background;
            actions.Padding = new Padding(0, 14, 0, 4);
            root.Controls.Add(actions, 0, 4);

            startButton = CreateActionButton("启动后台", 126, true);
            stopButton = CreateActionButton("停止后台", 126, false);
            refreshButton = CreateActionButton("刷新状态", 112, false);
            galleryButton = CreateActionButton("打开全图鉴", 126, false);
            readmeButton = CreateActionButton("使用说明", 112, false);
            actions.Controls.Add(startButton);
            actions.Controls.Add(stopButton);
            actions.Controls.Add(refreshButton);
            actions.Controls.Add(galleryButton);
            actions.Controls.Add(readmeButton);

            startButton.Click += delegate { ExecuteAction("start"); };
            stopButton.Click += delegate { ExecuteAction("stop"); };
            refreshButton.Click += delegate { RefreshStatus(); };
            galleryButton.Click += delegate { OpenSafely(PortableRuntime.OpenGallery); };
            readmeButton.Click += delegate { OpenSafely(PortableRuntime.OpenReadme); };

            Label footer = new Label();
            footer.Text = "关闭此窗口不会停止后台  ·  启动后台时需允许管理员权限以自动补次数";
            footer.ForeColor = CodexPalette.Muted;
            footer.BackColor = CodexPalette.Background;
            footer.Font = CodexFonts.Mono(8f, FontStyle.Regular);
            footer.AutoEllipsis = true;
            footer.Dock = DockStyle.Fill;
            footer.Height = 26;
            footer.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(footer, 0, 5);

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 3000;
            refreshTimer.Tick += delegate {
                if (!Visible || WindowState == FormWindowState.Minimized)
                    return;
                RefreshStatus();
            };
            refreshTimer.Start();

            Resize += delegate {
                messageLabel.Width = Math.Max(100, messagePanel.ClientSize.Width - 34);
                subMessageLabel.Width = Math.Max(100, messagePanel.ClientSize.Width - 34);
            };
            Activated += delegate {
                if (!busy)
                    RefreshStatus();
            };
            Shown += delegate { CodexWindowTheme.Apply(this); };
            FormClosed += delegate {
                refreshTimer.Stop();
                refreshTimer.Dispose();
            };
            RefreshStatus();
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new CodexTableLayoutPanel();
            header.AutoSize = true;
            header.Dock = DockStyle.Top;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.BackColor = CodexPalette.Background;

            FlowLayoutPanel identity = new CodexFlowLayoutPanel();
            identity.AutoSize = true;
            identity.BackColor = CodexPalette.Background;
            identity.WrapContents = false;
            identity.FlowDirection = FlowDirection.LeftToRight;
            identity.Dock = DockStyle.Fill;
            identity.Padding = new Padding(0, 1, 0, 0);

            RoundedPanel mark = new RoundedPanel();
            mark.Width = 46;
            mark.Height = 46;
            mark.Margin = new Padding(0, 0, 15, 0);
            mark.FillColor = CodexPalette.Accent;
            mark.BorderColor = CodexPalette.Accent;
            mark.Padding = new Padding(0);
            Label markText = new Label();
            markText.Text = "W";
            markText.Dock = DockStyle.Fill;
            markText.TextAlign = ContentAlignment.MiddleCenter;
            markText.ForeColor = Color.FromArgb(7, 24, 20);
            markText.BackColor = CodexPalette.Accent;
            markText.Font = CodexFonts.Ui(18f, FontStyle.Bold);
            mark.Controls.Add(markText);
            identity.Controls.Add(mark);

            TableLayoutPanel titles = new CodexTableLayoutPanel();
            titles.AutoSize = true;
            titles.RowCount = 2;
            titles.ColumnCount = 1;
            titles.Margin = new Padding(0);
            titles.BackColor = CodexPalette.Background;
            Label title = new Label();
            title.Text = "Wuwa Tappo";
            title.ForeColor = CodexPalette.Text;
            title.BackColor = CodexPalette.Background;
            title.Font = CodexFonts.Ui(20f, FontStyle.Bold);
            title.AutoSize = true;
            titles.Controls.Add(title, 0, 0);
            Label subtitle = new Label();
            subtitle.Text = "自动化控制台";
            subtitle.ForeColor = CodexPalette.Muted;
            subtitle.BackColor = CodexPalette.Background;
            subtitle.Font = CodexFonts.Mono(8.5f, FontStyle.Regular);
            subtitle.AutoSize = true;
            titles.Controls.Add(subtitle, 0, 1);
            identity.Controls.Add(titles);
            header.Controls.Add(identity, 0, 0);

            FlowLayoutPanel chip = new CodexFlowLayoutPanel();
            chip.AutoSize = true;
            chip.WrapContents = false;
            chip.FlowDirection = FlowDirection.LeftToRight;
            chip.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            chip.Margin = new Padding(16, 10, 0, 0);
            chip.BackColor = CodexPalette.Background;
            statusDot = new CodexStatusDot();
            statusDot.Margin = new Padding(7, 5, 8, 0);
            chip.Controls.Add(statusDot);
            statusText = new Label();
            statusText.Text = "未运行";
            statusText.AutoSize = true;
            statusText.ForeColor = CodexPalette.Warning;
            statusText.BackColor = CodexPalette.Background;
            statusText.Font = CodexFonts.Mono(9f, FontStyle.Bold);
            statusText.Margin = new Padding(0, 2, 5, 0);
            chip.Controls.Add(statusText);
            header.Controls.Add(chip, 1, 0);
            return header;
        }

        private static CodexButton CreateActionButton(string text, int width, bool primary)
        {
            CodexButton button = new CodexButton();
            button.Text = text;
            button.Width = width;
            button.Margin = new Padding(0, 0, 10, 8);
            if (primary)
            {
                button.FillColor = Color.FromArgb(32, 82, 70);
                button.HoverColor = Color.FromArgb(38, 101, 85);
                button.OutlineColor = CodexPalette.AccentDark;
                button.TextColor = Color.FromArgb(173, 248, 226);
            }
            else
            {
                button.FillColor = CodexPalette.SurfaceRaised;
                button.HoverColor = Color.FromArgb(36, 42, 51);
                button.OutlineColor = CodexPalette.Border;
                button.TextColor = CodexPalette.Text;
            }
            return button;
        }

        private void ExecuteAction(string action)
        {
            if (busy)
                return;
            busy = true;
            SetButtonsEnabled(false);
            statusText.Text = action == "start" ? "正在启动" : "正在停止";
            statusText.ForeColor = CodexPalette.Warning;
            statusDot.DotColor = CodexPalette.Warning;

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                int exitCode = action == "start"
                    ? PortableRuntime.StartWorker(out error)
                    : PortableRuntime.StopWorker(out error);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        busy = false;
                        SetButtonsEnabled(true);
                        statusSignature = "";
                        RefreshStatus();
                        if (exitCode != 0)
                        {
                            MessageBox.Show(
                                error,
                                "操作失败",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }));
                }
                catch
                {
                }
            });
        }

        private void SetButtonsEnabled(bool enabled)
        {
            startButton.Enabled = enabled;
            stopButton.Enabled = enabled;
            refreshButton.Enabled = enabled;
            galleryButton.Enabled = enabled;
            readmeButton.Enabled = enabled;
        }

        private void RefreshStatus()
        {
            string signature = GetFileSignature(PortableRuntime.StatusFile);
            DateTime nowUtc = DateTime.UtcNow;
            if (signature == statusSignature &&
                nowUtc - lastLivenessCheckUtc < TimeSpan.FromSeconds(30))
            {
                RefreshHistory();
                return;
            }
            bool runningNow = PortableRuntime.IsWorkerRunning();
            lastLivenessCheckUtc = nowUtc;
            if (signature == statusSignature && runningNow == lastRunningState)
            {
                RefreshHistory();
                return;
            }
            statusSignature = signature;

            StatusSnapshot snapshot = PortableRuntime.ReadStatus();
            lastRunningState = snapshot.Running;
            statusText.Text = snapshot.Running ? "运行中" : "未运行";
            statusText.ForeColor = snapshot.Running ? CodexPalette.Accent : CodexPalette.Warning;
            statusDot.DotColor = snapshot.Running ? CodexPalette.Accent : CodexPalette.Warning;

            workerCard.SetValue(snapshot.Running ? "运行中" : "已停止",
                snapshot.Running ? CodexPalette.Accent : CodexPalette.Warning);
            phaseCard.SetValue(TranslatePhase(snapshot.Phase), CodexPalette.Text);
            characterCard.SetValue(snapshot.CharacterCount, CodexPalette.Text);
            activityCard.SetValue(
                snapshot.ActivityEnabled == "True" ? "启用" : "暂停",
                snapshot.ActivityEnabled == "True" ? CodexPalette.Accent : CodexPalette.Muted);
            chestCard.SetValue(snapshot.RewardButtons, CodexPalette.Text);
            string lastOpen = String.IsNullOrWhiteSpace(snapshot.ItemName)
                ? (String.IsNullOrWhiteSpace(snapshot.LastAction)
                    ? "无"
                    : TranslateActionStatus(snapshot.LastActionStatus))
                : snapshot.ItemName;
            lastOpenCard.SetValue(
                lastOpen,
                snapshot.LastActionStatus == "unknown"
                    ? CodexPalette.Warning
                    : CodexPalette.Text);

            messageLabel.Text = String.IsNullOrWhiteSpace(snapshot.Message)
                ? "没有附加状态信息"
                : snapshot.Message;
            messageLabel.ForeColor = String.IsNullOrWhiteSpace(snapshot.LastError)
                ? CodexPalette.Text
                : CodexPalette.Error;
            subMessageLabel.Text = String.IsNullOrWhiteSpace(snapshot.LastError)
                ? String.Format("自动计数：{0}   ·   输入键 {1}   ·   PID {2}",
                    TranslateActivityResult(snapshot.ActivityLastResult),
                    snapshot.ActivityKeysSent,
                    snapshot.Running ? snapshot.WorkerPid : "-")
                : snapshot.LastError;
            if (!String.IsNullOrWhiteSpace(snapshot.LastAction))
            {
                subMessageLabel.Text =
                    String.Format("最近动作：{0}   ·   {1}   ·   {2}",
                        String.IsNullOrWhiteSpace(snapshot.ItemName)
                            ? TranslateActionStatus(snapshot.LastActionStatus)
                            : snapshot.ItemName,
                        TranslateSide(snapshot.LastActionSide),
                        FormatCompactTime(snapshot.LastActionTime));
            }
            RefreshHistory();
        }

        private static string GetFileSignature(string path)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                return file.Exists
                    ? file.Length.ToString() + ":" + file.LastWriteTimeUtc.Ticks.ToString()
                    : "missing";
            }
            catch
            {
                return "unavailable";
            }
        }

        private static string TranslatePhase(string phase)
        {
            switch (phase)
            {
                case "starting": return "启动中";
                case "waiting_for_chest": return "等待宝箱";
                case "candidate_detected": return "发现宝箱";
                case "submitting": return "正在开启";
                case "confirming": return "确认结果";
                case "cooldown": return "冷却中";
                case "blocked": return "受阻";
                case "stopping": return "正在停止";
                case "stopped": return "已停止";
                default: return String.IsNullOrWhiteSpace(phase) ? "未知" : phase;
            }
        }

        private static string TranslateActionStatus(string status)
        {
            if (status == "confirmed")
                return "开箱成功";
            if (status == "unknown")
                return "结果未知";
            return String.IsNullOrWhiteSpace(status) ? "无" : status;
        }

        private static string TranslateActivityResult(string result)
        {
            switch (result)
            {
                case "pulse_complete": return "自动计数中";
                case "character_threshold_reached": return "已达到字符门槛";
                case "app_window_not_found": return "未找到应用窗口";
                case "character_count_unavailable": return "无法读取字符数";
                case "stop_requested": return "已请求停止";
                case "reward_ready": return "已发现宝箱";
                case "input_injection_failed": return "输入注入失败";
                default: return String.IsNullOrWhiteSpace(result) ? "尚未执行" : result;
            }
        }

        private static string TranslateSide(string side)
        {
            switch (side)
            {
                case "left": return "左侧";
                case "right": return "右侧";
                case "single": return "单槽位";
                default: return String.IsNullOrWhiteSpace(side) ? "未知位置" : side;
            }
        }

        private static string FormatCompactTime(string value)
        {
            DateTimeOffset parsed;
            if (DateTimeOffset.TryParse(value, out parsed))
                return parsed.ToLocalTime().ToString("HH:mm:ss");
            return String.IsNullOrWhiteSpace(value) ? "时间未知" : value;
        }

        private void RefreshHistory()
        {
            try
            {
                FileInfo file = new FileInfo(PortableRuntime.HistoryFile);
                string signature = file.Exists
                    ? file.Length.ToString() + ":" + file.LastWriteTimeUtc.Ticks.ToString()
                    : "missing";
                if (signature == historySignature)
                    return;
                historySignature = signature;

                List<HistoryEntry> entries = PortableRuntime.ReadHistory(100);
                historyView.BeginUpdate();
                try
                {
                    historyView.VirtualListSize = 0;
                    historyEntries = entries;
                    historyView.VirtualListSize = entries.Count;
                    historyView.Invalidate();
                }
                finally
                {
                    historyView.EndUpdate();
                }
            }
            catch
            {
            }
        }

        private void RetrieveHistoryItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= historyEntries.Count)
            {
                e.Item = new ListViewItem(new string[] { "", "", "", "" });
                return;
            }

            HistoryEntry entry = historyEntries[e.ItemIndex];
            string item = String.IsNullOrWhiteSpace(entry.ItemName)
                ? "未识别物品"
                : entry.ItemName;
            string result = entry.Result == "confirmed" ||
                entry.Result == "opened"
                ? "已开启"
                : (entry.Result == "unknown" ? "结果未知" : entry.Result);
            e.Item = new ListViewItem(new string[] {
                entry.Time,
                item,
                TranslateSide(entry.Side),
                result
            });
        }

        private void OpenSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "无法打开",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
