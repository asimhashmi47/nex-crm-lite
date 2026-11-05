import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService, SILENT } from './auth.service';
import { ToastService } from './toast.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token;
  const authed =
    token && req.url.startsWith('/api')
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;

  return next(authed).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && token) auth.logout(location.pathname);
      return throwError(() => err);
    }),
  );
};

/** Every failed call becomes a readable toast, using the server's ProblemDetails title. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (!req.context.get(SILENT) && err.status !== 401)
        toast.error(problemMessage(err), problemTitle(err.status));
      return throwError(() => err);
    }),
  );
};

export function problemMessage(err: HttpErrorResponse): string {
  const body = err.error;
  if (body && typeof body === 'object') {
    const first = body.errors && Object.values(body.errors as Record<string, string[]>)[0];
    if (first?.length) return first[0];
    if (body.title) return body.title;
  }
  if (err.status === 0) return 'Cannot reach the server. Check the connection and try again.';
  if (err.status === 429) return 'Too many attempts. Wait a minute and try again.';
  return 'Something went wrong. Please try again.';
}

function problemTitle(status: number): string | undefined {
  switch (true) {
    case status === 400:
      return 'Check the form';
    case status === 403:
      return 'Not allowed';
    case status === 404:
      return 'Not found';
    case status === 409:
      return 'Could not save';
    case status >= 500:
      return 'Server error';
    default:
      return undefined;
  }
}
