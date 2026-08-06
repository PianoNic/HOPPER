import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Home } from './home/home';
import { Servers } from './servers/servers';
import { ServerOverview } from './servers/server-overview';
import { ServerMods } from './servers/server-mods';
import { ServerBrowse } from './servers/server-browse';
import { ServerClients } from './servers/server-clients';
import { ServerPending } from './servers/server-pending';
import { ServerSetup } from './servers/server-setup';

// Flat and eager, like before. The per-server pages are siblings rather than children of a shell
// component: they share nothing but the :id, which each one reads for itself, and a shell would
// buy an extra component boundary in exchange for nothing.
//
// Plural for the collection, singular for one of them: /servers lists, /server/:id is one server.
// Worth the extra word - a reader of a URL can tell which of the two they are looking at without
// counting path segments.
export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', component: Home },
      { path: 'servers', component: Servers },
      { path: 'server/:id', component: ServerOverview },
      { path: 'server/:id/mods', component: ServerMods },
      // Modrinth's catalogue, filtered to what this server runs. A page rather than a dialog: a
      // search box, two filters, a sort and paging is a browser pane, and it has to be able to open
      // the dependency preview on top of itself.
      { path: 'server/:id/browse', component: ServerBrowse },
      { path: 'server/:id/clients', component: ServerClients },
      // The checklist an import leaves behind. A route of its own because the work it describes -
      // a human downloading jars a machine is not allowed to - outlives any dialog session.
      { path: 'server/:id/pending', component: ServerPending },
      { path: 'server/:id/setup', component: ServerSetup },
    ],
  },
  { path: '**', redirectTo: '' },
];
