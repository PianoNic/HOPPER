import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCopy } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { copyText } from '../../utils/clipboard';

@Component({
  selector: 'app-copy-button',
  imports: [HlmButtonImports, HlmTooltipImports, NgIcon],
  providers: [provideIcons({ lucideCopy, lucideCheck })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon"
      type="button"
      [attr.aria-label]="copied() ? 'Copied' : 'Copy'"
      [hlmTooltip]="copied() ? 'Copied' : 'Copy'"
      (click)="copy()"
    >
      <ng-icon [name]="copied() ? 'lucideCheck' : 'lucideCopy'" size="14" />
    </button>
  `,
})
export class CopyButton {
  readonly value = input.required<string>();
  protected readonly copied = signal(false);

  protected async copy(): Promise<void> {
    if ((await copyText(this.value())) === 'failed') {
      toast.error('Could not reach the clipboard. Select the text and copy it by hand.');
      return;
    }

    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 1500);
  }
}
