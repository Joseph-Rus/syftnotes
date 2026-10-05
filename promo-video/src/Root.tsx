import React from 'react';
import {Composition, Still} from 'remotion';
import {FPS, TOTAL_FRAMES} from './config';
import {Promo} from './Promo';
import {KofiCard} from './stills/KofiCard';
import {KofiCard2} from './stills/KofiCard2';
import {Promo2} from './promo2/Promo2';
import {TOTAL_FRAMES2} from './promo2/config';
import {Demo} from './demo/Demo';
import {TOTAL as TOTAL_FRAMES3} from './demo/config';
import {KofiCard3} from './stills/KofiCard3';
import './fonts';

export const Root: React.FC = () => (
  <>
    <Composition id="Promo" component={Promo} durationInFrames={TOTAL_FRAMES} fps={FPS} width={1920} height={1080} />
    <Composition id="PromoVertical" component={Promo} durationInFrames={TOTAL_FRAMES} fps={FPS} width={1080} height={1920} />
    {/* The picture a shared Ko-fi link shows. */}
    <Still id="KofiCard" component={KofiCard} width={1200} height={630} />
    {/* The second video: what's new (src/promo2). */}
    <Composition id="Promo2" component={Promo2} durationInFrames={TOTAL_FRAMES2} fps={FPS} width={1920} height={1080} />
    <Composition id="Promo2Vertical" component={Promo2} durationInFrames={TOTAL_FRAMES2} fps={FPS} width={1080} height={1920} />
    <Still id="KofiCard2" component={KofiCard2} width={1200} height={630} />
    {/* The third video: a full demo of the app (src/demo). */}
    <Composition id="Demo" component={Demo} durationInFrames={TOTAL_FRAMES3} fps={FPS} width={1920} height={1080} />
    <Composition id="DemoVertical" component={Demo} durationInFrames={TOTAL_FRAMES3} fps={FPS} width={1080} height={1920} />
    {/* The demo's picture alone, no sound at all, for laying the real narration and effects under it (CUES.md). */}
    <Composition id="DemoSilent" component={Demo} defaultProps={{silent: true}} durationInFrames={TOTAL_FRAMES3} fps={FPS} width={1920} height={1080} />
    <Composition id="DemoSilentVertical" component={Demo} defaultProps={{silent: true}} durationInFrames={TOTAL_FRAMES3} fps={FPS} width={1080} height={1920} />
    <Still id="KofiCard3" component={KofiCard3} width={1200} height={630} />
  </>
);
