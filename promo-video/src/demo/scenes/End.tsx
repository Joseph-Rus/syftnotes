import React from 'react';
import {Cta2} from '../../promo2/scenes/Outro2';
import {Cut, lengthIn} from '../config';

// The end card, as the second video's (Cta2): the icon, Study Stash, "Free for Mac and Windows.", the link, the marks
// for Mac and Windows and the film's glimpses beside it, holding to the last frame.
export const End: React.FC<{cut?: Cut}> = ({cut}) => <Cta2 duration={lengthIn(cut, 'end')} />;
