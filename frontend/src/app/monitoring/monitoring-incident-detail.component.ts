import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription, forkJoin, switchMap } from 'rxjs';
import {
  IncidentNotificationRecord,
  SystemIncidentEventRecord,
  SystemIncidentNoteRecord,
  SystemIncidentRecord,
} from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';
import { severityClass } from '../core/system-monitoring.util';

@Component({
  selector: 'app-monitoring-incident-detail',
  standalone: true,
  imports: [DatePipe, RouterLink, FormsModule],
  templateUrl: './monitoring-incident-detail.component.html',
  styleUrl: './monitoring-incident-detail.component.scss',
})
export class MonitoringIncidentDetailComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private readonly route = inject(ActivatedRoute);
  private sub?: Subscription;

  incident: SystemIncidentRecord | null = null;
  events: SystemIncidentEventRecord[] = [];
  notes: SystemIncidentNoteRecord[] = [];
  notifications: IncidentNotificationRecord[] = [];
  noteDraft = '';
  error = '';
  toast = '';
  busy = false;

  ngOnInit(): void {
    this.sub = this.route.paramMap
      .pipe(
        switchMap((params) => {
          const id = params.get('id') ?? '';
          return forkJoin({
            incident: this.api.getIncident(id),
            events: this.api.listIncidentEvents(id),
            notes: this.api.listIncidentNotes(id),
            notifications: this.api.listIncidentNotifications(id),
          });
        }),
      )
      .subscribe({
        next: (bundle) => {
          this.error = '';
          this.incident = bundle.incident;
          this.events = bundle.events.sort(
            (a, b) => new Date(a.timestampUtc).getTime() - new Date(b.timestampUtc).getTime(),
          );
          this.notes = bundle.notes;
          this.notifications = bundle.notifications;
        },
        error: () => (this.error = 'Incident not found or access denied'),
      });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  sevClass(s: string): string {
    return severityClass(s);
  }

  private id(): string {
    return this.incident?.id ?? '';
  }

  acknowledge(): void {
    if (!confirm('Acknowledge incident?')) {
      return;
    }
    this.run(() => this.api.acknowledgeIncident(this.id()));
  }

  investigate(): void {
    this.run(() => this.api.investigateIncident(this.id()));
  }

  resolve(): void {
    if (!confirm('Resolve incident?')) {
      return;
    }
    this.run(() => this.api.resolveIncident(this.id()));
  }

  close(): void {
    if (!confirm('Close incident?')) {
      return;
    }
    this.run(() => this.api.closeIncident(this.id()));
  }

  addNote(): void {
    const note = this.noteDraft.trim();
    if (!note) {
      return;
    }
    this.api.addIncidentNote(this.id(), note).subscribe({
      next: (n) => {
        this.notes = [n, ...this.notes];
        this.noteDraft = '';
        this.toast = 'Note added';
      },
      error: () => (this.toast = 'Failed to add note'),
    });
  }

  private run(call: () => ReturnType<SystemMonitoringService['acknowledgeIncident']>): void {
    this.busy = true;
    call().subscribe({
      next: (inc) => {
        this.incident = inc;
        this.toast = 'Updated';
        this.api.listIncidentEvents(this.id()).subscribe((e) => (this.events = e));
        this.busy = false;
      },
      error: () => {
        this.toast = 'Action failed';
        this.busy = false;
      },
    });
  }
}