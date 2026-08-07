import { ModLoader } from '../api/model/modLoader';
import { ModSide } from '../api/model/modSide';
import { ModSource } from '../api/model/modSource';
import { PlanNodeKind } from '../api/model/planNodeKind';
import { PlanNodeStatus } from '../api/model/planNodeStatus';

export function modSideLabel(side: number): string {
  switch (side) {
    case ModSide.ClientOnly:
      return 'Client only';
    case ModSide.ServerOnly:
      return 'Server only';
    default:
      return 'Both';
  }
}

export function modSourceLabel(source: number): string {
  switch (source) {
    case ModSource.Manual:
      return 'Uploaded';
    case ModSource.Modrinth:
      return 'Modrinth';
    case ModSource.CurseForge:
      return 'CurseForge';
    default:
      return 'Unknown';
  }
}

export function modLoaderLabel(loader: number): string {
  switch (loader) {
    case ModLoader.Forge:
      return 'Forge';
    case ModLoader.NeoForge:
      return 'NeoForge';
    case ModLoader.Fabric:
      return 'Fabric';
    case ModLoader.Quilt:
      return 'Quilt';
    case ModLoader.Unknown:
      return 'Not set';
    default:
      return 'Unknown';
  }
}

export function modLoaderFacet(loader: number): string | null {
  switch (loader) {
    case ModLoader.Forge:
      return 'forge';
    case ModLoader.NeoForge:
      return 'neoforge';
    case ModLoader.Fabric:
      return 'fabric';
    case ModLoader.Quilt:
      return 'quilt';
    default:
      return null;
  }
}

export function modLoaderFromFacet(name: string): number {
  switch (name.toLowerCase()) {
    case 'forge':
      return ModLoader.Forge;
    case 'neoforge':
      return ModLoader.NeoForge;
    case 'fabric':
      return ModLoader.Fabric;
    case 'quilt':
      return ModLoader.Quilt;
    default:
      return ModLoader.Unknown;
  }
}

export function planNodeKindLabel(kind: number): string {
  switch (kind) {
    case PlanNodeKind.Root:
      return 'Selected';
    case PlanNodeKind.Required:
      return 'Required';
    case PlanNodeKind.Optional:
      return 'Optional';
    default:
      return 'Unknown';
  }
}

export function planNodeStatusLabel(status: number): string {
  switch (status) {
    case PlanNodeStatus.New:
      return 'New';
    case PlanNodeStatus.AlreadyInstalled:
      return 'Already installed';
    case PlanNodeStatus.OtherVersionInstalled:
      return 'Other version installed';
    case PlanNodeStatus.FileNameTaken:
      return 'Filename taken';
    default:
      return 'Unknown';
  }
}

export function planNodeStatusDetail(status: number): string {
  switch (status) {
    case PlanNodeStatus.AlreadyInstalled:
      return 'This exact version is already on this server, so it is not downloaded again.';
    case PlanNodeStatus.OtherVersionInstalled:
      return 'Another version of this mod is on this server. It is skipped unless you tick Replace, which removes the old jar.';
    case PlanNodeStatus.FileNameTaken:
      return 'A different file already has this name on this server. It is skipped unless you tick Replace.';
    default:
      return '';
  }
}

export function isReplaceable(status: number): boolean {
  return (
    status === PlanNodeStatus.OtherVersionInstalled || status === PlanNodeStatus.FileNameTaken
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
