using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenCrop;

internal sealed class MainWindow : Form
{
    private static readonly Color Canvas = Color.FromArgb(241, 235, 250);
    private static readonly Color Tile = Color.FromArgb(248, 244, 253);
    private static readonly Color TileBorder = Color.FromArgb(231, 220, 246);
    private static readonly Color Lavender = Color.FromArgb(226, 213, 248);
    private static readonly Color Foreground = Color.FromArgb(24, 21, 29);
    private static readonly Color Muted = Color.FromArgb(92, 82, 106);

    private static readonly AspectRatio[] Ratios =
    [
        new(0, 0, "自由"), new(1, 1, "1:1"), new(4, 3, "4:3"), new(3, 2, "3:2"),
        new(16, 9, "16:9"), new(21, 9, "21:9"), new(4, 5, "4:5"), new(9, 16, "9:16")
    ];

    private readonly ComboBox monitors = CreatePicker();
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label placeholder = CreateLabel("截圖後會顯示在這裡", 11, Muted);
    private readonly Label details = CreateLabel("", 10, Muted);
    private readonly Button save = CreateButton("儲存圖片…", true);
    private readonly Icon appIcon;
    private readonly ContextMenuStrip trayMenu;
    private readonly NotifyIcon trayIcon;
    private bool capturing;
    private string? savedFolder;

    public MainWindow()
    {
        Text = "畫幅截圖";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(924, 568);
        MinimumSize = new Size(770, 495);
        BackColor = Canvas;
        ForeColor = Foreground;
        Font = new Font("Microsoft JhengHei UI", 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(26, 18, 26, 26), ColumnCount = 2, RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var title = CreateLabel("畫幅截圖", 26, Foreground, FontStyle.Bold);
        title.Margin = Padding.Empty;
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18),
            Margin = new Padding(0, 0, 16, 0), ColumnCount = 1, RowCount = 6
        };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 30, 28, 150, 28, 78 })
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(options, 0, 1);

        options.Controls.Add(CreateLabel("擷取方式", 13, Foreground, FontStyle.Bold), 0, 0);
        options.Controls.Add(CreateLabel("點選比例後拖曳框選", 10, Muted), 0, 1);
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4, RowCount = 2
        };
        for (int column = 0; column < 4; column++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        options.Controls.Add(grid, 0, 2);

        for (int index = 0; index < Ratios.Length; index++)
        {
            AspectRatio ratio = Ratios[index];
            var region = CreateCaptureTile(ratio.Label, ratio);
            region.AccessibleName = ratio.Width == 0 ? "自由比例框選" : $"{ratio.Label} 比例框選";
            region.Margin = new Padding(0, 0, index % 4 == 3 ? 0 : 7, 8);
            region.Click += async (_, _) => await TakeScreenshot(ratio);
            grid.Controls.Add(region, index % 4, index / 4);
        }

        options.Controls.Add(CreateLabel("全螢幕擷取範圍", 10, Muted), 0, 3);
        var screenControls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1
        };
        screenControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        screenControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        screenControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        options.Controls.Add(screenControls, 0, 4);
        var fullScreen = CreateCaptureTile("全螢幕", null);
        fullScreen.Margin = new Padding(0, 0, 10, 5);
        fullScreen.Click += async (_, _) => await TakeScreenshot(null);
        screenControls.Controls.Add(fullScreen, 0, 0);
        screenControls.Controls.Add(monitors, 1, 0);
        monitors.DropDown += (_, _) => RefreshMonitors();
        Shown += (_, _) => RefreshMonitors();

        var result = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18),
            Margin = Padding.Empty, ColumnCount = 2, RowCount = 3
        };
        result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        result.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        result.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        result.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.Controls.Add(result, 1, 1);
        result.Controls.Add(CreateLabel("截圖預覽", 13, Foreground, FontStyle.Bold), 0, 0);

        var previewFrame = new Panel { Dock = DockStyle.Fill, BackColor = Tile, Margin = Padding.Empty };
        placeholder.AutoSize = false;
        placeholder.Dock = DockStyle.Fill;
        placeholder.TextAlign = ContentAlignment.MiddleCenter;
        previewFrame.Controls.Add(preview);
        previewFrame.Controls.Add(placeholder);
        placeholder.BringToFront();
        result.Controls.Add(previewFrame, 0, 1);
        result.SetColumnSpan(previewFrame, 2);

        details.AutoSize = false;
        details.AutoEllipsis = true;
        details.Dock = DockStyle.Fill;
        details.TextAlign = ContentAlignment.MiddleLeft;
        result.Controls.Add(details, 0, 2);
        save.Enabled = false;
        save.Margin = new Padding(0, 11, 0, 0);
        save.Click += (_, _) => SaveScreenshot();
        result.Controls.Add(save, 1, 2);

        appIcon = CreateAppIcon();
        Icon = appIcon;
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("開啟畫幅截圖", null, (_, _) => OpenFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("結束程式", null, (_, _) => Application.Exit());
        trayIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = Text,
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenFromTray();
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }

    private void OpenFromTray()
    {
        if (capturing || IsDisposed)
            return;

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        bool disposeResources = disposing && !IsDisposed;
        if (disposeResources)
        {
            trayIcon.Dispose();
            trayMenu.Dispose();
            preview.Image?.Dispose();
        }
        base.Dispose(disposing);
        if (disposeResources)
            appIcon.Dispose();
    }

    private void RefreshMonitors()
    {
        var previous = monitors.SelectedItem as MonitorChoice;
        monitors.BeginUpdate();
        try
        {
            monitors.Items.Clear();
            int primary = 0;
            int selected = -1;
            foreach (Screen screen in Screen.AllScreens)
            {
                int index = monitors.Items.Count;
                string label = $"螢幕 {index + 1}";
                if (screen.Primary)
                {
                    label += "（主螢幕）";
                    primary = index;
                }
                monitors.Items.Add(new MonitorChoice(screen.DeviceName, label));
                if (screen.DeviceName == previous?.DeviceName)
                    selected = index;
            }
            int desktopIndex = monitors.Items.Add(new MonitorChoice(null, "所有螢幕（整個桌面）"));
            if (previous is { DeviceName: null })
                selected = desktopIndex;
            monitors.SelectedIndex = selected >= 0 ? selected : primary;
        }
        finally
        {
            monitors.EndUpdate();
        }
    }

    private async Task TakeScreenshot(AspectRatio? ratio)
    {
        if (capturing)
            return;

        capturing = true;
        var monitor = monitors.SelectedItem as MonitorChoice;
        Point pointer = Cursor.Position;
        string? captureError = null;
        Hide();

        try
        {
            await Task.Delay(180);
            if (IsDisposed)
                return;

            DwmFlush();
            Screen[] screens = Screen.AllScreens;
            if (ratio is null && monitor is not { DeviceName: null })
            {
                Screen target = screens.FirstOrDefault(screen => screen.DeviceName == monitor?.DeviceName)
                    ?? Screen.PrimaryScreen ?? screens[0];
                SetPreview(ScreenCapture.Read(target.Bounds, [target]));
            }
            else
            {
                Rectangle desktop = screens.Select(screen => screen.Bounds).Aggregate(Rectangle.Union);
                if (ratio is AspectRatio regionRatio)
                {
                    using Bitmap snapshot = ScreenCapture.Read(desktop, screens);
                    using var selector = new SelectionWindow(snapshot, desktop, Screen.FromPoint(pointer).Bounds, regionRatio);
                    if (selector.ShowDialog() == DialogResult.OK && !IsDisposed)
                        SetPreview(selector.CopySelection());
                }
                else
                {
                    SetPreview(ScreenCapture.Read(desktop, screens));
                }
            }
        }
        catch (Exception error)
        {
            captureError = error.Message;
        }
        finally
        {
            capturing = false;
            if (!IsDisposed)
                OpenFromTray();
        }

        if (captureError is not null && !IsDisposed)
            MessageBox.Show(this, $"無法擷取畫面。\n{captureError}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetPreview(Bitmap image)
    {
        Image? previous = preview.Image;
        preview.Image = image;
        previous?.Dispose();
        placeholder.Visible = false;
        details.Text = $"{image.Width} × {image.Height} px";
        save.Enabled = true;
    }

    private void SaveScreenshot()
    {
        if (preview.Image is not Image image)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "儲存截圖",
            FileName = $"截圖_{DateTime.Now:yyyyMMdd_HHmmss}",
            InitialDirectory = savedFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Filter = "PNG 圖片 (*.png)|*.png|JPEG 圖片 (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || IsDisposed)
            return;

        try
        {
            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            ImageFormat format = extension switch
            {
                ".png" => ImageFormat.Png,
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                _ => throw new IOException("請使用 .png、.jpg 或 .jpeg 副檔名。")
            };
            image.Save(dialog.FileName, format);
            savedFolder = Path.GetDirectoryName(dialog.FileName);
            details.Text = $"已儲存 · {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"無法儲存截圖。\n{error.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Label CreateLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) =>
        new() { Text = text, AutoSize = true, ForeColor = color, Font = new Font("Microsoft JhengHei UI", size, style) };

    private static ComboBox CreatePicker()
    {
        var picker = new ComboBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            FlatStyle = FlatStyle.Flat,
            ItemHeight = 24,
            AccessibleName = "全螢幕擷取範圍",
            BackColor = Tile,
            ForeColor = Foreground,
            Margin = Padding.Empty
        };
        picker.DrawItem += (_, e) =>
        {
            bool isField = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool highlighted = isField ? picker.Focused : (e.State & DrawItemState.Selected) != 0;
            Color background = highlighted ? Lavender : isField ? Tile : Color.White;
            Color foreground = Foreground;
            if (SystemInformation.HighContrast)
            {
                background = highlighted ? SystemColors.Highlight : SystemColors.Window;
                foreground = highlighted ? SystemColors.HighlightText : SystemColors.WindowText;
            }

            using var fill = new SolidBrush(background);
            e.Graphics.FillRectangle(fill, e.Bounds);
            if (e.Index < 0 || e.Index >= picker.Items.Count)
                return;

            Rectangle textBounds = Rectangle.Inflate(e.Bounds, -picker.LogicalToDeviceUnits(7), 0);
            TextRenderer.DrawText(e.Graphics, picker.GetItemText(picker.Items[e.Index]), e.Font,
                textBounds, foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (isField && highlighted && (e.State & DrawItemState.NoFocusRect) == 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -1, -1),
                    foreground, background);
        };
        return picker;
    }

    private static Button CreateCaptureTile(string text, AspectRatio? ratio)
    {
        Button tile = CreateButton(text);
        tile.TextAlign = ContentAlignment.BottomCenter;
        tile.Padding = new Padding(0, 0, 0, 7);
        tile.FlatAppearance.BorderColor = TileBorder;
        tile.FlatAppearance.BorderSize = 1;
        tile.Paint += (_, e) => DrawCaptureIcon(e.Graphics, tile, ratio);
        return tile;
    }

    private static void DrawCaptureIcon(Graphics graphics, Button tile, AspectRatio? ratio)
    {
        GraphicsState state = graphics.Save();
        float scale = tile.DeviceDpi / 96f;
        graphics.TranslateTransform(tile.ClientSize.Width / 2f, tile.ClientSize.Height * 0.39f);
        graphics.ScaleTransform(scale, scale);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = new Pen(Foreground, 1.55f) { LineJoin = LineJoin.Round };

        if (ratio is { Width: > 0 } fixedRatio)
        {
            float unit = Math.Min(29f / fixedRatio.Width, 24f / fixedRatio.Height);
            float width = fixedRatio.Width * unit;
            float height = fixedRatio.Height * unit;
            using var tint = new SolidBrush(Lavender);
            graphics.FillRectangle(tint, -width / 2, -height / 2, width, height);
            graphics.DrawRectangle(outline, -width / 2, -height / 2, width, height);
        }
        else if (ratio is null)
        {
            graphics.DrawRectangle(outline, -15, -11, 30, 19);
            graphics.DrawLine(outline, 0, 8, 0, 12);
            graphics.DrawLine(outline, -6, 12, 6, 12);
        }
        else
        {
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    graphics.DrawLine(outline, 7 * x, 10 * y, 13 * x, 10 * y);
                    graphics.DrawLine(outline, 13 * x, 10 * y, 13 * x, 4 * y);
                }
            }
        }

        graphics.Restore(state);
    }

    private static Button CreateButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Foreground : Tile,
            ForeColor = primary ? Color.White : Foreground,
            Margin = Padding.Empty, Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(52, 42, 66) : Lavender;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(66, 53, 84) : Canvas;
        return button;
    }

    private static Icon CreateAppIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Lavender);
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var outline = new Pen(Foreground, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(outline, 11, 7, 11, 21);
        graphics.DrawLine(outline, 11, 21, 25, 21);
        graphics.DrawLine(outline, 7, 11, 21, 11);
        graphics.DrawLine(outline, 21, 11, 21, 25);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private sealed record MonitorChoice(string? DeviceName, string Label)
    {
        public override string ToString() => Label;
    }
}
