'use client';
import { useRef, type RefObject } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import * as THREE from 'three';

function SceneMotion({
  groupRef,
  progressRef,
}: {
  groupRef: RefObject<THREE.Group | null>;
  progressRef: RefObject<number>;
}) {
  useFrame((_state, rawDelta) => {
    const g = groupRef.current;
    if (!g) return;
    const delta = Math.min(rawDelta, 0.05); // clamp so a backgrounded tab doesn't jump on return
    g.rotation.y += delta * 0.18;
    const target = progressRef.current;
    g.rotation.x = THREE.MathUtils.lerp(g.rotation.x, target * 0.55, 0.06);
    g.position.y = THREE.MathUtils.lerp(g.position.y, -target * 0.85, 0.06);
    const s = THREE.MathUtils.lerp(g.scale.x, 1 - target * 0.12, 0.06);
    g.scale.setScalar(s);
  });
  return null;
}

interface HeroSceneProps {
  progressRef: RefObject<number>;
  active: boolean;
}

// The ONLY files (besides ViewfinderScene.tsx) permitted to import `three` or
// `@react-three/fiber` — reached exclusively through HeroSceneMount's dynamic
// import so this never executes during SSR/SSG.
export default function HeroScene({ progressRef, active }: HeroSceneProps) {
  const groupRef = useRef<THREE.Group>(null);
  return (
    <Canvas
      dpr={[1, 1.75]}
      gl={{ antialias: true, alpha: true, powerPreference: 'high-performance' }}
      camera={{ position: [0, 0, 4.2], fov: 42, near: 0.1, far: 100 }}
      frameloop={active ? 'always' : 'never'}
      style={{ width: '100%', height: '100%' }}
    >
      <ambientLight intensity={0.5} />
      <directionalLight position={[3, 4, 5]} intensity={2.2} />
      <pointLight position={[-4, -2, -3]} intensity={22} distance={14} color="#A855F7" />
      <pointLight position={[3.5, -3, 2]} intensity={12} distance={12} color="#4C1D95" />
      <group ref={groupRef}>
        <mesh>
          <icosahedronGeometry args={[1.25, 2]} />
          <meshStandardMaterial
            color="#7C3AED"
            metalness={0.75}
            roughness={0.22}
            flatShading
            emissive="#2E1065"
            emissiveIntensity={0.35}
          />
        </mesh>
        <mesh scale={1.58}>
          <icosahedronGeometry args={[1.25, 1]} />
          <meshBasicMaterial wireframe color="#A855F7" transparent opacity={0.34} />
        </mesh>
        <mesh rotation={[Math.PI / 2.4, 0, 0.22]}>
          <torusGeometry args={[2.05, 0.012, 8, 128]} />
          <meshBasicMaterial color="#8C8899" transparent opacity={0.5} />
        </mesh>
      </group>
      <SceneMotion groupRef={groupRef} progressRef={progressRef} />
    </Canvas>
  );
}
