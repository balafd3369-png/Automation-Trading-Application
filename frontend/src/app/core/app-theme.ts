export type AppThemePresetId = 'default' | 'midnight' | 'ocean' | 'forest' | 'warm' | 'amoled';

export interface AppThemePalette {
  bg: string;
  title: string;
  activity: string;
  sidebar: string;
  editor: string;
  border: string;
  fg: string;
  muted: string;
  dim: string;
  chartBg: string;
  chartGrid: string;
  chartText: string;
  accent: string;
  accentHover: string;
  accentMuted: string;
  accentBorder: string;
}

const TRADE_SEMANTIC = {
  success: '#3fb950',
  successMuted: 'rgba(63, 185, 80, 0.14)',
  danger: '#f85149',
  dangerMuted: 'rgba(248, 81, 73, 0.12)',
  warning: '#d29922',
  warningMuted: 'rgba(210, 153, 34, 0.14)',
} as const;

export interface AppThemePreset {
  id: AppThemePresetId;
  label: string;
  preview: string;
  palette: AppThemePalette;
}

export interface AppThemeSelection {
  presetId: AppThemePresetId | 'custom';
  customBg?: string;
}

export const APP_THEME_STORAGE_KEY = 'twb-app-theme';

const PRESET_LIST: AppThemePreset[] = [
  {
    id: 'default',
    label: 'Classic dark',
    preview: '#1e1e1e',
    palette: {
      bg: '#1e1e1e',
      title: '#3c3c3c',
      activity: '#333333',
      sidebar: '#252526',
      editor: '#1e1e1e',
      border: '#2b2b2b',
      fg: '#cccccc',
      muted: '#9d9d9d',
      dim: '#6e6e6e',
      chartBg: '#131313',
      chartGrid: '#2a2e39',
      chartText: '#d1d4dc',
      accent: '#388bfd',
      accentHover: '#58a6ff',
      accentMuted: 'rgba(56, 139, 253, 0.14)',
      accentBorder: '#3a6ea5',
    },
  },
  {
    id: 'midnight',
    label: 'Midnight blue',
    preview: '#0d1117',
    palette: {
      bg: '#0d1117',
      title: '#161b22',
      activity: '#21262d',
      sidebar: '#161b22',
      editor: '#0d1117',
      border: '#30363d',
      fg: '#e6edf3',
      muted: '#8b949e',
      dim: '#6e7681',
      chartBg: '#0a0e14',
      chartGrid: '#21262d',
      chartText: '#c9d1d9',
      accent: '#58a6ff',
      accentHover: '#79c0ff',
      accentMuted: 'rgba(88, 166, 255, 0.16)',
      accentBorder: '#388bfd',
    },
  },
  {
    id: 'ocean',
    label: 'Ocean',
    preview: '#0a1628',
    palette: {
      bg: '#0a1628',
      title: '#12243d',
      activity: '#1a3050',
      sidebar: '#0f1d32',
      editor: '#0a1628',
      border: '#1e3a5f',
      fg: '#dbeafe',
      muted: '#93c5fd',
      dim: '#64748b',
      chartBg: '#081220',
      chartGrid: '#1e3a5f',
      chartText: '#bfdbfe',
      accent: '#38bdf8',
      accentHover: '#7dd3fc',
      accentMuted: 'rgba(56, 189, 248, 0.16)',
      accentBorder: '#0ea5e9',
    },
  },
  {
    id: 'forest',
    label: 'Forest',
    preview: '#0f1a14',
    palette: {
      bg: '#0f1a14',
      title: '#1a2e22',
      activity: '#243b2e',
      sidebar: '#142019',
      editor: '#0f1a14',
      border: '#2d4a3a',
      fg: '#d1fae5',
      muted: '#86efac',
      dim: '#6b7280',
      chartBg: '#0c1510',
      chartGrid: '#2d4a3a',
      chartText: '#bbf7d0',
      accent: '#34d399',
      accentHover: '#6ee7b7',
      accentMuted: 'rgba(52, 211, 153, 0.14)',
      accentBorder: '#10b981',
    },
  },
  {
    id: 'warm',
    label: 'Warm charcoal',
    preview: '#1a1512',
    palette: {
      bg: '#1a1512',
      title: '#2a221c',
      activity: '#352c24',
      sidebar: '#211b17',
      editor: '#1a1512',
      border: '#3d342c',
      fg: '#f5ebe0',
      muted: '#c4b5a5',
      dim: '#78716c',
      chartBg: '#14100d',
      chartGrid: '#3d342c',
      chartText: '#e7dfd6',
      accent: '#f59e0b',
      accentHover: '#fbbf24',
      accentMuted: 'rgba(245, 158, 11, 0.14)',
      accentBorder: '#d97706',
    },
  },
  {
    id: 'amoled',
    label: 'AMOLED black',
    preview: '#000000',
    palette: {
      bg: '#000000',
      title: '#141414',
      activity: '#1a1a1a',
      sidebar: '#0a0a0a',
      editor: '#000000',
      border: '#262626',
      fg: '#f5f5f5',
      muted: '#a3a3a3',
      dim: '#737373',
      chartBg: '#000000',
      chartGrid: '#1f1f1f',
      chartText: '#e5e5e5',
      accent: '#3b82f6',
      accentHover: '#60a5fa',
      accentMuted: 'rgba(59, 130, 246, 0.16)',
      accentBorder: '#2563eb',
    },
  },
];

export const APP_THEME_PRESETS = PRESET_LIST;

export function loadThemeSelection(): AppThemeSelection {
  try {
    const raw = localStorage.getItem(APP_THEME_STORAGE_KEY);
    if (!raw) {
      return { presetId: 'default' };
    }
    const parsed = JSON.parse(raw) as AppThemeSelection;
    if (parsed.presetId === 'custom' && parsed.customBg) {
      return parsed;
    }
    if (PRESET_LIST.some((p) => p.id === parsed.presetId)) {
      return { presetId: parsed.presetId as AppThemePresetId };
    }
  } catch {
    /* ignore */
  }
  return { presetId: 'default' };
}

export function saveThemeSelection(selection: AppThemeSelection): void {
  localStorage.setItem(APP_THEME_STORAGE_KEY, JSON.stringify(selection));
}

export function resolveThemePalette(selection: AppThemeSelection): AppThemePalette {
  if (selection.presetId === 'custom' && selection.customBg) {
    return paletteFromBackground(selection.customBg);
  }
  const preset = PRESET_LIST.find((p) => p.id === selection.presetId) ?? PRESET_LIST[0];
  return preset.palette;
}

export function applyThemePalette(palette: AppThemePalette): void {
  const root = document.documentElement;
  root.style.setProperty('--bg', palette.bg);
  root.style.setProperty('--title', palette.title);
  root.style.setProperty('--activity', palette.activity);
  root.style.setProperty('--sidebar', palette.sidebar);
  root.style.setProperty('--editor', palette.editor);
  root.style.setProperty('--border', palette.border);
  root.style.setProperty('--fg', palette.fg);
  root.style.setProperty('--muted', palette.muted);
  root.style.setProperty('--dim', palette.dim);
  root.style.setProperty('--accent', palette.accent);
  root.style.setProperty('--accent-hover', palette.accentHover);
  root.style.setProperty('--accent-muted', palette.accentMuted);
  root.style.setProperty('--accent-border', palette.accentBorder);
  root.style.setProperty('--accent-gradient', `linear-gradient(135deg, ${palette.accentBorder} 0%, ${palette.accent} 55%, ${palette.accentHover} 100%)`);
  root.style.setProperty('--success', TRADE_SEMANTIC.success);
  root.style.setProperty('--success-muted', TRADE_SEMANTIC.successMuted);
  root.style.setProperty('--danger', TRADE_SEMANTIC.danger);
  root.style.setProperty('--danger-muted', TRADE_SEMANTIC.dangerMuted);
  root.style.setProperty('--warning', TRADE_SEMANTIC.warning);
  root.style.setProperty('--warning-muted', TRADE_SEMANTIC.warningMuted);
}

function paletteFromBackground(hex: string): AppThemePalette {
  const base = normalizeHex(hex);
  const { r, g, b } = hexToRgb(base);
  const light = relativeLuminance(r, g, b) > 0.45;
  const sidebar = shiftRgb(r, g, b, light ? -0.06 : 0.08);
  const title = shiftRgb(r, g, b, light ? -0.12 : 0.14);
  const activity = shiftRgb(r, g, b, light ? -0.08 : 0.1);
  const border = shiftRgb(r, g, b, light ? -0.18 : 0.22);
  const chartBg = shiftRgb(r, g, b, light ? -0.04 : -0.06);
  const chartGrid = shiftRgb(r, g, b, light ? -0.22 : 0.18);
  const fg = light ? '#1a1a1a' : '#e8e8e8';
  const muted = light ? '#525252' : '#a3a3a3';
  const dim = light ? '#737373' : '#6e6e6e';
  const chartText = light ? '#262626' : '#d1d4dc';

  return {
    bg: base,
    title: rgbToHex(title),
    activity: rgbToHex(activity),
    sidebar: rgbToHex(sidebar),
    editor: base,
    border: rgbToHex(border),
    fg,
    muted,
    dim,
    chartBg: rgbToHex(chartBg),
    chartGrid: rgbToHex(chartGrid),
    chartText,
    accent: light ? '#2563eb' : '#388bfd',
    accentHover: light ? '#3b82f6' : '#58a6ff',
    accentMuted: light ? 'rgba(37, 99, 235, 0.12)' : 'rgba(56, 139, 253, 0.14)',
    accentBorder: light ? '#1d4ed8' : '#3a6ea5',
  };
}

function normalizeHex(input: string): string {
  const trimmed = input.trim();
  if (/^#[0-9a-fA-F]{6}$/.test(trimmed)) {
    return trimmed.toLowerCase();
  }
  if (/^#[0-9a-fA-F]{3}$/.test(trimmed)) {
    const h = trimmed.slice(1);
    return `#${h[0]}${h[0]}${h[1]}${h[1]}${h[2]}${h[2]}`.toLowerCase();
  }
  return '#1e1e1e';
}

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const h = hex.slice(1);
  return {
    r: parseInt(h.slice(0, 2), 16),
    g: parseInt(h.slice(2, 4), 16),
    b: parseInt(h.slice(4, 6), 16),
  };
}

function rgbToHex(rgb: { r: number; g: number; b: number }): string {
  const clamp = (n: number) => Math.max(0, Math.min(255, Math.round(n)));
  const r = clamp(rgb.r).toString(16).padStart(2, '0');
  const g = clamp(rgb.g).toString(16).padStart(2, '0');
  const b = clamp(rgb.b).toString(16).padStart(2, '0');
  return `#${r}${g}${b}`;
}

function shiftRgb(r: number, g: number, b: number, amount: number): { r: number; g: number; b: number } {
  if (amount >= 0) {
    return {
      r: r + (255 - r) * amount,
      g: g + (255 - g) * amount,
      b: b + (255 - b) * amount,
    };
  }
  const factor = 1 + amount;
  return { r: r * factor, g: g * factor, b: b * factor };
}

function relativeLuminance(r: number, g: number, b: number): number {
  const srgb = [r, g, b].map((c) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * srgb[0] + 0.7152 * srgb[1] + 0.0722 * srgb[2];
}
