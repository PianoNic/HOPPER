namespace HOPPER.Infrastructure.Services
{
    public sealed class ContentTooLargeException(string message) : ArgumentException(message);

    public sealed class LimitedStream(Stream inner, long maxBytes, string what) : Stream
    {
        public long Consumed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => Consumed;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            if (read <= 0)
                return read;

            Consumed += read;

            if (Consumed > maxBytes)
                throw new ContentTooLargeException($"{what} is larger than the {maxBytes} byte limit.");

            return read;
        }
    }
}
