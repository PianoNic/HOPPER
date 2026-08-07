using System.IO.Compression;
using System.Net;
using System.Text;

namespace HOPPER.Tests.Imports
{
    internal sealed class CannedHttp(Func<string, string, HttpResponseMessage> respond)
        : HttpMessageHandler, IHttpClientFactory
    {
        internal sealed record Call(string Url, string Body, string? ApiKey);

        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Calls.Add(new Call(url, body,
                request.Headers.TryGetValues("x-api-key", out var values) ? values.First() : null));

            return respond(url, body);
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        public static HttpResponseMessage Json(HttpStatusCode code, string body) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage Ok(string body) => Json(HttpStatusCode.OK, body);
    }

    internal static class PackArchive
    {
        public static ZipArchive Of(params (string Path, string Content)[] entries)
        {
            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in entries)
                {
                    using var stream = archive.CreateEntry(path).Open();
                    stream.Write(Encoding.UTF8.GetBytes(content));
                }
            }

            buffer.Position = 0;
            return new ZipArchive(buffer, ZipArchiveMode.Read);
        }
    }
}
