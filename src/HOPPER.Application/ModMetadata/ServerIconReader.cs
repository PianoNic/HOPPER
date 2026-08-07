using SkiaSharp;

namespace HOPPER.Application.ModMetadata
{
    public static class ServerIconReader
    {
        public const int Size = 64;

        public const long MaxUploadBytes = 4 * 1024 * 1024;

        public static byte[]? ToServerIcon(Stream source)
        {
            try
            {
                using var buffer = new MemoryStream();
                source.CopyTo(buffer);

                if (buffer.Length == 0 || buffer.Length > MaxUploadBytes)
                    return null;

                buffer.Position = 0;

                using var decoded = SKBitmap.Decode(buffer);
                if (decoded is null || decoded.Width == 0 || decoded.Height == 0)
                    return null;

                using var square = Square(decoded);
                using var image = SKImage.FromBitmap(square);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

                return encoded?.ToArray();
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        // Centre-cropped to a square before the downscale, because Minecraft draws the icon into a
        // square either way and stretching a wide screenshot into it looks like a mistake.
        private static SKBitmap Square(SKBitmap source)
        {
            var edge = Math.Min(source.Width, source.Height);
            var left = (source.Width - edge) / 2;
            var top = (source.Height - edge) / 2;

            var target = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);

            using var canvas = new SKCanvas(target);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                new SKRect(left, top, left + edge, top + edge),
                new SKRect(0, 0, Size, Size),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            return target;
        }
    }
}
