import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin, Subscription } from 'rxjs';
import {
  EmergencyGateAuditEntry,
  TradingSafetySummarySnapshot,
} from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-trading-safety',
  standalone: true,
  imports: [DatePipe, FormsModule],
  templateUrl: './monitoring-trading-safety.component.html',
  styleUrl: './monitoring-trading-safety.component.scss',
})
export class MonitoringTradingSafetyComponent implements OnInit, OnDestroy {
  private readonly api = inject(SystemMonitoringService);
  private pollSub?: Subscription;

  /** Must match backend LiveTradingExecutionSafetyService.EmergencyConfirmPhrase */
  readonly confirmPhrase = 'I AUTHORIZE EMERGENCY GATE CHANGE';

  summary: TradingSafetySummarySnapshot | null = null;
  audit: EmergencyGateAuditEntry[] = [];
  error = '';
  message = '';
  gateReason = '';
  breakerReason = '';
  confirmation = '';

  ngOnInit(): void {
    this.pollSub = this.api.pollWhileVisible(
      () =>
        forkJoin({
          summary: this.api.getTradingSafetySummary(),
          audit: this.api.getTradingSafetyGateAudit(25),
        }),
      20_000,
      {
        next: ({ summary, audit }) => {
          this.error = '';
          this.summary = summary;
          this.audit = audit;
        },
        error: (msg) => (this.error = msg),
      },
    );
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  haltGate(): void {
    this.runGateAction('halt');
  }

  clearGate(): void {
    this.runGateAction('clear');
  }

  resetBreaker(): void {
    if (!confirm('Reset broker circuit breaker after verifying broker health?')) {
      return;
    }
    this.message = '';
    this.api.resetTradingCircuitBreaker(this.breakerReason.trim() || 'Admin reset', this.confirmation).subscribe({
      next: (snap) => {
        this.message = `Circuit breaker: ${snap.state}`;
        this.confirmation = '';
        this.refreshOnce();
      },
      error: (err: unknown) => {
        this.error = err instanceof Error ? err.message : 'Reset failed';
      },
    });
  }

  private runGateAction(kind: 'halt' | 'clear'): void {
    const label = kind === 'halt' ? 'Halt new live order placement' : 'Clear emergency gate halt';
    if (!confirm(`${label}? Cancels and status inquiries remain allowed per policy.`)) {
      return;
    }
    this.message = '';
    const reason = this.gateReason.trim() || (kind === 'halt' ? 'Monitoring dashboard halt' : 'Monitoring dashboard clear');
    const call =
      kind === 'halt'
        ? this.api.haltTradingEmergencyGate(reason, this.confirmation)
        : this.api.clearTradingEmergencyGate(reason, this.confirmation);
    call.subscribe({
      next: (gate) => {
        this.message = gate.isHalted ? `Gate halted: ${gate.reason}` : 'Gate cleared';
        this.confirmation = '';
        this.refreshOnce();
      },
      error: (err: unknown) => {
        this.error = err instanceof Error ? err.message : 'Gate change failed';
      },
    });
  }

  private refreshOnce(): void {
    forkJoin({
      summary: this.api.getTradingSafetySummary(),
      audit: this.api.getTradingSafetyGateAudit(25),
    }).subscribe({
      next: ({ summary, audit }) => {
        this.summary = summary;
        this.audit = audit;
      },
    });
  }
}