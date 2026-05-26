import { environment } from '../../../environments/environment';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { UpsertBusinessProductRequest } from '../models/auth.models';
import { MarketplaceBusiness } from '../models/marketplace.models';
import { AuthService } from './auth.service';
import { SessionService } from './session.service';

// Servicio de CRUD de productos para el portal empresarial.
@Injectable({ providedIn: 'root' })
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly authService = inject(AuthService);
  private readonly api = environment.apiUrl;

  async saveProduct(payload: UpsertBusinessProductRequest, productId?: number): Promise<MarketplaceBusiness> {
    const token = this.requireOwnerToken();
    const request = productId
      ? this.http.put(`${this.api}/admin/products/${productId}`, payload, { headers: this.ownerHeaders(token) })
      : this.http.post(`${this.api}/admin/products`, payload, { headers: this.ownerHeaders(token) });

    try {
      await firstValueFrom(request);
      return await this.requireCurrentBusiness();
    } catch (error) {
      throw this.toError(error, 'No se pudo guardar el producto.');
    }
  }

  async deleteProduct(productId: number): Promise<MarketplaceBusiness> {
    const token = this.requireOwnerToken();

    try {
      await firstValueFrom(
        this.http.delete<void>(`${this.api}/admin/products/${productId}`, { headers: this.ownerHeaders(token) }),
      );
      return await this.requireCurrentBusiness();
    } catch (error) {
      throw this.toError(error, 'No se pudo eliminar el producto.');
    }
  }

  private ownerHeaders(token: string): HttpHeaders {
    return new HttpHeaders({ 'X-Owner-Token': token });
  }

  private requireOwnerToken(): string {
    const token = this.session.readOwnerToken();
    if (!token) {
      throw new Error('Debes iniciar sesion como empresa para continuar.');
    }

    return token;
  }

  private async requireCurrentBusiness(): Promise<MarketplaceBusiness> {
    const business = await this.authService.getCurrentBusiness();
    if (!business) {
      throw new Error('La sesion empresarial no esta disponible. Inicia sesion nuevamente.');
    }

    return business;
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
