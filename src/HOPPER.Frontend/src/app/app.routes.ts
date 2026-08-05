import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Home } from './home/home';
import { Mods } from './mods/mods';
import { Clients } from './clients/clients';
import { Setup } from './setup/setup';

export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', component: Home },
      { path: 'mods', component: Mods },
      { path: 'clients', component: Clients },
      { path: 'setup', component: Setup },
    ],
  },
  { path: '**', redirectTo: '' },
];
