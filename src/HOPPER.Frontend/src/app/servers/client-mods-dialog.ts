import { ChangeDetectionStrategy, Component, inject, Injectable } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import {
  HlmDialogDescription,
  HlmDialogHeader,
  HlmDialogService,
  HlmDialogTitle,
} from '@spartan-ng/helm/dialog';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ClientDto } from '../api/model/clientDto';
import { ModDto } from '../api/model/modDto';

export type ClientModsDialogContext = {
  client: ClientDto;
  missing: ReadonlyArray<ModDto>;
};

@Component({
  selector: 'app-client-mods-dialog',
  imports: [
    HlmBadgeImports,
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmTableImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ ctx.client.username || 'no username' }}</h3>
      <p hlmDialogDescription class="font-mono text-xs">
        {{ ctx.client.clientId }}
        @if (ctx.client.lastIpAddress) {
          · {{ ctx.client.lastIpAddress }}
        }
      </p>
    </hlm-dialog-header>

    <div class="max-h-96 overflow-auto">
      <table hlmTable>
        <thead hlmTableHeader>
          <tr hlmTableRow>
            <th hlmTableHead>File</th>
            <th hlmTableHead>SHA-256</th>
            <th hlmTableHead class="text-right">State</th>
          </tr>
        </thead>
        <tbody hlmTableBody>
          @for (m of ctx.client.mods; track m.sha256 + m.fileName) {
            <tr hlmTableRow>
              <td hlmTableCell class="font-medium">{{ m.fileName }}</td>
              <td hlmTableCell class="font-mono text-xs" [title]="m.sha256">
                {{ short(m.sha256) }}
              </td>
              <td hlmTableCell class="text-right">
                @if (m.known) {
                  <span hlmBadge variant="secondary" class="text-xs">known</span>
                } @else {
                  <span hlmBadge variant="destructive" class="text-xs">unknown</span>
                }
              </td>
            </tr>
          }

          <!-- Rows the client has not got. These are not in client.mods by definition, so they are
               appended from the required set rather than filtered out of it. -->
          @for (m of ctx.missing; track m.id) {
            <tr hlmTableRow class="opacity-70">
              <td hlmTableCell class="font-medium">{{ m.fileName }}</td>
              <td hlmTableCell class="font-mono text-xs" [title]="m.sha256">
                {{ short(m.sha256) }}
              </td>
              <td hlmTableCell class="text-right">
                <span hlmBadge variant="outline" class="text-xs">missing</span>
              </td>
            </tr>
          }
        </tbody>
      </table>

      @if (ctx.client.mods.length === 0 && ctx.missing.length === 0) {
        <p class="text-muted-foreground p-4 text-sm">This client reported no jars at all.</p>
      }
    </div>

    <div class="flex justify-end">
      <button hlmBtn variant="outline" type="button" (click)="close()">Close</button>
    </div>
  `,
})
export class ClientModsDialog {
  protected readonly ctx = injectBrnDialogContext<ClientModsDialogContext>();
  private readonly ref = inject(BrnDialogRef);

  protected short(sha256: string): string {
    return sha256.slice(0, 12);
  }

  protected close(): void {
    this.ref.close(null);
  }
}

@Injectable({ providedIn: 'root' })
export class ClientModsDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(context: ClientModsDialogContext): void {
    this.dialog.open(ClientModsDialog, { context, contentClass: 'sm:max-w-2xl' });
  }
}
