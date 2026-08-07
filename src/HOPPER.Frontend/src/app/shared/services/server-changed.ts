import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ServerChanged {
  readonly revision = signal(0);

  changed(): void {
    this.revision.update((n) => n + 1);
  }
}
