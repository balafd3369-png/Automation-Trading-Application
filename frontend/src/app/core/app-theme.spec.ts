import {
  APP_THEME_PRESETS,
  applyThemePalette,
  buildSemanticThemeTokens,
  CHART_READABILITY_SURFACE,
  initializeAppTheme,
  lightweightChartDisplayOptions,
  loadThemeSelection,
  resolveChartSurface,
  resolveThemePalette,
  saveThemeSelection,
  themeTextContrastRatio,
} from './app-theme';

describe('app-theme', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('resolves TradingView dark preset', () => {
    const palette = resolveThemePalette({ presetId: 'default' });
    expect(palette.bg).toBe('#131722');
  });

  it('resolves Arctic Light preset with readable dark text', () => {
    const palette = resolveThemePalette({ presetId: 'white' });
    expect(palette.bg).toBe('#eef6fc');
    expect(palette.fg).toBe('#0c1a2e');
    applyThemePalette(palette, 'white');
    expect(document.documentElement.dataset['twbTheme']).toBe('white');
    expect(document.documentElement.style.colorScheme).toBe('light');
  });

  it('persists custom selection', () => {
    saveThemeSelection({ presetId: 'custom', customBg: '#112233' });
    expect(loadThemeSelection()).toEqual({ presetId: 'custom', customBg: '#112233' });
  });

  it('builds palette from custom background', () => {
    const palette = resolveThemePalette({ presetId: 'custom', customBg: '#0d1117' });
    expect(palette.bg).toBe('#0d1117');
    expect(palette.sidebar).not.toBe(palette.bg);
  });

  it('applyThemePalette sets css variables', () => {
    const palette = APP_THEME_PRESETS[0].palette;
    applyThemePalette(palette, 'default');
    expect(document.documentElement.style.getPropertyValue('--bg').trim()).toBe(palette.bg);
    expect(document.documentElement.style.getPropertyValue('--accent').trim()).toBe(palette.accent);
    expect(document.documentElement.style.getPropertyValue('--chart-bg').trim()).toBe(palette.chartBg);
    expect(document.documentElement.style.getPropertyValue('--color-bg').trim()).toBe(palette.bg);
    const tokens = buildSemanticThemeTokens(palette);
    expect(document.documentElement.style.getPropertyValue('--color-text-primary').trim()).toBe(tokens.textPrimary);
    expect(document.documentElement.style.getPropertyValue('--font-size-body').trim()).toBe('14px');
  });

  it('buildSemanticThemeTokens keeps badge contrast on custom dark bg', () => {
    const palette = resolveThemePalette({ presetId: 'custom', customBg: '#0d1117' });
    const tokens = buildSemanticThemeTokens(palette);
    expect(tokens.badgeSuccess.fg).toBeTruthy();
    expect(tokens.textPrimary).toBe(palette.fg);
  });

  it('resolveChartSurface uses fixed light readability palette', () => {
    expect(resolveChartSurface()).toEqual(CHART_READABILITY_SURFACE);
    const opts = lightweightChartDisplayOptions();
    const layout = opts['layout'] as { background: { color: string } };
    expect(layout.background.color).toBe('#ffffff');
  });

  it('initializeAppTheme applies saved preset', () => {
    saveThemeSelection({ presetId: 'ocean' });
    const palette = initializeAppTheme();
    expect(palette.bg).toBe('#0a1628');
    expect(document.documentElement.dataset['twbTheme']).toBe('ocean');
  });

  it('custom light background gets dark readable text', () => {
    const palette = resolveThemePalette({ presetId: 'custom', customBg: '#f5e6a8' });
    expect(palette.fg).toBe('#0d0d0d');
    applyThemePalette(palette, 'custom');
    expect(document.documentElement.style.getPropertyValue('--fg-on-accent-band').trim()).not.toBe('');
  });

  it('keeps semantic text readable on card/surface for every preset', () => {
    for (const preset of APP_THEME_PRESETS) {
      const palette = resolveThemePalette({ presetId: preset.id });
      const tokens = buildSemanticThemeTokens(palette);
      expect(themeTextContrastRatio(tokens.textPrimary, tokens.card)).toBeGreaterThanOrEqual(4.5);
      expect(themeTextContrastRatio(tokens.textSecondary, tokens.card)).toBeGreaterThanOrEqual(3.2);
      expect(themeTextContrastRatio(tokens.textSecondary, tokens.surface)).toBeGreaterThanOrEqual(3.2);
    }
  });

  it('warm preset applies readable muted css variable on card', () => {
    const palette = resolveThemePalette({ presetId: 'warm' });
    applyThemePalette(palette, 'warm');
    const muted = document.documentElement.style.getPropertyValue('--color-text-secondary').trim();
    const card = document.documentElement.style.getPropertyValue('--color-card').trim();
    expect(themeTextContrastRatio(muted, card)).toBeGreaterThanOrEqual(3.2);
  });

  it('custom dark backgrounds get readable semantic tokens', () => {
    for (const hex of ['#1a0f08', '#0c0618', '#000000', '#131722']) {
      const palette = resolveThemePalette({ presetId: 'custom', customBg: hex });
      const tokens = buildSemanticThemeTokens(palette);
      expect(themeTextContrastRatio(tokens.textPrimary, tokens.card)).toBeGreaterThanOrEqual(4.5);
      expect(themeTextContrastRatio(tokens.textSecondary, tokens.card)).toBeGreaterThanOrEqual(3.2);
    }
  });
});
