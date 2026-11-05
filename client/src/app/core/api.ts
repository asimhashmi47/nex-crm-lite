import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

/** Promise-returning helpers over HttpClient, plus file downloads. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  get<T>(url: string, params?: Record<string, string | number | boolean>) {
    return firstValueFrom(this.http.get<T>(url, { params }));
  }
  post<T>(url: string, body: unknown = {}, headers?: Record<string, string>) {
    return firstValueFrom(this.http.post<T>(url, body, { headers }));
  }
  put<T>(url: string, body: unknown) {
    return firstValueFrom(this.http.put<T>(url, body));
  }
  patch<T>(url: string, body: unknown) {
    return firstValueFrom(this.http.patch<T>(url, body));
  }
  delete<T = void>(url: string) {
    return firstValueFrom(this.http.delete<T>(url));
  }

  /** POST that answers with a file; saves it under the name the server chose. */
  async download(url: string, body: unknown, fallbackName = 'download'): Promise<void> {
    const req: Observable<HttpResponse<Blob>> = this.http.post(url, body, {
      responseType: 'blob',
      observe: 'response',
    });
    const res = await firstValueFrom(req);
    saveBlob(res.body!, fileNameFrom(res.headers.get('Content-Disposition')) ?? fallbackName);
  }
}

/**
 * One class per record type. The grid, the drawer and the board all talk to the API through it,
 * so a feature never hand-writes URLs.
 */
export abstract class RecordApi<TDto, TRequest> {
  protected readonly api = inject(Api);
  abstract readonly endpoint: string;

  get(id: number) {
    return this.api.get<TDto>(`${this.endpoint}/${id}`);
  }
  /** The key makes a retried or double-clicked create return the first result instead of a copy. */
  create(body: TRequest, idempotencyKey?: string) {
    return this.api.post<TDto>(
      this.endpoint,
      body,
      idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined,
    );
  }
  update(id: number, body: TRequest) {
    return this.api.put<TDto>(`${this.endpoint}/${id}`, body);
  }
  remove(id: number) {
    return this.api.delete(`${this.endpoint}/${id}`);
  }

  save(id: number | null, body: TRequest, idempotencyKey?: string) {
    return id ? this.update(id, body) : this.create(body, idempotencyKey);
  }
}

export function saveBlob(blob: Blob, name: string) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 10_000);
}

function fileNameFrom(header: string | null): string | null {
  if (!header) return null;
  const star = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (star) return decodeURIComponent(star[1]);
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : null;
}
