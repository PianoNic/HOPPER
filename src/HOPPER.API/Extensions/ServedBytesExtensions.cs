using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Extensions
{
    public static class ServedBytes
    {
        private const string Key = "hopper.served-bytes";

        public static void Bill(this HttpContext context, Guid serverId) => context.Items[Key] = serverId;

        public static Guid? Billed(this HttpContext context) =>
            context.Items.TryGetValue(Key, out var value) && value is Guid serverId ? serverId : null;
    }

    // A response can be a 304, a range, or a download the player cancelled halfway, so the blob's
    // own length is not what went out. This counts what the body actually wrote.
    internal sealed class CountingStream(Stream inner) : Stream
    {
        public long Written { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Written += buffer.Length;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            Written += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    public static class ServedBytesMiddlewareExtensions
    {
        public static IApplicationBuilder UseServedBytes(this IApplicationBuilder app) =>
            app.UseWhen(Downloads, branch => branch.Use(CountAsync));

        private static bool Downloads(HttpContext context)
        {
            var path = context.Request.Path;

            return path.StartsWithSegments("/api/blobs")
                || (path.Value?.EndsWith("/jar", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private static async Task CountAsync(HttpContext context, RequestDelegate next)
        {
            var original = context.Response.Body;
            var counting = new CountingStream(original);
            context.Response.Body = counting;

            try
            {
                await next(context);
            }
            finally
            {
                context.Response.Body = original;
                await RecordAsync(context, counting.Written);
            }
        }

        // Its own scope and its own cancellation: the request scope is on its way out by now, and a
        // client that hangs up mid-download still served the bytes it got.
        private static async Task RecordAsync(HttpContext context, long written)
        {
            if (written <= 0 || context.Billed() is not { } serverId)
                return;

            try
            {
                using var scope = context.RequestServices
                    .GetRequiredService<IServiceScopeFactory>().CreateScope();

                await scope.ServiceProvider.GetRequiredService<HopperDbContext>().Servers
                    .Where(s => s.Id == serverId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.BytesServed, x => x.BytesServed + written),
                        CancellationToken.None);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ObjectDisposedException)
            {
            }
        }
    }
}
