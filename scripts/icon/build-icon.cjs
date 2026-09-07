'use strict';

// Render the editable SVG and package lossless PNG frames into a Windows ICO.
// Requires sharp; no image-generation service or API key is needed to rebuild.
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require('sharp');

const assetDir = path.resolve(__dirname, '../../assets/app-icon');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

async function main() {
  const svg = await fs.readFile(path.join(assetDir, 'source.svg'));
  const source = await sharp(svg).resize(1024, 1024).png().toBuffer();
  const frames = await Promise.all(sizes.map(size =>
    sharp(source).resize(size, size, { kernel: 'lanczos3' }).png().toBuffer()
  ));

  // ICONDIR followed by ICONDIRENTRY records. A zero dimension denotes 256 px.
  const directory = Buffer.alloc(6 + sizes.length * 16);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  sizes.forEach((size, index) => {
    const entry = 6 + index * 16;
    directory[entry] = directory[entry + 1] = size === 256 ? 0 : size;
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(frames[index].length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += frames[index].length;
  });

  await fs.writeFile(path.join(assetDir, 'source.png'), source);
  await fs.writeFile(path.join(assetDir, 'app.ico'), Buffer.concat([directory, ...frames]));
  console.log(`Created source.png (1024 x 1024, RGBA) and app.ico (${sizes.join(', ')} px).`);
}

main().catch(error => {
  console.error(error.message);
  process.exitCode = 1;
});
