import type React from 'react';
import {AbsoluteFill, Composition, interpolate, useCurrentFrame, useVideoConfig} from 'remotion';

const MainComposition: React.FC = () => {
  const frame = useCurrentFrame();
  const {durationInFrames} = useVideoConfig();
  const opacity = interpolate(frame, [0, 20, durationInFrames - 20, durationInFrames], [0, 1, 1, 0]);
  const translateY = interpolate(frame, [0, durationInFrames], [40, -40]);

  return (
    <AbsoluteFill
      style={{
        justifyContent: 'center',
        alignItems: 'center',
        background: 'radial-gradient(circle at top, #6d28d9 0%, #111827 60%)',
        color: '#f5f3ff',
        fontSize: 72,
        fontWeight: 800,
      }}
    >
      <div style={{opacity, transform: `translateY(${translateY}px)`}}>ReelForge</div>
    </AbsoluteFill>
  );
};

export const Root: React.FC = () => {
  return <Composition id="Main" component={MainComposition} durationInFrames={180} fps={30} width={1920} height={1080} />;
};
