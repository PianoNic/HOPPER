import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Servers } from './servers/servers';
import { ServerOverview } from './servers/server-overview';
import { ServerMods } from './servers/server-mods';
import { ServerBrowse } from './servers/server-browse';
import { ServerClients } from './servers/server-clients';
import { ServerPending } from './servers/server-pending';
import { ServerSetup } from './servers/server-setup';

export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', component: Servers },

      { path: 'servers', redirectTo: '', pathMatch: 'full' },
      { path: 'server/:id', component: ServerOverview },
      { path: 'server/:id/mods', component: ServerMods },

      { path: 'server/:id/browse', component: ServerBrowse },
      { path: 'server/:id/clients', component: ServerClients },

      { path: 'server/:id/pending', component: ServerPending },
      { path: 'server/:id/setup', component: ServerSetup },
    ],
  },
  { path: '**', redirectTo: '' },
];
