// Exports the app icon, the Android adaptive icon layers, the splash image and the
// favicon from the logo drawing (assets/brand/uzu-head.svg, ADR 0044).
//
//   npm run brand:export                            writes the PNGs into assets/
//   node scripts/export-brand-assets.mjs <out-dir>  writes them elsewhere (to compare)
//
// Each image is checked against the platform rules before it is written: the iOS
// icon has no transparency, the Android layers and the splash image keep the drawing
// inside the circle the system shows.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { crc32, deflateSync } from 'node:zlib';

import { Resvg } from '@resvg/resvg-js';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(root, 'assets/brand/uzu-head.svg');
const outDir = resolve(process.argv[2] ?? resolve(root, 'assets'));

/** Sand of the « Uzu » palette (`infoSoft`, `BRAND.iconBackground`): behind the head. */
const SAND = '#E9C9A0';
const CANVAS = 1024;
/** Visual centre of the drawing (its ears lean to the right, its muzzle weighs down). */
const CENTER = { x: 532, y: 545 };

const drawing = readFileSync(source, 'utf8')
  .replace(/^[\s\S]*?<svg[^>]*>/, '')
  .replace(/<\/svg>\s*$/, '')
  .replace(/<!--[\s\S]*?-->/g, '');

/** Extracts one element of the drawing by its id (a `<g>` group or a `<path>`). */
function part(id) {
  const group = drawing.match(new RegExp(`<g id="${id}"[\\s\\S]*?</g>`));
  const path = drawing.match(new RegExp(`<path id="${id}"[^>]*/>`));
  const found = group?.[0] ?? path?.[0];
  if (!found) throw new Error(`brand:export — no element with id="${id}" in ${source}`);
  return found;
}

const place = (content, scale) =>
  `<g transform="translate(${CANVAS / 2} ${CANVAS / 2}) scale(${scale}) translate(${-CENTER.x} ${-CENTER.y})">${content}</g>`;
const svgDocument = (body) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${CANVAS} ${CANVAS}">${body}</svg>`;

/**
 * Single-colour silhouette for the Android themed icon (the system keeps only the
 * alpha): ears and head in white, a thin gap between them, eyes, nose and mouth cut out.
 */
function monochrome() {
  const white = (markup) =>
    markup
      .replace(/fill="#[0-9A-Fa-f]{6}"/g, 'fill="#fff"')
      .replace(/stroke="#[0-9A-Fa-f]{6}"/g, 'stroke="#fff"');
  const black = (markup) =>
    markup
      .replace(/fill="#[0-9A-Fa-f]{6}"/g, 'fill="#000"')
      .replace(/stroke="#[0-9A-Fa-f]{6}"/g, 'stroke="#000"');
  const head = part('head');
  const gap = head
    .replace(/id="head"/, '')
    .replace(/fill="[^"]*"/, 'fill="none" stroke="#000" stroke-width="44"');
  const features = black(part('features'));
  return `<defs><mask id="silhouette">${white(part('ears'))}${gap}${white(head)}${features}</mask></defs><rect width="${CANVAS}" height="${CANVAS}" fill="#fff" mask="url(#silhouette)"/>`;
}

/** Throws if a visible pixel lies outside the circle of the given radius around the centre. */
function assertInsideCircle(name, pixels, size, radius) {
  const centre = size / 2;
  for (let y = 0; y < size; y += 1) {
    for (let x = 0; x < size; x += 1) {
      const alpha = pixels[(y * size + x) * 4 + 3];
      if (alpha > 8 && Math.hypot(x + 0.5 - centre, y + 0.5 - centre) > radius) {
        throw new Error(
          `brand:export — ${name}: the drawing leaves the visible circle at (${x}, ${y}).`,
        );
      }
    }
  }
}

/** Encodes RGBA pixels as an RGB PNG (colour type 2): App Store icons must have no alpha channel. */
function encodeOpaquePng(pixels, size) {
  const rows = Buffer.alloc(size * (size * 3 + 1));
  for (let y = 0; y < size; y += 1) {
    const rowStart = y * (size * 3 + 1);
    rows[rowStart] = 0;
    for (let x = 0; x < size; x += 1) {
      const from = (y * size + x) * 4;
      if (pixels[from + 3] !== 255)
        throw new Error('brand:export — the iOS icon has a transparent pixel.');
      pixels.copy(rows, rowStart + 1 + x * 3, from, from + 3);
    }
  }
  const chunk = (type, data) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header.set([8, 2, 0, 0, 0], 8);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(rows, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

const assets = [
  {
    // iOS icon: full square (the system rounds the corners), opaque, 1024 px.
    file: 'icon.png',
    size: 1024,
    opaque: true,
    svg: svgDocument(
      `<rect width="${CANVAS}" height="${CANVAS}" fill="${SAND}"/>${place(drawing, 1.12)}`,
    ),
  },
  {
    // Android adaptive icon, front layer on the sand background colour: the launcher may
    // crop anything outside the central 66 dp circle of the 108 dp layer.
    file: 'android-icon-foreground.png',
    size: 1024,
    safeRadius: (CANVAS * 66) / 108 / 2,
    svg: svgDocument(place(drawing, 0.78)),
  },
  {
    // Android 13 themed icon: same frame as the front layer, one colour.
    file: 'android-icon-monochrome.png',
    size: 1024,
    safeRadius: (CANVAS * 66) / 108 / 2,
    svg: svgDocument(place(monochrome(), 0.78)),
  },
  {
    // Splash image: a sand disc with the head, readable on the light and the dark
    // background. Android 12+ shows only the central circle (two thirds of the image).
    file: 'splash-icon.png',
    size: 1024,
    safeRadius: CANVAS / 3,
    svg: svgDocument(
      `<circle cx="${CANVAS / 2}" cy="${CANVAS / 2}" r="${CANVAS / 3 - 4}" fill="${SAND}"/>${place(drawing, 0.74)}`,
    ),
  },
  {
    file: 'favicon.png',
    size: 48,
    opaque: true,
    svg: svgDocument(
      `<rect width="${CANVAS}" height="${CANVAS}" fill="${SAND}"/>${place(drawing, 1.12)}`,
    ),
  },
];

mkdirSync(outDir, { recursive: true });
for (const asset of assets) {
  const rendered = new Resvg(asset.svg, { fitTo: { mode: 'width', value: asset.size } }).render();
  if (asset.safeRadius) {
    assertInsideCircle(
      asset.file,
      rendered.pixels,
      asset.size,
      (asset.safeRadius * asset.size) / CANVAS,
    );
  }
  const png = asset.opaque ? encodeOpaquePng(rendered.pixels, asset.size) : rendered.asPng();
  writeFileSync(resolve(outDir, asset.file), png);
  console.log(`brand:export — ${relative(root, resolve(outDir, asset.file))} (${asset.size} px)`);
}
