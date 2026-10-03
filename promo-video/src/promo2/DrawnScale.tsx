import React from 'react';

// Chrome lays out an SVG's text for the size it's drawn at on the screen, and doesn't lay it out again when a CSS
// transform on an HTML element above the SVG changes that size. So under a scale that animates (a window settling in,
// a card popping up) the text stays laid out for whichever frame that render worker happened to lay it out at: up to
// 2% off, which moved a diagram's labels by up to 14 px and made them jump about from frame to frame as frames came
// from different workers. Each SVG with text keys itself by the scale it's drawn at, so it's laid out afresh whenever
// that changes, and every frame's text sits where it should.

const Drawn = React.createContext(1);

/** Says that what's inside is drawn `by` times its size by a transform on an element around it. */
export const DrawnScale: React.FC<{by: number; children?: React.ReactNode}> = ({by, children}) => {
  const outer = React.useContext(Drawn);
  return <Drawn.Provider value={outer * by}>{children}</Drawn.Provider>;
};

/** The scale what's here is drawn at by transforms around it. */
export const useDrawnScale = () => React.useContext(Drawn);

/** A key for SVG text drawn at `scale`: it changes whenever the scale does, by however little (a text laid out for
 * a scale a hair off can still land its letters a quarter pixel apart, and shimmer). */
export const textKey = (scale: number) => String(scale);

/** A key for an SVG with text (times any scale of its own): it changes with the scale the text is drawn at. */
export const useTextKey = (own = 1) => textKey(useDrawnScale() * own);
