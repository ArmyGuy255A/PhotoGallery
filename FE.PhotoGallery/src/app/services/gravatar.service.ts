import { Injectable } from '@angular/core';

/**
 * Service for generating Gravatar URLs and avatar fallbacks (initials + deterministic colors).
 *
 * Gravatar URL spec: https://docs.gravatar.com/api/avatars/images/
 * Hash algorithm: MD5 of the lowercased, trimmed email address.
 * MD5 is implemented inline (Web Crypto SubtleCrypto does not support MD5).
 */
@Injectable({ providedIn: 'root' })
export class GravatarService {
  /**
   * Build a Gravatar URL for the given email.
   * `d=mp` requests the "mystery person" silhouette as a fallback when the
   * email has no associated Gravatar account.
   */
  getGravatarUrl(email: string, size: number = 80): string {
    const normalized = (email || '').trim().toLowerCase();
    const hash = md5(normalized);
    return `https://www.gravatar.com/avatar/${hash}?s=${size}&d=mp`;
  }

  /**
   * Return up to two uppercase initial characters from a display name.
   * Examples: "Phil Dieppa" -> "PD", "phil" -> "P", "" -> "".
   */
  getInitials(displayName: string): string {
    if (!displayName) {
      return '';
    }
    const parts = displayName.trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) {
      return '';
    }
    if (parts.length === 1) {
      return parts[0].charAt(0).toUpperCase();
    }
    return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase();
  }

  /**
   * Deterministic HSL color from a seed string. Same input always produces
   * the same color; designed to be used as the background for initials.
   */
  getInitialsBackgroundColor(seed: string): string {
    const s = seed || '';
    let hash = 0;
    for (let i = 0; i < s.length; i++) {
      hash = (hash * 31 + s.charCodeAt(i)) | 0;
    }
    const hue = Math.abs(hash) % 360;
    return `hsl(${hue}, 50%, 45%)`;
  }
}

/* ------------------------------------------------------------------ */
/* Inline MD5 implementation (RFC 1321).                              */
/* Adapted from the public-domain reference implementation by         */
/* Joseph Myers (http://www.myersdaily.org/joseph/javascript/md5.js). */
/* ------------------------------------------------------------------ */

function md5(input: string): string {
  return rhex(md5cycle(utf8ToBlocks(input), input.length));
}

function md5cycle(blocks: number[], length: number): number[] {
  let a = 1732584193;
  let b = -271733879;
  let c = -1732584194;
  let d = 271733878;

  blocks[(((length + 8) >> 6) << 4) + 14] = length * 8;

  for (let i = 0; i < blocks.length; i += 16) {
    const olda = a, oldb = b, oldc = c, oldd = d;

    a = ff(a, b, c, d, blocks[i + 0], 7, -680876936);
    d = ff(d, a, b, c, blocks[i + 1], 12, -389564586);
    c = ff(c, d, a, b, blocks[i + 2], 17, 606105819);
    b = ff(b, c, d, a, blocks[i + 3], 22, -1044525330);
    a = ff(a, b, c, d, blocks[i + 4], 7, -176418897);
    d = ff(d, a, b, c, blocks[i + 5], 12, 1200080426);
    c = ff(c, d, a, b, blocks[i + 6], 17, -1473231341);
    b = ff(b, c, d, a, blocks[i + 7], 22, -45705983);
    a = ff(a, b, c, d, blocks[i + 8], 7, 1770035416);
    d = ff(d, a, b, c, blocks[i + 9], 12, -1958414417);
    c = ff(c, d, a, b, blocks[i + 10], 17, -42063);
    b = ff(b, c, d, a, blocks[i + 11], 22, -1990404162);
    a = ff(a, b, c, d, blocks[i + 12], 7, 1804603682);
    d = ff(d, a, b, c, blocks[i + 13], 12, -40341101);
    c = ff(c, d, a, b, blocks[i + 14], 17, -1502002290);
    b = ff(b, c, d, a, blocks[i + 15], 22, 1236535329);

    a = gg(a, b, c, d, blocks[i + 1], 5, -165796510);
    d = gg(d, a, b, c, blocks[i + 6], 9, -1069501632);
    c = gg(c, d, a, b, blocks[i + 11], 14, 643717713);
    b = gg(b, c, d, a, blocks[i + 0], 20, -373897302);
    a = gg(a, b, c, d, blocks[i + 5], 5, -701558691);
    d = gg(d, a, b, c, blocks[i + 10], 9, 38016083);
    c = gg(c, d, a, b, blocks[i + 15], 14, -660478335);
    b = gg(b, c, d, a, blocks[i + 4], 20, -405537848);
    a = gg(a, b, c, d, blocks[i + 9], 5, 568446438);
    d = gg(d, a, b, c, blocks[i + 14], 9, -1019803690);
    c = gg(c, d, a, b, blocks[i + 3], 14, -187363961);
    b = gg(b, c, d, a, blocks[i + 8], 20, 1163531501);
    a = gg(a, b, c, d, blocks[i + 13], 5, -1444681467);
    d = gg(d, a, b, c, blocks[i + 2], 9, -51403784);
    c = gg(c, d, a, b, blocks[i + 7], 14, 1735328473);
    b = gg(b, c, d, a, blocks[i + 12], 20, -1926607734);

    a = hh(a, b, c, d, blocks[i + 5], 4, -378558);
    d = hh(d, a, b, c, blocks[i + 8], 11, -2022574463);
    c = hh(c, d, a, b, blocks[i + 11], 16, 1839030562);
    b = hh(b, c, d, a, blocks[i + 14], 23, -35309556);
    a = hh(a, b, c, d, blocks[i + 1], 4, -1530992060);
    d = hh(d, a, b, c, blocks[i + 4], 11, 1272893353);
    c = hh(c, d, a, b, blocks[i + 7], 16, -155497632);
    b = hh(b, c, d, a, blocks[i + 10], 23, -1094730640);
    a = hh(a, b, c, d, blocks[i + 13], 4, 681279174);
    d = hh(d, a, b, c, blocks[i + 0], 11, -358537222);
    c = hh(c, d, a, b, blocks[i + 3], 16, -722521979);
    b = hh(b, c, d, a, blocks[i + 6], 23, 76029189);
    a = hh(a, b, c, d, blocks[i + 9], 4, -640364487);
    d = hh(d, a, b, c, blocks[i + 12], 11, -421815835);
    c = hh(c, d, a, b, blocks[i + 15], 16, 530742520);
    b = hh(b, c, d, a, blocks[i + 2], 23, -995338651);

    a = ii(a, b, c, d, blocks[i + 0], 6, -198630844);
    d = ii(d, a, b, c, blocks[i + 7], 10, 1126891415);
    c = ii(c, d, a, b, blocks[i + 14], 15, -1416354905);
    b = ii(b, c, d, a, blocks[i + 5], 21, -57434055);
    a = ii(a, b, c, d, blocks[i + 12], 6, 1700485571);
    d = ii(d, a, b, c, blocks[i + 3], 10, -1894986606);
    c = ii(c, d, a, b, blocks[i + 10], 15, -1051523);
    b = ii(b, c, d, a, blocks[i + 1], 21, -2054922799);
    a = ii(a, b, c, d, blocks[i + 8], 6, 1873313359);
    d = ii(d, a, b, c, blocks[i + 15], 10, -30611744);
    c = ii(c, d, a, b, blocks[i + 6], 15, -1560198380);
    b = ii(b, c, d, a, blocks[i + 13], 21, 1309151649);
    a = ii(a, b, c, d, blocks[i + 4], 6, -145523070);
    d = ii(d, a, b, c, blocks[i + 11], 10, -1120210379);
    c = ii(c, d, a, b, blocks[i + 2], 15, 718787259);
    b = ii(b, c, d, a, blocks[i + 9], 21, -343485551);

    a = add32(a, olda);
    b = add32(b, oldb);
    c = add32(c, oldc);
    d = add32(d, oldd);
  }
  return [a, b, c, d];
}

function cmn(q: number, a: number, b: number, x: number, s: number, t: number): number {
  a = add32(add32(a, q), add32(x, t));
  return add32((a << s) | (a >>> (32 - s)), b);
}

function ff(a: number, b: number, c: number, d: number, x: number, s: number, t: number): number {
  return cmn((b & c) | ((~b) & d), a, b, x, s, t);
}

function gg(a: number, b: number, c: number, d: number, x: number, s: number, t: number): number {
  return cmn((b & d) | (c & (~d)), a, b, x, s, t);
}

function hh(a: number, b: number, c: number, d: number, x: number, s: number, t: number): number {
  return cmn(b ^ c ^ d, a, b, x, s, t);
}

function ii(a: number, b: number, c: number, d: number, x: number, s: number, t: number): number {
  return cmn(c ^ (b | (~d)), a, b, x, s, t);
}

function add32(a: number, b: number): number {
  return (a + b) & 0xffffffff;
}

function utf8ToBlocks(input: string): number[] {
  const bytes: number[] = [];
  for (let i = 0; i < input.length; i++) {
    let code = input.charCodeAt(i);
    if (code < 0x80) {
      bytes.push(code);
    } else if (code < 0x800) {
      bytes.push(0xc0 | (code >> 6));
      bytes.push(0x80 | (code & 0x3f));
    } else if (code < 0xd800 || code >= 0xe000) {
      bytes.push(0xe0 | (code >> 12));
      bytes.push(0x80 | ((code >> 6) & 0x3f));
      bytes.push(0x80 | (code & 0x3f));
    } else {
      i++;
      code = 0x10000 + (((code & 0x3ff) << 10) | (input.charCodeAt(i) & 0x3ff));
      bytes.push(0xf0 | (code >> 18));
      bytes.push(0x80 | ((code >> 12) & 0x3f));
      bytes.push(0x80 | ((code >> 6) & 0x3f));
      bytes.push(0x80 | (code & 0x3f));
    }
  }
  const length = bytes.length;
  const nblk = ((length + 8) >> 6) + 1;
  const blocks: number[] = new Array(nblk * 16).fill(0);
  for (let i = 0; i < length; i++) {
    blocks[i >> 2] |= bytes[i] << ((i % 4) * 8);
  }
  blocks[length >> 2] |= 0x80 << ((length % 4) * 8);
  return blocks;
}

function rhex(state: number[]): string {
  const hex = '0123456789abcdef';
  let out = '';
  for (let i = 0; i < 4; i++) {
    const n = state[i];
    for (let j = 0; j < 4; j++) {
      const byte = (n >> (j * 8)) & 0xff;
      out += hex.charAt((byte >> 4) & 0x0f) + hex.charAt(byte & 0x0f);
    }
  }
  return out;
}
