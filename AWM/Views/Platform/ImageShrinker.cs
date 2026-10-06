using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AWM.Models;
using AWM.Services.Interfaces;

namespace AWM.Views.Platform;

/// <summary>
/// WPF 이미징으로 줄인다 — 이미지 패키지를 들이지 않는다. 무거운 일이라 호출자가 백그라운드에서 부른다(LL-034).
/// </summary>
public sealed class ImageShrinker : IImageShrinker
{
    // Claude 가 큰 그림을 줄여 보는 크기에 맞춘 값 [추정] (D-013)
    private const int MaxSide = 1568;
    private const int JpegQuality = 85;
    private const string OrientationQuery = "/app1/ifd/{ushort=274}";

    private static readonly PixelFormat[] AlphaFormats =
    [
        PixelFormats.Bgra32, PixelFormats.Pbgra32, PixelFormats.Rgba64, PixelFormats.Prgba64,
        PixelFormats.Rgba128Float, PixelFormats.Prgba128Float,
    ];

    public ImageInput Shrink(string fileName, byte[] original)
    {
        BitmapFrame frame;
        try
        {
            using var stream = new MemoryStream(original);
            frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or OverflowException)
        {
            throw new NotSupportedException($"그림을 읽지 못했습니다: {fileName}", ex);
        }

        BitmapSource image = frame;
        var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (longest > MaxSide)
        {
            var scale = (double)MaxSide / longest;
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }

        // 휴대폰 사진은 픽셀을 눕혀 저장하고 회전 값만 적어 둔다. 돌리지 않으면 Claude 가 누운 사진을 본다
        if (RotationOf(frame) is var angle and not 0)
            image = new TransformedBitmap(image, new RotateTransform(angle));

        // 투명한 부분이 있으면 JPEG 에서 검게 변한다 — 그런 그림은 PNG 로
        var hasAlpha = AlphaFormats.Contains(image.Format);
        BitmapEncoder encoder = hasAlpha ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return new ImageInput(fileName, output.ToArray(), hasAlpha ? "image/png" : "image/jpeg");
    }

    private static int RotationOf(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery(OrientationQuery)
                && metadata.GetQuery(OrientationQuery) is ushort orientation)
            {
                return orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
            }
        }
        catch (NotSupportedException)
        {
            // 메타데이터를 읽을 수 없는 형식(GIF 등)
        }
        return 0;
    }
}
