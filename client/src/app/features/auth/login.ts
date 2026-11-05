import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { LucideAngularModule } from 'lucide-angular';
import { AuthService } from '../../core/auth.service';
import { DemoAccount } from '../../core/models';
import { problemMessage } from '../../core/interceptors';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, LucideAngularModule],
  templateUrl: './login.html',
})
export class Login implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);

  readonly form = inject(FormBuilder).nonNullable.group({
    username: ['', [Validators.required, Validators.maxLength(50)]],
    password: ['', [Validators.required, Validators.maxLength(100)]],
  });

  readonly demoAccounts = signal<DemoAccount[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly showPassword = signal(false);

  readonly highlights = [
    {
      icon: 'kanban',
      title: 'A pipeline you can drag',
      text: 'Move deals between stages and watch the forecast follow.',
    },
    {
      icon: 'magnet',
      title: 'Leads to deals in one click',
      text: 'Convert a lead and the company, contact and deal appear together.',
    },
    {
      icon: 'list-checks',
      title: 'Nothing slips',
      text: 'Overdue calls and follow-ups surface in the bell and on the dashboard.',
    },
  ];

  async ngOnInit() {
    try {
      const config = await this.auth.config();
      if (config.demoMode) this.demoAccounts.set(config.demoAccounts);
    } catch {
      /* the API is down; the sign-in attempt will say so */
    }
  }

  useDemo(account: DemoAccount) {
    this.form.setValue({ username: account.username, password: account.username });
    void this.submit();
  }

  async submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      const { username, password } = this.form.getRawValue();
      await this.auth.login(username, password);
      this.toast.success(`Signed in as ${this.auth.user()?.fullName}.`, 'Welcome back');
      await this.router.navigateByUrl(
        this.route.snapshot.queryParamMap.get('returnUrl') ?? '/dashboard',
      );
    } catch (err) {
      this.error.set(
        err instanceof HttpErrorResponse ? problemMessage(err) : 'Sign-in failed. Try again.',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
