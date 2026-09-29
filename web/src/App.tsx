import { useEffect, useMemo, useState } from 'preact/hooks';
import { Api } from './api/client';
import { decide, type BootFacts, type Gate } from './boot';
import { secureAddress, withoutOffer } from './upgrade';
import { currentPlatform, isStandalone } from './platform';
import { onLinkClick, tabOf, useRoute } from './router';
import { useTheme } from './theme/useTheme';
import { Spinner } from './ui/kit';
import { TabBar } from './ui/TabBar';
import { Install } from './screens/Install';
import { Pair } from './screens/Pair';
import { Unreachable } from './screens/Unreachable';
import { Home } from './screens/Home';
import { ClassScreen } from './screens/ClassScreen';
import { LectureScreen } from './screens/LectureScreen';
import { UploadScreen } from './screens/Upload';
import { SearchScreen } from './screens/Search';
import { DueScreen } from './screens/Due';
import { AssignmentScreen } from './screens/Assignment';
import { AskScreen } from './screens/Ask';
import { VoiceMemoScreen } from './screens/VoiceMemo';
import { useResource } from './data/resource';
import { SettingsScreen } from './screens/Settings';

const SKIPPED_KEY = 'ss.skippedInstall';
const PAIRED_KEY = 'ss.pairedBefore';

function readFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}

function writeFlag(key: string, value: boolean) {
  try {
    if (value) localStorage.setItem(key, '1');
    else localStorage.removeItem(key);
  } catch {
    // private mode: this visit only
  }
}

/** The app: decides what to show first (install, pair, unreachable, or the library), then routes between screens. */
export function App() {
  useTheme();
  const api = useMemo(() => new Api({ onUnauthorized: () => setGate({ kind: 'pair', library: null }) }), []);
  const [gate, setGate] = useState<Gate | 'loading'>('loading');
  const { route } = useRoute();

  useEffect(() => {
    document.addEventListener('click', onLinkClick);
    return () => document.removeEventListener('click', onLinkClick);
  }, []);

  const check = async () => {
    // Opened from the QR code at the library's Tailscale IP: move to its https name if this phone can reach it.
    const secure = await secureAddress(window.location.href);
    if (secure) {
      window.location.replace(secure);
      return;
    }
    if (window.location.search.includes('https=')) history.replaceState(null, '', withoutOffer(window.location.href));
    const facts: BootFacts = {
      standalone: isStandalone(),
      platform: currentPlatform(),
      skippedInstall: readFlag(SKIPPED_KEY),
      pairedBefore: readFlag(PAIRED_KEY),
    };
    const next = await decide(api, facts);
    if (next.kind === 'ready') writeFlag(PAIRED_KEY, true);
    setGate(next);
  };

  useEffect(() => {
    void check();
  }, []);

  if (gate === 'loading') return <Splash />;

  if (gate.kind === 'install')
    return (
      <Install
        platform={gate.platform}
        onSkip={() => {
          writeFlag(SKIPPED_KEY, true);
          void check();
        }}
      />
    );

  if (gate.kind === 'unreachable') return <Unreachable onRetry={check} />;

  if (gate.kind === 'outdated') return <Unreachable onRetry={check} outdated />;

  if (gate.kind === 'pair')
    return (
      <Pair
        api={api}
        library={gate.library}
        onPaired={() => {
          writeFlag(PAIRED_KEY, true);
          void check();
        }}
      />
    );

  return (
    <div class="app-shell">
      <Screens api={api} offline={gate.offline} onRemoved={() => setGate({ kind: 'pair', library: null })} />
      <Tabs api={api} route={route} />
    </div>
  );
}

/** The tab bar, with how much is overdue on Due's icon. */
function Tabs({ api, route }: { api: Api; route: ReturnType<typeof useRoute>['route'] }) {
  const due = useResource('due', (signal) => api.due(signal));
  const overdue = due.data?.groups.find((g) => g.key === 'overdue')?.items.length ?? 0;
  return <TabBar active={tabOf(route)} badges={{ due: overdue }} />;
}

function Screens({ api, offline, onRemoved }: { api: Api; offline: boolean; onRemoved: () => void }) {
  const { route } = useRoute();
  switch (route.name) {
    case 'library':
    case 'not-found':
      return <Home api={api} offline={offline} />;
    case 'class':
      return <ClassScreen api={api} className={route.class} />;
    case 'lecture':
      return <LectureScreen api={api} id={route.id} />;
    case 'search':
      return <SearchScreen api={api} q={route.q} />;
    case 'due':
    case 'coming-up':
      return <DueScreen api={api} />;
    case 'assignment':
      return <AssignmentScreen api={api} className={route.class} id={route.id} />;
    case 'upload':
      return <UploadScreen api={api} className={route.class} lecture={route.lecture} />;
    case 'memo':
      return <VoiceMemoScreen api={api} />;
    case 'settings':
      return <SettingsScreen api={api} onRemoved={onRemoved} />;
    case 'ask':
      return <AskScreen api={api} />;
  }
}

function Splash() {
  return (
    <div class="splash">
      <Spinner size={28} />
    </div>
  );
}
