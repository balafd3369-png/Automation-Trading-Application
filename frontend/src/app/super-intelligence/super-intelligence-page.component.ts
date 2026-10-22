import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  STRATEGY_360_PILLARS,
  SuperIntelligenceDayEntry,
  SuperIntelligenceMonthJournal,
} from './super-intelligence.models';
import {
  dayCompletionScore,
  loadOctoberJournal,
  saveOctoberJournal,
  todayOctoberDay,
} from './super-intelligence.store';

@Component({
  selector: 'app-super-intelligence-page',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './super-intelligence-page.component.html',
  styleUrl: './super-intelligence-page.component.scss',
})
export class SuperIntelligencePageComponent implements OnInit {
  readonly pillars = STRATEGY_360_PILLARS;
  journal!: SuperIntelligenceMonthJournal;
  selectedDay = 1;

  ngOnInit(): void {
    this.journal = loadOctoberJournal();
    const today = todayOctoberDay();
    this.selectedDay = today ?? 1;
  }

  selectDay(day: number): void {
    this.selectedDay = day;
  }

  activeEntry(): SuperIntelligenceDayEntry {
    return this.journal.days[this.selectedDay - 1];
  }

  completion(day: SuperIntelligenceDayEntry): number {
    return dayCompletionScore(day);
  }

  persist(): void {
    saveOctoberJournal(this.journal);
  }

  onFieldChange(): void {
    this.persist();
  }

  textField(id: (typeof STRATEGY_360_PILLARS)[number]['id']): string {
    return this.activeEntry()[id] as string;
  }

  setTextField(id: (typeof STRATEGY_360_PILLARS)[number]['id'], value: string): void {
    (this.activeEntry()[id] as string) = value;
    this.onFieldChange();
  }
}
