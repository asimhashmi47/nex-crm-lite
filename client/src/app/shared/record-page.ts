import { Directive, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormGroup } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RecordApi } from '../core/api';
import { AuthService } from '../core/auth.service';
import { ConfirmService, ToastService } from '../core/toast.service';
import { LookupApi } from '../core/record-apis';
import { Option } from '../core/models';
import { DataTable } from './data-table/data-table';

/**
 * Base for every "grid + drawer" page. It owns the workflow (open, fill, validate, save, refresh, delete);
 * a page supplies the form, how a row maps into it, and the words it uses for the record.
 */
@Directive()
export abstract class RecordPage<TRow extends { id: number }, TForm> {
  protected readonly auth = inject(AuthService);
  protected readonly toast = inject(ToastService);
  protected readonly confirm = inject(ConfirmService);
  protected readonly lookups = inject(LookupApi);
  protected readonly route = inject(ActivatedRoute);

  protected abstract readonly records: RecordApi<TRow, TForm>;
  protected abstract readonly noun: string;
  abstract readonly form: FormGroup;

  protected abstract blank(): Partial<TForm>;
  protected abstract toForm(row: TRow): TForm;
  protected abstract describe(row: TRow): string;

  readonly table = viewChild(DataTable);
  readonly drawerOpen = signal(false);
  readonly editing = signal<TRow | null>(null);
  readonly saving = signal(false);
  readonly owners = signal<Option[]>([]);
  readonly initialSearch = this.route.snapshot.queryParamMap.get('search') ?? '';

  constructor() {
    void this.lookups.owners().then((o) => this.owners.set(o));
  }

  /** One key per opened "new" form: however often Save is pressed, one record is created. */
  private createKey = '';

  openNew() {
    this.createKey = crypto.randomUUID();
    this.editing.set(null);
    this.form.reset({ ownerId: this.auth.user()?.id, ...this.blank() });
    this.drawerOpen.set(true);
  }

  edit(row: TRow) {
    this.editing.set(row);
    this.form.reset(this.toForm(row));
    this.drawerOpen.set(true);
  }

  close() {
    this.drawerOpen.set(false);
  }

  async save() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    try {
      const id = this.editing()?.id ?? null;
      const saved = await this.records.save(id, this.payload(), this.createKey);
      this.toast.success(
        `${this.describe(saved)} ${id ? 'updated' : 'added'}.`,
        `${this.noun} saved`,
      );
      this.drawerOpen.set(false);
      this.afterChange();
    } catch (err) {
      this.showServerErrors(err);
    } finally {
      this.saving.set(false);
    }
  }

  async remove(row: TRow) {
    const ok = await this.confirm.danger(
      `Delete this ${this.noun.toLowerCase()}?`,
      `${this.describe(row)} will be archived and removed from every list.`,
    );
    if (!ok) return;
    try {
      await this.records.remove(row.id);
      this.toast.success(`${this.describe(row)} deleted.`);
      this.afterChange();
    } catch {
      /* the interceptor already explained why */
    }
  }

  /** What goes over the wire. Pages override this when the form shape differs from the request. */
  protected payload(): TForm {
    return this.form.getRawValue() as TForm;
  }

  protected afterChange() {
    this.table()?.reload();
  }

  /** Turns ?status=New style links (from the dashboard and the bell) into the grid's column filters. */
  protected filtersFromUrl(...keys: string[]): Record<string, string> {
    const params = this.route.snapshot.queryParamMap;
    return Object.fromEntries(keys.filter((k) => params.has(k)).map((k) => [k, params.get(k)!]));
  }

  invalid(name: string) {
    const c = this.form.get(name);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  private showServerErrors(err: unknown) {
    if (!(err instanceof HttpErrorResponse) || err.status !== 400) return;
    const errors = (err.error?.errors ?? {}) as Record<string, string[]>;
    for (const [field, messages] of Object.entries(errors)) {
      this.form.get(field)?.setErrors({ server: messages[0] });
    }
  }
}
