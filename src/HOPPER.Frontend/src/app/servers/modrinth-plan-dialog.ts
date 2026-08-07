import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  Injectable,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, debounceTime, EMPTY, firstValueFrom, switchMap } from 'rxjs';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideCircleCheck,
  lucideCircleHelp,
  lucideDownload,
  lucidePackage,
  lucideTriangleAlert,
} from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { ButtonLoading } from '../shared/directives/button-loading';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogService,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { ServerModrinthService } from '../api/api/serverModrinth.service';
import { ModrinthInstallPlanDto } from '../api/model/modrinthInstallPlanDto';
import { ModrinthInstallResultDto } from '../api/model/modrinthInstallResultDto';
import { ModrinthPlanNodeDto } from '../api/model/modrinthPlanNodeDto';
import { formatBytes, messageFrom, toNumber } from '../shared/utils/format';
import { isReplaceable, planNodeStatusDetail, planNodeStatusLabel } from './mod-labels';
import { PlanNodeStatus } from '../api/model/planNodeStatus';

export type ModrinthPlanDialogContext = {
  serverId: string;
  rootVersionIds: ReadonlyArray<string>;
  rootTitles: ReadonlyArray<string>;
};

const REPLAN_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-modrinth-plan-dialog',
  imports: [
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmSpinnerImports,
    ButtonLoading,
    HlmCheckboxImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmLabelImports,
  ],
  providers: [
    provideIcons({
      lucideCircleAlert,
      lucideCircleCheck,
      lucideCircleHelp,
      lucideDownload,
      lucidePackage,
      lucideTriangleAlert,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Add to this server</h3>
      <p hlmDialogDescription>{{ headline() }}</p>
    </hlm-dialog-header>
    @if (result() !== null) {
      <div class="max-h-96 min-h-0 flex-1 overflow-auto text-sm">
        <div class="flex flex-col gap-4">
          @if (installedCount() > 0) {
            <section class="flex flex-col gap-1">
              <h4 class="flex items-center gap-1.5 text-sm font-medium">
                <ng-icon name="lucideCircleCheck" size="14" />
                Added {{ installedCount() }}
              </h4>
              @for (m of result()!.installed; track m.id) {
                <p class="text-muted-foreground pl-5 font-mono text-xs">{{ m.fileName }}</p>
              }
            </section>
          }

          @if (result()!.replaced.length > 0) {
            <section class="flex flex-col gap-1">
              <h4 class="text-sm font-medium">Replaced {{ result()!.replaced.length }}</h4>
              @for (m of result()!.replaced; track m.id) {
                <p class="text-muted-foreground pl-5 font-mono text-xs">{{ m.fileName }}</p>
              }
            </section>
          }

          @if (result()!.adopted.length > 0) {
            <section class="flex flex-col gap-1">
              <h4 class="text-sm font-medium">Already had these bytes</h4>
              @for (a of result()!.adopted; track a.mod.id) {
                <p class="text-muted-foreground pl-5 text-xs">{{ a.message }}</p>
              }
            </section>
          }

          @if (result()!.skipped.length > 0) {
            <section class="flex flex-col gap-1">
              <h4 class="text-sm font-medium">Skipped {{ result()!.skipped.length }}</h4>
              @for (s of result()!.skipped; track s.name) {
                <p class="text-muted-foreground pl-5 text-xs">
                  <span class="font-mono">{{ s.name }}</span> - {{ s.reason }}
                </p>
              }
            </section>
          }

          @if (result()!.failed.length > 0) {
            <section class="flex flex-col gap-1">
              <h4 class="text-destructive flex items-center gap-1.5 text-sm font-medium">
                <ng-icon name="lucideCircleAlert" size="14" />
                Failed {{ result()!.failed.length }}
              </h4>
              @for (f of result()!.failed; track f.name) {
                <p class="text-muted-foreground pl-5 text-xs">
                  <span class="font-mono">{{ f.name }}</span> - {{ f.error }}
                </p>
              }
            </section>
          }
        </div>
      </div>
      <div class="flex justify-end gap-2">
        <button hlmBtn type="button" (click)="finish()">Done</button>
      </div>
    } @else {
      <div class="max-h-[26rem] min-h-0 flex-1 overflow-auto">
        @if (failed()) {
          <div class="flex h-full flex-col items-center justify-center gap-2 p-10 text-center">
            <ng-icon
              name="lucideTriangleAlert"
              size="28"
              class="text-muted-foreground opacity-60"
            />
            <button hlmBtn variant="outline" size="sm" type="button" (click)="retry()">
              Try again
            </button>
          </div>
        } @else if (loading() && plan() === null) {
          <p class="text-muted-foreground flex items-center justify-center gap-2 p-6 text-sm">
            <hlm-spinner aria-label="Resolving dependencies" />
            Resolving dependencies
          </p>
        } @else if (plan(); as p) {
          <div class="flex flex-col gap-4" [class.opacity-60]="loading()">
            <section class="flex flex-col gap-1.5">
              <h4 class="flex items-center gap-1.5 text-sm font-medium">
                <ng-icon name="lucideDownload" size="14" />
                Will be added
                <span class="text-muted-foreground font-normal">{{ addSummary() }}</span>
              </h4>
              @if (newNodes().length === 0) {
                <p class="text-muted-foreground pl-5 text-xs">
                  Nothing new - everything picked is already on this server.
                </p>
              } @else {
                @for (n of newNodes(); track n.versionId) {
                  <div class="flex items-start gap-2 rounded-md border p-2">
                    <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                      <div class="flex flex-wrap items-center gap-1.5">
                        <span class="truncate text-sm font-medium">{{ n.title }}</span>
                        <span class="text-muted-foreground text-xs">{{ n.versionNumber }}</span>
                        @if (transitive(n)) {
                          <span hlmBadge variant="outline" class="text-xs">Required</span>
                        }
                        @if (n.pinned) {
                          <span hlmBadge variant="outline" class="text-xs">pinned version</span>
                        }
                        @if (n.prerelease) {
                          <span hlmBadge variant="destructive" class="text-xs">
                            {{ n.versionType }}
                          </span>
                        }
                      </div>
                      <span class="text-muted-foreground truncate font-mono text-xs">
                        {{ n.fileName }}
                      </span>
                      @if (n.requiredBy.length > 0) {
                        <span class="text-muted-foreground text-xs">
                          required by {{ requiredBy(n) }}
                        </span>
                      }
                    </div>
                    <span class="text-muted-foreground shrink-0 font-mono text-xs">
                      {{ size(n) }}
                    </span>
                  </div>
                }
              }
            </section>
            @if (p.optional.length > 0) {
              <section class="flex flex-col gap-1.5">
                <h4 class="text-sm font-medium">
                  Optional
                  <span class="text-muted-foreground font-normal">
                    · {{ p.optional.length }} offered, none added unless you tick it
                  </span>
                </h4>
                @for (n of p.optional; track n.versionId) {
                  <div class="flex items-start gap-2 rounded-md border p-2">
                    <hlm-checkbox
                      class="mt-0.5"
                      [inputId]="optionalId(n)"
                      [checked]="isTicked(n)"
                      [disabled]="installing()"
                      (checkedChange)="toggleOptional(n)"
                    />
                    <label
                      hlmLabel
                      class="flex min-w-0 flex-1 flex-col items-start gap-0.5"
                      [attr.for]="optionalId(n)"
                    >
                      <span class="flex flex-wrap items-center gap-1.5">
                        <span class="truncate text-sm font-medium">{{ n.title }}</span>
                        <span class="text-muted-foreground text-xs">{{ n.versionNumber }}</span>
                      </span>
                      <span class="text-muted-foreground truncate font-mono text-xs font-normal">
                        {{ n.fileName }}
                      </span>
                    </label>
                    <span class="text-muted-foreground shrink-0 font-mono text-xs">
                      {{ size(n) }}
                    </span>
                  </div>
                }
              </section>
            }

            @if (existingNodes().length > 0) {
              <section class="flex flex-col gap-1.5">
                <h4 class="text-sm font-medium">Already on this server</h4>
                @for (n of existingNodes(); track n.versionId) {
                  <div class="flex flex-col gap-1 rounded-md border p-2">
                    <div class="flex items-start gap-2">
                      <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                        <div class="flex flex-wrap items-center gap-1.5">
                          <span class="text-muted-foreground truncate text-sm">{{ n.title }}</span>
                          <span hlmBadge variant="outline" class="text-xs">
                            {{ statusLabel(n) }}
                          </span>
                        </div>
                        <span class="text-muted-foreground text-xs">{{ statusDetail(n) }}</span>
                      </div>
                    </div>
                    @if (replaceable(n)) {
                      <div class="flex items-center gap-2 pt-1">
                        <hlm-checkbox
                          [inputId]="replaceId(n)"
                          [checked]="isReplacing(n)"
                          [disabled]="installing()"
                          (checkedChange)="toggleReplace(n)"
                        />
                        <label hlmLabel class="text-xs" [attr.for]="replaceId(n)">
                          Replace with {{ n.versionNumber }} - the old jar is removed
                        </label>
                      </div>
                    }
                  </div>
                }
              </section>
            }

            @if (p.incompatible.length > 0) {
              <section class="flex flex-col gap-1.5">
                <h4
                  class="flex items-center gap-1.5 text-sm font-medium"
                  [class.text-destructive]="p.blocked"
                >
                  <ng-icon name="lucideTriangleAlert" size="14" />
                  Incompatible
                </h4>
                @for (i of p.incompatible; track i.projectId + i.declaredBy) {
                  <p class="pl-5 text-xs" [class.text-destructive]="i.applies">
                    @if (i.applies) {
                      {{ i.declaredBy }} declares {{ incompatibleName(i.title, i.projectId) }}
                      incompatible, and it is on this server.
                    } @else {
                      <span class="text-muted-foreground">
                        {{ i.declaredBy }} declares
                        {{ incompatibleName(i.title, i.projectId) }} incompatible. That mod is not on
                        this server, so nothing is blocked.
                      </span>
                    }
                  </p>
                }
              </section>
            }

            @if (p.unresolvable.length > 0) {
              <section class="flex flex-col gap-1.5">
                <h4 class="flex items-center gap-1.5 text-sm font-medium">
                  <ng-icon name="lucideCircleHelp" size="14" />
                  Cannot be resolved automatically
                </h4>
                @for (u of p.unresolvable; track u.name + u.requestedBy) {
                  <p class="text-muted-foreground pl-5 text-xs">
                    <span class="font-mono">{{ u.name }}</span> - {{ u.reason }}, needed by
                    {{ u.requestedBy }}. Add it by hand if this server needs it.
                  </p>
                }
              </section>
            }

            @if (p.embedded.length > 0) {
              <section class="flex flex-col gap-1.5">
                <h4 class="flex items-center gap-1.5 text-sm font-medium">
                  <ng-icon name="lucidePackage" size="14" />
                  Bundled, not added
                </h4>
                @for (e of p.embedded; track e.projectId + e.bundledBy) {
                  <p class="text-muted-foreground pl-5 text-xs">
                    {{ incompatibleName(e.title, e.projectId) }} is already inside
                    {{ e.bundledBy }}.
                  </p>
                }
              </section>
            }

            @if (p.warnings.length > 0) {
              <section class="flex flex-col gap-1">
                @for (w of p.warnings; track w) {
                  <p class="text-muted-foreground text-xs">{{ w }}</p>
                }
              </section>
            }
          </div>
        }
      </div>
      <div class="flex items-center justify-end gap-2">
        <button hlmBtn variant="ghost" type="button" [disabled]="installing()" (click)="cancel()">
          Cancel
        </button>
        @if (!failed()) {
          <button
            hlmBtn
            type="button"
            [disabled]="!canInstall()"
            [loading]="installing() || loading()"
            (click)="install()"
          >
            {{ confirmLabel() }}
          </button>
        }
      </div>
    }
  `,
})
export class ModrinthPlanDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(ServerModrinthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ctx = injectBrnDialogContext<ModrinthPlanDialogContext>();

  protected readonly plan = signal<ModrinthInstallPlanDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly installing = signal(false);
  protected readonly failed = signal(false);
  protected readonly result = signal<ModrinthInstallResultDto | null>(null);

  private readonly ticked = signal<ReadonlyArray<string>>([]);
  private readonly retryTick = signal(0);

  private readonly replacing = signal<ReadonlyArray<string>>([]);

  private readonly replanKey = computed(() => ({
    optional: this.ticked(),
    tick: this.retryTick(),
  }));

  protected readonly newNodes = computed(() =>
    (this.plan()?.nodes ?? []).filter((n) => n.status === PlanNodeStatus.New),
  );

  protected readonly existingNodes = computed(() =>
    (this.plan()?.nodes ?? []).filter((n) => n.status !== PlanNodeStatus.New),
  );

  protected readonly installedCount = computed(() => this.result()?.installed.length ?? 0);

  protected readonly headline = computed(() => {
    const subject = this.ctx.rootTitles.join(', ');
    const p = this.plan();
    if (p === null) return `Working out what ${subject} needs…`;

    const extra = this.newNodes().filter((n) => this.transitive(n)).length;
    if (extra === 0) {
      return `${subject} needs no other mods. ${this.addSummaryPlain()}`;
    }
    return `Adding ${subject} also installs ${extra} required mod${extra === 1 ? '' : 's'}. ${this.addSummaryPlain()}`;
  });

  protected readonly addSummary = computed(() => {
    const nodes = this.newNodes();
    if (nodes.length === 0) return '';
    return `· ${this.addSummaryPlain()}`;
  });

  protected readonly canInstall = computed(() => {
    const p = this.plan();
    if (p === null || this.loading() || this.installing()) return false;
    if (p.blocked) return false;
    return this.newNodes().length > 0 || this.replacing().length > 0;
  });

  protected readonly confirmLabel = computed(() => {
    if (this.installing()) return 'Downloading from Modrinth';
    if (this.loading()) return 'Resolving';

    const p = this.plan();
    if (p?.blocked) return 'Cannot add - incompatible mods';

    const count = this.newNodes().length;
    if (count === 0) return 'Nothing to add';
    return `Add ${count} mod${count === 1 ? '' : 's'} (${formatBytes(this.addBytes())})`;
  });

  constructor() {
    toObservable(this.replanKey)
      .pipe(
        debounceTime(REPLAN_DEBOUNCE_MS),
        switchMap(({ optional }) => {
          this.loading.set(true);
          this.failed.set(false);
          return this.api
            .apiServersIdModrinthPlanPost(this.ctx.serverId, {
              versionIds: [...this.ctx.rootVersionIds],
              optionalVersionIds: [...optional],
            })
            .pipe(
              catchError((err: unknown) => {
                toast.error(messageFrom(err, 'Failed to resolve the dependencies of this mod'));
                this.failed.set(true);
                this.loading.set(false);
                return EMPTY;
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((plan) => {
        this.plan.set(plan);

        const replaceable = new Set(
          plan.nodes.filter((n) => isReplaceable(n.status)).map((n) => n.versionId),
        );
        this.replacing.update((ids) => ids.filter((id) => replaceable.has(id)));
        this.failed.set(false);
        this.loading.set(false);
      });
  }

  protected retry(): void {
    this.failed.set(false);
    this.loading.set(true);
    this.retryTick.update((tick) => tick + 1);
  }

  protected requiredBy(node: ModrinthPlanNodeDto): string {
    return node.requiredBy.join(', ');
  }

  protected transitive(node: ModrinthPlanNodeDto): boolean {
    return toNumber(node.depth) > 0;
  }

  protected size(node: ModrinthPlanNodeDto): string {
    return formatBytes(toNumber(node.fileSize));
  }

  protected statusLabel(node: ModrinthPlanNodeDto): string {
    return planNodeStatusLabel(node.status);
  }

  protected statusDetail(node: ModrinthPlanNodeDto): string {
    return planNodeStatusDetail(node.status);
  }

  protected replaceable(node: ModrinthPlanNodeDto): boolean {
    return isReplaceable(node.status);
  }

  protected incompatibleName(title: string | null | undefined, projectId: string): string {
    return title && title.length > 0 ? title : projectId;
  }

  protected optionalId(node: ModrinthPlanNodeDto): string {
    return `optional-${node.versionId}`;
  }

  protected replaceId(node: ModrinthPlanNodeDto): string {
    return `replace-${node.versionId}`;
  }

  protected isTicked(node: ModrinthPlanNodeDto): boolean {
    return this.ticked().includes(node.versionId);
  }

  protected isReplacing(node: ModrinthPlanNodeDto): boolean {
    return this.replacing().includes(node.versionId);
  }

  protected toggleOptional(node: ModrinthPlanNodeDto): void {
    if (this.installing()) return;
    this.ticked.update((ids) =>
      ids.includes(node.versionId)
        ? ids.filter((id) => id !== node.versionId)
        : [...ids, node.versionId],
    );
  }

  protected toggleReplace(node: ModrinthPlanNodeDto): void {
    if (this.installing()) return;
    this.replacing.update((ids) =>
      ids.includes(node.versionId)
        ? ids.filter((id) => id !== node.versionId)
        : [...ids, node.versionId],
    );
  }

  protected install(): void {
    if (!this.canInstall()) return;
    this.installing.set(true);

    const items = [
      ...this.newNodes().map((n) => ({ versionId: n.versionId, replace: false })),
      ...this.existingNodes()
        .filter((n) => this.isReplacing(n))
        .map((n) => ({ versionId: n.versionId, replace: true })),
    ];

    this.api.apiServersIdModrinthInstallPost(this.ctx.serverId, { items }).subscribe({
      next: (result) => {
        this.result.set(result);
        this.installing.set(false);
        if (result.failed.length > 0) {
          toast.error(`${result.failed.length} of ${items.length} could not be added`);
        }
      },
      error: (err) => {
        toast.error(messageFrom(err, 'Failed to add the mods'));
        this.installing.set(false);
      },
    });
  }

  protected cancel(): void {
    this.ref.close(null);
  }

  protected finish(): void {
    this.ref.close(this.result());
  }

  private addBytes(): number {
    return this.newNodes().reduce((sum, n) => sum + toNumber(n.fileSize), 0);
  }

  private addSummaryPlain(): string {
    const count = this.newNodes().length;
    if (count === 0) return 'Nothing to download.';
    return `${count} file${count === 1 ? '' : 's'}, ${formatBytes(this.addBytes())}.`;
  }
}

@Injectable({ providedIn: 'root' })
export class ModrinthPlanDialogService {
  private readonly dialog = inject(HlmDialogService);
  private readonly api = inject(ServerModrinthService);

  async add(context: ModrinthPlanDialogContext): Promise<ModrinthInstallResultDto | null> {
    let plan: ModrinthInstallPlanDto;
    try {
      plan = await firstValueFrom(
        this.api.apiServersIdModrinthPlanPost(context.serverId, {
          versionIds: [...context.rootVersionIds],
          optionalVersionIds: [],
        }),
      );
    } catch (err: unknown) {
      toast.error(messageFrom(err, 'Failed to resolve the dependencies of this mod'));
      return null;
    }

    if (needsADecision(plan)) return this.open(context);

    try {
      const result = await firstValueFrom(
        this.api.apiServersIdModrinthInstallPost(context.serverId, {
          items: plan.nodes.map((n) => ({ versionId: n.versionId, replace: false })),
        }),
      );

      if (result.failed.length > 0) {
        toast.error(`${result.failed.length} of ${plan.nodes.length} could not be added`);
      }
      return result;
    } catch (err: unknown) {
      toast.error(messageFrom(err, 'Failed to add the mods'));
      return null;
    }
  }

  open(context: ModrinthPlanDialogContext): Promise<ModrinthInstallResultDto | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(ModrinthPlanDialog, { context, contentClass: 'sm:max-w-2xl' });
      ref.closed$.subscribe((result) => resolve((result as ModrinthInstallResultDto | null) ?? null));
    });
  }
}

export function needsADecision(plan: ModrinthInstallPlanDto): boolean {
  return (
    plan.blocked
    || plan.nodes.length === 0
    || plan.optional.length > 0
    || plan.incompatible.length > 0
    || plan.unresolvable.length > 0
    || plan.embedded.length > 0
    || plan.warnings.length > 0
    || plan.nodes.some((n) => n.status !== PlanNodeStatus.New)
  );
}
