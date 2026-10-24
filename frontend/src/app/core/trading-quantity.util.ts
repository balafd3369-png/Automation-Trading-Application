import { TradingQuantityRules } from './trading-quantity.models';

export function validateTradingQuantity(
  rules: TradingQuantityRules,
  quantity: number,
): { valid: boolean; message: string } {
  if (quantity <= 0) {
    return { valid: false, message: 'Quantity must be at least 1.' };
  }
  if (!rules.usesLots) {
    return { valid: true, message: '' };
  }
  if (quantity < rules.lotSize) {
    return { valid: false, message: `Minimum quantity is ${rules.lotSize} (1 lot).` };
  }
  if (quantity % rules.lotSize !== 0) {
    return {
      valid: false,
      message: `Quantity must be a multiple of lot size (${rules.lotSize}).`,
    };
  }
  return { valid: true, message: '' };
}

export function quantityFromLots(rules: TradingQuantityRules, lots: number): number {
  const n = Math.max(1, Math.floor(lots));
  if (!rules.usesLots) {
    return n;
  }
  return n * rules.lotSize;
}

export function lotsFromQuantity(rules: TradingQuantityRules, quantity: number): number {
  if (!rules.usesLots || rules.lotSize <= 0) {
    return quantity;
  }
  return Math.max(1, Math.floor(quantity / rules.lotSize));
}

export function normalizeStockQuantity(raw: number): number {
  const n = Math.floor(Number(raw));
  return Number.isFinite(n) && n >= 1 ? n : 1;
}
