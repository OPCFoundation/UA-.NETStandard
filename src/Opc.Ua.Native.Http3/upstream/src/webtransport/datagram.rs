//! WebTransport Datagram (RFC 9297, draft-ietf-webtrans-http3-15 Section 4.5)
//!
//! HTTP Datagram の Quarter Stream ID を使った WebTransport データグラムの
//! エンコード・デコードを提供。
//!
//! # フォーマット
//!
//! QUIC DATAGRAM フレームペイロードは以下の形式:
//!
//! ```text
//! HTTP Datagram {
//!   Quarter Stream ID (i),
//!   HTTP Datagram Payload (..)
//! }
//! ```
//!
//! Quarter Stream ID = session_id / 4 (RFC 9297 Section 2.1)
//!
//! QUIC ストリーム ID の下位 2 ビットはストリームタイプを表すため、
//! HTTP Datagram では session_id を 4 で割った値を使用する。
//!
//! # 参照
//!
//! - Section 4.5: Datagrams

use crate::varint::{self, VarInt};

/// `Datagram::new` のバリデーションエラー
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DatagramError {
    /// `session_id` が client-initiated bidirectional stream ID ではない
    /// (`session_id % 4 != 0`)
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.5 / RFC 9000 Section 2.1:
    /// クライアント開始双方向ストリームの ID は 4 の倍数)
    InvalidSessionId,

    /// `session_id` が QUIC ストリーム ID 空間の上限 (`2^62 - 1`) を超えている
    ///
    /// (RFC 9000 Section 2.1, Section 16: ストリーム ID は QUIC VarInt)
    SessionIdOutOfRange,
}

/// WebTransport データグラム (Section 4.5)
///
/// QUIC DATAGRAM フレームのペイロードに格納される HTTP Datagram。
/// Quarter Stream ID = session_id / 4 として送受信される。
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Datagram {
    /// WebTransport セッション ID (CONNECT ストリーム ID)
    pub session_id: u64,
    /// WebTransport ペイロード
    pub payload: Vec<u8>,
}

/// デコードエラー
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DatagramDecodeError {
    /// バッファが不足している (追加データが必要)
    BufferTooShort,
    /// Quarter Stream ID が不正 (H3_DATAGRAM_ERROR で接続を閉じる)
    InvalidQuarterStreamId,
}

impl core::fmt::Display for DatagramDecodeError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            Self::BufferTooShort => write!(f, "datagram buffer too short"),
            Self::InvalidQuarterStreamId => write!(f, "invalid quarter stream id"),
        }
    }
}

impl core::error::Error for DatagramDecodeError {}

impl Datagram {
    /// 新しいデータグラムを作成
    ///
    /// `session_id` は client-initiated bidirectional stream ID
    /// (`session_id % 4 == 0`) でなければならない。不正な値の場合は
    /// `Err(DatagramError::InvalidSessionId)` を返す。
    ///
    /// Sans I/O 境界として呼び出し側にエラー判定を委ねるため、パニックしない。
    pub fn new(session_id: u64, payload: Vec<u8>) -> Result<Self, DatagramError> {
        // session_id は QUIC ストリーム ID であり、RFC 9000 Section 16 の VarInt 範囲
        // (`0..=2^62 - 1`) に収まらなければならない。
        // この検査は `encode` (Quarter Stream ID をエンコードする) の安全性を構造的に
        // 保証する役割も持つ。
        if VarInt::new(session_id).is_err() {
            return Err(DatagramError::SessionIdOutOfRange);
        }
        if !session_id.is_multiple_of(4) {
            return Err(DatagramError::InvalidSessionId);
        }
        Ok(Self {
            session_id,
            payload,
        })
    }

    /// Quarter Stream ID を取得 (session_id / 4)
    ///
    /// HTTP Datagram の先頭フィールドとして使用される。
    pub fn quarter_stream_id(&self) -> u64 {
        self.session_id / 4
    }

    /// データグラムをエンコード (HTTP Datagram フォーマット)
    ///
    /// `buf` に Quarter Stream ID (varint) とペイロードを追記する。
    pub fn encode(&self, buf: &mut Vec<u8>) {
        // `Datagram::new` で session_id <= 2^62 - 1 を検査済みのため、
        // quarter_stream_id = session_id / 4 は必ず VarInt 範囲内 (<= 2^60 - 1)。
        let qsi = VarInt::new(self.quarter_stream_id())
            .expect("quarter_stream_id is bounded by Datagram::new");
        varint::encode_into_vec(buf, qsi);
        buf.extend_from_slice(&self.payload);
    }

    /// データグラムをデコード (HTTP Datagram フォーマット)
    ///
    /// `buf` はひとつの QUIC DATAGRAM フレームのペイロード全体を渡す。
    /// 成功時は `(Datagram, 消費バイト数)` を返す。
    /// バッファが不足している場合は `Err(BufferTooShort)` を返す。
    /// Quarter Stream ID が不正な場合は `Err(InvalidQuarterStreamId)` を返す。
    ///
    /// `session_id = quarter_stream_id * 4` として復元する。
    pub fn decode(buf: &[u8]) -> Result<(Self, usize), DatagramDecodeError> {
        let (qsi, varint_len) =
            varint::decode(buf).map_err(|_| DatagramDecodeError::BufferTooShort)?;
        let qsi = qsi.get();
        // RFC 9297 Section 2.1: Quarter Stream ID は QUIC ストリーム ID 空間
        // (2^62 - 1) を 4 で割った値、すなわち 2^60 - 1 が上限。
        // これを超える値は不正であり、呼び出し側は H3_DATAGRAM_ERROR で
        // 接続を閉じる。
        if qsi > (1u64 << 60) - 1 {
            return Err(DatagramDecodeError::InvalidQuarterStreamId);
        }
        let session_id = qsi
            .checked_mul(4)
            .ok_or(DatagramDecodeError::InvalidQuarterStreamId)?;
        let payload = buf[varint_len..].to_vec();
        let consumed = buf.len();
        Ok((
            Self {
                session_id,
                payload,
            },
            consumed,
        ))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_datagram_quarter_stream_id() {
        let d = Datagram::new(0, vec![]).expect("test must succeed");
        assert_eq!(d.quarter_stream_id(), 0);

        let d = Datagram::new(4, vec![]).expect("test must succeed");
        assert_eq!(d.quarter_stream_id(), 1);

        let d = Datagram::new(8, vec![]).expect("test must succeed");
        assert_eq!(d.quarter_stream_id(), 2);

        let d = Datagram::new(400, vec![]).expect("test must succeed");
        assert_eq!(d.quarter_stream_id(), 100);
    }

    #[test]
    fn test_datagram_encode_decode_empty_payload() {
        let d = Datagram::new(4, vec![]).expect("test must succeed");

        let mut buf = Vec::new();
        d.encode(&mut buf);

        let (decoded, consumed) = Datagram::decode(&buf).expect("test must succeed");
        assert_eq!(consumed, buf.len());
        assert_eq!(decoded.session_id, 4);
        assert_eq!(decoded.payload, vec![] as Vec<u8>);
    }

    #[test]
    fn test_datagram_encode_decode_with_payload() {
        let payload = vec![1, 2, 3, 4, 5];
        let d = Datagram::new(8, payload.clone()).expect("test must succeed");

        let mut buf = Vec::new();
        d.encode(&mut buf);

        let (decoded, consumed) = Datagram::decode(&buf).expect("test must succeed");
        assert_eq!(consumed, buf.len());
        assert_eq!(decoded.session_id, 8);
        assert_eq!(decoded.payload, payload);
    }

    #[test]
    fn test_datagram_decode_empty_buffer() {
        assert!(Datagram::decode(&[]).is_err());
    }

    #[test]
    fn test_datagram_session_id_roundtrip_multiples_of_4() {
        // WebTransport セッション ID はクライアント開始双方向ストリーム (4 の倍数)
        for i in 0u64..100 {
            let session_id = i * 4;
            let d = Datagram::new(session_id, b"test".to_vec()).expect("test must succeed");
            let mut buf = Vec::new();
            d.encode(&mut buf);
            let (decoded, _) = Datagram::decode(&buf).expect("test must succeed");
            assert_eq!(decoded.session_id, session_id);
        }
    }

    #[test]
    fn test_datagram_rejects_invalid_session_id() {
        // session_id = 5 は client-initiated bidirectional stream ID ではないため拒否
        assert_eq!(
            Datagram::new(5, vec![0xff]),
            Err(DatagramError::InvalidSessionId)
        );
    }

    #[test]
    fn test_datagram_rejects_session_id_out_of_range() {
        // session_id が VarInt 範囲 (2^62 - 1) を超える場合は拒否
        // (4 の倍数判定より先に値域判定を行う)
        let too_large = (1u64 << 62) & !0x3;
        assert_eq!(
            Datagram::new(too_large, vec![]),
            Err(DatagramError::SessionIdOutOfRange)
        );
    }
}
