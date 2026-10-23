import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { initializeAppTheme } from './app/core/app-theme';

initializeAppTheme();

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
