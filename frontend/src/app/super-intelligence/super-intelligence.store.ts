import {
  SUPER_INTELLIGENCE_STORAGE_KEY,
  SuperIntelligenceDayEntry,
  SuperIntelligenceMonthJournal,
} from './super-intelligence.models';

const OCTOBER_YEAR = 2026;
const OCTOBER_MONTH = 10;

function emptyDay(day: number): SuperIntelligenceDayEntry {
  const iso = `${OCTOBER_YEAR}-10-${String(day).padStart(2, '0')}`;
  const date = new Date(`${iso}T12:00:00+05:30`);
  const weekday = date.toLocaleDateString('en-IN', { weekday: 'short', timeZone: 'Asia/Kolkata' });
  return {
    day,
    isoDate: iso,
    weekday,
    marketContext: '',
    strategyRules: '',
    setupTiming: '',
    riskSizing: '',
    execution: '',
    reviewLesson: '',
  };
}

export function createOctoberJournal(): SuperIntelligenceMonthJournal {
  const days: SuperIntelligenceDayEntry[] = [];
  for (let d = 1; d <= 31; d++) {
    days.push(emptyDay(d));
  }
  return {
    year: OCTOBER_YEAR,
    month: OCTOBER_MONTH,
    monthLabel: 'October 2026',
    strategyFocus:
      'Single strategy, 360° coverage — market read, rules, setup, risk, execution, and daily lesson. Add notes each session; we build this book day by day.',
    days,
  };
}

function mergeWithDefaults(stored: SuperIntelligenceMonthJournal): SuperIntelligenceMonthJournal {
  const base = createOctoberJournal();
  const byDay = new Map(stored.days.map((d) => [d.day, d]));
  return {
    ...base,
    strategyFocus: stored.strategyFocus || base.strategyFocus,
    days: base.days.map((d) => ({ ...d, ...byDay.get(d.day) })),
  };
}

export function loadOctoberJournal(): SuperIntelligenceMonthJournal {
  if (typeof window === 'undefined') {
    return createOctoberJournal();
  }
  try {
    const raw = localStorage.getItem(SUPER_INTELLIGENCE_STORAGE_KEY);
    if (!raw) {
      return createOctoberJournal();
    }
    return mergeWithDefaults(JSON.parse(raw) as SuperIntelligenceMonthJournal);
  } catch {
    return createOctoberJournal();
  }
}

export function saveOctoberJournal(journal: SuperIntelligenceMonthJournal): void {
  if (typeof window === 'undefined') {
    return;
  }
  localStorage.setItem(SUPER_INTELLIGENCE_STORAGE_KEY, JSON.stringify(journal));
}

export function todayOctoberDay(): number | null {
  const now = new Date();
  const ist = new Date(now.toLocaleString('en-US', { timeZone: 'Asia/Kolkata' }));
  if (ist.getFullYear() !== OCTOBER_YEAR || ist.getMonth() + 1 !== OCTOBER_MONTH) {
    return null;
  }
  return ist.getDate();
}

export function dayCompletionScore(day: SuperIntelligenceDayEntry): number {
  const fields = [
    day.marketContext,
    day.strategyRules,
    day.setupTiming,
    day.riskSizing,
    day.execution,
    day.reviewLesson,
  ];
  const filled = fields.filter((t) => t.trim().length > 0).length;
  return Math.round((filled / fields.length) * 100);
}
