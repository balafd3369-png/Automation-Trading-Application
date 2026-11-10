const STORAGE_KEY = 'twb.zone-analyzer-alerts';
const SEEN_KEY = 'twb.zone-analyzer-alerts-seen';

export interface ZoneAnalyzerAlertSettings {
  enabled: boolean;
  minScore: number;
  notifyOnBuyList: boolean;
  watchSymbols: string[];
}

const DEFAULT: ZoneAnalyzerAlertSettings = {
  enabled: false,
  minScore: 80,
  notifyOnBuyList: true,
  watchSymbols: [],
};

export function loadAnalyzerAlertSettings(): ZoneAnalyzerAlertSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return { ...DEFAULT };
    }
    return { ...DEFAULT, ...(JSON.parse(raw) as Partial<ZoneAnalyzerAlertSettings>) };
  } catch {
    return { ...DEFAULT };
  }
}

export function saveAnalyzerAlertSettings(settings: ZoneAnalyzerAlertSettings): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
}

export function loadSeenAlertKeys(): Set<string> {
  try {
    const raw = localStorage.getItem(SEEN_KEY);
    if (!raw) {
      return new Set();
    }
    return new Set(JSON.parse(raw) as string[]);
  } catch {
    return new Set();
  }
}

export function persistSeenAlertKeys(keys: Set<string>): void {
  const list = [...keys].slice(-500);
  localStorage.setItem(SEEN_KEY, JSON.stringify(list));
}

export async function ensureNotificationPermission(): Promise<boolean> {
  if (typeof Notification === 'undefined') {
    return false;
  }
  if (Notification.permission === 'granted') {
    return true;
  }
  if (Notification.permission === 'denied') {
    return false;
  }
  const result = await Notification.requestPermission();
  return result === 'granted';
}

export function fireAnalyzerNotification(title: string, body: string): void {
  if (typeof Notification === 'undefined' || Notification.permission !== 'granted') {
    return;
  }
  try {
    new Notification(title, { body, tag: 'zone-analyzer' });
  } catch {
    // ignore
  }
}
