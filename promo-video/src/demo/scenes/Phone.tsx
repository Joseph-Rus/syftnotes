import React from 'react';
import {AbsoluteFill} from 'remotion';
import {Desktop} from '../../components/Layout';

export const Phone: React.FC = () => (
  <AbsoluteFill>
    <Desktop />
  </AbsoluteFill>
);
