import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthConfig, AuthUser, TokenResponse } from './models';

/** Requests flagged with this do not raise an error toast. */
export const SILENT = new HttpContextToken<boolean>(() => false);

const STORAGE_KEY = 'nx:session';

interface Session {
  token: string;
  expiresAt: string;
  user: AuthUser;
}

/**
 * Keeps the session for this browser tab only (sessionStorage), so closing the tab signs you out.
 * The API checks every request, so nothing here is a security boundary.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(this.read());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly canDelete = computed(() => this.user()?.canDelete ?? false);

  get token() {
    return this.session()?.token ?? null;
  }

  config() {
    return firstValueFrom(
      this.http.get<AuthConfig>('/api/auth/config', {
        context: new HttpContext().set(SILENT, true),
      }),
    );
  }

  async login(username: string, password: string) {
    const res = await firstValueFrom(
      this.http.post<TokenResponse>(
        '/api/auth/login',
        { username, password },
        { context: new HttpContext().set(SILENT, true) },
      ),
    );
    this.store({ token: res.accessToken, expiresAt: res.expiresAt, user: res.user });
  }

  /** Ends the session on the server (every token stops working), then locally. */
  async signOut() {
    if (this.token) {
      try {
        await firstValueFrom(
          this.http.post('/api/auth/logout', {}, { context: new HttpContext().set(SILENT, true) }),
        );
      } catch {
        /* the token may already be dead; signing out locally is what matters now */
      }
    }
    this.logout();
  }

  /** Local sign-out only, for when the server already ended the session (a 401). */
  logout(returnUrl?: string) {
    this.store(null);
    void this.router.navigate(['/login'], { queryParams: returnUrl ? { returnUrl } : {} });
  }

  private store(session: Session | null) {
    this.session.set(session);
    try {
      if (session) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
      else sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      /* private mode: the session lasts until reload */
    }
  }

  private read(): Session | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      const session = raw ? (JSON.parse(raw) as Session) : null;
      return session && new Date(session.expiresAt).getTime() > Date.now() ? session : null;
    } catch {
      return null;
    }
  }
}
