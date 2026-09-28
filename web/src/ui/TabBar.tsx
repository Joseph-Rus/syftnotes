import type { JSX } from 'preact';
import { href, type Route, type Tab } from '../router';
import { Books, Search, Calendar, PlusCircle, Gear } from './icons';

const TABS: { tab: Tab; label: string; icon: (p: { filled?: boolean }) => JSX.Element; to: Route }[] = [
  { tab: 'library', label: 'Library', icon: (p) => <Books filled={p.filled} />, to: { name: 'library' } },
  { tab: 'search', label: 'Search', icon: (p) => <Search filled={p.filled} />, to: { name: 'search', q: '' } },
  { tab: 'week', label: 'Coming up', icon: (p) => <Calendar filled={p.filled} />, to: { name: 'coming-up' } },
  { tab: 'upload', label: 'Add files', icon: (p) => <PlusCircle filled={p.filled} />, to: { name: 'upload', class: null, lecture: null } },
  { tab: 'settings', label: 'Settings', icon: (p) => <Gear filled={p.filled} />, to: { name: 'settings' } },
];

/** The bottom bar (an iPhone) or the sidebar (an iPad, in wide.css) between the app's five places. */
export function TabBar({ active }: { active: Tab }) {
  return (
    <nav class="tabbar" aria-label="Study Stash">
      {TABS.map((t) => (
        <a key={t.tab} href={href(t.to)} class={t.tab === active ? 'on' : ''} aria-current={t.tab === active ? 'page' : undefined}>
          {t.icon({ filled: t.tab === active })}
          <span>{t.label}</span>
        </a>
      ))}
    </nav>
  );
}
