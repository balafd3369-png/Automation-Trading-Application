import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  NotificationSettingsResponse,
  NotificationSettingsUpdateRequest,
} from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-settings',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './monitoring-settings.component.html',
  styleUrl: './monitoring-settings.component.scss',
})
export class MonitoringSettingsComponent implements OnInit {
  private readonly api = inject(SystemMonitoringService);

  settings: NotificationSettingsResponse | null = null;
  form: NotificationSettingsUpdateRequest = {};
  primaryEmails = '';
  secondaryEmails = '';
  primaryChats = '';
  secondaryChats = '';
  testEmailOverride = '';
  testChatOverride = '';
  error = '';
  toast = '';
  toastErr = false;
  saving = false;

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.api.getNotificationSettings().subscribe({
      next: (s) => {
        this.settings = s;
        this.form = {
          notificationsEnabled: s.notificationsEnabled,
          emailEnabled: s.emailEnabled,
          telegramEnabled: s.telegramEnabled,
          senderEmail: s.senderEmail,
          telegramOnMediumSeverity: s.telegramOnMediumSeverity,
          notifyOnLowSeverity: s.notifyOnLowSeverity,
          incidentPageUrlTemplate: s.incidentPageUrlTemplate,
          notificationThrottleSeconds: s.notificationThrottleSeconds,
        };
        this.primaryEmails = s.primaryEmailRecipients.join(', ');
        this.secondaryEmails = s.secondaryEmailRecipients.join(', ');
        this.primaryChats = s.primaryTelegramChatIds.join(', ');
        this.secondaryChats = s.secondaryTelegramChatIds.join(', ');
      },
      error: () => (this.error = 'Failed to load settings'),
    });
  }

  save(): void {
    const body: NotificationSettingsUpdateRequest = { ...this.form };
    body.primaryEmailRecipients = splitEmails(this.primaryEmails);
    body.secondaryEmailRecipients = splitEmails(this.secondaryEmails);
    body.primaryTelegramChatIds = splitChats(this.primaryChats);
    body.secondaryTelegramChatIds = splitChats(this.secondaryChats);
    this.saving = true;
    this.api.updateNotificationSettings(body).subscribe({
      next: (s) => {
        this.settings = s;
        this.saving = false;
        this.toastErr = false;
        this.toast = 'Settings saved (secrets remain server-side only)';
      },
      error: () => {
        this.saving = false;
        this.toastErr = true;
        this.toast = 'Save failed';
      },
    });
  }

  testEmail(): void {
    this.api.testEmail(this.testEmailOverride.trim() || undefined).subscribe({
      next: () => {
        this.toastErr = false;
        this.toast = 'Test email queued for processing';
      },
      error: () => {
        this.toastErr = true;
        this.toast = 'Test email failed — verify SMTP configuration on the server';
      },
    });
  }

  testTelegram(): void {
    const chat = Number(this.testChatOverride);
    this.api.testTelegram(Number.isFinite(chat) && chat > 0 ? chat : undefined).subscribe({
      next: () => {
        this.toastErr = false;
        this.toast = 'Test Telegram message queued';
      },
      error: () => {
        this.toastErr = true;
        this.toast = 'Test Telegram failed — verify bot token on the server';
      },
    });
  }
}

function splitEmails(raw: string): string[] {
  return raw
    .split(/[,;]+/)
    .map((s) => s.trim())
    .filter((s) => s.includes('@'));
}

function splitChats(raw: string): number[] {
  return raw
    .split(/[,;]+/)
    .map((s) => Number(s.trim()))
    .filter((n) => Number.isFinite(n) && n !== 0);
}