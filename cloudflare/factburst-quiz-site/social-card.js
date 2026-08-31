const WIDTH = 1200;
const HEIGHT = 630;
const CHANNELS = 3;

const FONT = {
  " ": ["00000","00000","00000","00000","00000","00000","00000"],
  "A": ["01110","10001","10001","11111","10001","10001","10001"],
  "B": ["11110","10001","10001","11110","10001","10001","11110"],
  "C": ["01111","10000","10000","10000","10000","10000","01111"],
  "D": ["11110","10001","10001","10001","10001","10001","11110"],
  "E": ["11111","10000","10000","11110","10000","10000","11111"],
  "F": ["11111","10000","10000","11110","10000","10000","10000"],
  "G": ["01111","10000","10000","10111","10001","10001","01111"],
  "H": ["10001","10001","10001","11111","10001","10001","10001"],
  "I": ["11111","00100","00100","00100","00100","00100","11111"],
  "J": ["00111","00010","00010","00010","10010","10010","01100"],
  "K": ["10001","10010","10100","11000","10100","10010","10001"],
  "L": ["10000","10000","10000","10000","10000","10000","11111"],
  "M": ["10001","11011","10101","10101","10001","10001","10001"],
  "N": ["10001","11001","10101","10011","10001","10001","10001"],
  "O": ["01110","10001","10001","10001","10001","10001","01110"],
  "P": ["11110","10001","10001","11110","10000","10000","10000"],
  "Q": ["01110","10001","10001","10001","10101","10010","01101"],
  "R": ["11110","10001","10001","11110","10100","10010","10001"],
  "S": ["01111","10000","10000","01110","00001","00001","11110"],
  "T": ["11111","00100","00100","00100","00100","00100","00100"],
  "U": ["10001","10001","10001","10001","10001","10001","01110"],
  "V": ["10001","10001","10001","10001","10001","01010","00100"],
  "W": ["10001","10001","10001","10101","10101","10101","01010"],
  "X": ["10001","10001","01010","00100","01010","10001","10001"],
  "Y": ["10001","10001","01010","00100","00100","00100","00100"],
  "Z": ["11111","00001","00010","00100","01000","10000","11111"],
  "0": ["01110","10001","10011","10101","11001","10001","01110"],
  "1": ["00100","01100","00100","00100","00100","00100","01110"],
  "2": ["01110","10001","00001","00010","00100","01000","11111"],
  "3": ["11110","00001","00001","01110","00001","00001","11110"],
  "4": ["00010","00110","01010","10010","11111","00010","00010"],
  "5": ["11111","10000","10000","11110","00001","00001","11110"],
  "6": ["01110","10000","10000","11110","10001","10001","01110"],
  "7": ["11111","00001","00010","00100","01000","01000","01000"],
  "8": ["01110","10001","10001","01110","10001","10001","01110"],
  "9": ["01110","10001","10001","01111","00001","00001","01110"],
  "-": ["00000","00000","00000","11111","00000","00000","00000"],
  "&": ["01100","10010","10100","01000","10101","10010","01101"],
  "/": ["00001","00010","00010","00100","01000","01000","10000"],
  "?": ["01110","10001","00001","00010","00100","00000","00100"],
  "!": ["00100","00100","00100","00100","00100","00000","00100"],
  ".": ["00000","00000","00000","00000","00000","00100","00100"],
  ":": ["00000","00100","00100","00000","00100","00100","00000"],
  "'": ["00100","00100","00010","00000","00000","00000","00000"],
  "#": ["01010","01010","11111","01010","11111","01010","01010"],
  "+": ["00000","00100","00100","11111","00100","00100","00000"],
  "|": ["00100","00100","00100","00100","00100","00100","00100"],
};

export async function buildQuizSocialCardPng({ title, category, questionCount }) {
  const pixels = new Uint8Array(WIDTH * HEIGHT * CHANNELS);
  fill(pixels, [5, 13, 27]);

  fillRect(pixels, 0, 0, WIDTH, 12, [57, 217, 255]);
  fillRect(pixels, 820, 12, 380, 618, [8, 24, 45]);
  fillRect(pixels, 842, 38, 16, 554, [18, 63, 91]);
  fillRect(pixels, 58, 552, 704, 2, [38, 73, 101]);

  drawText(pixels, "FACTBURST QUIZ", 60, 62, 5, [57, 217, 255]);

  const safeCategory = cardText(category || "QUIZ");
  const categoryScale = 4;
  const pillWidth = Math.min(700, measureText(safeCategory, categoryScale) + 36);
  fillRect(pixels, 60, 124, pillWidth, 52, [57, 217, 255]);
  drawText(pixels, safeCategory, 78, 137, categoryScale, [4, 20, 35]);

  const safeTitle = cardText(title || "FACTBURST QUIZ");
  const titleScale = safeTitle.length <= 34 ? 8 : safeTitle.length <= 62 ? 7 : 6;
  const lines = wrapText(safeTitle, 690, titleScale, 4);
  let titleY = 212;
  for (const line of lines) {
    drawText(pixels, line, 60, titleY, titleScale, [245, 249, 255]);
    titleY += 7 * titleScale + 22;
  }

  const count = Math.max(1, Math.min(999, Number(questionCount) || 10));
  const countText = String(count);
  const countScale = countText.length >= 3 ? 13 : 16;
  const countWidth = measureText(countText, countScale);
  drawText(pixels, countText, 1010 - Math.floor(countWidth / 2), 178, countScale, [245, 249, 255]);
  drawText(pixels, count === 1 ? "QUESTION" : "QUESTIONS", 905, 318, 4, [169, 199, 225]);
  drawText(pixels, "CAN YOU", 914, 395, 5, [57, 217, 255]);
  drawText(pixels, "SCORE", 929, 448, 5, [57, 217, 255]);
  drawText(pixels, "10/10?", 909, 501, 5, [245, 249, 255]);

  drawText(pixels, `${count} QUESTIONS | FAST. FACTUAL. FUN.`, 60, 572, 3, [169, 199, 225]);
  drawText(pixels, "FACTBURSTQUIZ.COM", 870, 572, 3, [169, 199, 225]);

  return encodePng(pixels, WIDTH, HEIGHT);
}

function cardText(value) {
  return String(value || "")
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[’‘]/g, "'")
    .replace(/[“”]/g, "'")
    .replace(/[–—]/g, "-")
    .replace(/…/g, "...")
    .replace(/[^A-Za-z0-9 &/?!.'#:+|-]/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .toUpperCase();
}

function wrapText(text, maxWidth, scale, maxLines) {
  const words = String(text || "QUIZ").split(/\s+/).filter(Boolean);
  const lines = [];
  let line = "";
  for (const word of words) {
    const candidate = line ? `${line} ${word}` : word;
    if (measureText(candidate, scale) <= maxWidth) {
      line = candidate;
      continue;
    }
    if (line) lines.push(line);
    line = word;
    while (measureText(line, scale) > maxWidth && line.length > 1) {
      const take = Math.max(1, Math.floor(maxWidth / (6 * scale)) - 1);
      lines.push(line.slice(0, take));
      line = line.slice(take);
      if (lines.length >= maxLines) break;
    }
    if (lines.length >= maxLines) break;
  }
  if (line && lines.length < maxLines) lines.push(line);
  if (lines.length > maxLines) lines.length = maxLines;
  if (words.length && lines.length === maxLines) {
    const rebuilt = lines.join(" ");
    if (rebuilt.length < text.length) {
      let last = lines[maxLines - 1];
      while (last.length > 1 && measureText(`${last}...`, scale) > maxWidth) last = last.slice(0, -1);
      lines[maxLines - 1] = `${last.replace(/[.]+$/, "")}...`;
    }
  }
  return lines.length ? lines : ["QUIZ"];
}

function measureText(text, scale) {
  const length = String(text || "").length;
  return length ? length * 6 * scale - scale : 0;
}

function drawText(pixels, text, x, y, scale, color) {
  let cursor = x;
  for (const raw of String(text || "")) {
    const char = FONT[raw] ? raw : "?";
    const glyph = FONT[char];
    for (let row = 0; row < glyph.length; row++) {
      for (let col = 0; col < 5; col++) {
        if (glyph[row][col] === "1") {
          fillRect(pixels, cursor + col * scale, y + row * scale, scale, scale, color);
        }
      }
    }
    cursor += 6 * scale;
  }
}

function fill(pixels, color) {
  for (let i = 0; i < pixels.length; i += CHANNELS) {
    pixels[i] = color[0];
    pixels[i + 1] = color[1];
    pixels[i + 2] = color[2];
  }
}

function fillRect(pixels, x, y, width, height, color) {
  const left = Math.max(0, Math.floor(x));
  const top = Math.max(0, Math.floor(y));
  const right = Math.min(WIDTH, Math.ceil(x + width));
  const bottom = Math.min(HEIGHT, Math.ceil(y + height));
  for (let py = top; py < bottom; py++) {
    let offset = (py * WIDTH + left) * CHANNELS;
    for (let px = left; px < right; px++) {
      pixels[offset] = color[0];
      pixels[offset + 1] = color[1];
      pixels[offset + 2] = color[2];
      offset += CHANNELS;
    }
  }
}

async function encodePng(pixels, width, height) {
  const stride = width * CHANNELS;
  const raw = new Uint8Array((stride + 1) * height);
  for (let y = 0; y < height; y++) {
    const rowOffset = y * (stride + 1);
    raw[rowOffset] = 0;
    raw.set(pixels.subarray(y * stride, (y + 1) * stride), rowOffset + 1);
  }

  const compressed = await deflate(raw);
  const ihdr = new Uint8Array(13);
  writeU32(ihdr, 0, width);
  writeU32(ihdr, 4, height);
  ihdr[8] = 8;
  ihdr[9] = 2;
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;

  return concat([
    new Uint8Array([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk("IHDR", ihdr),
    chunk("IDAT", compressed),
    chunk("IEND", new Uint8Array()),
  ]);
}

async function deflate(bytes) {
  const stream = new Blob([bytes]).stream().pipeThrough(new CompressionStream("deflate"));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

function chunk(type, data) {
  const typeBytes = new TextEncoder().encode(type);
  const body = concat([typeBytes, data]);
  const result = new Uint8Array(12 + data.length);
  writeU32(result, 0, data.length);
  result.set(typeBytes, 4);
  result.set(data, 8);
  writeU32(result, 8 + data.length, crc32(body));
  return result;
}

function writeU32(target, offset, value) {
  const n = Number(value) >>> 0;
  target[offset] = (n >>> 24) & 255;
  target[offset + 1] = (n >>> 16) & 255;
  target[offset + 2] = (n >>> 8) & 255;
  target[offset + 3] = n & 255;
}

function concat(parts) {
  const length = parts.reduce((sum, part) => sum + part.length, 0);
  const result = new Uint8Array(length);
  let offset = 0;
  for (const part of parts) {
    result.set(part, offset);
    offset += part.length;
  }
  return result;
}

let crcTable = null;
function crc32(bytes) {
  if (!crcTable) {
    crcTable = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = (c & 1) ? (0xedb88320 ^ (c >>> 1)) : (c >>> 1);
      crcTable[n] = c >>> 0;
    }
  }
  let c = 0xffffffff;
  for (const byte of bytes) c = crcTable[(c ^ byte) & 255] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
