import {
  MSTOCK_CREDENTIALS_STORAGE_KEY,
  clearMStockCredentials,
  hasMStockCredentials,
  loadMStockCredentials,
  saveMStockCredentials,
} from './broker-credentials.storage';

describe('broker-credentials.storage', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('saves and loads credentials', () => {
    saveMStockCredentials('user1', 'secret');
    expect(hasMStockCredentials()).toBeTrue();
    const loaded = loadMStockCredentials();
    expect(loaded?.username).toBe('user1');
    expect(loaded?.password).toBe('secret');
  });

  it('clears stored credentials', () => {
    saveMStockCredentials('user1', 'secret');
    clearMStockCredentials();
    expect(localStorage.getItem(MSTOCK_CREDENTIALS_STORAGE_KEY)).toBeNull();
    expect(hasMStockCredentials()).toBeFalse();
  });
});
