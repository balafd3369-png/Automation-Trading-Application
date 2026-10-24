export type AppThemePresetId =
  | 'default'
  | 'white'
  | 'midnight'
  | 'ocean'
  | 'forest'
  | 'warm'
  | 'amoled'
  | 'aurora';

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

const TRADE_SEMANTIC_DARK = {
  success: '#3fb950',
  successMuted: 'rgba(63, 185, 80, 0.14)',
  danger: '#f85149',
  dangerMuted: 'rgba(248, 81, 73, 0.12)',
  warning: '#d29922',
  warningMuted: 'rgba(210, 153, 34, 0.14)',
} as const;

const TRADE_SEMANTIC_LIGHT = {
  success: '#047857',
  successMuted: 'rgba(16, 185, 129, 0.14)',
  danger: '#dc2626',
  dangerMuted: 'rgba(239, 68, 68, 0.12)',
  warning: '#b45309',
  warningMuted: 'rgba(245, 158, 11, 0.16)',
} as const;

const SHADOWS_DARK = {
  sm: '0 1px 2px rgba(0, 0, 0, 0.35)',
  md: '0 8px 24px rgba(0, 0, 0, 0.4)',
  lg: '0 16px 48px rgba(0, 0, 0, 0.45)',
} as const;

const SHADOWS_LIGHT = {
  sm: '0 1px 2px rgba(15, 23, 42, 0.06)',
  md: '0 6px 20px rgba(15, 23, 42, 0.08)',
  lg: '0 14px 40px rgba(15, 23, 42, 0.1)',
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

/** High-contrast chart surface (broker-style light pane); independent of app chrome theme. */
export const CHART_READABILITY_SURFACE = {
  chartBg: '#ffffff',
  chartGrid: '#e3e6ec',
  chartText: '#2a2e39',
  crosshair: '#787b86',
  fontFamily: '"DM Sans", "Segoe UI", system-ui, -apple-system, sans-serif',
  fontSize: 12,
} as const;

export type ChartReadabilitySurface = {
  chartBg: string;
  chartGrid: string;
  chartText: string;
  crosshair: string;
  fontFamily: string;
  fontSize: number;
};

export function resolveChartSurface(): ChartReadabilitySurface {
  return CHART_READABILITY_SURFACE;
}

/** Theme-aware chart surface (readable text/grid on the active palette). */
export function resolveChartSurfaceFromPalette(palette: AppThemePalette): ChartReadabilitySurface {
  return {
    ...CHART_READABILITY_SURFACE,
    chartBg: palette.chartBg,
    chartGrid: palette.chartGrid,
    chartText: palette.chartText,
  };
}

export interface SemanticBadgeColors {
  fg: string;
  bg: string;
  border: string;
}

export interface SemanticThemeTokens {
  bg: string;
  surface: string;
  card: string;
  textPrimary: string;
  textSecondary: string;
  border: string;
  success: string;
  danger: string;
  warning: string;
  info: string;
  hover: string;
  tableHeaderBg: string;
  tableRowHover: string;
  tableBorder: string;
  badgeSuccess: SemanticBadgeColors;
  badgeDanger: SemanticBadgeColors;
  badgeWarning: SemanticBadgeColors;
}

export function buildSemanticThemeTokens(palette: AppThemePalette): SemanticThemeTokens {
  const light = isLightPalette(palette);
  const semantic = light ? TRADE_SEMANTIC_LIGHT : TRADE_SEMANTIC_DARK;
  const card = light
    ? mixHex('#ffffff', palette.bg, 0.08)
    : mixHex(palette.title, palette.bg, 0.72);
  const surface = palette.sidebar;
  const hover = light
    ? mixHex(palette.accent, palette.activity, 0.08)
    : mixHex(palette.accent, palette.activity, 0.14);
  const tableHeaderBg = light
    ? mixHex(palette.fg, palette.bg, 0.04)
    : mixHex(palette.title, palette.bg, 0.55);
  const tableRowHover = light
    ? mixHex(palette.accent, card, 0.06)
    : mixHex(palette.accent, palette.editor, 0.12);
  const tableBorder = mixHex(palette.border, palette.bg, light ? 0.65 : 0.85);

  return {
    bg: palette.bg,
    surface,
    card,
    textPrimary: palette.fg,
    textSecondary: palette.muted,
    border: palette.border,
    success: semantic.success,
    danger: semantic.danger,
    warning: semantic.warning,
    info: palette.accent,
    hover,
    tableHeaderBg,
    tableRowHover,
    tableBorder,
    badgeSuccess: semanticBadgePair(semantic.success, palette, light),
    badgeDanger: semanticBadgePair(semantic.danger, palette, light),
    badgeWarning: semanticBadgePair(semantic.warning, palette, light),
  };
}

function semanticBadgePair(accent: string, palette: AppThemePalette, light: boolean): SemanticBadgeColors {
  const bg = light
    ? mixHex(accent, '#ffffff', 0.14)
    : mixHex(accent, palette.bg, 0.22);
  const fg =
    contrastRatio(accent, bg) >= 3.2 ? accent : pickReadableText(bg);
  const border = mixHex(accent, palette.border, light ? 0.38 : 0.45);
  return { fg, bg, border };
}

function applySemanticThemeTokens(tokens: SemanticThemeTokens, light: boolean): void {
  const root = document.documentElement;
  root.style.setProperty('--color-bg', tokens.bg);
  root.style.setProperty('--color-surface', tokens.surface);
  root.style.setProperty('--color-card', tokens.card);
  root.style.setProperty('--color-text-primary', tokens.textPrimary);
  root.style.setProperty('--color-text-secondary', tokens.textSecondary);
  root.style.setProperty('--color-border', tokens.border);
  root.style.setProperty('--color-success', tokens.success);
  root.style.setProperty('--color-danger', tokens.danger);
  root.style.setProperty('--color-warning', tokens.warning);
  root.style.setProperty('--color-info', tokens.info);
  root.style.setProperty('--color-hover', tokens.hover);
  root.style.setProperty('--color-table-header-bg', tokens.tableHeaderBg);
  root.style.setProperty('--color-table-row-hover', tokens.tableRowHover);
  root.style.setProperty('--color-table-border', tokens.tableBorder);
  root.style.setProperty('--color-badge-success-fg', tokens.badgeSuccess.fg);
  root.style.setProperty('--color-badge-success-bg', tokens.badgeSuccess.bg);
  root.style.setProperty('--color-badge-success-border', tokens.badgeSuccess.border);
  root.style.setProperty('--color-badge-danger-fg', tokens.badgeDanger.fg);
  root.style.setProperty('--color-badge-danger-bg', tokens.badgeDanger.bg);
  root.style.setProperty('--color-badge-danger-border', tokens.badgeDanger.border);
  root.style.setProperty('--color-badge-warning-fg', tokens.badgeWarning.fg);
  root.style.setProperty('--color-badge-warning-bg', tokens.badgeWarning.bg);
  root.style.setProperty('--color-badge-warning-border', tokens.badgeWarning.border);
  const infoBg = light ? mixHex(tokens.info, '#ffffff', 0.14) : mixHex(tokens.info, tokens.bg, 0.2);
  const infoFg = contrastRatio(tokens.info, infoBg) >= 3.2 ? tokens.info : pickReadableText(infoBg);
  root.style.setProperty('--color-badge-info-fg', infoFg);
  root.style.setProperty('--color-badge-info-bg', infoBg);
  root.style.setProperty('--color-badge-info-border', mixHex(tokens.info, tokens.border, light ? 0.38 : 0.45));
}

function applyTypographyTokens(light: boolean): void {
  const root = document.documentElement;
  root.style.setProperty('--font-size-page-title', '32px');
  root.style.setProperty('--font-size-section-title', '20px');
  root.style.setProperty('--font-size-card-label', '13px');
  root.style.setProperty('--font-size-body', '14px');
  root.style.setProperty('--font-size-table', '14px');
  root.style.setProperty('--font-size-value-lg', '24px');
  root.style.setProperty('--font-size-value-md', '18px');
  root.style.setProperty('--font-size-min', '13px');
  root.style.setProperty('--line-height-body', '1.5');
  root.style.setProperty('--font-weight-page-title', '800');
  root.style.setProperty('--font-weight-section-title', '700');
  root.style.setProperty('--font-weight-value', '700');
  document.body.style.fontSize = '14px';
  root.style.setProperty(
    '--twb-table-head-color',
    light ? 'var(--color-text-secondary)' : 'var(--color-text-secondary)',
  );
}

/** Lightweight Charts layout/grid options for readable candlestick pane. */
export function lightweightChartDisplayOptions(
  surface: ChartReadabilitySurface = CHART_READABILITY_SURFACE,
): Record<string, unknown> {
  return {
    layout: {
      background: { color: surface.chartBg },
      textColor: surface.chartText,
      fontSize: surface.fontSize,
      fontFamily: surface.fontFamily,
    },
    grid: {
      vertLines: { color: surface.chartGrid, style: 2 },
      horzLines: { color: surface.chartGrid, style: 2 },
    },
    rightPriceScale: {
      borderColor: surface.chartGrid,
      scaleMargins: { top: 0.08, bottom: 0.08 },
    },
    timeScale: {
      borderColor: surface.chartGrid,
      visible: true,
      timeVisible: true,
      secondsVisible: false,
    },
    crosshair: {
      mode: 1,
      vertLine: {
        width: 1,
        color: surface.crosshair,
        style: 2,
        labelVisible: true,
        labelBackgroundColor: '#2a2e39',
      },
      horzLine: {
        width: 1,
        color: surface.crosshair,
        style: 2,
        labelVisible: true,
        labelBackgroundColor: '#2a2e39',
      },
    },
  };
}

export const CHART_CANDLE_STYLE = {
  upColor: '#089981',
  downColor: '#f23645',
  borderVisible: true,
  wickUpColor: '#089981',
  wickDownColor: '#f23645',
  borderUpColor: '#089981',
  borderDownColor: '#f23645',
} as const;

const PRESET_LIST: AppThemePreset[] = [
  {
    id: 'white',
    label: 'Arctic Light',
    preview: 'linear-gradient(145deg, #7dd3fc 0%, #e0f2fe 38%, #ffffff 100%)',
    palette: {
      bg: '#eef6fc',
      title: '#ffffff',
      activity: '#e8f1fa',
      sidebar: '#f6f9fd',
      editor: '#ffffff',
      border: '#c5d7eb',
      fg: '#0c1a2e',
      muted: '#4a6178',
      dim: '#7b8fa3',
      chartBg: '#ffffff',
      chartGrid: '#dce6f2',
      chartText: '#131722',
      accent: '#0284c7',
      accentHover: '#0369a1',
      accentMuted: 'rgba(2, 132, 199, 0.12)',
      accentBorder: '#0369a1',
    },
  },
  {
    id: 'midnight',
    label: 'Midnight Blue',
    preview: 'linear-gradient(145deg, #010409 0%, #161b22 55%, #58a6ff 100%)',
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
    label: 'Ocean Blue',
    preview: 'linear-gradient(145deg, #02101f 0%, #0ea5e9 52%, #7dd3fc 100%)',
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
    label: 'Emerald Pro',
    preview: 'linear-gradient(145deg, #022c22 0%, #10b981 50%, #a7f3d0 100%)',
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
    id: 'aurora',
    label: 'Neon Purple',
    preview: 'linear-gradient(145deg, #4c1d95 0%, #a855f7 48%, #22d3ee 100%)',
    palette: {
      bg: '#0c0618',
      title: '#16082a',
      activity: '#221038',
      sidebar: '#120a22',
      editor: '#0c0618',
      border: '#3d2a5c',
      fg: '#f5f0ff',
      muted: '#c4b5fd',
      dim: '#8b7cb8',
      chartBg: '#080412',
      chartGrid: '#2e2248',
      chartText: '#e9e0ff',
      accent: '#a855f7',
      accentHover: '#e879f9',
      accentMuted: 'rgba(168, 85, 247, 0.2)',
      accentBorder: '#7c3aed',
    },
  },
  {
    id: 'warm',
    label: 'Sunset Orange',
    preview: 'linear-gradient(145deg, #431407 0%, #ea580c 48%, #fde68a 100%)',
    palette: {
      bg: '#1a0f08',
      title: '#2a1810',
      activity: '#3d2214',
      sidebar: '#24140c',
      editor: '#1a0f08',
      border: '#5c3d28',
      fg: '#fff7ed',
      muted: '#fdba74',
      dim: '#a8a29e',
      chartBg: '#140c06',
      chartGrid: '#4a3020',
      chartText: '#ffedd5',
      accent: '#f97316',
      accentHover: '#fb923c',
      accentMuted: 'rgba(249, 115, 22, 0.18)',
      accentBorder: '#ea580c',
    },
  },
  {
    id: 'amoled',
    label: 'Pure Black',
    preview: 'linear-gradient(145deg, #000000 0%, #171717 55%, #404040 100%)',
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
  {
    id: 'default',
    label: 'TradingView Dark',
    preview: 'linear-gradient(145deg, #0b0e14 0%, #131722 50%, #2962ff 100%)',
    palette: {
      bg: '#131722',
      title: '#1e222d',
      activity: '#2a2e39',
      sidebar: '#1e222d',
      editor: '#131722',
      border: '#363a45',
      fg: '#d1d4dc',
      muted: '#9598a1',
      dim: '#6a6d78',
      chartBg: '#131722',
      chartGrid: '#363a45',
      chartText: '#d1d4dc',
      accent: '#2962ff',
      accentHover: '#5b8def',
      accentMuted: 'rgba(41, 98, 255, 0.16)',
      accentBorder: '#1e53e5',
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
    return withReadableTextHierarchy(paletteFromBackground(selection.customBg));
  }
  const preset = PRESET_LIST.find((p) => p.id === selection.presetId) ?? PRESET_LIST[0];
  return withReadableTextHierarchy(preset.palette);
}

/** Load saved theme and apply CSS variables on `document.documentElement` (call before bootstrap). */
export function initializeAppTheme(): AppThemePalette {
  const selection = loadThemeSelection();
  const palette = resolveThemePalette(selection);
  applyThemePalette(palette, selection.presetId);
  return palette;
}

/** Apply palette to the document without persisting (theme picker hover preview). */
export function previewThemeSelection(selection: AppThemeSelection): AppThemePalette {
  const palette = resolveThemePalette(selection);
  applyThemePalette(palette, selection.presetId);
  return palette;
}

function isLightPalette(palette: AppThemePalette): boolean {
  const { r, g, b } = hexToRgb(palette.bg);
  return relativeLuminance(r, g, b) > 0.45;
}

export function applyThemePalette(
  palette: AppThemePalette,
  presetId: AppThemeSelection['presetId'] = 'default',
): void {
  const root = document.documentElement;
  root.classList.add('twb-theme-instant');
  const light = isLightPalette(palette);
  const semantic = light ? TRADE_SEMANTIC_LIGHT : TRADE_SEMANTIC_DARK;
  const shadows = light ? SHADOWS_LIGHT : SHADOWS_DARK;
  const semanticTokens = buildSemanticThemeTokens(palette);
  applySemanticThemeTokens(semanticTokens, light);
  applyTypographyTokens(light);
  root.style.setProperty('--bg', palette.bg);
  root.style.setProperty('--title', palette.title);
  root.style.setProperty('--activity', palette.activity);
  root.style.setProperty('--sidebar', palette.sidebar);
  root.style.setProperty('--editor', palette.editor);
  root.style.setProperty('--border', palette.border);
  root.style.setProperty('--fg', palette.fg);
  root.style.setProperty('--muted', palette.muted);
  root.style.setProperty('--dim', palette.dim);
  root.style.setProperty('--chart-bg', palette.chartBg);
  root.style.setProperty('--chart-grid', palette.chartGrid);
  root.style.setProperty('--chart-text', palette.chartText);
  root.style.setProperty('--accent', palette.accent);
  root.style.setProperty('--accent-hover', palette.accentHover);
  root.style.setProperty('--accent-muted', palette.accentMuted);
  root.style.setProperty('--accent-border', palette.accentBorder);
  root.style.setProperty(
    '--accent-gradient',
    `linear-gradient(135deg, ${palette.accentBorder} 0%, ${palette.accent} 55%, ${palette.accentHover} 100%)`,
  );
  root.style.setProperty('--success', semantic.success);
  root.style.setProperty('--success-muted', semantic.successMuted);
  root.style.setProperty('--danger', semantic.danger);
  root.style.setProperty('--danger-muted', semantic.dangerMuted);
  root.style.setProperty('--warning', semantic.warning);
  root.style.setProperty('--warning-muted', semantic.warningMuted);
  root.style.setProperty('--shadow-sm', shadows.sm);
  root.style.setProperty('--shadow-md', shadows.md);
  root.style.setProperty('--shadow-lg', shadows.lg);
  root.style.setProperty(
    '--focus-ring',
    light
      ? `0 0 0 2px ${palette.bg}, 0 0 0 4px ${palette.accent}`
      : `0 0 0 2px ${palette.bg}, 0 0 0 4px ${palette.accent}`,
  );
  root.style.setProperty('--panel', palette.sidebar);
  root.style.setProperty('--surface-raised', palette.title);
  root.style.setProperty('--surface-inset', palette.editor);
  root.style.setProperty('--fg-on-title', pickReadableText(palette.title));
  root.style.setProperty('--fg-on-sidebar', pickReadableText(palette.sidebar));
  root.style.setProperty('--fg-on-activity', pickReadableText(palette.activity));
  root.style.setProperty('--fg-on-editor', pickReadableText(palette.editor));
  root.style.setProperty('--fg-on-accent-band', pickReadableText(palette.accent));
  root.style.setProperty('--muted-on-title', mixHex(pickReadableText(palette.title), palette.title, 0.58));
  root.style.setProperty('--muted-on-sidebar', mixHex(pickReadableText(palette.sidebar), palette.sidebar, 0.58));

  const titlebarSearchBg = light
    ? mixHex('#ffffff', palette.activity, 0.52)
    : mixHex(palette.editor, palette.title, 0.44);
  const titlebarSearchBgFocus = light
    ? '#ffffff'
    : mixHex(palette.editor, palette.title, 0.3);
  const titlebarSearchFg = pickReadableText(titlebarSearchBg);
  root.style.setProperty('--titlebar-search-bg', titlebarSearchBg);
  root.style.setProperty('--titlebar-search-bg-focus', titlebarSearchBgFocus);
  root.style.setProperty('--titlebar-search-fg', titlebarSearchFg);
  root.style.setProperty('--titlebar-search-muted', mixHex(titlebarSearchFg, titlebarSearchBg, 0.52));
  root.style.setProperty(
    '--titlebar-search-border',
    mixHex(palette.border, palette.accent, light ? 0.2 : 0.34),
  );
  root.style.setProperty(
    '--titlebar-search-kbd-bg',
    light ? mixHex(palette.activity, '#ffffff', 0.42) : mixHex(palette.activity, palette.title, 0.36),
  );
  root.style.setProperty(
    '--titlebar-search-kbd-border',
    mixHex(palette.border, palette.accentBorder, light ? 0.24 : 0.32),
  );
  root.style.setProperty(
    '--titlebar-search-inset',
    light
      ? 'color-mix(in srgb, #ffffff 82%, transparent)'
      : `color-mix(in srgb, ${pickReadableText(palette.title)} 14%, transparent)`,
  );
  root.style.setProperty(
    '--titlebar-search-focus-glow',
    light
      ? `0 0 0 4px ${palette.accentMuted}`
      : `0 0 0 4px color-mix(in srgb, ${palette.accent} 28%, transparent)`,
  );
  root.style.setProperty(
    '--ambient-ce',
    `color-mix(in srgb, ${semantic.success} ${light ? '16%' : '22%'}, transparent)`,
  );
  root.style.setProperty(
    '--ambient-pe',
    `color-mix(in srgb, ${semantic.danger} ${light ? '14%' : '20%'}, transparent)`,
  );
  root.style.setProperty(
    '--glass-panel',
    `color-mix(in srgb, ${palette.sidebar} 88%, ${palette.bg})`,
  );
  root.style.setProperty('--pressure-sell-fg', light ? '#c62828' : '#ff6b6b');
  root.style.setProperty('--pressure-buy-fg', light ? '#047857' : '#5ee89a');
  root.style.setProperty('--pressure-range-fg', light ? '#b45309' : '#fcd34d');
  root.style.setProperty('--rail-sell-label', light ? '#991b1b' : '#ffa8a2');
  root.style.setProperty('--rail-buy-label', light ? '#047857' : '#86efac');
  root.style.setProperty('--rail-range-label', light ? '#b45309' : '#fcd34d');
  root.style.setProperty('--twb-table-head-color', semanticTokens.textSecondary);
  root.style.setProperty('--nav-active-fg', '#ffffff');
  root.style.setProperty('--rail-head-tint', light ? '14%' : '22%');
  root.style.setProperty('--rail-head-border-mix', light ? '30%' : '38%');
  root.style.setProperty('--pressure-sell-bg-mix', light ? '11%' : '18%');
  root.style.setProperty('--pressure-sell-border-mix', light ? '26%' : '34%');
  root.style.setProperty('--pressure-buy-bg-mix', light ? '11%' : '18%');
  root.style.setProperty('--pressure-buy-border-mix', light ? '26%' : '34%');
  root.style.setProperty('--pressure-range-bg-mix', light ? '13%' : '20%');
  root.style.setProperty('--pressure-range-border-mix', light ? '28%' : '36%');
  root.style.setProperty('--pressure-row-hover-lift', light ? '16%' : '24%');
  root.style.setProperty('--pressure-sell-border-hover-mix', light ? '34%' : '42%');
  root.style.setProperty('--pressure-buy-border-hover-mix', light ? '34%' : '42%');
  root.style.setProperty('--pressure-range-border-hover-mix', light ? '36%' : '44%');
  root.dataset['twbTheme'] = presetId;
  const { r, g, b } = hexToRgb(palette.bg);
  root.style.colorScheme = relativeLuminance(r, g, b) > 0.45 ? 'light' : 'dark';

  const body = document.body;
  if (body) {
    body.style.backgroundColor = palette.bg;
    body.style.color = palette.fg;
  }

  requestAnimationFrame(() => {
    root.classList.remove('twb-theme-instant');
  });
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
  const fg = pickReadableText(base);
  const muted = mixHex(fg, base, 0.58);
  const dim = mixHex(fg, base, 0.42);
  const chartText = pickReadableText(rgbToHex(chartBg));

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

const READABLE_TEXT_LIGHT = '#f5f5f5';
const READABLE_TEXT_DARK = '#0d0d0d';
const MIN_BODY_CONTRAST = 4.5;
const MIN_MUTED_CONTRAST = 3;

/** Ensures primary / secondary text meets contrast on the main background. */
function withReadableTextHierarchy(palette: AppThemePalette): AppThemePalette {
  const fg =
    contrastRatio(palette.fg, palette.bg) >= MIN_BODY_CONTRAST
      ? palette.fg
      : pickReadableText(palette.bg);
  const muted =
    contrastRatio(palette.muted, palette.bg) >= MIN_MUTED_CONTRAST
      ? palette.muted
      : mixHex(fg, palette.bg, 0.58);
  const dim =
    contrastRatio(palette.dim, palette.bg) >= 2.8 ? palette.dim : mixHex(fg, palette.bg, 0.42);
  const chartText =
    contrastRatio(palette.chartText, palette.chartBg) >= MIN_MUTED_CONTRAST
      ? palette.chartText
      : pickReadableText(palette.chartBg);
  return { ...palette, fg, muted, dim, chartText };
}

function pickReadableText(bgHex: string): string {
  const onWhite = contrastRatio(READABLE_TEXT_LIGHT, bgHex);
  const onBlack = contrastRatio(READABLE_TEXT_DARK, bgHex);
  return onWhite >= onBlack ? READABLE_TEXT_LIGHT : READABLE_TEXT_DARK;
}

function contrastRatio(fgHex: string, bgHex: string): number {
  const fgRgb = hexToRgb(fgHex);
  const bgRgb = hexToRgb(bgHex);
  const fg = relativeLuminance(fgRgb.r, fgRgb.g, fgRgb.b);
  const bg = relativeLuminance(bgRgb.r, bgRgb.g, bgRgb.b);
  const lighter = Math.max(fg, bg);
  const darker = Math.min(fg, bg);
  return (lighter + 0.05) / (darker + 0.05);
}

function mixHex(fgHex: string, bgHex: string, fgWeight: number): string {
  const w = Math.max(0, Math.min(1, fgWeight));
  const fg = hexToRgb(fgHex);
  const bg = hexToRgb(bgHex);
  return rgbToHex({
    r: fg.r * w + bg.r * (1 - w),
    g: fg.g * w + bg.g * (1 - w),
    b: fg.b * w + bg.b * (1 - w),
  });
}
