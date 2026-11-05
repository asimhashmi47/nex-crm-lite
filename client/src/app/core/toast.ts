// Pure helpers with no Angular imports, so they run under node --test.

export type ToastTone = 'success' | 'error' | 'warning' | 'info';

export const TOAST_MS = 10_000;

const TONES: Record<ToastTone, { bg: string; ink: string; icon: string }> = {
  success: { bg: 'oklch(0.97 0.03 165)', ink: 'oklch(0.36 0.09 165)', icon: 'oklch(0.6 0.13 165)' },
  error: { bg: 'oklch(0.97 0.022 12)', ink: 'oklch(0.42 0.15 12)', icon: 'oklch(0.5 0.17 12)' },
  warning: { bg: 'oklch(0.975 0.035 80)', ink: 'oklch(0.45 0.12 55)', icon: 'oklch(0.76 0.15 65)' },
  info: { bg: '#F1EAFF', ink: 'oklch(0.42 0.17 300)', icon: 'oklch(0.52 0.2 302)' },
};

/**
 * SweetAlert2 options for a top-right toast with a 10 second progress bar.
 * Messages often quote user data (names, server errors), so they go through titleText / text,
 * which SweetAlert2 renders as plain text. Never title or html.
 */
export function toastOptions(tone: ToastTone, message: string, title?: string) {
  const t = TONES[tone];
  return {
    toast: true,
    position: 'top-end' as const,
    icon: tone,
    iconColor: t.icon,
    titleText: title ?? message,
    text: title ? message : undefined,
    background: t.bg,
    color: t.ink,
    showConfirmButton: false,
    showCloseButton: true,
    timer: TOAST_MS,
    timerProgressBar: true,
  };
}
