// Generates PNG icons from web/assets/logo.svg into web/public/ and web/app/.
// Run via `npm run icons`. Requires `sharp` (rasterizing) and `png-to-ico` (combining the 16/32
// PNGs into a classic favicon.ico) as devDependencies. Adapted from site/scripts/generate-icons.mjs
// — vendored rather than shared, since web/'s Docker build context is `./web` only (see
// web/Dockerfile) and cannot reach `../site`.
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import sharp from 'sharp';
import pngToIco from 'png-to-ico';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(__dirname, '..');
const logoPath = path.join(webRoot, 'assets', 'logo.svg');
const publicDir = path.join(webRoot, 'public');
const appDir = path.join(webRoot, 'app');

// Background fill used behind the logo — matches the logo's own rounded-square fill
// (web/assets/logo.svg) so a maskable icon's padding is seamless with the glyph itself.
const BRAND_BG = '#7C3AED';

// Plain (non-maskable) rasterizations, written to public/ and referenced explicitly by size/
// purpose from app/manifest.ts.
const targets = [
  { file: 'favicon-16.png', size: 16 },
  { file: 'favicon-32.png', size: 32 },
  { file: 'favicon-192.png', size: 192 },
  { file: 'favicon-512.png', size: 512 },
];

async function main() {
  await mkdir(publicDir, { recursive: true });
  const svg = await readFile(logoPath);

  for (const { file, size } of targets) {
    const outPath = path.join(publicDir, file);
    await sharp(svg, { density: 384 }).resize(size, size).png().toFile(outPath);
    console.log(`wrote ${path.relative(webRoot, outPath)} (${size}x${size})`);
  }

  // Classic favicon.ico fallback, combined from the 16/32 renders above.
  const icoBuffer = await pngToIco([
    path.join(publicDir, 'favicon-16.png'),
    path.join(publicDir, 'favicon-32.png'),
  ]);
  const icoPath = path.join(publicDir, 'favicon.ico');
  await writeFile(icoPath, icoBuffer);
  console.log(`wrote ${path.relative(webRoot, icoPath)}`);

  // Maskable 512x512 variant: a genuinely different render, not the plain 512 relabeled.
  // Maskable icons get cropped to arbitrary shapes (circle, squircle, rounded square, ...) by
  // the OS, so the important content must sit inside a centered "safe zone" — roughly the
  // inner 80% of the canvas — with the background extending to the edges (opaque, not
  // transparent) so no crop shape shows a hard edge. We resize the logo to 80% of the canvas
  // and composite it centered onto a full-bleed brand-color background.
  const maskableCanvas = 512;
  const maskableLogoSize = Math.round(maskableCanvas * 0.8);
  const maskableLogo = await sharp(svg, { density: 384 })
    .resize(maskableLogoSize, maskableLogoSize)
    .png()
    .toBuffer();
  const maskablePath = path.join(publicDir, 'favicon-512-maskable.png');
  await sharp({
    create: {
      width: maskableCanvas,
      height: maskableCanvas,
      channels: 4,
      background: BRAND_BG,
    },
  })
    .composite([{ input: maskableLogo, gravity: 'center' }])
    .png()
    .toFile(maskablePath);
  console.log(`wrote ${path.relative(webRoot, maskablePath)} (${maskableCanvas}x${maskableCanvas}, maskable)`);

  // Next.js file-based icon convention: app/apple-icon.png (180x180), picked up automatically
  // for the apple-touch-icon meta tag. app/icon.svg is a plain vendored copy (see below).
  const appleIconPath = path.join(appDir, 'apple-icon.png');
  await sharp(svg, { density: 384 }).resize(180, 180).png().toFile(appleIconPath);
  console.log(`wrote ${path.relative(webRoot, appleIconPath)} (180x180)`);

  const iconSvgPath = path.join(appDir, 'icon.svg');
  await copyFile(logoPath, iconSvgPath);
  console.log(`wrote ${path.relative(webRoot, iconSvgPath)}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
