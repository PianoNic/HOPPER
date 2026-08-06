using System.Threading.Channels;

namespace HOPPER.Application.Imports
{
    public interface IImportQueue
    {
        void Enqueue(Guid importId);

        IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
    }

    public class ImportQueue : IImportQueue
    {
        private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
            new UnboundedChannelOptions { SingleReader = true });

        public void Enqueue(Guid importId) => _channel.Writer.TryWrite(importId);

        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
