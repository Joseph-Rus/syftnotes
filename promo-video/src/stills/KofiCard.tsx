import React from 'react';
import {AbsoluteFill} from 'remotion';
import {colors, fonts} from '../config';
import {Wallpaper} from '../components/Layout';
import {LibraryWindow} from '../components/Sidebar';
import {Logo} from '../components/Logo';
import {Body, Page} from '../scenes/Diagrams';

// A frame of the Diagrams scene after every organ has its label, so the note is drawn in full.
const SETTLED = 90;
// The note's window, shown at ZOOM and centred on the right. CSS zoom scales its position too, hence the division.
const ZOOM = 0.64;
const WIN = {w: 784, h: 820};
const WIN_LEFT = 634;

/**
 * The picture a Ko-fi link shows when it's shared (1200×630): the video's desktop, the ask on the left, and on the
 * right the nursing note from the video, open on its own as the vertical cut shows it.
 */
export const KofiCard: React.FC = () => (
  <AbsoluteFill style={{fontFamily: fonts.ui, color: colors.text}}>
    {/* The desktop, framed taller than the card so the wave's bright edge runs below the words. */}
    <div style={{position: 'absolute', left: 0, top: 0, width: 1200, height: 900}}>
      <Wallpaper />
    </div>
    <div style={{position: 'absolute', left: 72, top: 78, width: 520}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 16}}>
        <Logo size={60} />
        <span style={{fontFamily: fonts.display, fontSize: 32, fontWeight: 600, letterSpacing: '-0.02em', color: colors.onDesk}}>
          Study Stash
        </span>
      </div>
      <div
        style={{
          marginTop: 64,
          fontFamily: fonts.display,
          fontSize: 72,
          fontWeight: 700,
          lineHeight: 1.02,
          letterSpacing: '-0.03em',
          color: colors.onDesk,
        }}
      >
        Buy us more Claude usage.
      </div>
      <div style={{marginTop: 26, fontSize: 25, lineHeight: 1.4, color: 'rgba(224, 229, 255, 0.8)', width: 470}}>
        Study Stash turns your lectures into notes, free for Mac and Windows. Two students build it, with Claude.
      </div>
    </div>
    <div style={{position: 'absolute', left: WIN_LEFT / ZOOM, top: (630 - WIN.h * ZOOM) / 2 / ZOOM, zoom: ZOOM}}>
      <LibraryWindow w={WIN.w} h={WIN.h}>
        <Page
          frame={SETTLED}
          cls="NURS 210"
          color={colors.health}
          meta="Thu 25 Sep · 1 h 04 min"
          title="Organs of the torso"
          by="Claude Code"
          summary="Where each major organ sits, seen from the front, and what's around it."
        >
          <Body frame={SETTLED} />
        </Page>
      </LibraryWindow>
    </div>
  </AbsoluteFill>
);
