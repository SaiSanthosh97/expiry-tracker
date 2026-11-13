using Android.Graphics;
using Android.Media;
using Orientation = Android.Media.Orientation;

namespace HomeTracker.Device.Vision;

/// <summary>
/// Saves a smaller copy of a camera photo for keeping: turned the right way up, longest side at most
/// <c>maxSide</c> pixels, JPEG. A 12-megapixel photo (3–5 MB) becomes roughly 100–200 KB.
/// </summary>
public static class PhotoShrinker
{
    /// <returns>True when the copy was written.</returns>
    public static bool SaveAsJpeg(string sourcePath, string targetPath, int maxSide = 1280, int quality = 80)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(sourcePath, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            return false;
        }

        // Decode at a power-of-two reduction first, so a large photo never has to fit in memory at full size.
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maxSide)
        {
            sample *= 2;
        }

        using var decoded = BitmapFactory.DecodeFile(sourcePath, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null)
        {
            return false;
        }

        var matrix = new Matrix();
        var scale = Math.Min(1f, (float)maxSide / Math.Max(decoded.Width, decoded.Height));
        matrix.PostScale(scale, scale);
        var rotation = RotationOf(sourcePath);
        if (rotation != 0)
        {
            matrix.PostRotate(rotation);
        }

        using var output = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
        using (var stream = File.Create(targetPath))
        {
            output.Compress(Bitmap.CompressFormat.Jpeg!, quality, stream);
        }

        if (!ReferenceEquals(output, decoded))
        {
            output.Recycle();
        }

        decoded.Recycle();
        return true;
    }

    /// <summary>Camera photos are stored sideways with a note saying how to turn them; this reads that note.</summary>
    private static int RotationOf(string path)
    {
        try
        {
            var orientation = new ExifInterface(path).GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal);
            return orientation switch
            {
                (int)Orientation.Rotate90 => 90,
                (int)Orientation.Rotate180 => 180,
                (int)Orientation.Rotate270 => 270,
                _ => 0,
            };
        }
        catch (Java.IO.IOException)
        {
            return 0;
        }
    }
}
