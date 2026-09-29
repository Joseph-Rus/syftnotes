import type { Platform } from '../platform';
import { AddSquare, Share } from '../ui/icons';
import { Button } from '../ui/kit';
import { BASE } from '../router';

/**
 * Shown in a phone's browser, before the app is on the Home Screen: how to add it, for iOS (Share → Add to Home
 * Screen) and Android (the browser's own "Install app" prompt, or the same Share menu on some browsers). "Use it
 * in the browser instead" skips this and goes straight to pairing or the library.
 */
export function Install({ platform, onSkip }: { platform: Platform; onSkip: () => void }) {
  return (
    <div class="install">
      <div class="install-body">
        <img class="install-icon" src={`${BASE}icon-192.png`} alt="" />
        <h1>Add Study Stash to your Home Screen</h1>
        <p class="lede">So it opens full screen, like any other app, and reads your notes even when you're offline.</p>
        {platform === 'ios' ? <IosSteps /> : <AndroidSteps />}
      </div>
      <div class="install-footer">
        <Button kind="plain" onClick={onSkip}>
          Use it in the browser instead
        </Button>
      </div>
    </div>
  );
}

function IosSteps() {
  return (
    <ol class="steps">
      <li>
        <span class="steps-icon">
          <Share size={20} />
        </span>
        <span class="steps-text">Tap <strong>Share</strong> at the bottom of Safari</span>
      </li>
      <li>
        <span class="steps-icon">
          <AddSquare size={20} />
        </span>
        <span class="steps-text">Scroll down and tap <strong>Add to Home Screen</strong></span>
      </li>
      <li>
        <span class="steps-icon" aria-hidden="true">
          →
        </span>
        <span class="steps-text">Tap <strong>Add</strong>, then open it from your Home Screen</span>
      </li>
    </ol>
  );
}

function AndroidSteps() {
  return (
    <ol class="steps">
      <li>
        <span class="steps-icon" aria-hidden="true">
          ⋮
        </span>
        <span class="steps-text">Open your browser's menu</span>
      </li>
      <li>
        <span class="steps-icon">
          <AddSquare size={20} />
        </span>
        <span class="steps-text">Tap <strong>Install app</strong> or <strong>Add to Home screen</strong></span>
      </li>
      <li>
        <span class="steps-icon" aria-hidden="true">
          →
        </span>
        <span class="steps-text">Open it from your Home screen</span>
      </li>
    </ol>
  );
}
