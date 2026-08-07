using System.Text;
using HOPPER.Application.ModMetadata;
using SkiaSharp;

namespace HOPPER.Tests.ModMetadata
{
    public class ServerIconReaderTests
    {
        private static MemoryStream Image(int width, int height, SKEncodedImageFormat format)
        {
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.CornflowerBlue);
                canvas.DrawRect(0, 0, width / 2f, height / 2f, new SKPaint { Color = SKColors.Orange });
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 90);

            return new MemoryStream(data.ToArray());
        }

        private static (int Width, int Height) SizeOf(byte[] png)
        {
            using var bitmap = SKBitmap.Decode(png);
            return (bitmap.Width, bitmap.Height);
        }

        [Test]
        [Arguments(1024, 1024)]
        [Arguments(16, 16)]
        [Arguments(64, 64)]
        public async Task AnySquareImage_ComesBackAs64Square(int width, int height)
        {
            using var source = Image(width, height, SKEncodedImageFormat.Png);

            var icon = ServerIconReader.ToServerIcon(source);

            await Assert.That(icon).IsNotNull();
            await Assert.That(SizeOf(icon!)).IsEqualTo((64, 64));
        }

        [Test]
        public async Task AWideImage_IsCroppedRatherThanSquashed()
        {
            // Minecraft draws the icon into a square either way, so a stretched screenshot would just
            // look like a bug. This only pins the output shape; the crop itself is centred.
            using var source = Image(1920, 1080, SKEncodedImageFormat.Png);

            var icon = ServerIconReader.ToServerIcon(source);

            await Assert.That(icon).IsNotNull();
            await Assert.That(SizeOf(icon!)).IsEqualTo((64, 64));
        }

        [Test]
        public async Task AJpeg_IsAcceptedAndComesBackAsPng()
        {
            using var source = Image(200, 200, SKEncodedImageFormat.Jpeg);

            var icon = ServerIconReader.ToServerIcon(source);

            await Assert.That(icon).IsNotNull();
            await Assert.That(icon![..4]).IsEquivalentTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        }

        [Test]
        public async Task SomethingThatIsNotAnImage_IsRefusedRatherThanStored()
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes("this is not an image at all"));

            await Assert.That(ServerIconReader.ToServerIcon(source)).IsNull();
        }

        [Test]
        public async Task AnEmptyUpload_IsRefused()
        {
            using var source = new MemoryStream();

            await Assert.That(ServerIconReader.ToServerIcon(source)).IsNull();
        }

        [Test]
        public async Task AnUploadOverTheCap_IsRefusedBeforeItIsDecoded()
        {
            using var source = new MemoryStream(new byte[ServerIconReader.MaxUploadBytes + 1]);

            await Assert.That(ServerIconReader.ToServerIcon(source)).IsNull();
        }
    }
}
