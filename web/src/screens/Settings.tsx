import type { Api } from '../api/client';
import { useResource } from '../data/resource';
import { Screen, Section, Row } from '../ui/kit';

/** What this phone is doing here, and which library it's talking to. Pairing and removing phones stays on the
 * library's own computer (Settings → Phone) — this screen just says what's true. */
export function SettingsScreen({ api }: { api: Api }) {
  const me = useResource('me', (signal) => api.me(signal));
  return (
    <Screen title="Settings" large>
      <Section title="This library">
        <Row title={me.data?.library.name ?? '…'} subtitle={me.data ? `Study Stash ${me.data.library.version}` : undefined} />
      </Section>
      <Section footer="To remove this phone, or add another, use Settings → Phone on the library's own computer.">
        <Row title="Add or remove phones" subtitle="On the library's computer" />
      </Section>
    </Screen>
  );
}
