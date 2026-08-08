using SkiaSharp;

namespace HOPPER.Application.ModMetadata
{
    public static class ServerIconReader
    {
        public const int Size = 64;

        public const long MaxUploadBytes = 4 * 1024 * 1024;

        public const long MaxPixels = 4096L * 4096L;

        public static byte[]? ToServerIcon(Stream source)
        {
            try
            {
                using var buffer = new MemoryStream();
                source.CopyTo(buffer);

                if (buffer.Length == 0 || buffer.Length > MaxUploadBytes)
                    return null;

                buffer.Position = 0;

                using var codec = SKCodec.Create(buffer);
                if (codec is null)
                    return null;

                var declared = codec.Info;
                if (declared.Width <= 0 || declared.Height <= 0)
                    return null;

                // A flat-colour PNG of 30000x30000 fits in a few hundred KiB and asks Skia for 3.6 GB,
                // so the dimensions have to be refused from the header rather than after the decode.
                if ((long)declared.Width * declared.Height > MaxPixels)
                    return null;

                using var decoded = DecodeNoLargerThanNeeded(codec);
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

        // Codecs only honour a handful of discrete scales, so this is a ceiling on what gets
        // allocated rather than the final size - the crop below still does the exact fit.
        private static SKBitmap? DecodeNoLargerThanNeeded(SKCodec codec)
        {
            var edge = Math.Min(codec.Info.Width, codec.Info.Height);
            var scaled = codec.GetScaledDimensions(Math.Min(1f, (float)Size / edge));

            if (scaled.Width <= 0 || scaled.Height <= 0)
                return null;

            return SKBitmap.Decode(
                codec,
                new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
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
