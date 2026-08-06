using HOPPER.API.Auth;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Controllers
{
    /// <summary>Streams jar bytes by content address. No mediator here: this is a single indexed read
    /// plus a file stream, which is the case KRINT's BackupsController.Download handles the same way.</summary>
    [ApiController]
    [Route("api/blobs")]
    [Authorize(AuthenticationSchemes = ClientTokenDefaults.AuthenticationScheme)]
    public class BlobsController(HopperDbContext db, IBlobStorage blobs) : ControllerBase
    {
        [HttpGet("{sha256}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string sha256, CancellationToken cancellationToken = default)
        {
            // Reject anything that is not exactly 64 lowercase hex characters before it can reach the
            // storage layer. Uppercase is rejected rather than folded so one jar has exactly one URL.
            if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower))
                return BadRequest(new { error = "Not a sha256." });

            // Resolve through the index rather than the filesystem: a blob whose last Mod row is gone
            // must be unreachable even if the file survived a failed delete. The row also supplies the
            // filename for the download header.
            //
            // Scoped to the caller's server, and this is the second half of tenant isolation. The
            // file on disk is shared across servers by design, but a token only reaches it through a
            // row on its own server. A blob belonging solely to another server answers 404, not 403 -
            // a client has no business learning that some other server's mod set contains that hash.
            var serverId = User.ServerId();
            var mod = await db.Mods.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.Sha256 == sha256, cancellationToken);

            if (mod is null)
                return NotFound();

            var stream = blobs.OpenRead(sha256);
            if (stream is null)
                return NotFound();

            // Content-addressed, so this URL can never answer with different bytes. That makes the
            // usual caching risk impossible and a one-year immutable lifetime simply correct: a
            // changed mod is a changed hash is a different URL.
            //
            // "private" rather than "public" on purpose. The response is still gated by the client
            // token and scoped to one server, so a shared cache holding it would serve one server's
            // jar to another server's client. Putting a CDN in front of this endpoint therefore
            // needs the authorization dropped first - that is a deliberate decision about the
            // security model, not something this header should quietly enable.
            Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            // The hash IS the entity tag. Costs nothing and lets a client revalidate with a
            // conditional request instead of pulling the whole jar again.
            Response.Headers.ETag = $"\"{sha256}\"";

            return File(stream, "application/java-archive", mod.FileName);
        }
    }
}
