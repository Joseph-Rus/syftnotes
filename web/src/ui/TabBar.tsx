import type { JSX } from 'preact';
import { href, type Route, type Tab } from '../router';
import { Books, Checklist, Gear, Search, Sparkle } from './icons';

const TABS: { tab: Tab; label: string; icon: (p: { filled?: boolean }) => JSX.Element; to: Route }[] = [
  { tab: 'library', label: 'Library', icon: (p) => <Books filled={p.filled} />, to: { name: 'library' } },
  { tab: 'due', label: 'Due', icon: (p) => <Checklist filled={p.filled} />, to: { name: 'due' } },
  { tab: 'ask', label: 'Ask', icon: (p) => <Sparkle filled={p.filled} />, to: { name: 'ask', chat: null } },
  { tab: 'search', label: 'Search', icon: (p) => <Search filled={p.filled} />, to: { name: 'search', q: '' } },
  { tab: 'settings', label: 'Settings', icon: (p) => <Gear filled={p.filled} />, to: { name: 'settings' } },
];

/** The bottom bar (an iPhone) or the sidebar (an iPad, in screens.css) between the app's five places. A tab's own
 * badge (how much is overdue) sits on its icon. */
export function TabBar({ active, badges = {} }: { active: Tab; badges?: Partial<Record<Tab, number>> }) {
  return (
    <nav class="tabbar" aria-label="Study Stash">
      {TABS.map((t) => {
        const badge = badges[t.tab] ?? 0;
        return (
          <a key={t.tab} href={href(t.to)} class={t.tab === active ? 'on' : ''} aria-current={t.tab === active ? 'page' : undefined}>
            <span class="tab-icon">
              {t.icon({ filled: t.tab === active })}
              {badge > 0 ? <span class="tab-badge">{badge > 99 ? '99+' : badge}</span> : null}
            </span>
            <span>{t.label}</span>
          </a>
        );
      })}
    </nav>
  );
}
