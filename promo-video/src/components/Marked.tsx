import React from 'react';
import {colors} from '../config';

/** Words run over with the website's highlighter; `p` sweeps it across (0 → 1). */
export const Marked: React.FC<{p: number; children: React.ReactNode}> = ({p, children}) => (
  <span style={{position: 'relative', whiteSpace: 'nowrap', color: colors.ink}}>
    <span
      style={{
        position: 'absolute',
        left: -6,
        right: -6,
        top: '20%',
        bottom: '4%',
        background: colors.highlighter,
        borderRadius: '4px 8px 5px 7px',
        transform: `scaleX(${p}) skewX(-4deg)`,
        transformOrigin: 'left center',
      }}
    />
    <span style={{position: 'relative'}}>{children}</span>
  </span>
);
