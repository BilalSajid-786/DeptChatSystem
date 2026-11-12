using Android.Graphics;
using Android.Media;

namespace DeptChat.Mobile.Platforms.Android;

/// <summary>
/// Decodes a picture at reduced size and writes a small copy for the chat list.
/// Applies the photo's EXIF rotation (like the normal image viewer does) so phone photos
/// are not shown sideways. Pictures with transparency are kept as PNG, others become JPEG.
/// </summary>
public static class ThumbnailMaker
{
    public static bool TryWrite(byte[] bytes, int maxSidePx, string outputPath)
    {
        Bitmap? decoded = null;
        Bitmap? scaled = null;
        Bitmap? rotated = null;
        try
        {
            // 1. Read only the size, then decode at a power-of-two reduction close to the target.
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) return false;

            var sample = 1;
            while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maxSidePx)
            {
                sample *= 2;
            }

            decoded = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, new BitmapFactory.Options { InSampleSize = sample });
            if (decoded == null) return false;

            // 2. Final exact fit to maxSidePx on the longest side.
            var current = decoded;
            var longest = Math.Max(current.Width, current.Height);
            if (longest > maxSidePx)
            {
                var factor = (float)maxSidePx / longest;
                scaled = Bitmap.CreateScaledBitmap(current, Math.Max(1, (int)(current.Width * factor)), Math.Max(1, (int)(current.Height * factor)), true);
                if (scaled != null) current = scaled;
            }

            // 3. Apply EXIF rotation.
            var degrees = ReadExifRotation(bytes);
            if (degrees != 0)
            {
                var matrix = new Matrix();
                matrix.PostRotate(degrees);
                rotated = Bitmap.CreateBitmap(current, 0, 0, current.Width, current.Height, matrix, true);
                if (rotated != null) current = rotated;
            }

            // 4. Write.
            using var stream = File.Create(outputPath);
            var format = current.HasAlpha ? Bitmap.CompressFormat.Png! : Bitmap.CompressFormat.Jpeg!;
            return current.Compress(format, 85, stream);
        }
        catch
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            return false;
        }
        finally
        {
            rotated?.Recycle();
            scaled?.Recycle();
            decoded?.Recycle();
            rotated?.Dispose();
            scaled?.Dispose();
            decoded?.Dispose();
        }
    }

    private static int ReadExifRotation(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var exif = new ExifInterface(ms);
            var orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal);
            return orientation switch
            {
                (int)Orientation.Rotate90 => 90,
                (int)Orientation.Rotate180 => 180,
                (int)Orientation.Rotate270 => 270,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }
}
