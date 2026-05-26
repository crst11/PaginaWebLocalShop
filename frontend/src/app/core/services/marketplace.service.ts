import { environment } from '../../../environments/environment';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { MarketplaceSnapshot } from '../models/marketplace.models';

// Servicio de lectura publica del marketplace (vitrina empresarial).
@Injectable({ providedIn: 'root' })
export class MarketplaceService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiUrl;

  async getMarketplaceSnapshot(): Promise<MarketplaceSnapshot> {
    try {
      return await firstValueFrom(this.http.get<MarketplaceSnapshot>(`${this.api}/store/overview`));
    } catch (error) {
      throw this.toError(error, 'No se pudo cargar la vitrina empresarial.');
    }
  }

  private toError(error: unknown, fallback: string): Error {
    if (error instanceof Error && !(error instanceof HttpErrorResponse)) {
      return error;
    }

    if (error instanceof HttpErrorResponse) {
      const message =
        (typeof error.error === 'object' && error.error && 'message' in error.error
          ? String((error.error as { message?: unknown }).message)
          : undefined) ?? fallback;

      return new Error(message);
    }

    return new Error(fallback);
  }
}
