using NoFences.Model;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class PersonalizationDialog : Form
    {
        private static readonly Color Canvas = Color.FromArgb(243, 245, 247);
        private static readonly Color Surface = Color.White;
        private static readonly Color Ink = Color.FromArgb(29, 37, 48);
        private static readonly Color Muted = Color.FromArgb(103, 115, 129);
        private static readonly Color Border = Color.FromArgb(216, 222, 230);
        private static readonly Color Accent = Color.FromArgb(37, 99, 235);

        private readonly FencePreviewPanel preview;
        private readonly Button titleFontButton;
        private readonly Button titleTextColorButton;
        private readonly Button titleBackgroundColorButton;
        private readonly Button bodyBackgroundColorButton;
        private readonly TrackBar bodyTransparencyInput;
        private readonly Label bodyTransparencyValue;
        private readonly TrackBar iconSizeInput;
        private readonly Label iconSizeValue;
        private readonly TrackBar iconSpacingInput;
        private readonly Label iconSpacingValue;
        private readonly TrackBar cornerRadiusInput;
        private readonly Label cornerRadiusValue;
        private readonly Button itemFontButton;
        private readonly Button itemTextColorButton;
        private FenceAppearance appearance;
        private bool isUpdatingControls;

        public FenceAppearance Appearance => appearance.Clone();

        public PersonalizationDialog(FenceAppearance initialAppearance, bool batchMode, FenceInfo previewFence = null)
        {
            appearance = (initialAppearance ?? FenceAppearance.CreateDefault()).Clone();
            appearance.Normalize();
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint, true);

            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Canvas;
            ClientSize = new Size(860, 610);
            Font = new Font("Microsoft YaHei UI", 9f);
            ForeColor = Ink;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(760, 540);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = batchMode ? "批量个性化" : "个性化";

            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            Controls.Add(root);
            root.Controls.Add(CreateHeader(batchMode), 0, 0);

            var content = new TableLayoutPanel
            {
                BackColor = Canvas,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 10, 24, 18),
                RowCount = 1
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            root.Controls.Add(content, 0, 1);

            var previewCard = CreatePreviewCard();
            preview = new FencePreviewPanel(previewFence)
            {
                Appearance = appearance,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0)
            };
            previewCard.Controls.Add(preview);
            previewCard.Controls.SetChildIndex(preview, 0);
            content.Controls.Add(previewCard, 0, 0);

            var settingsHost = new Panel
            {
                AutoScroll = true,
                BackColor = Canvas,
                Dock = DockStyle.Fill,
                Margin = new Padding(18, 0, 0, 0),
                Padding = Padding.Empty
            };
            var settingsStack = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Canvas,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                WrapContents = false
            };
            settingsHost.Controls.Add(settingsStack);
            settingsHost.ClientSizeChanged += (sender, args) =>
            {
                settingsStack.Width = Math.Max(340, settingsHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
                foreach (Control control in settingsStack.Controls)
                    control.Width = settingsStack.Width;
            };
            content.Controls.Add(settingsHost, 1, 0);

            titleFontButton = CreateValueButton(ChooseTitleFont);
            titleTextColorButton = CreateColorButton(ChooseTitleTextColor);
            titleBackgroundColorButton = CreateColorButton(ChooseTitleBackgroundColor);
            settingsStack.Controls.Add(CreateSection("标题栏", new[]
            {
                CreateRow("标题字体", titleFontButton),
                CreateRow("文字颜色", titleTextColorButton),
                CreateRow("背景颜色", titleBackgroundColorButton)
            }));

            bodyBackgroundColorButton = CreateColorButton(ChooseBodyBackgroundColor);
            bodyTransparencyValue = CreateValueLabel();
            bodyTransparencyInput = new WheelSafeTrackBar
            {
                AccessibleName = "标题栏下方背景透明度",
                AutoSize = false,
                BackColor = Surface,
                Height = 28,
                Maximum = 90,
                Minimum = 0,
                TickFrequency = 10,
                TickStyle = TickStyle.None,
                Value = appearance.BodyTransparency
            };
            bodyTransparencyInput.ValueChanged += AppearanceValueChanged;
            cornerRadiusValue = CreateValueLabel();
            cornerRadiusInput = new WheelSafeTrackBar
            {
                AccessibleName = "分区窗口圆角",
                AutoSize = false,
                BackColor = Surface,
                Height = 28,
                Maximum = 48,
                Minimum = 0,
                TickStyle = TickStyle.None,
                Value = appearance.CornerRadius
            };
            cornerRadiusInput.ValueChanged += AppearanceValueChanged;
            settingsStack.Controls.Add(CreateSection("分区背景", new[]
            {
                CreateRow("背景颜色", bodyBackgroundColorButton),
                CreateRow("透明度", CreateSliderEditor(bodyTransparencyInput, bodyTransparencyValue)),
                CreateRow("窗口圆角", CreateSliderEditor(cornerRadiusInput, cornerRadiusValue))
            }));

            iconSizeValue = CreateValueLabel();
            iconSizeInput = new WheelSafeTrackBar
            {
                AccessibleName = "图标大小",
                AutoSize = false,
                BackColor = Surface,
                Height = 28,
                Maximum = 96,
                Minimum = 24,
                SmallChange = 4,
                LargeChange = 8,
                TickFrequency = 8,
                TickStyle = TickStyle.None,
                Value = appearance.IconSize
            };
            iconSizeInput.ValueChanged += AppearanceValueChanged;
            iconSpacingValue = CreateValueLabel();
            iconSpacingInput = new WheelSafeTrackBar
            {
                AccessibleName = "图标间距",
                AutoSize = false,
                BackColor = Surface,
                Height = 28,
                Maximum = 64,
                Minimum = 0,
                SmallChange = 1,
                LargeChange = 4,
                TickFrequency = 8,
                TickStyle = TickStyle.None,
                Value = appearance.IconSpacing
            };
            iconSpacingInput.ValueChanged += AppearanceValueChanged;
            itemFontButton = CreateValueButton(ChooseItemFont);
            itemTextColorButton = CreateColorButton(ChooseItemTextColor);
            settingsStack.Controls.Add(CreateSection("项目", new[]
            {
                CreateRow("图标大小", CreateSliderEditor(iconSizeInput, iconSizeValue)),
                CreateRow("图标间距", CreateSliderEditor(iconSpacingInput, iconSpacingValue)),
                CreateRow("名称字体", itemFontButton),
                CreateRow("名称颜色", itemTextColorButton)
            }));

            var resetButton = CreateActionButton("恢复默认", false);
            resetButton.Click += ResetButton_Click;
            var cancelButton = CreateActionButton("取消", false);
            cancelButton.DialogResult = DialogResult.Cancel;
            var applyButton = CreateActionButton(batchMode ? "应用到全部" : "应用", true);
            applyButton.DialogResult = DialogResult.OK;
            root.Controls.Add(CreateFooter(resetButton, cancelButton, applyButton), 0, 2);

            AcceptButton = applyButton;
            CancelButton = cancelButton;
            UpdateControls();
        }

        private Control CreateHeader(bool batchMode)
        {
            var panel = new Panel { BackColor = Surface, Dock = DockStyle.Fill, Padding = new Padding(24, 17, 24, 10) };
            var title = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 15f, FontStyle.Bold),
                ForeColor = Ink,
                Location = new Point(24, 15),
                Text = batchMode ? "统一所有分区的外观" : "调整这个分区的外观"
            };
            panel.Controls.Add(title);
            return panel;
        }

        private Panel CreatePreviewCard()
        {
            var card = new BorderedPanel { BackColor = Surface, Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(16) };
            var caption = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
                ForeColor = Ink,
                Height = 24,
                Text = "实时预览"
            };
            card.Controls.Add(caption);
            return card;
        }

        private Control CreateSection(string titleText, Control[] rows)
        {
            var card = new BorderedPanel
            {
                BackColor = Surface,
                Height = 53 + rows.Length * 45,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(16, 13, 16, 13)
            };
            var title = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
                ForeColor = Ink,
                Location = new Point(16, 12),
                Text = titleText
            };
            card.Controls.Add(title);
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index].Location = new Point(16, 42 + index * 45);
                rows[index].Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                rows[index].Width = card.Width - 34;
                card.Controls.Add(rows[index]);
            }
            card.Resize += (sender, args) =>
            {
                foreach (var row in rows)
                    row.Width = Math.Max(100, card.ClientSize.Width - 32);
            };
            return card;
        }

        private Control CreateRow(string labelText, Control editor)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Height = 36,
                Margin = Padding.Empty,
                RowCount = 1
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var label = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Ink,
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft
            };
            editor.Dock = DockStyle.Fill;
            editor.Margin = new Padding(0, 3, 0, 3);
            row.Controls.Add(label, 0, 0);
            row.Controls.Add(editor, 1, 0);
            return row;
        }

        private Control CreateSliderEditor(TrackBar slider, Label valueLabel)
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Margin = Padding.Empty,
                RowCount = 1
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            slider.Dock = DockStyle.Fill;
            valueLabel.Dock = DockStyle.Fill;
            panel.Controls.Add(slider, 0, 0);
            panel.Controls.Add(valueLabel, 1, 0);
            return panel;
        }

        private Label CreateValueLabel()
        {
            return new Label { ForeColor = Muted, TextAlign = ContentAlignment.MiddleRight };
        }

        private Button CreateValueButton(EventHandler clickHandler)
        {
            var button = new Button
            {
                AutoEllipsis = true,
                BackColor = Surface,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Ink,
                Height = 30,
                Padding = new Padding(8, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(247, 249, 252);
            button.Click += clickHandler;
            return button;
        }

        private Button CreateColorButton(EventHandler clickHandler)
        {
            var button = CreateValueButton(clickHandler);
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            return button;
        }

        private Button CreateActionButton(string text, bool primary)
        {
            var button = new Button
            {
                BackColor = primary ? Accent : Surface,
                FlatStyle = FlatStyle.Flat,
                ForeColor = primary ? Color.White : Ink,
                MinimumSize = new Size(primary ? 110 : 88, 36),
                Padding = new Padding(12, 0, 12, 0),
                Text = text,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Color.FromArgb(247, 249, 252);
            return button;
        }

        private Control CreateFooter(Button resetButton, Button cancelButton, Button applyButton)
        {
            var footer = new TableLayoutPanel
            {
                BackColor = Surface,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 15, 24, 15),
                RowCount = 1
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            resetButton.Anchor = AnchorStyles.Left;
            footer.Controls.Add(resetButton, 0, 0);
            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = Padding.Empty,
                WrapContents = false
            };
            actions.Controls.Add(cancelButton);
            actions.Controls.Add(applyButton);
            footer.Controls.Add(actions, 1, 0);
            return footer;
        }

        private void ChooseTitleFont(object sender, EventArgs e)
        {
            using (var dialog = CreateFontDialog(appearance.TitleFontFamily, appearance.TitleFontSize))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                appearance.TitleFontFamily = dialog.Font.FontFamily.Name;
                appearance.TitleFontSize = dialog.Font.SizeInPoints;
                UpdateControls();
            }
        }

        private void ChooseItemFont(object sender, EventArgs e)
        {
            using (var dialog = CreateFontDialog(appearance.ItemFontFamily, appearance.ItemFontSize))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                appearance.ItemFontFamily = dialog.Font.FontFamily.Name;
                appearance.ItemFontSize = dialog.Font.SizeInPoints;
                UpdateControls();
            }
        }

        private FontDialog CreateFontDialog(string family, float size)
        {
            return new FontDialog
            {
                Font = CreateFont(family, size),
                FontMustExist = true,
                MaxSize = 36,
                MinSize = 7,
                ShowApply = false,
                ShowColor = false,
                ShowEffects = false
            };
        }

        private void ChooseTitleTextColor(object sender, EventArgs e)
        {
            ChooseColor(appearance.TitleTextColorArgb, value => appearance.TitleTextColorArgb = value);
        }

        private void ChooseTitleBackgroundColor(object sender, EventArgs e)
        {
            ChooseColor(appearance.TitleBackgroundColorArgb, value => appearance.TitleBackgroundColorArgb = value);
        }

        private void ChooseBodyBackgroundColor(object sender, EventArgs e)
        {
            ChooseColor(appearance.BodyBackgroundColorArgb, value => appearance.BodyBackgroundColorArgb = value);
        }

        private void ChooseItemTextColor(object sender, EventArgs e)
        {
            ChooseColor(appearance.ItemTextColorArgb, value => appearance.ItemTextColorArgb = value);
        }

        private void ChooseColor(int initialArgb, Action<int> assign)
        {
            using (var dialog = new ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                Color = Color.FromArgb(initialArgb),
                FullOpen = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                assign(dialog.Color.ToArgb());
                UpdateControls();
            }
        }

        private void AppearanceValueChanged(object sender, EventArgs e)
        {
            if (isUpdatingControls)
                return;
            appearance.BodyTransparency = bodyTransparencyInput.Value;
            appearance.IconSize = iconSizeInput.Value;
            appearance.IconSpacing = iconSpacingInput.Value;
            appearance.CornerRadius = cornerRadiusInput.Value;
            cornerRadiusValue.Text = appearance.CornerRadius + " px";
            bodyTransparencyValue.Text = appearance.BodyTransparency + "%";
            iconSizeValue.Text = appearance.IconSize + " px";
            iconSpacingValue.Text = appearance.IconSpacing + " px";
            preview.Invalidate();
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            appearance = FenceAppearance.CreateDefault();
            UpdateControls();
        }

        private void UpdateControls()
        {
            appearance.Normalize();
            titleFontButton.Text = FormatFont(appearance.TitleFontFamily, appearance.TitleFontSize);
            itemFontButton.Text = FormatFont(appearance.ItemFontFamily, appearance.ItemFontSize);
            UpdateColorButton(titleTextColorButton, Color.FromArgb(appearance.TitleTextColorArgb));
            UpdateColorButton(titleBackgroundColorButton, Color.FromArgb(appearance.TitleBackgroundColorArgb));
            UpdateColorButton(bodyBackgroundColorButton, Color.FromArgb(appearance.BodyBackgroundColorArgb));
            UpdateColorButton(itemTextColorButton, Color.FromArgb(appearance.ItemTextColorArgb));
            isUpdatingControls = true;
            try
            {
                bodyTransparencyInput.Value = appearance.BodyTransparency;
                iconSizeInput.Value = appearance.IconSize;
                iconSpacingInput.Value = appearance.IconSpacing;
                cornerRadiusInput.Value = appearance.CornerRadius;
            }
            finally
            {
                isUpdatingControls = false;
            }
            bodyTransparencyValue.Text = appearance.BodyTransparency + "%";
            iconSizeValue.Text = appearance.IconSize + " px";
            iconSpacingValue.Text = appearance.IconSpacing + " px";
            preview.Appearance = appearance;
            cornerRadiusValue.Text = appearance.CornerRadius + " px";
            preview.Invalidate();
        }

        private void UpdateColorButton(Button button, Color color)
        {
            if (button.Image != null)
                button.Image.Dispose();
            var swatch = new Bitmap(18, 18);
            using (var graphics = Graphics.FromImage(swatch))
            {
                graphics.Clear(color);
                using (var pen = new Pen(Border))
                    graphics.DrawRectangle(pen, 0, 0, 17, 17);
            }
            button.Image = swatch;
            button.Text = string.Format("  #{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private string FormatFont(string family, float size)
        {
            return string.Format("{0}  ·  {1:0.#} 磅", family, size);
        }

        private static Font CreateFont(string family, float size)
        {
            try
            {
                return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
            }
        }

        private sealed class WheelSafeTrackBar : TrackBar
        {
            [DllImport("user32.dll")]
            private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x020A || message.Msg == 0x020E)
                {
                    // The native trackbar also handles wheel messages after OnMouseWheel.
                    // Bypass it entirely; only the settings container may consume scrolling.
                    if (message.Msg == 0x020A)
                    {
                        for (var parent = Parent; parent != null; parent = parent.Parent)
                        {
                            var panel = parent as ScrollableControl;
                            if (panel == null || !panel.AutoScroll)
                                continue;
                            SendMessage(panel.Handle, message.Msg, message.WParam, message.LParam);
                            break;
                        }
                    }
                    message.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref message);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                var handled = e as HandledMouseEventArgs;
                if (handled != null)
                    handled.Handled = true;
            }
        }

        private sealed class BorderedPanel : Panel
        {
            public BorderedPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

    }
}
