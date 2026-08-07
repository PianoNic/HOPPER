using HOPPER.API.Auth;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Controllers
{
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
        public async Task<IActionResult> Get(string sha256, [FromQuery] string? side = null, CancellationToken cancellationToken = default)
        {
            if (!ModSideRules.TryParse(side, out var syncSide))
                return BadRequest(new { error = "side must be 'client' or 'server'." });

            if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower))
                return BadRequest(new { error = "Not a sha256." });

            var serverId = User.ServerId();
            var mod = await db.Mods.AsNoTracking()
                .Where(ModSideRules.ReachesExpression(syncSide))
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.Sha256 == sha256, cancellationToken);

            if (mod is null)
                return NotFound();

            var stream = blobs.OpenRead(sha256);
            if (stream is null)
                return NotFound();

            Response.Headers.CacheControl = "private, max-age=31536000, immutable";

            Response.Headers.ETag = $"\"{sha256}\"";

            return File(stream, "application/java-archive", mod.FileName);
        }
    }
}
