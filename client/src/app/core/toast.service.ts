import { Injectable } from '@angular/core';
import Swal from 'sweetalert2';
import { ToastTone, toastOptions } from './toast';

/** Top-right toasts: 10 s, progress bar, pause on hover, tinted by type. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  success(message: string, title?: string) {
    this.show('success', message, title);
  }
  error(message: string, title?: string) {
    this.show('error', message, title);
  }
  warning(message: string, title?: string) {
    this.show('warning', message, title);
  }
  info(message: string, title?: string) {
    this.show('info', message, title);
  }

  private show(tone: ToastTone, message: string, title?: string) {
    void Swal.fire({
      ...toastOptions(tone, message, title),
      didOpen: (el) => {
        el.addEventListener('mouseenter', Swal.stopTimer);
        el.addEventListener('mouseleave', Swal.resumeTimer);
      },
    });
  }
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  /** Resolves true only on an explicit click of the destructive button. */
  async danger(title: string, text: string, confirmText = 'Delete'): Promise<boolean> {
    const result = await Swal.fire({
      titleText: title,
      text,
      icon: 'warning',
      iconColor: 'oklch(0.5 0.17 12)',
      showCancelButton: true,
      confirmButtonText: confirmText,
      cancelButtonText: 'Keep it',
      reverseButtons: true,
      focusCancel: true,
      customClass: { confirmButton: 'danger' },
    });
    return result.isConfirmed;
  }

  async ask(title: string, text: string, confirmText = 'Continue'): Promise<boolean> {
    const result = await Swal.fire({
      titleText: title,
      text,
      icon: 'question',
      iconColor: 'oklch(0.52 0.2 302)',
      showCancelButton: true,
      confirmButtonText: confirmText,
      cancelButtonText: 'Cancel',
      reverseButtons: true,
    });
    return result.isConfirmed;
  }
}
