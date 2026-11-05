import { Component, booleanAttribute, computed, input, output } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { initials } from '../core/format';

@Component({
  selector: 'app-page-header',
  imports: [LucideAngularModule],
  template: `
    <div class="flex flex-wrap items-end gap-3 mb-4 enter-rise">
      <div class="min-w-0">
        <h1 class="text-xl font-bold tracking-tight text-ink flex items-center gap-2">
          @if (icon()) {
            <span class="grid place-items-center size-8 rounded-lg bg-lilac-50 text-brand-ink"
              ><lucide-icon [name]="icon()!" [size]="17"
            /></span>
          }
          {{ title() }}
        </h1>
        @if (subtitle()) {
          <p class="text-[13px] text-muted mt-1">{{ subtitle() }}</p>
        }
      </div>
      <div class="ml-auto flex flex-wrap items-center gap-2"><ng-content /></div>
    </div>
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly icon = input<string>();
}

/** Right-hand panel for create and edit forms, so the list stays in view behind it. */
@Component({
  selector: 'app-drawer',
  imports: [LucideAngularModule],
  host: { '(document:keydown.escape)': 'open() && close.emit()' },
  template: `
    @if (open()) {
      <div
        class="fixed inset-0 bg-ink/25 backdrop-blur-[1px] enter-fade"
        style="z-index: var(--z-drawer-backdrop)"
        (click)="close.emit()"
        aria-hidden="true"
      ></div>
      <aside
        class="fixed right-0 top-0 h-dvh bg-surface shadow-pop flex flex-col enter-drawer border-l border-line"
        [style.width]="'min(100vw, ' + width() + ')'"
        style="z-index: var(--z-drawer)"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="title()"
      >
        <header class="flex items-start gap-3 px-5 py-4 border-b border-line">
          <div class="min-w-0">
            <h2 class="text-base font-bold text-ink">{{ title() }}</h2>
            @if (subtitle()) {
              <p class="text-xs text-muted mt-0.5">{{ subtitle() }}</p>
            }
          </div>
          <button
            type="button"
            class="btn btn-ghost btn-sm btn-icon ml-auto"
            (click)="close.emit()"
            aria-label="Close panel"
          >
            <lucide-icon name="x" [size]="16" />
          </button>
        </header>
        <div class="flex-1 overflow-y-auto px-5 py-4"><ng-content /></div>
        <footer
          class="px-5 py-3 border-t border-line flex items-center justify-end gap-2 bg-subtle/50"
        >
          <ng-content select="[drawer-footer]" />
        </footer>
      </aside>
    }
  `,
})
export class Drawer {
  readonly open = input(false, { transform: booleanAttribute });
  readonly title = input('');
  readonly subtitle = input<string>();
  readonly width = input('520px');
  readonly close = output<void>();
}

/** First validation message for a control, once the user has touched it. */
@Component({
  selector: 'app-field-error',
  template: `@if (message()) {
    <p class="error-text" role="alert">{{ message() }}</p>
  }`,
})
export class FieldError {
  readonly control = input<AbstractControl | null>(null);
  readonly label = input('This field');

  message(): string {
    const c = this.control();
    if (!c?.errors || !(c.touched || c.dirty)) return '';
    const e = c.errors;
    if (e['required']) return `${this.label()} is required.`;
    if (e['email']) return 'Enter a valid email address.';
    if (e['min']) return `${this.label()} must be at least ${e['min'].min}.`;
    if (e['max']) return `${this.label()} must be at most ${e['max'].max}.`;
    if (e['maxlength'])
      return `${this.label()} can be at most ${e['maxlength'].requiredLength} characters.`;
    if (e['pattern']) return `${this.label()} has an invalid format.`;
    if (e['server']) return e['server'];
    return `${this.label()} is invalid.`;
  }
}

@Component({
  selector: 'app-avatar',
  template: `<span
    class="inline-grid place-items-center rounded-full font-bold shrink-0 bg-lilac-100 text-brand-strong"
    [style.width.px]="size()"
    [style.height.px]="size()"
    [style.font-size.px]="size() * 0.38"
    >{{ letters() }}</span
  >`,
})
export class Avatar {
  readonly name = input<string | null>('');
  readonly size = input(26);
  readonly letters = computed(() => initials(this.name()));
}

@Component({
  selector: 'app-stat',
  imports: [LucideAngularModule],
  template: `
    <div class="kpi" [style.background]="'var(--grad-' + tone() + ')'">
      <div class="relative z-10 flex items-start justify-between gap-2">
        <p class="text-xs font-semibold opacity-90">{{ label() }}</p>
        <span class="grid place-items-center size-8 rounded-full bg-white/18 ring-1 ring-white/25"
          ><lucide-icon [name]="icon()" [size]="15"
        /></span>
      </div>
      <p class="relative z-10 mt-2 text-[26px] leading-none font-bold tracking-tight tabular">
        {{ value() }}
      </p>
      <div class="relative z-10 mt-auto pt-3 flex items-center gap-1.5 text-2xs font-medium">
        @if (delta() !== null) {
          <span
            class="inline-flex items-center gap-0.5 h-5 px-1.5 rounded-full bg-white/20 font-bold tabular"
          >
            <lucide-icon [name]="delta()! >= 0 ? 'trending-up' : 'trending-down'" [size]="12" />{{
              delta()! >= 0 ? '+' : ''
            }}{{ delta() }}%
          </span>
        }
        <span class="opacity-90 truncate">{{ caption() }}</span>
      </div>
    </div>
  `,
})
export class Stat {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly icon = input.required<string>();
  readonly tone = input<'brand' | 'mint' | 'amber' | 'danger' | 'teal' | 'orchid'>('brand');
  readonly caption = input('');
  readonly delta = input<number | null>(null);
}

/** Percentage change, or null when there is nothing to compare against. */
export function change(current: number, previous: number): number | null {
  if (!previous) return null;
  return Math.round(((current - previous) / previous) * 1000) / 10;
}
