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
            var mod = await db.Mods.AsNoTracking().FirstOrDefaultAsync(m => m.Sha256 == sha256, cancellationToken);
            if (mod is null)
                return NotFound();

            var stream = blobs.OpenRead(sha256);
            if (stream is null)
                return NotFound();

            return File(stream, "application/java-archive", mod.FileName);
        }
    }
}
