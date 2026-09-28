import {loadFont} from '@remotion/fonts';
import {staticFile} from 'remotion';

// Inter and Inter Display, the app's own fonts (engine/src/StudyStash.App/Assets/Fonts, SIL Open Font License).
const faces: [family: string, file: string, weight: string][] = [
  ['Inter', 'Inter-Regular.ttf', '400'],
  ['Inter', 'Inter-Medium.ttf', '500'],
  ['Inter', 'Inter-SemiBold.ttf', '600'],
  ['Inter', 'Inter-Bold.ttf', '700'],
  ['Inter Display', 'InterDisplay-SemiBold.ttf', '600'],
  ['Inter Display', 'InterDisplay-Bold.ttf', '700'],
];

export const fontsLoaded = Promise.all(
  faces.map(([family, file, weight]) =>
    loadFont({family, url: staticFile(`fonts/${file}`), weight, format: 'truetype'}),
  ),
);
