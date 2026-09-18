import { ImageResponse } from 'next/og';

export const size = { width: 1200, height: 630 };
export const contentType = 'image/png';
export const alt = 'ReelForge: an AI video team for the product you already built';

export default function OpengraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: '100%',
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'flex-start',
          justifyContent: 'center',
          background: 'linear-gradient(135deg, #2E1065 0%, #6D28D9 55%, #A855F7 100%)',
          padding: '80px',
          fontFamily: 'sans-serif',
        }}
      >
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '20px',
          }}
        >
          <div
            style={{
              display: 'flex',
              width: '84px',
              height: '84px',
              borderRadius: '20px',
              background: 'rgba(255,255,255,0.16)',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <div
              style={{
                width: 0,
                height: 0,
                borderTop: '22px solid transparent',
                borderBottom: '22px solid transparent',
                borderLeft: '32px solid white',
              }}
            />
          </div>
          <div style={{ display: 'flex', fontSize: 56, fontWeight: 700, color: 'white' }}>
            ReelForge
          </div>
        </div>
        <div
          style={{
            display: 'flex',
            marginTop: '48px',
            fontSize: 40,
            fontWeight: 600,
            color: 'white',
            maxWidth: '920px',
            lineHeight: 1.25,
          }}
        >
          An AI video team for the product you already built
        </div>
        <div
          style={{
            display: 'flex',
            marginTop: '28px',
            fontSize: 26,
            color: 'rgba(255,255,255,0.85)',
          }}
        >
          Scripted, edited and reviewed by AI. Ready to publish.
        </div>
      </div>
    ),
    { ...size }
  );
}
