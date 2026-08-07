import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideServer } from '@ng-icons/lucide';
import { BASE_PATH } from '../../../api/variables';

@Component({
  selector: 'app-server-icon',
  imports: [NgIcon],
  providers: [provideIcons({ lucideServer })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (source(); as src) {
      <img
        [src]="src"
        [alt]="name()"
        [width]="size()"
        [height]="size()"
        loading="lazy"
        class="shrink-0 rounded object-cover"
        (error)="failed.set(true)"
      />
    } @else {
      <span
        class="bg-muted text-muted-foreground flex shrink-0 items-center justify-center rounded"
        [style.width.px]="size()"
        [style.height.px]="size()"
        [attr.aria-label]="name() ? name() + ' has no icon' : 'No icon'"
      >
        <ng-icon name="lucideServer" [size]="glyph()" />
      </span>
    }
  `,
})
export class ServerIcon {
  private readonly apiBaseUrl = inject(BASE_PATH, { optional: true }) ?? '';

  public readonly sha256 = input<string | null | undefined>(null);
  public readonly name = input('');
  public readonly size = input(28);

  protected readonly failed = signal(false);

  protected readonly source = computed(() => {
    const sha256 = this.sha256();
    if (this.failed() || !sha256) return null;

    return `${this.apiBaseUrl}/api/icons/${sha256}`;
  });

  protected readonly glyph = computed(() => `${Math.max(12, Math.round(this.size() * 0.55))}`);
}
