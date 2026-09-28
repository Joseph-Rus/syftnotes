import { useRef, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { Me } from '../api/types';
import { cleanCode } from '../boot';
import { currentDeviceName } from '../platform';
import { Button } from '../ui/kit';

/**
 * The 6-digit code from Settings → Add a phone. Reachable-but-not-paired (`GET /me` said `paired: false`) means
 * there's a real library on the other end; typing the wrong or an expired code, or too many tries, comes back in
 * the library's own words (POST /api/v2/devices/pair).
 */
export function Pair({ api, library, onPaired }: { api: Api; library: Me['library'] | null; onPaired: () => void }) {
  const [code, setCode] = useState('');
  const [name, setName] = useState(() => currentDeviceName());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);

  const submit = async (typed = code) => {
    if (typed.length !== 6 || busy) return;
    setBusy(true);
    setError(null);
    try {
      await api.pair(typed, name.trim() || currentDeviceName());
      onPaired();
    } catch (e) {
      setError(e instanceof ApiError ? e.detail : "Couldn't reach your library. Try again.");
      setCode('');
      input.current?.focus();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div class="pair">
      <div class="pair-body">
        <div class="install-icon" aria-hidden="true">
          SS
        </div>
        <h1>{library ? `Add this phone to ${library.name}` : 'Add this phone'}</h1>
        <p class="lede">
          On the computer, open Settings → <strong>Add a phone</strong> and type the 6-digit code it shows here.
        </p>
        <input
          ref={input}
          class="code-input"
          inputMode="numeric"
          autocomplete="one-time-code"
          pattern="\d*"
          maxLength={6}
          placeholder="000000"
          value={code}
          disabled={busy}
          aria-label="6-digit code"
          onInput={(e) => {
            const next = cleanCode(e.currentTarget.value);
            setCode(next);
            if (next.length === 6) void submit(next);
          }}
        />
        {error ? (
          <p class="pair-error" role="alert">
            {error}
          </p>
        ) : null}
        <label class="field">
          <span>What should we call this phone?</span>
          <input
            type="text"
            value={name}
            maxLength={60}
            disabled={busy}
            onInput={(e) => setName(e.currentTarget.value)}
            placeholder={currentDeviceName()}
          />
        </label>
        <Button kind="filled" wide disabled={code.length !== 6 || busy} onClick={() => void submit()}>
          {busy ? 'Pairing…' : 'Pair'}
        </Button>
      </div>
    </div>
  );
}
