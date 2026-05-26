import { environment } from '../../../environments/environment';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthResponseApi, LoginBusinessRequest, RegisterBusinessRequest, UpdateBusinessProfileRequest } from '../models/auth.models';
import { MarketplaceBusiness } from '../models/marketplace.models';
import { SessionService } from './session.service';

// Servicio de autenticacion y gestion de perfil para empresarios.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly api = environment.apiUrl;

  // Lectura publica del token de empresario (usado por componentes para decidir visibilidad).
  readOwnerToken(): string | null {
    return this.session.readOwnerToken();
  }

  hasBusinessSession(): boolean {
    return this.session.hasOwnerSession();
  }

  async getCurrentBusiness(): Promise<MarketplaceBusiness | null> {
    const token = this.session.readOwnerToken();
    if (!token) {
      return null;
    }

    try {
      return await firstValueFrom(
        this.http.get<MarketplaceBusiness>(`${this.api}/auth/me`, {
          headers: this.ownerHeaders(token),
        }),
      );
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.session.clearOwnerToken();
        return null;
      }

      throw this.toError(error, 'No se pudo recuperar la sesion empresarial.');
    }
  }

  async registerBusiness(payload: RegisterBusinessRequest): Promise<MarketplaceBusiness> {
    try {
      const response = await firstValueFrom(
        this.http.post<AuthResponseApi>(`${this.api}/auth/register`, payload),
      );
      this.session.writeOwnerToken(response.token);
      return response.business;
    } catch (error) {
      throw this.toError(error, 'No se pudo registrar la empresa.');
    }
  }

  async loginBusiness(payload: LoginBusinessRequest): Promise<MarketplaceBusiness> {
    try {
      const response = await firstValueFrom(
        this.http.post<AuthResponseApi>(`${this.api}/auth/login`, payload),
      );
      this.session.writeOwnerToken(response.token);
      return response.business;
    } catch (error) {
      throw this.toError(error, 'No se pudo iniciar sesion.');
    }
  }

  async logoutBusiness(): Promise<void> {
    const token = this.session.readOwnerToken();

    try {
      if (token) {
        await firstValueFrom(
          this.http.post<void>(`${this.api}/auth/logout`, {}, { headers: this.ownerHeaders(token) }),
        );
      }
    } catch (error) {
      throw this.toError(error, 'No se pudo cerrar la sesion empresarial.');
    } finally {
      this.session.clearOwnerToken();
    }
  }

  async updateCurrentBusiness(payload: UpdateBusinessProfileRequest): Promise<MarketplaceBusiness> {
    const token = this.requireOwnerToken();

    try {
      return await firstValueFrom(
        this.http.put<MarketplaceBusiness>(`${this.api}/auth/me`, payload, {
          headers: this.ownerHeaders(token),
        }),
      );
    } catch (error) {
      throw this.toError(error, 'No se pudo guardar el perfil empresarial.');
    }
  }

  async deleteBusiness(): Promise<void> {
    const token = this.requireOwnerToken();

    try {
      await firstValueFrom(
        this.http.delete<void>(`${this.api}/auth/me`, { headers: this.ownerHeaders(token) }),
      );
      this.session.clearOwnerToken();
    } catch (error) {
      throw this.toError(error, 'No se pudo eliminar la cuenta empresarial.');
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
        this.session.clearOwnerToken();
      }

      return new Error(message);
    }

    return new Error(fallback);
  }
}
