export interface NavItem {
  label: string;
  icon: string;
  route: string;
  /** Shown only to these roles; the API enforces the same rule. */
  roles?: string[];
}
export interface NavGroup {
  label: string;
  items: NavItem[];
}

export const NAV: NavGroup[] = [
  {
    label: 'Sell',
    items: [
      { label: 'Dashboard', icon: 'layout-dashboard', route: '/dashboard' },
      { label: 'Pipeline', icon: 'kanban', route: '/deals' },
      { label: 'Leads', icon: 'magnet', route: '/leads' },
    ],
  },
  {
    label: 'Relationships',
    items: [
      { label: 'Contacts', icon: 'contact', route: '/contacts' },
      { label: 'Companies', icon: 'building-2', route: '/companies' },
    ],
  },
  {
    label: 'Work',
    items: [{ label: 'Tasks', icon: 'list-checks', route: '/tasks' }],
  },
  {
    label: 'Admin',
    items: [{ label: 'Audit log', icon: 'scroll-text', route: '/audit', roles: ['Admin'] }],
  },
];

/** The navigation a user may see. */
export function navFor(role: string | undefined): NavGroup[] {
  return NAV.map((g) => ({
    ...g,
    items: g.items.filter((i) => !i.roles || (!!role && i.roles.includes(role))),
  })).filter((g) => g.items.length);
}
