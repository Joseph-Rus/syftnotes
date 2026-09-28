import { useEffect, useMemo, useState } from 'preact/hooks';
import { Api } from './api/client';
import { decide, type BootFacts, type Gate } from './boot';
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
import { ComingUpScreen } from './screens/ComingUp';
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
      <Screens api={api} offline={gate.offline} />
      <TabBar active={tabOf(route)} />
    </div>
  );
}

function Screens({ api, offline }: { api: Api; offline: boolean }) {
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
      return <ComingUpScreen api={api} />;
    case 'upload':
      return <UploadScreen api={api} className={route.class} lecture={route.lecture} />;
    case 'settings':
      return <SettingsScreen api={api} />;
    case 'ask':
      return <Home api={api} offline={offline} />;
  }
}

function Splash() {
  return (
    <div class="splash">
      <Spinner size={28} />
    </div>
  );
}
