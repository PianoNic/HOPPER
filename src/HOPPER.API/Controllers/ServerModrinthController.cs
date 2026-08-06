using HOPPER.Application.Command.Modrinth;
using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Queries.Modrinth;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>Adding a Modrinth mod to one server, in two steps that cannot be collapsed into one.
    ///
    /// plan says what would be written; install writes exactly the version ids it is handed and
    /// resolves nothing further. That split is the entire guarantee that nothing arrives which the
    /// admin did not see named on screen, and it is why install does not take a project id.
    ///
    /// Admin surface: no [Authorize], the fallback policy covers it, and neither route is
    /// client-facing.</summary>
    [ApiController]
    [Route("api/servers/{id:guid}/modrinth")]
    public class ServerModrinthController(IMediator mediator) : ControllerBase
    {
        /// <summary>Resolves the full set that adding these versions would install: the picks
        /// themselves, every transitive required dependency, the optional ones offered but not taken,
        /// anything bundled inside a parent jar, declared incompatibilities, and whatever could not be
        /// resolved at all.
        ///
        /// A POST although it writes nothing: it takes two arrays of version ids, which stop fitting a
        /// query string as soon as optionals are being ticked on and off. Ticking one resends it in
        /// optionalVersionIds, where it is resolved as a root, so whatever IT requires appears in the
        /// list before the admin can confirm.</summary>
        [HttpPost("plan")]
        [ProducesResponseType(typeof(ModrinthInstallPlanDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Plan(
            Guid id, [FromBody] ModrinthPlanRequest body, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(
                new PlanModrinthInstallQuery(id, body.VersionIds ?? [], body.OptionalVersionIds ?? []),
                cancellationToken);

            return Ok(result);
        }

        /// <summary>Downloads and installs exactly the listed versions.
        ///
        /// 200 with a result object rather than 201: a batch has no single created resource, and a
        /// batch where one jar's hash did not match Modrinth's is a partial success that has to be
        /// reportable per row. 409 is the one all-or-nothing case - a set that is incompatible with
        /// what the server already carries is refused whole and writes nothing.</summary>
        [HttpPost("install")]
        [ProducesResponseType(typeof(ModrinthInstallResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Install(
            Guid id, [FromBody] ModrinthInstallRequest body, CancellationToken cancellationToken = default)
        {
            var items = (body.Items ?? [])
                .Select(i => new ModrinthInstallItem(i.VersionId, i.Replace))
                .ToList();

            var result = await mediator.Send(new InstallModrinthModsCommand(id, items), cancellationToken);
            return Ok(result);
        }
    }

    /// <summary>Ticked optionals arrive separately from the original picks so the dialog can tell the
    /// two apart when it re-renders; the resolver treats both as roots.</summary>
    public record ModrinthPlanRequest(IReadOnlyList<string>? VersionIds, IReadOnlyList<string>? OptionalVersionIds);

    public record ModrinthInstallRequest(IReadOnlyList<ModrinthInstallItemDto>? Items);
}
