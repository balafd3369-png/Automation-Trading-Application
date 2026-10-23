import {
  APP_THEME_PRESETS,
  applyThemePalette,
  CHART_READABILITY_SURFACE,
  initializeAppTheme,
  lightweightChartDisplayOptions,
  loadThemeSelection,
  resolveChartSurface,
  resolveThemePalette,
  saveThemeSelection,
} from './app-theme';

describe('app-theme', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('resolves default preset', () => {
    const palette = resolveThemePalette({ presetId: 'default' });
    expect(palette.bg).toBe('#1e1e1e');
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
});
