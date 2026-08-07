import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { showBootstrapFailure } from './app/shared/auth/bootstrap-failure';

bootstrapApplication(App, appConfig).catch((err: unknown) => {
  console.error(err);
  showBootstrapFailure('unknown');
});
