import { describe, expect, it } from 'vitest';
import { offeredSecure, secureAddress, withoutOffer } from '../src/upgrade';

const secure = 'https://mini.tail1234.ts.net:8443/app/';
const fromQr = `http://100.87.191.40:8787/app/?https=${encodeURIComponent(secure)}`;

describe('moving from the Tailscale IP to the https name', () => {
  it('moves when the https name answers', async () => {
    const asked: string[] = [];
    const to = await secureAddress(fromQr, async (url) => {
      asked.push(url);
      return true;
    });
    expect(to).toBe(secure);
    expect(asked).toEqual(['https://mini.tail1234.ts.net:8443/api/v2/me']);
  });

  it('stays when the phone can’t reach the name (MagicDNS off)', async () => {
    expect(await secureAddress(fromQr, async () => false)).toBeNull();
  });

  it('never moves without an offer, from https, or to somewhere that isn’t Tailscale’s', async () => {
    const never = async () => {
      throw new Error('asked');
    };
    expect(await secureAddress('http://100.87.191.40:8787/app/', never)).toBeNull();
    expect(
      await secureAddress(`https://mini.tail1234.ts.net:8443/app/?https=${encodeURIComponent(secure)}`, never),
    ).toBeNull();
    expect(
      offeredSecure(`http://100.1.2.3:8787/app/?https=${encodeURIComponent('https://evil.example/app/')}`),
    ).toBeNull();
    expect(
      offeredSecure(`http://100.1.2.3:8787/app/?https=${encodeURIComponent('http://mini.tail1234.ts.net/app/')}`),
    ).toBeNull();
    expect(offeredSecure('http://100.1.2.3:8787/app/?https=not%20a%20url')).toBeNull();
  });

  it('drops the offer from the address it stays on', () => {
    expect(withoutOffer(fromQr)).toBe('http://100.87.191.40:8787/app/');
  });
});
