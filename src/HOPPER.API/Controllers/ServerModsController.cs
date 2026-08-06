using HOPPER.Application.Command.Mods;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Queries.Mods;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>Admin surface for one server's mod set. No [Authorize] attribute: the fallback policy
    /// already requires an authenticated user, so an endpoint is protected by doing nothing.
    ///
    /// Unlike the client-facing routes this one names the server in the path - an admin works across
    /// servers with one OIDC identity, so the URL is the only place the server can come from.</summary>
    [ApiController]
    [Route("api/servers/{id:guid}/mods")]
    public class ServerModsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ModDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListModsQuery(id), cancellationToken);
            return Ok(result);
        }

        /// <summary>Takes any number of jars at once, and a .zip is expanded server-side into the jars
        /// it holds. This is the drag-a-folder-of-mods case, which is the normal one - nobody sets up
        /// a server one jar at a time.
        ///
        /// 200 with a result object rather than 201: a batch has no single created resource, and a
        /// batch where one file was a duplicate is a partial success that has to be reportable. Per-file
        /// failures ride in the body; only an empty request is a 400.</summary>
        [HttpPost]
        // Forge content mods run to tens of megabytes and a batch is many of them, so the framework's
        // 30 MB default would reject an ordinary drop with an opaque 413 that looks like a network
        // problem. FormOptions.MultipartBodyLengthLimit is raised to match in Program.cs.
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        [ProducesResponseType(typeof(ModUploadResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Upload(Guid id, IFormFileCollection files, CancellationToken cancellationToken = default)
        {
            if (files is null || files.Count == 0)
                return BadRequest(new { error = "No files uploaded." });

            // Opened lazily and disposed after the command returns: a batch of forty jars must not
            // hold forty streams open at once, but the handler reads them in order, so a single
            // list of open streams is the shape the command wants and the list is short-lived.
            var opened = new List<Stream>(files.Count);
            try
            {
                var batch = new List<UploadFile>(files.Count);
                foreach (var file in files)
                {
                    var stream = file.OpenReadStream();
                    opened.Add(stream);
                    batch.Add(new UploadFile(file.FileName, stream));
                }

                var result = await mediator.Send(new UploadModsCommand(id, batch), cancellationToken);
                return Ok(result);
            }
            finally
            {
                foreach (var stream in opened)
                    await stream.DisposeAsync();
            }
        }

        [HttpDelete("{modId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id, Guid modId, CancellationToken cancellationToken = default)
        {
            await mediator.Send(new DeleteModCommand(id, modId), cancellationToken);
            return NoContent();
        }
    }
}
