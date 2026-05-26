import { environment } from '../../../environments/environment';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CustomerAuthResponseApi, CustomerOrdersHistory, CustomerProfile, LoginCustomerRequest, RegisterCustomerRequest, UpdateCustomerProfileRequest } from '../models/customer.models';
import { SessionService } from './session.service';

// Servicio de autenticacion y gestion de perfil para clientes (compradores).
@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly api = environment.apiUrl;

  // Lectura publica del token de cliente (usado por CartService al iniciar sesion).
  readCurrentCustomerToken(): string | null {
    return this.session.readCustomerToken();
  }

  hasCustomerSession(): boolean {
    return this.session.hasCustomerSession();
  }

  async getCurrentCustomer(): Promise<CustomerProfile | null> {
    const token = this.session.readCustomerToken();
    if (!token) {
      return null;
    }

    try {
      return await firstValueFrom(
        this.http.get<CustomerProfile>(`${this.api}/customers/me`, { headers: this.customerHeaders(token) }),
      );
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.session.clearCustomerToken();
        return null;
      }

      throw this.toError(error, 'No se pudo recuperar la sesion del cliente.');
    }
  }

  async registerCustomer(payload: RegisterCustomerRequest): Promise<{ token: string; profile: CustomerProfile }> {
    try {
      const response = await firstValueFrom(
        this.http.post<CustomerAuthResponseApi>(`${this.api}/customers/register`, payload),
      );
      this.session.writeCustomerToken(response.token);
      return { token: response.token, profile: response.customer };
    } catch (error) {
      throw this.toError(error, 'No se pudo registrar el cliente.');
    }
  }

  async loginCustomer(payload: LoginCustomerRequest): Promise<{ token: string; profile: CustomerProfile }> {
    try {
      const response = await firstValueFrom(
        this.http.post<CustomerAuthResponseApi>(`${this.api}/customers/login`, payload),
      );
      this.session.writeCustomerToken(response.token);
      return { token: response.token, profile: response.customer };
    } catch (error) {
      throw this.toError(error, 'No se pudo iniciar sesion como cliente.');
    }
  }

  async logoutCustomer(): Promise<void> {
    const token = this.session.readCustomerToken();

    try {
      if (token) {
        await firstValueFrom(
          this.http.post<void>(`${this.api}/customers/logout`, {}, { headers: this.customerHeaders(token) }),
        );
      }
    } catch (error) {
      throw this.toError(error, 'No se pudo cerrar la sesion del cliente.');
    } finally {
      this.session.clearCustomerToken();
    }
  }

  async updateCustomerProfile(payload: UpdateCustomerProfileRequest): Promise<CustomerProfile> {
    const token = this.requireCustomerToken();

    try {
      return await firstValueFrom(
        this.http.put<CustomerProfile>(`${this.api}/customers/me`, payload, { headers: this.customerHeaders(token) }),
      );
    } catch (error) {
      throw this.toError(error, 'No se pudo actualizar el perfil del comprador.');
    }
  }

  async deleteCustomerProfile(): Promise<void> {
    const token = this.requireCustomerToken();

    try {
      await firstValueFrom(
        this.http.delete<void>(`${this.api}/customers/me`, { headers: this.customerHeaders(token) }),
      );
      this.session.clearCustomerToken();
    } catch (error) {
      throw this.toError(error, 'No se pudo eliminar la cuenta del comprador.');
    }
  }

  async getCurrentCustomerOrders(): Promise<CustomerOrdersHistory> {
    const token = this.requireCustomerToken();

    try {
      return await firstValueFrom(
        this.http.get<CustomerOrdersHistory>(`${this.api}/customers/orders`, { headers: this.customerHeaders(token) }),
      );
    } catch (error) {
      throw this.toError(error, 'No se pudo cargar el historial del comprador.');
    }
  }

  private customerHeaders(token: string): HttpHeaders {
    return new HttpHeaders({ 'X-Customer-Token': token });
  }

  private requireCustomerToken(): string {
    const token = this.session.readCustomerToken();
    if (!token) {
      throw new Error('Debes iniciar sesion como cliente para continuar.');
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

      if (error.status === 401) {
        this.session.clearCustomerToken();
      }

      return new Error(message);
    }

    return new Error(fallback);
  }
}
