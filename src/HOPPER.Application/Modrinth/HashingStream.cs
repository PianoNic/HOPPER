using System.Security.Cryptography;

namespace HOPPER.Application.Modrinth
{
    public sealed class HashingStream(Stream inner, bool leaveOpen = false) : Stream
    {
        private readonly IncrementalHash _sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        private readonly IncrementalHash _sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        private string? _sha1Hex;
        private string? _sha512Hex;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string Sha1Hex => _sha1Hex ??= Convert.ToHexStringLower(_sha1.GetHashAndReset());

        public string Sha512Hex => _sha512Hex ??= Convert.ToHexStringLower(_sha512.GetHashAndReset());

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if (read > 0)
                Feed(buffer.AsSpan(offset, read));

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (read > 0)
                Feed(buffer.Span[..read]);

            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (read > 0)
                Feed(buffer.AsSpan(offset, read));

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Feed(ReadOnlySpan<byte> bytes)
        {
            _sha1.AppendData(bytes);
            _sha512.AppendData(bytes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!leaveOpen) inner.Dispose();
                _sha1.Dispose();
                _sha512.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
