import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  EscalationPoliciesUpdateRequest,
  EscalationPolicyTierResponse,
  EscalationStepResponse,
} from '../core/system-monitoring.models';
import { SystemMonitoringService } from '../core/system-monitoring.service';

@Component({
  selector: 'app-monitoring-escalation',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './monitoring-escalation.component.html',
  styleUrl: './monitoring-escalation.component.scss',
})
export class MonitoringEscalationComponent implements OnInit {
  private readonly api = inject(SystemMonitoringService);

  tiers: EscalationPolicyTierResponse[] = [];
  error = '';
  toast = '';
  saving = false;

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.api.getEscalationPolicies().subscribe({
      next: (tiers) => (this.tiers = tiers),
      error: () => (this.error = 'Failed to load escalation policies'),
    });
  }

  addStep(tier: EscalationPolicyTierResponse): void {
    tier.steps = [
      ...tier.steps,
      { kind: 'UnacknowledgedReminder', delayMinutes: 5, useSecondaryRecipients: false },
    ];
  }

  removeStep(tier: EscalationPolicyTierResponse, index: number): void {
    tier.steps = tier.steps.filter((_, i) => i !== index);
  }

  preview(tier: EscalationPolicyTierResponse): string {
    if (!tier.steps.length) {
      return 'No steps configured';
    }
    return tier.steps
      .map((s, i) => {
        const who = s.useSecondaryRecipients || s.kind === 'SecondaryRecipients' ? 'secondary recipients' : 'primary recipients';
        return `T+${s.delayMinutes}m · ${s.kind} → ${who}`;
      })
      .join(' | ');
  }

  save(): void {
    const body: EscalationPoliciesUpdateRequest = {
      tiers: this.tiers.map((t) => ({
        severity: t.severity,
        steps: t.steps.map(normalizeStep),
      })),
    };
    for (const tier of body.tiers) {
      for (const step of tier.steps) {
        if (step.delayMinutes < 1) {
          this.toast = 'Delay must be at least 1 minute';
          return;
        }
      }
    }
    this.saving = true;
    this.api.updateEscalationPolicies(body).subscribe({
      next: (tiers) => {
        this.tiers = tiers;
        this.saving = false;
        this.toast = 'Escalation policies saved';
      },
      error: () => {
        this.saving = false;
        this.toast = 'Save failed';
      },
    });
  }
}

function normalizeStep(step: EscalationStepResponse): EscalationStepResponse {
  return {
    kind: step.kind,
    delayMinutes: Math.max(1, Math.floor(step.delayMinutes)),
    useSecondaryRecipients: step.useSecondaryRecipients,
  };
}