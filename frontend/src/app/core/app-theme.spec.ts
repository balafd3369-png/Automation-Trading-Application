import {
  APP_THEME_PRESETS,
  applyThemePalette,
  loadThemeSelection,
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
    applyThemePalette(palette);
    expect(document.documentElement.style.getPropertyValue('--bg').trim()).toBe(palette.bg);
    expect(document.documentElement.style.getPropertyValue('--accent').trim()).toBe(palette.accent);
  });
});
