using System.Text;
using HOPPER.Infrastructure.Services;

namespace HOPPER.Tests.Infrastructure
{
    public class LimitedStreamTests
    {
        private static Stream Source(int length) => new MemoryStream(new byte[length]);

        [Test]
        public async Task Read_ExactlyTheLimit_Succeeds()
        {
            var stream = new LimitedStream(Source(100), 100, "This file");

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            await Assert.That(buffer.Length).IsEqualTo(100L);
            await Assert.That(stream.Consumed).IsEqualTo(100L);
        }

        [Test]
        public async Task Read_OneByteOverTheLimit_ThrowsContentTooLarge()
        {
            var stream = new LimitedStream(Source(101), 100, "This file");

            await Assert.That(async () => await stream.CopyToAsync(new MemoryStream()))
                .Throws<ContentTooLargeException>();
        }

        [Test]
        public async Task ReadAsync_OverTheLimitAcrossManyReads_ThrowsOnTheReadThatCrosses()
        {
            var stream = new LimitedStream(new DripStream(totalLength: 40, chunk: 10), 25, "This file");

            var buffer = new byte[10];
            var reads = 0;

            await Assert.That(async () =>
            {
                while (await stream.ReadAsync(buffer) > 0)
                    reads++;
            }).Throws<ContentTooLargeException>();

            await Assert.That(reads).IsEqualTo(2);
        }

        [Test]
        public async Task ContentTooLarge_IsAnArgumentException_SoExistingHandlersStillCatchIt()
        {
            await Assert.That(new ContentTooLargeException("too big")).IsAssignableTo<ArgumentException>();
        }

        [Test]
        public async Task Message_NamesWhatWasTooLargeAndTheLimit()
        {
            var stream = new LimitedStream(Source(20), 10, "The archive");

            var thrown = await Assert.That(async () => await stream.CopyToAsync(new MemoryStream()))
                .Throws<ContentTooLargeException>();

            await Assert.That(thrown!.Message).Contains("The archive");
            await Assert.That(thrown.Message).Contains("10 byte limit");
        }

        [Test]
        public async Task Read_PassesTheBytesThroughUnchanged()
        {
            var bytes = Encoding.UTF8.GetBytes("pretend this is a forge mod");
            var stream = new LimitedStream(new MemoryStream(bytes), bytes.Length, "This file");

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            await Assert.That(buffer.ToArray()).IsEquivalentTo(bytes);
        }

        private sealed class DripStream(int totalLength, int chunk) : Stream
        {
            private int _served;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _served; set => throw new NotSupportedException(); }
            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                var take = Math.Min(Math.Min(chunk, buffer.Length), totalLength - _served);
                if (take <= 0)
                    return 0;

                buffer[..take].Clear();
                _served += take;
                return take;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(Read(buffer.Span));

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
