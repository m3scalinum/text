//! SHA-256, so a model's identity is reproducible without a package.
//!
//! The algorithm is the one of FIPS 180-4 §6.2. It is written out here rather
//! than pulled in with a crate: the only use is hashing the two model files once
//! per engine, the implementation is forty lines of well-specified arithmetic,
//! and a dependency for it would put a third-party crate between a caller and the
//! number that names its weights. The tests below pin the digest to the published
//! test vectors, so an error in this file fails `cargo test` rather than quietly
//! producing a plausible-looking identity.

/// The first 32 bits of the fractional parts of the cube roots of the first 64
/// primes: the round constants of FIPS 180-4 §4.2.2.
const K: [u32; 64] = [
    0x428a_2f98,
    0x7137_4491,
    0xb5c0_fbcf,
    0xe9b5_dba5,
    0x3956_c25b,
    0x59f1_11f1,
    0x923f_82a4,
    0xab1c_5ed5,
    0xd807_aa98,
    0x1283_5b01,
    0x2431_85be,
    0x550c_7dc3,
    0x72be_5d74,
    0x80de_b1fe,
    0x9bdc_06a7,
    0xc19b_f174,
    0xe49b_69c1,
    0xefbe_4786,
    0x0fc1_9dc6,
    0x240c_a1cc,
    0x2de9_2c6f,
    0x4a74_84aa,
    0x5cb0_a9dc,
    0x76f9_88da,
    0x983e_5152,
    0xa831_c66d,
    0xb003_27c8,
    0xbf59_7fc7,
    0xc6e0_0bf3,
    0xd5a7_9147,
    0x06ca_6351,
    0x1429_2967,
    0x27b7_0a85,
    0x2e1b_2138,
    0x4d2c_6dfc,
    0x5338_0d13,
    0x650a_7354,
    0x766a_0abb,
    0x81c2_c92e,
    0x9272_2c85,
    0xa2bf_e8a1,
    0xa81a_664b,
    0xc24b_8b70,
    0xc76c_51a3,
    0xd192_e819,
    0xd699_0624,
    0xf40e_3585,
    0x106a_a070,
    0x19a4_c116,
    0x1e37_6c08,
    0x2748_774c,
    0x34b0_bcb5,
    0x391c_0cb3,
    0x4ed8_aa4a,
    0x5b9c_ca4f,
    0x682e_6ff3,
    0x748f_82ee,
    0x78a5_636f,
    0x84c8_7814,
    0x8cc7_0208,
    0x90be_fffa,
    0xa450_6ceb,
    0xbef9_a3f7,
    0xc671_78f2,
];

/// The initial hash value: the first 32 bits of the fractional parts of the
/// square roots of the first eight primes (FIPS 180-4 §5.3.3).
const H0: [u32; 8] = [
    0x6a09_e667,
    0xbb67_ae85,
    0x3c6e_f372,
    0xa54f_f53a,
    0x510e_527f,
    0x9b05_688c,
    0x1f83_d9ab,
    0x5be0_cd19,
];

/// The SHA-256 digest of `bytes`.
pub fn digest(bytes: &[u8]) -> [u8; 32] {
    let mut state = H0;
    let mut blocks = bytes.chunks_exact(64);

    for block in blocks.by_ref() {
        compress(
            &mut state,
            block.try_into().expect("chunks_exact yields 64 bytes"),
        );
    }

    // Padding: a single 1 bit, zeroes to 56 bytes modulo 64, then the message
    // length in bits as a big-endian u64. Two blocks are needed when the
    // remainder leaves fewer than 9 bytes for the length.
    let remainder = blocks.remainder();
    let mut tail = [0u8; 128];
    tail[..remainder.len()].copy_from_slice(remainder);
    tail[remainder.len()] = 0x80;

    let tail_len = if remainder.len() < 56 { 64 } else { 128 };
    let bit_len = (bytes.len() as u64).wrapping_mul(8);
    tail[tail_len - 8..tail_len].copy_from_slice(&bit_len.to_be_bytes());

    for block in tail[..tail_len].chunks_exact(64) {
        compress(
            &mut state,
            block.try_into().expect("chunks_exact yields 64 bytes"),
        );
    }

    let mut out = [0u8; 32];
    for (word, bytes) in state.iter().zip(out.chunks_exact_mut(4)) {
        bytes.copy_from_slice(&word.to_be_bytes());
    }
    out
}

/// The digest as sixty-four lowercase hexadecimal characters.
pub fn hex(digest: &[u8; 32]) -> String {
    let mut out = String::with_capacity(64);
    for byte in digest {
        out.push_str(&format!("{byte:02x}"));
    }
    out
}

/// The digest of the concatenation of two digests, which is how a model *pair*
/// gets one identity.
pub fn digest_pair(first: &[u8; 32], second: &[u8; 32]) -> [u8; 32] {
    let mut joined = [0u8; 64];
    joined[..32].copy_from_slice(first);
    joined[32..].copy_from_slice(second);
    digest(&joined)
}

fn compress(state: &mut [u32; 8], block: &[u8; 64]) {
    let mut schedule = [0u32; 64];
    for (index, word) in block.chunks_exact(4).enumerate() {
        schedule[index] = u32::from_be_bytes(word.try_into().expect("four bytes"));
    }
    for index in 16..64 {
        let s0 = schedule[index - 15].rotate_right(7)
            ^ schedule[index - 15].rotate_right(18)
            ^ (schedule[index - 15] >> 3);
        let s1 = schedule[index - 2].rotate_right(17)
            ^ schedule[index - 2].rotate_right(19)
            ^ (schedule[index - 2] >> 10);
        schedule[index] = schedule[index - 16]
            .wrapping_add(s0)
            .wrapping_add(schedule[index - 7])
            .wrapping_add(s1);
    }

    let [mut a, mut b, mut c, mut d, mut e, mut f, mut g, mut h] = *state;

    for index in 0..64 {
        let s1 = e.rotate_right(6) ^ e.rotate_right(11) ^ e.rotate_right(25);
        let ch = (e & f) ^ (!e & g);
        let temp1 = h
            .wrapping_add(s1)
            .wrapping_add(ch)
            .wrapping_add(K[index])
            .wrapping_add(schedule[index]);
        let s0 = a.rotate_right(2) ^ a.rotate_right(13) ^ a.rotate_right(22);
        let maj = (a & b) ^ (a & c) ^ (b & c);
        let temp2 = s0.wrapping_add(maj);

        h = g;
        g = f;
        f = e;
        e = d.wrapping_add(temp1);
        d = c;
        c = b;
        b = a;
        a = temp1.wrapping_add(temp2);
    }

    for (word, value) in state.iter_mut().zip([a, b, c, d, e, f, g, h]) {
        *word = word.wrapping_add(value);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_empty_input_hashes_to_the_published_vector() {
        assert_eq!(
            hex(&digest(b"")),
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        );
    }

    #[test]
    fn abc_hashes_to_the_published_vector() {
        assert_eq!(
            hex(&digest(b"abc")),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
    }

    #[test]
    fn the_quick_brown_fox_hashes_to_the_published_vector() {
        assert_eq!(
            hex(&digest(b"The quick brown fox jumps over the lazy dog")),
            "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592"
        );
    }

    #[test]
    fn a_length_across_the_padding_boundary_is_hashed_in_two_blocks() {
        // 56 bytes is the first length that needs the second padding block; the
        // published vector is for 56 zero bytes.
        assert_eq!(
            hex(&digest(&[0u8; 56])),
            "d4817aa5497628e7c77e6b606107042bbba3130888c5f47a375e6179be789fbb"
        );
        assert_eq!(
            hex(&digest(&[0u8; 64])),
            "f5a5fd42d16a20302798ef6ed309979b43003d2320d9f0e8ea9831a92759fb4b"
        );
    }

    #[test]
    fn two_different_contents_have_different_digests_and_one_content_is_stable() {
        let detection = b"detection weights";
        let recognition = b"recognition weights";

        assert_ne!(digest(detection), digest(recognition));
        assert_eq!(digest(detection), digest(detection));
    }

    #[test]
    fn the_pair_digest_is_order_sensitive() {
        let first = digest(b"detection");
        let second = digest(b"recognition");

        assert_eq!(digest_pair(&first, &second), digest_pair(&first, &second));
        assert_ne!(digest_pair(&first, &second), digest_pair(&second, &first));
    }
}
