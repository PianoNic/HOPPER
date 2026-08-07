using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/icons")]

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

            var claimed = await db.Mods.AsNoTracking()
                .AnyAsync(m => m.IconSha256 == sha256, cancellationToken);

            if (!claimed) return NotFound();

            var stream = blobs.OpenRead(sha256);
            if (stream is null) return NotFound();

            Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            Response.Headers.ETag = $"\"{sha256}\"";

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
