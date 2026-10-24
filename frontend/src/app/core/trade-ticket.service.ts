import { Injectable, signal } from '@angular/core';
import { TradeTicketContext } from '../trade-ticket/trade-ticket.models';

@Injectable({ providedIn: 'root' })
export class TradeTicketService {
  private readonly openState = signal<TradeTicketContext | null>(null);

  readonly context = this.openState.asReadonly();

  open(ctx: TradeTicketContext): void {
    this.openState.set(ctx);
  }

  close(): void {
    this.openState.set(null);
  }

  isOpen(): boolean {
    return this.openState() !== null;
  }
}
