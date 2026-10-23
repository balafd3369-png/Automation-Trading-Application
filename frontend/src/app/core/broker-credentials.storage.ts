export const MSTOCK_CREDENTIALS_STORAGE_KEY = 'twb-mstock-login-v1';

export interface MStockSavedCredentials {
  username: string;
  password: string;
  apiKey?: string;
  savedAt: string;
}

export function loadMStockCredentials(): MStockSavedCredentials | null {
  try {
    const raw = localStorage.getItem(MSTOCK_CREDENTIALS_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as MStockSavedCredentials;
    if (!parsed?.username?.trim() || !parsed.password) {
      return null;
    }
    return {
      username: parsed.username.trim(),
      password: parsed.password,
      apiKey: parsed.apiKey?.trim() || undefined,
      savedAt: parsed.savedAt ?? '',
    };
  } catch {
    return null;
  }
}

export function saveMStockCredentials(username: string, password: string, apiKey?: string): void {
  const payload: MStockSavedCredentials = {
    username: username.trim(),
    password,
    apiKey: apiKey?.trim() || undefined,
    savedAt: new Date().toISOString(),
  };
  localStorage.setItem(MSTOCK_CREDENTIALS_STORAGE_KEY, JSON.stringify(payload));
}

export function clearMStockCredentials(): void {
  localStorage.removeItem(MSTOCK_CREDENTIALS_STORAGE_KEY);
}

export function hasMStockCredentials(): boolean {
  return loadMStockCredentials() !== null;
}
