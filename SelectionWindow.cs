using System.Drawing.Imaging;

namespace ScreenCrop;

internal sealed class SelectionWindow : Form
{
    private readonly Bitmap screenshot;
    private readonly Rectangle desktop;
    private readonly Rectangle hintScreen;
    private readonly AspectRatio ratio;
    private readonly Button confirm;
    private Point? dragStart;
    private Rectangle selectionBeforeDrag;
    private bool movingSelection;
    private Rectangle selection;

    public SelectionWindow(Bitmap screenshot, Rectangle desktop, Rectangle hintScreen, AspectRatio ratio)
    {
        this.screenshot = screenshot;
        this.desktop = desktop;
        this.hintScreen = hintScreen;
        this.ratio = ratio;
        Text = "框選截圖";
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = desktop;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        Font = new Font("Microsoft JhengHei UI", 11);

        confirm = new Button
        {
            Text = "確認",
            AutoSize = true,
            MinimumSize = new Size(80, 34),
            Padding = new Padding(12, 3, 12, 3),
            BackColor = Color.FromArgb(226, 213, 248),
            ForeColor = Color.FromArgb(24, 21, 29),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK,
            Visible = false
        };
        confirm.FlatAppearance.BorderColor = Color.FromArgb(24, 21, 29);
        Controls.Add(confirm);
        AcceptButton = confirm;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = desktop;
        Activate();
    }

    public Bitmap CopySelection() => screenshot.Clone(selection, PixelFormat.Format32bppRgb);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
        }
        else if (e.Button == MouseButtons.Left)
        {
            dragStart = e.Location;
            selectionBeforeDrag = selection;
            movingSelection = HasSelection && selection.Contains(e.Location);
            if (!movingSelection)
                selection = Rectangle.Empty;
            confirm.Hide();
            Capture = true;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragStart.HasValue)
        {
            UpdateSelection(e.Location);
            Invalidate();
        }
        Cursor = movingSelection || (!dragStart.HasValue && HasSelection && selection.Contains(e.Location))
            ? Cursors.SizeAll
            : Cursors.Cross;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !dragStart.HasValue)
            return;

        UpdateSelection(e.Location);
        FinishDrag(restoreSelection: !HasSelection);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && dragStart.HasValue)
            FinishDrag(restoreSelection: true);
    }

    private bool HasSelection => selection.Width > 0 && selection.Height > 0;

    private void UpdateSelection(Point pointer)
    {
        if (dragStart is not Point start)
            return;

        if (movingSelection)
        {
            selection.Location = new Point(
                Math.Clamp(selectionBeforeDrag.X + pointer.X - start.X, 0, screenshot.Width - selection.Width),
                Math.Clamp(selectionBeforeDrag.Y + pointer.Y - start.Y, 0, screenshot.Height - selection.Height));
        }
        else
        {
            selection = ratio.Select(Clamp(start), Clamp(pointer));
        }
    }

    private void FinishDrag(bool restoreSelection)
    {
        if (restoreSelection)
            selection = selectionBeforeDrag;
        dragStart = null;
        movingSelection = false;
        Capture = false;
        confirm.Visible = HasSelection;
        if (HasSelection)
            PositionConfirm();
        Invalidate();
    }

    private void PositionConfirm()
    {
        var corner = new Point(desktop.Left + selection.Right - 1, desktop.Top + selection.Bottom - 1);
        Rectangle screen = Screen.FromPoint(corner).Bounds;
        screen.Offset(-desktop.Left, -desktop.Top);
        int gap = LogicalToDeviceUnits(8);
        int minX = screen.Left + gap;
        int minY = screen.Top + gap;
        int maxX = Math.Max(minX, screen.Right - confirm.Width - gap);
        int maxY = Math.Max(minY, screen.Bottom - confirm.Height - gap);
        int y = selection.Bottom + gap <= maxY
            ? selection.Bottom + gap
            : selection.Bottom - confirm.Height - gap;
        confirm.Location = new Point(Math.Clamp(selection.Right - confirm.Width, minX, maxX),
            Math.Clamp(y, minY, maxY));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(screenshot, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(136, 24, 21, 29));
        using var shadedRegion = new Region(ClientRectangle);
        shadedRegion.Exclude(selection);
        e.Graphics.FillRegion(shade, shadedRegion);

        if (HasSelection)
        {
            using var border = new Pen(Color.FromArgb(226, 213, 248), 2);
            e.Graphics.DrawRectangle(border, selection);
            var screen = Screen.FromPoint(Cursor.Position).Bounds;
            int left = screen.Left - desktop.Left;
            int top = screen.Top - desktop.Top;
            string label = $"{selection.Width} × {selection.Height} px";
            if (!dragStart.HasValue)
                label += "    拖曳選框可移動";
            int width = TextRenderer.MeasureText(e.Graphics, label, Font).Width + 16;
            DrawLabel(e.Graphics, label,
                Math.Clamp(selection.Left, left, left + Math.Max(0, screen.Width - width)),
                Math.Clamp(selection.Top - 32, top, top + Math.Max(0, screen.Height - 32)));
        }
        else if (!dragStart.HasValue)
        {
            DrawLabel(e.Graphics, $"{(ratio.Width == 0 ? "自由比例" : ratio.Label)}    拖曳框選截圖區域    Esc 取消",
                hintScreen.Left - desktop.Left + 20, hintScreen.Top - desktop.Top + 20);
        }
    }

    private void DrawLabel(Graphics graphics, string label, int x, int y)
    {
        Size size = TextRenderer.MeasureText(graphics, label, Font);
        using var fill = new SolidBrush(Color.FromArgb(24, 21, 29));
        graphics.FillRectangle(fill, x, y, size.Width + 16, size.Height + 10);
        TextRenderer.DrawText(graphics, label, Font, new Point(x + 8, y + 5), Color.White);
    }

    private Point Clamp(Point p) => new(
        Math.Clamp(p.X, 0, screenshot.Width), Math.Clamp(p.Y, 0, screenshot.Height));
}
