// Wire-side enums for provenance and the Modrinth browser, and the wording the dashboard puts on
// them. Same shape and the same rules as import-labels.ts: HOPPER persists these as ints, the
// generated client types them `number` and nothing else, and this file is the only place the
// mapping lives. Plain functions rather than pipes so a template can reach them through a component
// method and keep the no-arrow-functions rule.
//
// The numbers must stay in step with src/HOPPER.Domain/Enums/*.cs and with
// src/HOPPER.Application/Modrinth/ModrinthPlanModels.cs. An unknown value renders as such rather
// than falling back to the first case: a server one version ahead of the dashboard should read as
// "unknown", not silently as "Uploaded".

/** Mirrors HOPPER.Domain.Enums.ModSource. */
export const MOD_SOURCE = {
  manual: 0,
  modrinth: 1,
  curseForge: 2,
} as const;

/** Mirrors HOPPER.Domain.Enums.ModLoader. */
export const MOD_LOADER = {
  unknown: 0,
  forge: 1,
  neoForge: 2,
  fabric: 3,
  quilt: 4,
} as const;

/** Mirrors HOPPER.Application.Modrinth.PlanNodeKind. */
export const PLAN_NODE_KIND = {
  root: 0,
  required: 1,
  optional: 2,
} as const;

/** Mirrors HOPPER.Application.Modrinth.PlanNodeStatus. */
export const PLAN_NODE_STATUS = {
  new: 0,
  alreadyInstalled: 1,
  otherVersionInstalled: 2,
  fileNameTaken: 3,
} as const;

/** Mirrors HOPPER.Application.Modrinth.ModrinthSearchIndex - the sort order of a result page. */
export const SEARCH_INDEX = {
  relevance: 0,
  downloads: 1,
  follows: 2,
  newest: 3,
  updated: 4,
} as const;

export function modSourceLabel(source: number): string {
  switch (source) {
    case MOD_SOURCE.manual:
      return 'Uploaded';
    case MOD_SOURCE.modrinth:
      return 'Modrinth';
    case MOD_SOURCE.curseForge:
      return 'CurseForge';
    default:
      return 'Unknown';
  }
}

export function modLoaderLabel(loader: number): string {
  switch (loader) {
    case MOD_LOADER.forge:
      return 'Forge';
    case MOD_LOADER.neoForge:
      return 'NeoForge';
    case MOD_LOADER.fabric:
      return 'Fabric';
    case MOD_LOADER.quilt:
      return 'Quilt';
    case MOD_LOADER.unknown:
      return 'Not set';
    default:
      return 'Unknown';
  }
}

/**
 * The name Modrinth knows this loader by, which is what both the search facet and the version
 * filter are keyed on. Null for Unknown, and that null is load-bearing: the browser refuses to
 * search at all without a loader rather than searching every loader at once and offering the admin
 * jars their server cannot load.
 */
export function modLoaderFacet(loader: number): string | null {
  switch (loader) {
    case MOD_LOADER.forge:
      return 'forge';
    case MOD_LOADER.neoForge:
      return 'neoforge';
    case MOD_LOADER.fabric:
      return 'fabric';
    case MOD_LOADER.quilt:
      return 'quilt';
    default:
      return null;
  }
}

/** The reverse, for reading a loader name out of Modrinth's tag list back into HOPPER's enum. */
export function modLoaderFromFacet(name: string): number {
  switch (name.toLowerCase()) {
    case 'forge':
      return MOD_LOADER.forge;
    case 'neoforge':
      return MOD_LOADER.neoForge;
    case 'fabric':
      return MOD_LOADER.fabric;
    case 'quilt':
      return MOD_LOADER.quilt;
    default:
      return MOD_LOADER.unknown;
  }
}

/** Why a row is in the plan. "Selected" covers a ticked optional too - it was resolved as a root. */
export function planNodeKindLabel(kind: number): string {
  switch (kind) {
    case PLAN_NODE_KIND.root:
      return 'Selected';
    case PLAN_NODE_KIND.required:
      return 'Required';
    case PLAN_NODE_KIND.optional:
      return 'Optional';
    default:
      return 'Unknown';
  }
}

export function planNodeStatusLabel(status: number): string {
  switch (status) {
    case PLAN_NODE_STATUS.new:
      return 'New';
    case PLAN_NODE_STATUS.alreadyInstalled:
      return 'Already installed';
    case PLAN_NODE_STATUS.otherVersionInstalled:
      return 'Other version installed';
    case PLAN_NODE_STATUS.fileNameTaken:
      return 'Filename taken';
    default:
      return 'Unknown';
  }
}

/**
 * The sentence under a row that is not New: what is already on the server, and therefore what the
 * admin has to decide. Only the last two are decisions - the other two are statements.
 */
export function planNodeStatusDetail(status: number): string {
  switch (status) {
    case PLAN_NODE_STATUS.alreadyInstalled:
      return 'This exact version is already on this server, so it is not downloaded again.';
    case PLAN_NODE_STATUS.otherVersionInstalled:
      return 'Another version of this mod is on this server. It is skipped unless you tick Replace, which removes the old jar.';
    case PLAN_NODE_STATUS.fileNameTaken:
      return 'A different file already has this name on this server. It is skipped unless you tick Replace.';
    default:
      return '';
  }
}

/** True for the two statuses that offer a Replace tick. Both default to skipping. */
export function isReplaceable(status: number): boolean {
  return (
    status === PLAN_NODE_STATUS.otherVersionInstalled || status === PLAN_NODE_STATUS.fileNameTaken
  );
}

/** How a release channel reads on a badge. Modrinth publish these three, lowercase. */
export function versionTypeLabel(versionType: string | null | undefined): string {
  switch ((versionType ?? '').toLowerCase()) {
    case 'release':
      return 'Release';
    case 'beta':
      return 'Beta';
    case 'alpha':
      return 'Alpha';
    default:
      return 'Unknown';
  }
}

/**
 * The project page on modrinth.com. Takes whichever identifier the caller happens to have: a search
 * hit carries a slug, a stored Mod row carries only the base62 project id, and modrinth.com/mod/
 * resolves both. Null in means no link rather than a link to /mod/undefined - a Manual mod has no
 * project at all.
 */
export function modrinthProjectUrl(slugOrId: string | null | undefined): string | null {
  return slugOrId && slugOrId.length > 0 ? `https://modrinth.com/mod/${slugOrId}` : null;
}

/**
 * Compact download counts. Modrinth's numbers run to nine digits, and "67.6M" is the difference
 * between a card that reads at a glance and one that does not.
 */
export function formatCount(value: number): string {
  if (value < 1000) return `${value}`;
  if (value < 1_000_000) return `${(value / 1000).toFixed(value < 10_000 ? 1 : 0)}K`;
  return `${(value / 1_000_000).toFixed(value < 10_000_000 ? 1 : 0)}M`;
}
