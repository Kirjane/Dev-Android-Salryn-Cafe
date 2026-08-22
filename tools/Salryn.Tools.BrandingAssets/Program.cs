using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Salryn.Tools.BrandingAssets <repoRoot> <sourceIconPng>");
    return 1;
}

string repoRoot = Path.GetFullPath(args[0]);
string sourceIcon = Path.GetFullPath(args[1]);
string sourceBannerClear = args.Length > 2 ? Path.GetFullPath(args[2]) : string.Empty;
string sourceBannerFilled = args.Length > 3 ? Path.GetFullPath(args[3]) : string.Empty;

if (!File.Exists(sourceIcon))
{
    throw new FileNotFoundException("Source icon not found.", sourceIcon);
}

string brandingDir = Path.Combine(repoRoot, "assets", "branding");
string publicDir = Path.Combine(repoRoot, "src", "Salryn.App.Web", "public");
Directory.CreateDirectory(brandingDir);
Directory.CreateDirectory(publicDir);

CopyIfPresent(sourceIcon, Path.Combine(brandingDir, "salryn-icon.png"));
if (!string.IsNullOrWhiteSpace(sourceBannerClear))
{
    CopyIfPresent(sourceBannerClear, Path.Combine(brandingDir, "salryn-banner-clear.png"));
}

if (!string.IsNullOrWhiteSpace(sourceBannerFilled))
{
    CopyIfPresent(sourceBannerFilled, Path.Combine(brandingDir, "salryn-banner-filled.png"));
}

int[] pngSizes = [16, 32, 48, 64, 128, 192, 256, 512, 1024];
int[] icoSizes = [16, 32, 48, 64, 128, 256];

foreach (int size in pngSizes)
{
    ResizePng(sourceIcon, Path.Combine(brandingDir, $"salryn-icon-{size}x{size}.png"), size);
}

string icoPath = Path.Combine(brandingDir, "salryn-icon.ico");
SaveMultiSizeIcon(sourceIcon, icoPath, icoSizes);

File.Copy(icoPath, Path.Combine(publicDir, "favicon.ico"), overwrite: true);
ResizePng(sourceIcon, Path.Combine(publicDir, "favicon-16x16.png"), 16);
ResizePng(sourceIcon, Path.Combine(publicDir, "favicon-32x32.png"), 32);
ResizePng(sourceIcon, Path.Combine(publicDir, "apple-touch-icon.png"), 180);
ResizePng(sourceIcon, Path.Combine(publicDir, "icon-192.png"), 192);
ResizePng(sourceIcon, Path.Combine(publicDir, "icon-512.png"), 512);

Console.WriteLine($"Branding assets generated under {brandingDir}");
Console.WriteLine($"Web public icons generated under {publicDir}");
return 0;

static void CopyIfPresent(string source, string destination)
{
    if (!File.Exists(source))
    {
        throw new FileNotFoundException("Missing approved source asset.", source);
    }

    File.Copy(source, destination, overwrite: true);
}

static void ResizePng(string sourcePath, string destPath, int size)
{
    using Bitmap source = new(sourcePath);
    using Bitmap target = new(size, size, PixelFormat.Format32bppArgb);
    using (Graphics graphics = Graphics.FromImage(target))
    {
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(source, 0, 0, size, size);
    }

    string? dir = Path.GetDirectoryName(destPath);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }

    target.Save(destPath, ImageFormat.Png);
}

static void SaveMultiSizeIcon(string sourcePath, string destPath, int[] sizes)
{
    List<byte[]> pngEntries = new();
    foreach (int size in sizes)
    {
        using Bitmap source = new(sourcePath);
        using Bitmap target = new(size, size, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, 0, 0, size, size);
        }

        using MemoryStream ms = new();
        target.Save(ms, ImageFormat.Png);
        pngEntries.Add(ms.ToArray());
    }

    string? dir = Path.GetDirectoryName(destPath);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }

    using FileStream output = new(destPath, FileMode.Create, FileAccess.Write);
    using BinaryWriter writer = new(output);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)sizes.Length);

    int dataOffset = 6 + (16 * sizes.Length);
    int currentOffset = dataOffset;
    for (int i = 0; i < sizes.Length; i++)
    {
        int size = sizes[i];
        byte[] data = pngEntries[i];
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(data.Length);
        writer.Write(currentOffset);
        currentOffset += data.Length;
    }

    foreach (byte[] data in pngEntries)
    {
        writer.Write(data);
    }
}
