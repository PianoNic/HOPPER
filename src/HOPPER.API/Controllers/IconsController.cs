using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Controllers
{
    /// <summary>
    /// Mod icons, served from the blob store rather than hot-linked. Keeps the dashboard working on
    /// a network that cannot reach Modrinth, and costs the icon's host nothing per page view.
    /// </summary>
    [ApiController]
    [Route("api/icons")]
    // Anonymous, because an <img src> cannot carry a bearer token and the dashboard renders these
    // as images rather than fetching them. What is on offer is a mod's own logo, the same artwork
    // its project page serves to anyone, and only for a sha some mod already claims as its icon -
    // never an arbitrary blob.
    [AllowAnonymous]
    public class IconsController(HopperDbContext db, IBlobStorage blobs) : ControllerBase
    {
        [HttpGet("{sha256}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string sha256, CancellationToken cancellationToken = default)
        {
            if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower))
                return BadRequest(new { error = "Not a sha256." });

            // Only what some mod actually claims as its icon: the blob store also holds every jar,
            // and this endpoint must not become a second way to download them.
            var claimed = await db.Mods.AsNoTracking()
                .AnyAsync(m => m.IconSha256 == sha256, cancellationToken);

            if (!claimed) return NotFound();

            var stream = blobs.OpenRead(sha256);
            if (stream is null) return NotFound();

            // Content-addressed, so it can never go stale.
            Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            Response.Headers.ETag = $"\"{sha256}\"";

            // Sniffed rather than stored: ModIconReader only admits PNG, JPEG and GIF, and telling
            // the browser the wrong one of those three would break the image for no reason.
            return File(stream, ContentType(stream));
        }

        private static string ContentType(Stream stream)
        {
            Span<byte> head = stackalloc byte[4];
            var read = stream.Read(head);
            if (stream.CanSeek) stream.Position = 0;

            if (read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "image/jpeg";
            if (read >= 4 && head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46) return "image/gif";

            return "image/png";
        }
    }
}
