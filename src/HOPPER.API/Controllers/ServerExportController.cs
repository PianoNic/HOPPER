using HOPPER.Application.Queries.Exports;
using HOPPER.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>Downloads a server's mod set as a portable modpack.
    ///
    /// Admin surface, no [Authorize] needed. The exported file is deliberately portable in a way
    /// HOPPER's own distribution is not: a HOPPER blob URL needs this server's bearer token, so no
    /// blob URL and no HOPPER hostname appears anywhere inside any of these three archives. A mod with
    /// Modrinth provenance becomes a manifest entry with its real CDN URL; everything else ships as
    /// bytes.</summary>
    [ApiController]
    [Route("api/servers/{id:guid}/export")]
    public class ServerExportController(IMediator mediator) : ControllerBase
    {
        /// <summary>Format 1 Modrinth (.mrpack), 2 CurseForge (.zip), 3 Prism instance (.zip) - the
        /// same PackFormat numbers the importer already uses, so the dashboard's existing label table
        /// covers them.
        ///
        /// 400 when the server has no Minecraft version, loader or loader version set: all three
        /// formats name an exact platform and there is nothing sensible to guess.</summary>
        [HttpGet]
        // Declared for the OpenAPI document, not for the response: File(...) sets the real content
        // type per format, and ProducesAttribute only rewrites an ObjectResult, which a file result is
        // not. Without it the generator reads the default text/plain + application/json list, picks a
        // JSON mime and hands HttpClient responseType 'json' - and the dashboard then tries to parse a
        // zip as JSON and fails on every successful export. The jar endpoint carries the same
        // attribute for the same reason.
        [Produces("application/octet-stream")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Export(
            Guid id, [FromQuery] PackFormat format = PackFormat.Modrinth, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ExportServerPackQuery(id, format), cancellationToken);

            if (result.Warnings.Count > 0)
            {
                // A header rather than a failure: a server with 200 mods and one missing blob wants
                // 199 mods and a note, not a 500 and no pack. The response body is the archive itself,
                // so there is nowhere else for this to go.
                //
                // Warnings quote mod filenames, which are stored data rather than constants, so the
                // value is flattened to one line and capped. A CR or LF in a header value is a
                // response-splitting primitive, and a header of unbounded length is a denial of
                // service against whatever proxy sits in front.
                var joined = string.Join(" | ", result.Warnings)
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');

                Response.Headers["X-Hopper-Export-Warnings"] =
                    joined.Length > 1000 ? joined[..1000] : joined;
            }

            // The stream owns its scratch file and deletes it on close, which File(...) does once the
            // response has been written.
            return File(result.Content, result.ContentType, result.FileName);
        }
    }
}
