import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { showBootstrapFailure } from './app/shared/auth/bootstrap-failure';

// Catches what the loader's own catchError never sees - a bad provider, a template that failed to
// compile. The console keeps the raw error; the static block is the part a user can act on.
bootstrapApplication(App, appConfig).catch((err: unknown) => {
  console.error(err);
  showBootstrapFailure('unknown');
});
