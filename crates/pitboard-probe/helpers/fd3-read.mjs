// Usage: bun fd3-read.mjs   (and: node fd3-read.mjs)
//
// Reads descriptor 3 the way Claude Code 2.1.300 reads its OAuth token descriptor: fstat to
// see whether it is a pipe or a socket, then readSync until a newline or end of stream. It
// prints the byte count and what fstat saw, never what it read, which is a dummy the probe
// hands it on the descriptor, never a token.

import fs from "node:fs";

const FD = 3;
const MAX = 1 << 20;
const runtime = typeof Bun !== "undefined" ? `bun ${Bun.version}` : `node ${process.version}`;
const out = { runtime, fd: FD };

let stat = null;
try {
  stat = fs.fstatSync(FD);
  out.fstat = { is_fifo: stat.isFIFO(), is_socket: stat.isSocket(), is_file: stat.isFile() };
} catch (e) {
  out.fstat_error = String(e?.code ?? e?.message ?? e);
}

if (stat) {
  try {
    const buf = Buffer.alloc(8192);
    let total = 0;
    let stoppedAtNewline = false;
    while (true) {
      const n = fs.readSync(FD, buf, 0, buf.length, null);
      if (n === 0) break;
      const nl = buf.subarray(0, n).indexOf(10);
      total += nl === -1 ? n : nl + 1;
      if (total > MAX) {
        out.read_error = "exceeds limit";
        break;
      }
      if (nl !== -1) {
        stoppedAtNewline = true;
        break;
      }
    }
    out.read_bytes = total;
    out.stopped_at_newline = stoppedAtNewline;
  } catch (e) {
    out.read_error = String(e?.code ?? e?.message ?? e);
  }
}

console.log(JSON.stringify(out));
