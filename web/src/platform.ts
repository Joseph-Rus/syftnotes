// What phone this is, and whether the app is running from the Home Screen.

export type Platform = 'ios' | 'android' | 'other';

/** iPhone and iPad (an iPad says it's a Mac, but a Mac has no touch screen), Android, or anything else. */
export function platformOf(userAgent: string, maxTouchPoints = 0): Platform {
  if (/iPhone|iPad|iPod/.test(userAgent)) return 'ios';
  if (/Macintosh/.test(userAgent) && maxTouchPoints > 1) return 'ios';
  if (/Android/.test(userAgent)) return 'android';
  return 'other';
}

/** An iPad (or a big Android tablet): the split layout with a sidebar is the natural one. */
export function isTablet(userAgent: string, maxTouchPoints = 0): boolean {
  return /iPad/.test(userAgent) || (/Macintosh/.test(userAgent) && maxTouchPoints > 1) || /Android(?!.*Mobile)/.test(userAgent);
}

/** A name for this phone in the library's list of devices, before the student changes it. */
export function deviceName(userAgent: string, maxTouchPoints = 0): string {
  if (/iPhone/.test(userAgent)) return 'iPhone';
  if (isTablet(userAgent, maxTouchPoints) && platformOf(userAgent, maxTouchPoints) === 'ios') return 'iPad';
  if (/Android/.test(userAgent)) return isTablet(userAgent) ? 'Android tablet' : 'Android phone';
  return 'This browser';
}

/** Running as an installed app (from the Home Screen), not in a browser tab. */
export function isStandalone(): boolean {
  if (typeof window === 'undefined') return false;
  const nav = navigator as Navigator & { standalone?: boolean };
  return nav.standalone === true || window.matchMedia?.('(display-mode: standalone)').matches === true;
}

export function currentPlatform(): Platform {
  return platformOf(navigator.userAgent, navigator.maxTouchPoints);
}

export function currentDeviceName(): string {
  return deviceName(navigator.userAgent, navigator.maxTouchPoints);
}
