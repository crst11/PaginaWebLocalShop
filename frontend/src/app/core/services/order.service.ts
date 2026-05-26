import { environment } from '../../../environments/environment';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CheckoutCartRequest, CheckoutCartResponse, CreateShipmentRequest } from '../models/cart.models';
import { BusinessOrdersFeed, OrderCreatedResponse } from '../models/order.models';
import { SessionService } from './session.service';

// Servicio de pedidos: checkout del carrito y bandeja de pedidos de empresa.
@Injectable({ providedIn: 'root' })
export class OrderService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly api = environment.apiUrl;

  async createOrder(payload: CreateShipmentRequest): Promise<OrderCreatedResponse> {
    const customerToken = this.requireCustomerToken();

    try {
      return await firstValueFrom(
        this.http.post<OrderCreatedResponse>(`${this.api}/orders`, payload, {
          headers: this.customerHeaders(customerToken),
        }),
      );
    } catch (error) {
      throw this.toError(error, 'No se pudo crear el pedido.');
    }
  }

  async checkoutCart(payload: CheckoutCartRequest): Promise<CheckoutCartResponse> {
    const customerToken = this.requireCustomerToken();

    try {
      return await firstValueFrom(
        this.http.post<CheckoutCartResponse>(`${this.api}/orders/checkout`, payload, {
          headers: this.customerHeaders(customerToken),
        }),
      );
    } catch (error) {
      throw this.toError(error, 'No se pudo registrar la compra del carrito.');
    }
  }

  async getCurrentBusinessOrders(): Promise<BusinessOrdersFeed> {
    const token = this.requireOwnerToken();

    try {
      return await firstValueFrom(
        this.http.get<BusinessOrdersFeed>(`${this.api}/admin/orders`, { headers: this.ownerHeaders(token) }),
      );
    } catch (error) {
      throw this.toError(error, 'No se pudieron cargar los pedidos de la empresa.');
    }
  }

  private ownerHeaders(token: string): HttpHeaders {
    return new HttpHeaders({ 'X-Owner-Token': token });
  }

  private customerHeaders(token: string): HttpHeaders {
    return new HttpHeaders({ 'X-Customer-Token': token });
  }

  private requireOwnerToken(): string {
    const token = this.session.readOwnerToken();
    if (!token) {
      throw new Error('Debes iniciar sesion como empresa para continuar.');
    }

    return token;
  }

  private requireCustomerToken(): string {
    const token = this.session.readCustomerToken();
    if (!token) {
      throw new Error('Debes registrarte o iniciar sesion como cliente para comprar.');
    }

    return token;
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
