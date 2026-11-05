import {
  Component,
  ElementRef,
  booleanAttribute,
  effect,
  forwardRef,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { Option } from '../core/models';

/**
 * Type-ahead picker for records that are too many for a plain select (companies, contacts, deals).
 * Works as a form control: its value is the record id, and `label` shows the current choice when editing.
 */
@Component({
  selector: 'app-lookup-select',
  imports: [LucideAngularModule],
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => LookupSelect), multi: true },
  ],
  host: { class: 'block relative', '(document:click)': 'onDocClick($event)' },
  template: `
    <div class="relative">
      <lucide-icon
        name="search"
        [size]="14"
        class="absolute left-3 top-1/2 -translate-y-1/2 text-faint pointer-events-none"
      />
      <input
        class="input pl-11 pr-8"
        [id]="inputId()"
        [placeholder]="placeholder()"
        [value]="text()"
        [disabled]="disabled()"
        [class.invalid]="invalid()"
        (focus)="openList()"
        (input)="onType($any($event.target).value)"
        (keydown)="onKey($event)"
        autocomplete="off"
        role="combobox"
        [attr.aria-expanded]="open()"
      />
      @if (value() !== null && !disabled()) {
        <button
          type="button"
          class="absolute right-1.5 top-1/2 -translate-y-1/2 btn btn-ghost btn-sm btn-icon !size-6"
          (click)="clear()"
          aria-label="Clear choice"
        >
          <lucide-icon name="x" [size]="13" />
        </button>
      }
    </div>
    @if (open()) {
      <div
        class="menu-pop absolute left-0 right-0 mt-1 max-h-64 overflow-y-auto enter-fade"
        role="listbox"
      >
        @for (o of options(); track o.id; let i = $index) {
          <button
            type="button"
            class="menu-item !h-auto py-1.5"
            [class.bg-lilac-50]="i === active()"
            (mousedown)="pick(o)"
            role="option"
            [attr.aria-selected]="o.id === value()"
          >
            <span class="flex-1 min-w-0">
              <span class="block truncate text-ink">{{ o.label }}</span>
              @if (o.hint) {
                <span class="block truncate text-2xs text-muted">{{ o.hint }}</span>
              }
            </span>
            @if (o.id === value()) {
              <lucide-icon name="check" [size]="14" class="text-brand" />
            }
          </button>
        } @empty {
          <p class="px-2.5 py-2 text-xs text-muted">
            {{ loading() ? 'Searching…' : 'No matches' }}
          </p>
        }
      </div>
    }
  `,
})
export class LookupSelect implements ControlValueAccessor {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly search = input.required<(term: string) => Promise<Option[]>>();
  readonly label = input<string | null>(null);
  readonly placeholder = input('Search…');
  readonly inputId = input<string>();
  readonly invalid = input(false, { transform: booleanAttribute });

  readonly value = signal<number | null>(null);
  readonly text = signal('');
  readonly options = signal<Option[]>([]);
  readonly open = signal(false);
  readonly loading = signal(false);
  readonly active = signal(0);
  readonly disabled = signal(false);

  constructor() {
    // The drawer sets the form value before the label input catches up, so follow the label as it arrives.
    effect(() => {
      const label = this.label();
      if (untracked(this.value) !== null) this.text.set(label ?? '');
    });
  }

  private onChange: (v: number | null) => void = () => {};
  private onTouched: () => void = () => {};
  private timer?: ReturnType<typeof setTimeout>;
  private request = 0;

  writeValue(id: number | null): void {
    this.value.set(id ?? null);
    this.text.set(id ? (this.label() ?? '') : '');
  }

  registerOnChange(fn: (v: number | null) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }

  openList() {
    this.open.set(true);
    if (!this.options().length) this.load('');
  }

  onType(term: string) {
    this.text.set(term);
    this.open.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.load(term.trim()), 220);
  }

  onKey(e: KeyboardEvent) {
    const count = this.options().length;
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        this.open.set(true);
        this.active.set(Math.min(count - 1, this.active() + 1));
        break;
      case 'ArrowUp':
        e.preventDefault();
        this.active.set(Math.max(0, this.active() - 1));
        break;
      case 'Enter':
        if (this.open() && count) {
          e.preventDefault();
          this.pick(this.options()[this.active()]);
        }
        break;
      case 'Escape':
        if (this.open()) {
          e.stopPropagation();
          this.close();
        }
        break;
    }
  }

  pick(option: Option) {
    this.value.set(option.id);
    this.text.set(option.label);
    this.onChange(option.id);
    this.close();
  }

  clear() {
    this.value.set(null);
    this.text.set('');
    this.onChange(null);
    this.load('');
  }

  onDocClick(e: Event) {
    if (this.open() && !this.host.nativeElement.contains(e.target as Node)) this.close();
  }

  private close() {
    this.open.set(false);
    this.onTouched();
    // Typing without picking keeps the previous choice rather than a half-typed name.
    if (this.value() !== null && !this.text()) this.clear();
  }

  private async load(term: string) {
    const ticket = ++this.request;
    this.loading.set(true);
    try {
      const result = await this.search()(term);
      if (ticket === this.request) {
        this.options.set(result);
        this.active.set(0);
      }
    } finally {
      if (ticket === this.request) this.loading.set(false);
    }
  }
}
