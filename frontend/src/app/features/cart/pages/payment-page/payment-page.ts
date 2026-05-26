/*
 * payment-page.ts
 *
 * Muestra el portal de seleccion de metodo de pago antes de confirmar
 * una compra. El pago exige una sesion de cliente activa:
 *
 *   1. El cliente inicia sesion o se registra antes de llegar aqui.
 *   2. Selecciona un metodo y confirma la compra.
 *   3. El frontend llama a /api/orders/checkout con X-Customer-Token.
 *   4. El backend crea los pedidos, descuenta inventario y los deja en
 *      el historial del comprador y la bandeja de la empresa.
 *
 * Si el usuario cancela, el carrito y los datos de envio se conservan.
 */

import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';

import { CartService } from '../../../../core/services/cart.service';
import { CustomerService } from '../../../../core/services/customer.service';
import { OrderService } from '../../../../core/services/order.service';

@Component({
  selector: 'app-payment-page',
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './payment-page.html',
  styleUrl: './payment-page.css',
})
export class PaymentPage implements OnInit {
  private readonly cartService = inject(CartService);
  private readonly customerService = inject(CustomerService);
  private readonly orderService = inject(OrderService);
  private readonly router = inject(Router);

  protected readonly cartTotal = this.cartService.total;
  protected readonly currentCartItems = this.cartService.items;
  protected readonly pendingShipment = this.cartService.pendingShipment;

  protected paymentMethod = signal<string>('');
  protected isLoading = signal(false);
  protected pageFeedback = signal<{ type: 'error' | 'success' | 'info'; message: string } | null>(null);

  async ngOnInit(): Promise<void> {
    // Si no hay datos de envio o el carrito esta vacio, no hay nada que pagar.
    if (!this.pendingShipment() || this.currentCartItems().length === 0) {
      void this.router.navigate(['/carrito']);
      return;
    }

    const customer = await this.customerService.getCurrentCustomer();
    if (!customer) {
      this.pageFeedback.set({
        type: 'error',
        message: 'Debes registrarte o iniciar sesion como cliente para completar la compra.',
      });
      void this.router.navigate(['/cliente']);
    }
  }

  protected async submitPayment(): Promise<void> {
    if (!this.paymentMethod()) {
      this.pageFeedback.set({ type: 'error', message: 'Selecciona un metodo de pago antes de continuar.' });
      return;
    }

    const shipment = this.pendingShipment();
    const items = this.currentCartItems();

    if (!shipment || items.length === 0) {
      void this.router.navigate(['/carrito']);
      return;
    }

    this.isLoading.set(true);
    this.pageFeedback.set(null);

    try {
      const response = await this.orderService.checkoutCart({
        fullName: shipment.fullName,
        email: shipment.email,
        phone: shipment.phone,
        city: shipment.city,
        address: shipment.address,
        notes: shipment.notes,
        paymentMethod: this.paymentMethod(),
        items: items.map(item => ({
          businessId: item.businessId,
          productId: item.productId,
          quantity: item.quantity,
        })),
      });

      this.cartService.clear();
      this.pageFeedback.set({ type: 'success', message: response.message });
      setTimeout(() => void this.router.navigate(['/']), 3000);
    } catch (error) {
      this.pageFeedback.set({
        type: 'error',
        message: error instanceof Error ? error.message : 'No se pudo procesar el pedido.',
      });
    } finally {
      this.isLoading.set(false);
    }
  }
}
