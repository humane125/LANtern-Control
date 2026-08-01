using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.App.Controls;

namespace Lantern.BrandAssets;

internal static class Program
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: Lantern.BrandAssets <output.ico>");
            return 1;
        }

        _ = new Application();
        Application.Current.Resources["Accent"] =
            new SolidColorBrush(Color.FromRgb(215, 44, 67));

        var frames = new List<byte[]>();
        foreach (var size in IconSizes)
        {
            frames.Add(RenderLogo(size));
        }

        var destination = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var stream = File.Create(destination);
        WriteIcon(stream, frames);
        Console.WriteLine($"Generated {destination} with {IconSizes.Length} icon frames.");
        return 0;
    }

    private static byte[] RenderLogo(int size)
    {
        var logo = new RedWatcherLogo
        {
            Width = size,
            Height = size,
        };
        logo.Measure(new Size(size, size));
        logo.Arrange(new Rect(0, 0, size, size));
        logo.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            size,
            size,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(logo);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteIcon(Stream destination, IReadOnlyList<byte[]> frames)
    {
        using var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        for (var index = 0; index < frames.Count; index++)
        {
            var size = IconSizes[index];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[index].Length);
            writer.Write((uint)offset);
            offset += frames[index].Length;
        }

        foreach (var frame in frames)
        {
            writer.Write(frame);
        }
    }

}
