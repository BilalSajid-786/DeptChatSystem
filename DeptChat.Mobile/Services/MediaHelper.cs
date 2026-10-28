namespace DeptChat.Mobile.Services;

#if ANDROID
using Android.Graphics;
#endif

public static class MediaHelper
{
    public static async Task<string> CompressImageAsync(string sourcePath, int maxDimension = 1280, int quality = 75)
    {
#if ANDROID
        return await Task.Run(() =>
        {
            try
            {
                using var sourceBitmap = BitmapFactory.DecodeFile(sourcePath);
                if (sourceBitmap == null) return sourcePath;

                int originalWidth = sourceBitmap.Width;
                int originalHeight = sourceBitmap.Height;

                int targetWidth = originalWidth;
                int targetHeight = originalHeight;

                if (originalWidth > maxDimension || originalHeight > maxDimension)
                {
                    if (originalWidth > originalHeight)
                    {
                        targetWidth = maxDimension;
                        targetHeight = (int)((double)originalHeight / originalWidth * maxDimension);
                    }
                    else
                    {
                        targetHeight = maxDimension;
                        targetWidth = (int)((double)originalWidth / originalHeight * maxDimension);
                    }
                }

                using var scaledBitmap = (targetWidth == originalWidth && targetHeight == originalHeight)
                    ? sourceBitmap
                    : Bitmap.CreateScaledBitmap(sourceBitmap, targetWidth, targetHeight, true);

                var destFileName = $"img_{Guid.NewGuid():N}.jpg";
                var destPath = System.IO.Path.Combine(FileSystem.CacheDirectory, destFileName);

                using (var outStream = System.IO.File.OpenWrite(destPath))
                {
                    scaledBitmap.Compress(Bitmap.CompressFormat.Jpeg!, quality, outStream);
                }

                return destPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaHelper] Compress error: {ex.Message}");
                return sourcePath;
            }
        });
#else
        return await Task.FromResult(sourcePath);
#endif
    }
}
