import type { Platform } from '../platform';
import { AddSquare, Share } from '../ui/icons';
import { Button } from '../ui/kit';

/**
 * Shown in a phone's browser, before the app is on the Home Screen: how to add it, for iOS (Share → Add to Home
 * Screen) and Android (the browser's own "Install app" prompt, or the same Share menu on some browsers). "Use it
 * in the browser instead" skips this and goes straight to pairing or the library.
 */
export function Install({ platform, onSkip }: { platform: Platform; onSkip: () => void }) {
  return (
    <div class="install">
      <div class="install-body">
        <div class="install-icon" aria-hidden="true">
          SS
        </div>
        <h1>Add Study Stash to your Home Screen</h1>
        <p class="lede">So it opens full screen, like any other app, and works while you're offline.</p>
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
        Tap <strong>Share</strong> at the bottom of Safari
      </li>
      <li>
        <span class="steps-icon">
          <AddSquare size={20} />
        </span>
        Scroll down and tap <strong>Add to Home Screen</strong>
      </li>
      <li>
        <span class="steps-icon" aria-hidden="true">
          →
        </span>
        Tap <strong>Add</strong>, then open it from your Home Screen
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
        Open your browser's menu
      </li>
      <li>
        <span class="steps-icon">
          <AddSquare size={20} />
        </span>
        Tap <strong>Install app</strong> or <strong>Add to Home screen</strong>
      </li>
      <li>
        <span class="steps-icon" aria-hidden="true">
          →
        </span>
        Open it from your Home screen
      </li>
    </ol>
  );
}
