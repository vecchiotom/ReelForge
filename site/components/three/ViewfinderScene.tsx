'use client';
import { useRef } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import * as THREE from 'three';

function ViewfinderMotion({ groupRef }: { groupRef: React.RefObject<THREE.Group | null> }) {
  useFrame((_state, rawDelta) => {
    const g = groupRef.current;
    if (!g) return;
    const delta = Math.min(rawDelta, 0.05);
    g.rotation.y += delta * 0.25;
    g.rotation.x += delta * 0.09;
  });
  return null;
}

interface ViewfinderSceneProps {
  active: boolean;
}

// The other file (besides HeroScene.tsx) permitted to import `three` or
// `@react-three/fiber` — reached only through ViewfinderMount's dynamic import.
export default function ViewfinderScene({ active }: ViewfinderSceneProps) {
  const groupRef = useRef<THREE.Group>(null);
  return (
    <Canvas
      camera={{ position: [0, 0, 3.4], fov: 40 }}
      gl={{ antialias: true, alpha: true }}
      frameloop={active ? 'always' : 'never'}
      style={{ width: '100%', height: '100%' }}
    >
      <ambientLight intensity={0.6} />
      <pointLight position={[2, 2, 3]} intensity={14} color="#A855F7" />
      <group ref={groupRef}>
        <mesh>
          <octahedronGeometry args={[1.05, 0]} />
          <meshBasicMaterial wireframe color="#C4B5FD" />
        </mesh>
        <mesh scale={0.52}>
          <octahedronGeometry args={[1.05, 0]} />
          <meshStandardMaterial color="#6D28D9" flatShading metalness={0.4} roughness={0.35} />
        </mesh>
      </group>
      <ViewfinderMotion groupRef={groupRef} />
    </Canvas>
  );
}
