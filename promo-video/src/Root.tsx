import React from 'react';
import {Composition, Still} from 'remotion';
import {FPS, TOTAL_FRAMES} from './config';
import {Promo} from './Promo';
import {KofiCard} from './stills/KofiCard';
import {KofiCard2} from './stills/KofiCard2';
import {Promo2} from './promo2/Promo2';
import {TOTAL_FRAMES2} from './promo2/config';
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
  </>
);
