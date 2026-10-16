// Standard base64 with padding, as Node's `Buffer` writes it: Claude Code keeps a login too
// large for one Credential Manager item in pieces of it. Written out, as jwt.rs's decode is.

const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

pub(crate) fn encode(bytes: &[u8]) -> String {
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let held = chunk.iter().enumerate().fold(0u32, |held, (at, byte)| {
            held | (u32::from(*byte) << (16 - 8 * at))
        });
        for at in 0..4 {
            if at <= chunk.len() {
                out.push(char::from(
                    ALPHABET[((held >> (18 - 6 * at)) & 63) as usize],
                ));
            } else {
                out.push('=');
            }
        }
    }
    out
}

// None for anything `encode` would not have written.
pub(crate) fn decode(text: &str) -> Option<Vec<u8>> {
    if !text.len().is_multiple_of(4) {
        return None;
    }
    let padding = text.bytes().rev().take_while(|b| *b == b'=').count();
    if padding > 2 {
        return None;
    }
    let mut out = Vec::with_capacity(text.len() / 4 * 3);
    let (mut held, mut bits) = (0u32, 0);
    for byte in text[..text.len() - padding].bytes() {
        let sextet = ALPHABET.iter().position(|a| *a == byte)?;
        held = (held << 6) | u32::try_from(sextet).ok()?;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push(u8::try_from((held >> bits) & 0xff).ok()?);
        }
    }
    ((held & ((1u32 << bits) - 1)) == 0).then_some(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn it_reads_back_what_it_wrote_as_node_writes_it() {
        for (bytes, written) in [
            (&b""[..], ""),
            (b"f", "Zg=="),
            (b"fo", "Zm8="),
            (b"foo", "Zm9v"),
            ("Đạt".as_bytes(), "xJDhuqF0"),
            (&[0xff, 0xfe, 0x00][..], "//4A"),
        ] {
            assert_eq!(encode(bytes), written);
            assert_eq!(decode(written).as_deref(), Some(bytes));
        }
    }

    #[test]
    fn what_it_would_not_write_is_not_read() {
        for text in ["Zg=", "Zg===", "Z===", "Zh==", "Zm9v!", "Zm-v", "Zm9v Zm9v"] {
            assert_eq!(decode(text), None, "{text}");
        }
    }
}
