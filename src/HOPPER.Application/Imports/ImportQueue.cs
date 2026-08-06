using System.Threading.Channels;

namespace HOPPER.Application.Imports
{
    /// <summary>Hands accepted import ids to the background worker. In-process and unbounded: HOPPER
    /// is one instance with one admin, so a durable queue would be infrastructure with no failure it
    /// protects against - and an import whose ids were lost to a restart is recoverable by pressing
    /// the button again, which is why the ModImport row is written before anything is enqueued.</summary>
    public interface IImportQueue
    {
        void Enqueue(Guid importId);

        IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
    }

    public class ImportQueue : IImportQueue
    {
        // One reader: the worker. Imports run one at a time on purpose - two concurrent 340-file packs
        // would saturate the same CDN and the same disk for no gain.
        private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
            new UnboundedChannelOptions { SingleReader = true });

        public void Enqueue(Guid importId) => _channel.Writer.TryWrite(importId);

        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
