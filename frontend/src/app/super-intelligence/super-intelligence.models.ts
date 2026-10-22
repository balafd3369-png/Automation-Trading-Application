/** One trading day in the 360° strategy journal. */
export interface SuperIntelligenceDayEntry {
  day: number;
  isoDate: string;
  weekday: string;
  marketContext: string;
  strategyRules: string;
  setupTiming: string;
  riskSizing: string;
  execution: string;
  reviewLesson: string;
}

export interface SuperIntelligenceMonthJournal {
  year: number;
  month: number;
  monthLabel: string;
  strategyFocus: string;
  days: SuperIntelligenceDayEntry[];
}

export const SUPER_INTELLIGENCE_STORAGE_KEY = 'twb-super-intelligence-oct-2026';

export const STRATEGY_360_PILLARS: ReadonlyArray<{ id: keyof SuperIntelligenceDayEntry; title: string; hint: string }> = [
  {
    id: 'marketContext',
    title: 'Market context',
    hint: 'Index zone, pressure rails, gap, events — what did the board say?',
  },
  {
    id: 'strategyRules',
    title: 'Strategy rules (our playbook)',
    hint: 'Which rules applied today? What did we commit to follow 360°?',
  },
  {
    id: 'setupTiming',
    title: 'Setup & timing',
    hint: 'Valid setup or no-trade? Entry window, symbol, structure.',
  },
  {
    id: 'riskSizing',
    title: 'Risk & sizing',
    hint: 'Stop, target, R:R, paper capital alignment (e.g. ₹10L book).',
  },
  {
    id: 'execution',
    title: 'Execution & discipline',
    hint: 'What we did vs plan — slips, patience, confirmations.',
  },
  {
    id: 'reviewLesson',
    title: 'EOD review & lesson',
    hint: 'One line to carry forward tomorrow.',
  },
];
