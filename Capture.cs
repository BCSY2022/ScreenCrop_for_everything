using System.Drawing.Imaging;

namespace ScreenCrop;

internal readonly record struct AspectRatio(int Width, int Height, string Label)
{
    public override string ToString() => Label;

    public Rectangle Select(Point start, Point end)
    {
        int width = Math.Abs(end.X - start.X);
        int height = Math.Abs(end.Y - start.Y);

        if (Width > 0)
        {
            int units = Math.Min(width / Width, height / Height);
            width = units * Width;
            height = units * Height;
        }

        return new Rectangle(
            end.X < start.X ? start.X - width : start.X,
            end.Y < start.Y ? start.Y - height : start.Y,
            width, height);
    }
}

internal static class ScreenCapture
{
    public static Bitmap Read(Rectangle bounds, IEnumerable<Screen> screens)
    {
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Black);
            foreach (Screen screen in screens)
            {
                Rectangle area = Rectangle.Intersect(bounds, screen.Bounds);
                if (area.Width > 0 && area.Height > 0)
                {
                    graphics.CopyFromScreen(area.X, area.Y, area.X - bounds.X, area.Y - bounds.Y,
                        area.Size, CopyPixelOperation.SourceCopy);
                }
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
