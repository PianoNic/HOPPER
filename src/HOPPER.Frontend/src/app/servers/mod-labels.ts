export const MOD_SOURCE = {
  manual: 0,
  modrinth: 1,
  curseForge: 2,
} as const;

export const MOD_SIDE = {
  both: 0,
  clientOnly: 1,
  serverOnly: 2,
} as const;

export const MOD_LOADER = {
  unknown: 0,
  forge: 1,
  neoForge: 2,
  fabric: 3,
  quilt: 4,
} as const;

export const PLAN_NODE_KIND = {
  root: 0,
  required: 1,
  optional: 2,
} as const;

export const PLAN_NODE_STATUS = {
  new: 0,
  alreadyInstalled: 1,
  otherVersionInstalled: 2,
  fileNameTaken: 3,
} as const;

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

export function isReplaceable(status: number): boolean {
  return (
    status === PLAN_NODE_STATUS.otherVersionInstalled || status === PLAN_NODE_STATUS.fileNameTaken
  );
}

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

export function modrinthProjectUrl(slugOrId: string | null | undefined): string | null {
  return slugOrId && slugOrId.length > 0 ? `https://modrinth.com/mod/${slugOrId}` : null;
}

export function formatCount(value: number): string {
  if (value < 1000) return `${value}`;
  if (value < 1_000_000) return `${(value / 1000).toFixed(value < 10_000 ? 1 : 0)}K`;
  return `${(value / 1_000_000).toFixed(value < 10_000_000 ? 1 : 0)}M`;
}
