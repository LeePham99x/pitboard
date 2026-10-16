// Usage: bun fd3-read.mjs   (and: node fd3-read.mjs)
//
// Reads descriptor 3 the way Claude Code 2.1.300 reads its OAuth token descriptor (win32-x64
// build, read as bytes). Its reader gates a direct read on `fstat(fd).isSocket()` alone
// (`BNr`); for anything else it opens `/proc/self/fd/<fd>` (`bQe`), and only on an EACCES or
// EPERM there does it fall back to the direct read (`Cje`/`PWs`). So on Windows a FIFO on
// descriptor 3 takes the `/proc/self/fd` branch, which has no such path, and the token is not
// reached. This prints which branch was taken and whether it got the bytes, never what it
// read, which is a dummy the probe hands it on the descriptor, never a token.

import fs from "node:fs";

const FD = 3;
const MAX = 1 << 20;
const runtime = typeof Bun !== "undefined" ? `bun ${Bun.version}` : `node ${process.version}`;
const out = { runtime, fd: FD };

function errCode(e) {
  return String(e?.code ?? e?.message ?? e);
}

// PWs/he: read the descriptor directly, until a newline or end of stream.
function readUntilNewline(fd) {
  const buf = Buffer.alloc(8192);
  let total = 0;
  let stoppedAtNewline = false;
  while (true) {
    const n = fs.readSync(fd, buf, 0, buf.length, null);
    if (n === 0) break;
    const nl = buf.subarray(0, n).indexOf(10);
    total += nl === -1 ? n : nl + 1;
    if (total > MAX) break;
    if (nl !== -1) {
      stoppedAtNewline = true;
      break;
    }
  }
  return { bytes: total, stopped_at_newline: stoppedAtNewline };
}

// bQe: open a path and read it whole, up to MAX bytes.
function readWholeByPath(path) {
  const pfd = fs.openSync(path, "r");
  try {
    const buf = Buffer.alloc(8192);
    let total = 0;
    while (true) {
      const n = fs.readSync(pfd, buf, 0, buf.length, null);
      if (n === 0) break;
      total += n;
      if (total > MAX) break;
    }
    return total;
  } finally {
    fs.closeSync(pfd);
  }
}

let stat = null;
try {
  stat = fs.fstatSync(FD);
  out.fstat = { is_fifo: stat.isFIFO(), is_socket: stat.isSocket(), is_file: stat.isFile() };
} catch (e) {
  out.fstat_error = errCode(e);
}

if (stat) {
  if (stat.isSocket()) {
    out.branch = "socket_direct";
    try {
      const r = readUntilNewline(FD);
      out.read_bytes = r.bytes;
      out.stopped_at_newline = r.stopped_at_newline;
      out.got = r.bytes > 0;
    } catch (e) {
      out.read_error = errCode(e);
      out.got = false;
    }
  } else {
    out.branch = "proc_self_fd";
    try {
      out.read_bytes = readWholeByPath(`/proc/self/fd/${FD}`);
      out.got = out.read_bytes > 0;
    } catch (e) {
      const code = errCode(e);
      out.proc_error = code;
      if (code === "EACCES" || code === "EPERM") {
        out.branch = "proc_self_fd_then_direct";
        try {
          const r = readUntilNewline(FD);
          out.read_bytes = r.bytes;
          out.stopped_at_newline = r.stopped_at_newline;
          out.got = r.bytes > 0;
        } catch (e2) {
          out.read_error = errCode(e2);
          out.got = false;
        }
      } else {
        out.got = false;
      }
    }
  }
}

console.log(JSON.stringify(out));
