import { Injectable } from '@angular/core';
import { ChartResponse } from './board.models';

const STORAGE_KEY = 'twb.chart.v2';
const MAX_ENTRIES = 24;
const MAX_BAR_COUNT = 3_000;
const MAX_AGE_MS = 10 * 24 * 60 * 60 * 1000;

interface ChartPersistEntry {
  savedAt: number;
  data: ChartResponse;
}

interface ChartPersistStore {
  entries: Record<string, ChartPersistEntry>;
}

/** Persists chart candles in localStorage so the dashboard can paint instantly after reload. */
@Injectable({ providedIn: 'root' })
export class ChartPersistenceService {
  load(key: string): ChartResponse | null {
    try {
      const entry = this.readStore().entries[key];
      if (!entry?.data?.candles?.length) {
        return null;
      }
      if (Date.now() - entry.savedAt > MAX_AGE_MS) {
        return null;
      }
      return {
        ...entry.data,
        candles: [...entry.data.candles],
      };
    } catch {
      return null;
    }
  }

  save(key: string, data: ChartResponse): void {
    if (!data.candles?.length) {
      return;
    }
    try {
      const store = this.readStore();
      const candles =
        data.candles.length > MAX_BAR_COUNT
          ? data.candles.slice(data.candles.length - MAX_BAR_COUNT)
          : data.candles;
      store.entries[key] = {
        savedAt: Date.now(),
        data: { ...data, candles: [...candles] },
      };
      const keys = Object.keys(store.entries);
      if (keys.length > MAX_ENTRIES) {
        keys
          .sort((a, b) => store.entries[a].savedAt - store.entries[b].savedAt)
          .slice(0, keys.length - MAX_ENTRIES)
          .forEach((stale) => delete store.entries[stale]);
      }
      localStorage.setItem(STORAGE_KEY, JSON.stringify(store));
    } catch {
      // Quota or private mode — in-memory cache still helps for the session.
    }
  }

  private readStore(): ChartPersistStore {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return { entries: {} };
    }
    const parsed = JSON.parse(raw) as ChartPersistStore;
    if (!parsed?.entries || typeof parsed.entries !== 'object') {
      return { entries: {} };
    }
    return parsed;
  }
}
