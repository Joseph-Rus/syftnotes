import { useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import { forget, useResource } from '../data/resource';
import { useStore } from '../data/store';
import { ago } from '../format';
import { themes, type Appearance } from '../theme/themes';
import { look } from '../theme/useTheme';
import { Check } from '../ui/icons';
import { Button, Row, Screen, Section, Segmented } from '../ui/kit';

/** This phone and the library it reads: which library, this phone's name, the look (the Mac's twenty themes, light or
 * dark), and removing this phone, which locks it out at once as Remove does on the computer. */
export function SettingsScreen({ api, onRemoved }: { api: Api; onRemoved: () => void }) {
  const me = useResource('me', (signal) => api.me(signal));
  const chosen = useStore(look);
  const [confirming, setConfirming] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  const remove = async () => {
    const id = me.data?.device?.id;
    if (!id) return;
    try {
      await api.removeDevice(id);
      forget();
      onRemoved();
    } catch (e) {
      setProblem(e instanceof ApiError ? e.detail : "Couldn't reach your library.");
    }
  };

  const device = me.data?.device;
  return (
    <Screen title="Settings" large>
      <Section title="Library">
        <Row title={me.data?.library.name ?? '…'} subtitle={me.data ? `Study Stash ${me.data.library.version}` : undefined} />
      </Section>

      <Section title="Look">
        <div class="setting-block">
          <Segmented<Appearance>
            label="Light or dark"
            value={chosen.appearance}
            options={[
              { value: 'system', label: 'Automatic' },
              { value: 'light', label: 'Light' },
              { value: 'dark', label: 'Dark' },
            ]}
            onChange={(appearance) => look.set({ ...chosen, appearance })}
          />
          <div class="swatches" role="radiogroup" aria-label="Colour theme">
            {themes.map((t) => (
              <button
                type="button"
                role="radio"
                aria-checked={chosen.theme === t.name}
                aria-label={t.name}
                title={t.name}
                class={`swatch${chosen.theme === t.name ? ' on' : ''}`}
                style={{ background: `oklch(${t.l} ${t.c} ${t.h})` }}
                onClick={() => look.set({ ...chosen, theme: t.name })}
              >
                {chosen.theme === t.name ? <Check size={16} /> : null}
              </button>
            ))}
          </div>
          <p class="setting-note">{chosen.theme}</p>
        </div>
      </Section>

      <Section
        title="This phone"
        footer={device ? `Paired ${ago(device.added)}. Add another phone from Study Stash's Settings → Phone on your computer.` : undefined}
      >
        <Row title={device?.name ?? 'This phone'} subtitle={device ? 'Reads your library until it’s removed' : undefined} />
        {device ? (
          confirming ? (
            <div class="confirm">
              <p>This phone won't be able to read your library until it's added again with a new code.</p>
              <div class="confirm-buttons">
                <Button kind="tinted" small onClick={() => setConfirming(false)}>
                  Keep it
                </Button>
                <Button kind="danger" small onClick={() => void remove()}>
                  Remove this phone
                </Button>
              </div>
              {problem ? <p class="pair-error">{problem}</p> : null}
            </div>
          ) : (
            <Row title={<span class="danger-text">Remove this phone</span>} onClick={() => setConfirming(true)} />
          )
        ) : null}
      </Section>
    </Screen>
  );
}
