import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const outDir = join(__dirname, "..", "public");
mkdirSync(outDir, { recursive: true });

const size = 16;
const biSize = 40;
const xorSize = size * size * 4;
const andRowBytes = (((size * 1 + 31) >> 5) << 2); // 1bpp rows padded to 32 bits
const andSize = andRowBytes * size;
const imageBytes = biSize + xorSize + andSize;

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(1, 4);

const dirEntry = Buffer.alloc(16);
dirEntry.writeUInt8(size, 0);
dirEntry.writeUInt8(size, 1);
dirEntry.writeUInt8(0, 2);
dirEntry.writeUInt8(0, 3);
dirEntry.writeUInt16LE(1, 4);
dirEntry.writeUInt16LE(32, 6);
dirEntry.writeUInt32LE(imageBytes, 8);
dirEntry.writeUInt32LE(22, 12);

const bi = Buffer.alloc(biSize);
bi.writeUInt32LE(40, 0);
bi.writeInt32LE(size, 4);
bi.writeInt32LE(size * 2, 8);
bi.writeUInt16LE(1, 12);
bi.writeUInt16LE(32, 14);
bi.writeUInt32LE(0, 16);
bi.writeUInt32LE(xorSize, 20);

const xor = Buffer.alloc(xorSize);
const B = 0xff;
const G = 0x64;
const R = 0x16;
const A = 0xff;
for (let y = 0; y < size; y++) {
  for (let x = 0; x < size; x++) {
    const row = size - 1 - y;
    const i = (row * size + x) * 4;
    xor[i] = B;
    xor[i + 1] = G;
    xor[i + 2] = R;
    xor[i + 3] = A;
  }
}

const andMask = Buffer.alloc(andSize, 0);
const out = Buffer.concat([header, dirEntry, bi, xor, andMask]);
writeFileSync(join(outDir, "favicon.ico"), out);
