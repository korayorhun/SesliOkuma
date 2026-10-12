// EMA Lightning (Canberk Aslan, Apache-2.0) in the browser: the same pipeline as Sesli Okuma's desktop engine
// (frontend, chunking, frame plan, the three ONNX graphs) on ONNX Runtime Web. Numbers, dates, times and
// percentages get a compact Turkish reading here; the desktop app uses the full normalizer-tr (Erdem Tuna,
// Apache-2.0). Works in browsers and in Node (pass the `ort` module and a byte loader to EmaTts.load).

const TYPOGRAPHY = {
  '’': "'", '‘': "'", 'ʼ': "'", '´': "'", '`': "'",
  '“': '"', '”': '"', '„': '"', '«': '"', '»': '"',
  '–': '-', '—': '-', '−': '-', '…': '...',
};
const UNSAFE = /[\x00-\x08\x0b-\x1f\x7f-\x9f؜‎‏‪-‮⁦-⁩]/g;
const TURKISH = 'çğıöşüÇĞİÖŞÜ';

const ONES = ['', 'bir', 'iki', 'üç', 'dört', 'beş', 'altı', 'yedi', 'sekiz', 'dokuz'];
const TENS = ['', 'on', 'yirmi', 'otuz', 'kırk', 'elli', 'altmış', 'yetmiş', 'seksen', 'doksan'];
const SCALES = ['', 'bin', 'milyon', 'milyar', 'trilyon'];
const MONTHS = ['ocak', 'şubat', 'mart', 'nisan', 'mayıs', 'haziran', 'temmuz', 'ağustos', 'eylül', 'ekim', 'kasım', 'aralık'];
const ABBREVIATIONS = [
  [/(\d)\s*(?:TL|₺)(?=$|[\s.,;:!?'"])/g, '$1 lira'],
  [/(\d)\s*km(?=$|[\s.,;:!?])/g, '$1 kilometre'],
  [/(\d)\s*kg(?=$|[\s.,;:!?])/g, '$1 kilogram'],
  [/(\d)\s*cm(?=$|[\s.,;:!?])/g, '$1 santimetre'],
  [/(^|\s)vb\.(?=$|\s)/g, '$1ve benzeri'],
  [/(^|\s)vs\.(?=$|\s)/g, '$1vesaire'],
  [/(^|\s)Dr\.(?=\s)/g, '$1doktor'],
  [/(^|\s)Prof\.(?=\s)/g, '$1profesör'],
];

function underThousand(n) {
  const h = Math.floor(n / 100), t = Math.floor((n % 100) / 10), o = n % 10;
  const parts = [];
  if (h) { if (h > 1) parts.push(ONES[h]); parts.push('yüz'); }
  if (t) parts.push(TENS[t]);
  if (o) parts.push(ONES[o]);
  return parts.join(' ');
}

export function numberWords(n) {
  if (n === 0) return 'sıfır';
  const parts = [];
  for (let i = 0; n > 0 && i < SCALES.length; i++, n = Math.floor(n / 1000)) {
    const group = n % 1000;
    if (!group) continue;
    const words = i === 1 && group === 1 ? '' : underThousand(group);   // "bin", never "bir bin"
    parts.unshift([words, SCALES[i]].filter(Boolean).join(' '));
  }
  return parts.join(' ');
}

function digitWords(s) { return [...s].map((d) => numberWords(+d)).join(' '); }

// "1.250,75" / "3,14" / "2026" -> words (Turkish notation: '.' thousands, ',' decimals)
function readNumber(s) {
  let [whole, fraction] = s.split(',');
  whole = whole.replace(/\./g, '');
  let words = whole.length > 15 || (whole.length > 1 && whole[0] === '0') ? digitWords(whole) : numberWords(Number(whole));
  if (fraction) words += ' virgül ' + (fraction.length > 6 || fraction[0] === '0' ? digitWords(fraction) : numberWords(Number(fraction)));
  return words;
}

// Compact stand-in for normalizer-tr: dates, times, percentages, money/units, numbers.
export function normalize(text) {
  text = text.replace(/\b(\d{1,2})\.(\d{1,2})\.(\d{4})\b/g, (m, d, mo, y) =>
    +mo >= 1 && +mo <= 12 && +d >= 1 && +d <= 31 ? `${numberWords(+d)} ${MONTHS[+mo - 1]} ${numberWords(+y)}` : m);
  text = text.replace(/\b([01]?\d|2[0-4]):([0-5]\d)\b/g, (m, h, mi) => numberWords(+h) + (+mi ? ' ' + numberWords(+mi) : ''));
  text = text.replace(/%\s?(\d+(?:,\d+)?)/g, (m, n) => 'yüzde ' + readNumber(n));
  text = text.replace(/(\d+(?:,\d+)?)\s?%/g, (m, n) => 'yüzde ' + readNumber(n));
  for (const [pattern, replacement] of ABBREVIATIONS) text = text.replace(pattern, replacement);
  return text.replace(/\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+,\d+|\d+/g, (m) => readNumber(m));
}

// TextChunker: ~10 s pieces, cut at a sentence end, then a clause mark, then a space.
const CUTS = [[/[.!?]+["')]*(?= )/g, 0.25], [/[,;:](?= )/g, 0.12], [/\S(?= )/g, 0.12]];

function finish(piece) {
  const core = piece.replace(/["')]+$/, '');
  if (core.length > 0 && /[.!?]$/.test(core)) return piece;
  return piece.replace(/[,;:\- ]+$/, '') + '.';
}

export function chunk(text, speed) {
  const limit = Math.floor(Math.min(250, 18 * 10 * speed));
  const pieces = [];
  let rest = text.trim();
  while (rest.length > 0) {
    let cut = rest.length, pause = 0;
    if (rest.length > limit) {
      cut = limit;
      const window = rest.substring(0, Math.min(rest.length, limit + 1));
      for (const [pattern, gap] of CUTS) {
        pattern.lastIndex = 0;
        let last = -1, match;
        while ((match = pattern.exec(window)) !== null) last = match.index + match[0].length;
        if (last >= 0) { cut = last; pause = gap; break; }
      }
    }
    const piece = rest.substring(0, cut).trim();
    rest = rest.substring(cut).trim();
    if (/\p{L}/u.test(piece)) pieces.push({ text: finish(piece), pause });
  }
  if (pieces.length > 0) pieces[pieces.length - 1].pause = 0;
  return pieces;
}

function roundHalfEven(x) {
  const f = Math.floor(x), d = x - f;
  if (d < 0.5) return f;
  if (d > 0.5) return f + 1;
  return f % 2 === 0 ? f : f + 1;
}

function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export class EmaTts {
  // fetchBytes(name, onProgress?) -> Promise<Uint8Array>; onProgress(fraction 0..1) for the whole download.
  static async load(ort, fetchBytes, onProgress) {
    const json = async (name) => JSON.parse(new TextDecoder().decode(await fetchBytes(name)));
    const config = await json('model-config.json');
    const vocab = await json('vocab.json');
    const graphs = [['text.onnx', 0.14], ['sound.onnx', 0.52], ['decoder.onnx', 0.34]];
    const sessions = [];
    let done = 0;
    for (const [name, share] of graphs) {
      const bytes = await fetchBytes(name, (f) => onProgress && onProgress(done + f * share));
      done += share;
      sessions.push(await ort.InferenceSession.create(bytes, { executionProviders: ['wasm'], graphOptimizationLevel: 'all' }));
      if (onProgress) onProgress(done);
    }
    return new EmaTts(ort, config, vocab, sessions);
  }

  constructor(ort, config, vocab, [text, sound, decoder]) {
    this.ort = ort;
    this.config = config;
    this.text = text;
    this.sound = sound;
    this.decoder = decoder;
    this.stoi = new Map();
    vocab.forEach((entry, i) => { if (entry.length === 1) this.stoi.set(entry, i); });
  }

  get sampleRate() { return this.config.sample_rate; }

  // Any written Turkish in, the model's alphabet out (EmaFrontend.Spoken).
  spoken(text) {
    text = text.replace(UNSAFE, ' ');
    if (!text.trim()) return '';
    let ascii = '';
    for (const ch of normalize(text)) ascii += TYPOGRAPHY[ch] ?? ch;
    const lower = ascii.replace(/İ/g, 'i').replace(/I/g, 'ı').toLowerCase();
    let out = '';
    for (const ch of lower) {
      const piece = TURKISH.includes(ch) ? ch : ch.normalize('NFKD').replace(/\p{M}/gu, '');
      out += piece.length > 0 && [...piece].every((c) => this.stoi.has(c)) ? piece : ' ';
    }
    return out.replace(/\s+/g, ' ').trim();
  }

  pieces(text, speed = 1) { return chunk(this.spoken(text), speed); }

  // One piece (letters of the alphabet, ending in punctuation) -> { audio: Float32Array, frames }.
  async synthesize(spoken, speed = 1, seed = 42) {
    const { ort, config } = this;
    const length = spoken.length;
    if (length === 0) return { audio: new Float32Array(0), frames: 0 };
    const ids = new BigInt64Array(length);
    for (let i = 0; i < length; i++) ids[i] = BigInt(this.stoi.get(spoken[i]) ?? 1);

    const starts = [];
    for (let i = 0; i < length; i++) if (spoken[i] !== ' ' && (i === 0 || spoken[i - 1] === ' ')) starts.push(i);
    if (starts.length === 0) starts.push(0);
    const bounds = [0, ...starts.slice(1), length];
    const words = bounds.length - 1;
    const cw = new BigInt64Array(length), wstart = new BigInt64Array(length);
    for (let w = 0; w < words; w++) for (let i = bounds[w]; i < bounds[w + 1]; i++) { cw[i] = BigInt(w); wstart[i] = BigInt(bounds[w]); }
    const mask = new Uint8Array(length);
    for (let i = 0; i < length; i++) mask[i] = ids[i] !== 0n ? 1 : 0;

    const textOut = await this.text.run({
      ids: new ort.Tensor('int64', ids, [1, length]),
      mask: new ort.Tensor('bool', mask, [1, length]),
    });
    const h = textOut.h.data;
    const dur = Float32Array.from(textOut.dur.data);

    // frame plan: frames per word (float32 sums), half-to-even rounding, 1..max per word, a cap in all
    for (let i = 0; i < length; i++) dur[i] = Math.fround(dur[i] / speed);
    const sums = new Float32Array(words);
    for (let i = 0; i < length; i++) sums[Number(cw[i])] += dur[i];
    const counts = new Array(words);
    let total = 0;
    for (let w = 0; w < words; w++) {
      counts[w] = Math.min(Math.max(roundHalfEven(sums[w]), 1), config.max_word_frames);
      total += counts[w];
    }
    const frames = Math.min(total, config.max_frames);
    const fw = new BigInt64Array(frames), fp = new Float32Array(frames);
    for (let w = 0, frame = 0; w < words && frame < frames; w++)
      for (let k = 0; k < counts[w] && frame < frames; k++, frame++) { fw[frame] = BigInt(w); fp[frame] = k / counts[w]; }

    const steps = config.times.length, dim = config.latent_dim;
    const noise = new Float32Array(steps * frames * dim);
    const random = mulberry32(seed);
    for (let i = 0; i < noise.length; i += 2) {
      const u1 = 1 - random(), u2 = random();
      const magnitude = Math.sqrt(-2 * Math.log(u1));
      noise[i] = magnitude * Math.cos(2 * Math.PI * u2);
      if (i + 1 < noise.length) noise[i + 1] = magnitude * Math.sin(2 * Math.PI * u2);
    }
    const soundOut = await this.sound.run({
      h: new ort.Tensor('float32', h, [1, length, h.length / length]),
      dur: new ort.Tensor('float32', dur, [1, length]),
      mask: new ort.Tensor('bool', new Uint8Array(length).fill(1), [1, length]),
      cw: new ort.Tensor('int64', cw, [1, length]),
      wstart: new ort.Tensor('int64', wstart, [1, length]),
      fw: new ort.Tensor('int64', fw, [1, frames]),
      fp: new ort.Tensor('float32', fp, [1, frames]),
      fmask: new ort.Tensor('bool', new Uint8Array(frames).fill(1), [1, frames]),
      noise: new ort.Tensor('float32', noise, [1, steps, frames, dim]),
    });
    const latents = soundOut[this.sound.outputNames[0]].data;

    // the decoder takes [1, latent_dim, T]
    const z = new Float32Array(dim * frames);
    for (let t = 0; t < frames; t++) for (let c = 0; c < dim; c++) z[c * frames + t] = latents[t * dim + c];
    const decoded = await this.decoder.run({ z: new ort.Tensor('float32', z, [1, dim, frames]) });
    return { audio: Float32Array.from(decoded[this.decoder.outputNames[0]].data), frames };
  }
}

// 16-bit PCM mono WAV
export function wavBytes(audio, sampleRate) {
  const bytes = new ArrayBuffer(44 + audio.length * 2);
  const view = new DataView(bytes);
  const ascii = (offset, s) => { for (let i = 0; i < s.length; i++) view.setUint8(offset + i, s.charCodeAt(i)); };
  ascii(0, 'RIFF'); view.setUint32(4, 36 + audio.length * 2, true); ascii(8, 'WAVE');
  ascii(12, 'fmt '); view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
  view.setUint32(24, sampleRate, true); view.setUint32(28, sampleRate * 2, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
  ascii(36, 'data'); view.setUint32(40, audio.length * 2, true);
  for (let i = 0; i < audio.length; i++) view.setInt16(44 + i * 2, Math.round(Math.max(-1, Math.min(1, audio[i])) * 32767), true);
  return new Uint8Array(bytes);
}
