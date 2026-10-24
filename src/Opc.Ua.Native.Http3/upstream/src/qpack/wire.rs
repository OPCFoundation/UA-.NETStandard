//! QPACK ワイヤーフォーマットの共通ヘルパー Encoder/Decoder の重複解消
//!
//! `Encoder` / `DynamicEncoder` / `Decoder` / `DynamicDecoder` で重複していた
//! 文字列エンコード/デコードのヘルパー関数を集約する。
//! (RFC 7541 Section 5.2, RFC 9204 Section 4.1.1)

use crate::error::QpackError;
use crate::qpack::{huffman, integer};

/// 文字列を指定された prefix bits でエンコード (ハフマン対応)
///
/// `use_huffman` が true かつハフマン符号化が短くなる場合は H ビットを設定して
/// ハフマン符号化する。それ以外はリテラル文字列としてエンコードする。
pub(crate) fn encode_string_with_prefix(
    buf: &mut [u8],
    data: &[u8],
    prefix_bits: u8,
    prefix: u8,
    use_huffman: bool,
) -> Option<usize> {
    if use_huffman {
        let huffman_len = huffman::encoded_len(data);
        if huffman_len < data.len() {
            // ハフマン符号化を使用
            // H ビットを設定: prefix に 1 << prefix_bits を OR
            let h_bit = 1u8 << prefix_bits;
            let offset =
                integer::encode_integer(buf, huffman_len as u64, prefix_bits, prefix | h_bit)?;
            huffman::encode(&mut buf[offset..], data)?;
            return Some(offset + huffman_len);
        }
    }

    // リテラル文字列 (H=0)
    let offset = integer::encode_integer(buf, data.len() as u64, prefix_bits, prefix)?;
    if buf.len() < offset + data.len() {
        return None;
    }
    buf[offset..offset + data.len()].copy_from_slice(data);
    Some(offset + data.len())
}

/// 文字列を 7-bit prefix でエンコード (ハフマン対応)
///
/// QPACK の値文字列エンコードに使用する。
pub(crate) fn encode_string(buf: &mut [u8], data: &[u8], use_huffman: bool) -> Option<usize> {
    if use_huffman {
        let huffman_len = huffman::encoded_len(data);
        if huffman_len < data.len() {
            let offset = integer::encode_integer(buf, huffman_len as u64, 7, 0x80)?;
            huffman::encode(&mut buf[offset..], data)?;
            return Some(offset + huffman_len);
        }
    }

    let offset = integer::encode_integer(buf, data.len() as u64, 7, 0x00)?;
    if buf.len() < offset + data.len() {
        return None;
    }
    buf[offset..offset + data.len()].copy_from_slice(data);
    Some(offset + data.len())
}

/// 文字列をデコード (7-bit prefix, ハフマン対応)
pub(crate) fn decode_string(data: &[u8]) -> Result<(Vec<u8>, usize), QpackError> {
    if data.is_empty() {
        return Err(QpackError::BufferTooShort);
    }

    let is_huffman = (data[0] & 0x80) != 0;
    let (length, prefix_len) = integer::decode_integer(data, 7)?;

    let total_len = prefix_len + length as usize;
    if data.len() < total_len {
        return Err(QpackError::BufferTooShort);
    }

    let encoded = &data[prefix_len..total_len];

    let decoded = if is_huffman {
        huffman::decode(encoded)?
    } else {
        encoded.to_vec()
    };

    Ok((decoded, total_len))
}

/// 長さ指定で文字列をデコード
pub(crate) fn decode_string_with_len(
    data: &[u8],
    length: usize,
    is_huffman: bool,
) -> Result<(Vec<u8>, usize), QpackError> {
    if data.len() < length {
        return Err(QpackError::BufferTooShort);
    }

    let encoded = &data[..length];

    let decoded = if is_huffman {
        huffman::decode(encoded)?
    } else {
        encoded.to_vec()
    };

    Ok((decoded, length))
}
