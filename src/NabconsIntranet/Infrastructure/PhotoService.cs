using SkiaSharp;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Staff photos. The upload is decoded with SkiaSharp (real image content only: JPEG, PNG or WebP), auto-oriented
/// from its EXIF orientation, centre-cropped to a square and re-encoded as a fresh JPEG. Re-encoding drops all
/// metadata (EXIF, GPS, camera data). Files are named from the user ID only and kept in data\uploads\photos.
/// </summary>
public sealed class PhotoService(IntranetOptions o)
{
    public const int Size = 400, ThumbSize = 96, Quality = 85;
    private const long MaxPixels = 40_000_000; // refuse decompression bombs

    public string PhotoPath(long userId, bool thumb) => Path.Combine(o.PhotosPath, thumb ? $"{userId}_t.jpg" : $"{userId}.jpg");

    public bool Exists(long userId) => File.Exists(PhotoPath(userId, false));

    /// <summary>Validates and processes an upload. Throws UploadRejectedException with a user-readable reason.</summary>
    public void Save(long userId, Stream upload, long length)
    {
        if (length <= 0) throw new UploadRejectedException("The file is empty.");
        if (length > o.MaxPhotoMB * 1024L * 1024L) throw new UploadRejectedException($"The photo is larger than {o.MaxPhotoMB} MB.");
        using var ms = new MemoryStream();
        upload.CopyTo(ms);
        if (ms.Length > o.MaxPhotoMB * 1024L * 1024L) throw new UploadRejectedException($"The photo is larger than {o.MaxPhotoMB} MB.");
        ms.Position = 0;

        using var data = SKData.Create(ms);
        using var codec = data == null ? null : SKCodec.Create(data);
        if (codec == null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new UploadRejectedException("This is not a JPEG, PNG or WebP image.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels || codec.Info.Width < 32 || codec.Info.Height < 32)
            throw new UploadRejectedException("The image dimensions are not acceptable (minimum 32×32, maximum 40 megapixels).");

        using var decoded = SKBitmap.Decode(codec, codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul))
            ?? throw new UploadRejectedException("The image could not be read.");
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        using var square = CropSquare(oriented);

        Directory.CreateDirectory(o.PhotosPath);
        WriteJpeg(square, Size, PhotoPath(userId, false));
        WriteJpeg(square, ThumbSize, PhotoPath(userId, true));
    }

    public void Delete(long userId)
    {
        foreach (var p in new[] { PhotoPath(userId, false), PhotoPath(userId, true) })
            if (File.Exists(p)) File.Delete(p);
    }

    private static void WriteJpeg(SKBitmap square, int size, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White); // flatten transparency (PNG/WebP) onto white
        using var img = SKImage.FromBitmap(square);
        canvas.DrawImage(img, new SKRect(0, 0, size, size), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var snapshot = surface.Snapshot();
        using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, Quality);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) jpeg.SaveTo(fs);
        File.Move(tmp, path, overwrite: true); // never leaves a half-written photo
    }

    private static SKBitmap CropSquare(SKBitmap src)
    {
        int side = Math.Min(src.Width, src.Height);
        var result = new SKBitmap(side, side, SKColorType.Rgba8888, SKAlphaType.Premul);
        src.ExtractSubset(result, SKRectI.Create((src.Width - side) / 2, (src.Height - side) / 2, side, side));
        var copy = result.Copy();
        result.Dispose();
        return copy;
    }

    /// <summary>Applies the EXIF orientation so the photo is upright once the metadata is removed.</summary>
    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return src.Copy();
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height, src.ColorType, src.AlphaType);
        using var c = new SKCanvas(dst);
        float w = src.Width, h = src.Height;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: c.Scale(-1, 1); c.Translate(-w, 0); break;
            case SKEncodedOrigin.BottomRight: c.RotateDegrees(180); c.Translate(-w, -h); break;
            case SKEncodedOrigin.BottomLeft: c.Scale(1, -1); c.Translate(0, -h); break;
            case SKEncodedOrigin.LeftTop: c.RotateDegrees(90); c.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: c.Translate(h, 0); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: c.Translate(h, w); c.RotateDegrees(90); c.Scale(-1, 1); c.Translate(-w, 0); break;
            case SKEncodedOrigin.LeftBottom: c.Translate(0, w); c.RotateDegrees(-90); break;
        }
        using var img = SKImage.FromBitmap(src);
        c.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return dst;
    }
}
