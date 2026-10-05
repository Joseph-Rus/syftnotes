import React from 'react';
import {AbsoluteFill, useCurrentFrame} from 'remotion';
import {colors} from '../../config';
import {Cursor, Desktop, Title} from '../../components/Layout';
import {prog, px} from '../../promo2/scenes/Bookends';
import {cue, titles} from '../config';
import {G, Glyph} from '../chrome';
import {CW, CX, GroupBox, Head, PageTitle, RowText, Sep, SettingsWindow, Switch, useSettingsStage} from '../settings';

// Settings → AI tool access (MacAiAccess, "mac-14-ai-tool-access-*"): what AI apps may read, and the AI apps on this
// computer, each connected in one click. Connect, for Claude Desktop: "Added. Quit and reopen Claude Desktop to load
// it." (AiAccessModel), and once it's opened again, "Connected · started 13:41".

const Check: React.FC<{on: boolean}> = ({on}) => (
  <div style={{width: 16, height: 16, borderRadius: 4, background: on ? colors.lagoon : 'transparent', border: on ? 'none' : '1.5px solid rgba(255,255,255,0.35)', boxSizing: 'border-box', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0}}>
    {on ? <G name="check" size={12} color="#FFFFFF" width={2.6} /> : null}
  </div>
);

const AppIcon: React.FC<{glyph: Glyph}> = ({glyph}) => (
  <div style={{width: 28, height: 28, borderRadius: 7, background: 'rgba(255,255,255,0.08)', display: 'flex', alignItems: 'center', justifyContent: 'center', marginRight: 12, flexShrink: 0}}>
    <G name={glyph} size={16} color={colors.text} />
  </div>
);

const Connect: React.FC<{press?: number}> = ({press = 0}) => (
  <div style={{height: 30, padding: '0 14px', borderRadius: 15, background: 'rgba(14,149,148,0.28)', border: '1px solid rgba(43,183,178,0.35)', boxSizing: 'border-box', color: '#6FD9D3', fontSize: 13, fontWeight: 700, display: 'flex', alignItems: 'center', transform: `scale(${px(1 - 0.05 * press)})`, flexShrink: 0}}>
    Connect
  </div>
);

const Disconnect: React.FC = () => <div style={{fontSize: 13, color: colors.text2, padding: '0 14px', flexShrink: 0}}>Disconnect</div>;

export const AiApps: React.FC = () => {
  const f = useCurrentFrame();
  const st = useSettingsStage();
  const {z, h, left, top} = st;
  const CONNECT = cue('aiapps-connect');
  const LOADED = CONNECT + 66; // Claude Desktop is opened again, and starts Study Stash
  const enter = prog(f, 0, 18);
  const added = f >= CONNECT + 1;
  const connected = f >= LOADED;
  const status = connected ? (
    <span style={{display: 'inline-flex', alignItems: 'center', gap: 6}}>
      <span style={{width: 6, height: 6, borderRadius: 3, background: '#34C759'}} /> Connected · started 13:41
    </span>
  ) : added ? (
    'Added. Quit and reopen Claude Desktop to load it.'
  ) : (
    'Not connected'
  );
  const statusP = added ? prog(f, added && !connected ? CONNECT + 1 : LOADED, (added && !connected ? CONNECT : LOADED) + 8) : 1;

  // Claude Desktop's Connect, on the screen (the page unscrolled: the AI apps group starts after the read options).
  const rowsTop = 62 + 32 + 8 + 40 + 47 + 4 * 41 + 3 + 47;
  const connectAt = st.at(CX + CW - 16 - 40, rowsTop + 30);

  return (
    <AbsoluteFill>
      <Desktop clock={connected ? 'Thu 25 Sep  13:41' : 'Thu 25 Sep  13:40'}>
        <div style={{position: 'absolute', left, top, opacity: enter, transform: `translateY(${px((1 - enter) * 24)}px) scale(${z})`, transformOrigin: '0 0'}}>
          <SettingsWindow on="AI tool access" h={h}>
            <PageTitle title="AI tool access" right={<Switch on={1} />} lede="Claude, Codex, Gemini and other apps that support MCP can read your library. They can’t change or delete anything." />
            <Head>What they can read</Head>
            <GroupBox>
              {[
                ['Lectures and transcripts', true],
                ['Study notes', true],
                ['Canvas assignments and files', true],
                ['Audio recordings', false],
              ].map(([t, on], i) => (
                <React.Fragment key={t as string}>
                  {i ? <Sep /> : null}
                  <div style={{display: 'flex', alignItems: 'center', gap: 12, height: 40, padding: '0 16px', fontSize: 13.5}}>
                    <Check on={on as boolean} /> {t as string}
                  </div>
                </React.Fragment>
              ))}
            </GroupBox>
            <Head>AI apps on this computer</Head>
            <GroupBox>
              <div style={{display: 'flex', alignItems: 'center', minHeight: 60, padding: '8px 16px', boxSizing: 'border-box'}}>
                <AppIcon glyph="desktop" />
                <RowText title="Claude Desktop" sub={<span style={{opacity: statusP}}>{status}</span>} />
                {added ? <Disconnect /> : <Connect press={Math.max(0, 1 - Math.abs(f - CONNECT - 1) / 3)} />}
              </div>
              {[
                ['Claude Code', 'terminal'],
                ['Codex', 'code'],
                ['Gemini CLI', 'sparkle'],
              ].map(([t, g]) => (
                <React.Fragment key={t}>
                  <Sep />
                  <div style={{display: 'flex', alignItems: 'center', minHeight: 60, padding: '8px 16px', boxSizing: 'border-box'}}>
                    <AppIcon glyph={g as Glyph} />
                    <RowText title={t} sub="Not connected" />
                    <Connect />
                  </div>
                </React.Fragment>
              ))}
              <Sep />
              <div style={{display: 'flex', alignItems: 'center', minHeight: 60, padding: '8px 16px', boxSizing: 'border-box'}}>
                <div style={{width: 28, marginRight: 12, display: 'flex', justifyContent: 'center'}}>
                  <G name="hub" size={16} color={colors.text2} />
                </div>
                <div style={{flex: 1, fontSize: 11.5, lineHeight: 1.4, color: colors.text2, paddingRight: 16}}>Another app that supports MCP: copy the setup, then paste it into the app’s settings.</div>
                <div style={{fontSize: 13, fontWeight: 600, padding: '0 14px'}}>Copy setup</div>
              </div>
            </GroupBox>
            <div style={{fontSize: 13, lineHeight: 1.5, color: colors.text2, width: 520, margin: '10px 4px 0'}}>
              Each app starts Study Stash itself and reads your library through this computer. No Tailscale, no internet address, and no password goes into the app’s settings.
            </div>
          </SettingsWindow>
        </div>
        <Cursor
          softPress
          stops={[
            [6, connectAt.x - 300, connectAt.y + 260],
            [CONNECT - 6, connectAt.x - 4, connectAt.y - 4],
            [CONNECT + 10, connectAt.x - 4, connectAt.y - 4],
            [CONNECT + 50, connectAt.x - 160, connectAt.y + 150],
          ]}
          clicks={[CONNECT]}
        />
      </Desktop>
      <Title text={titles.aiapps!} delay={10} />
    </AbsoluteFill>
  );
};
