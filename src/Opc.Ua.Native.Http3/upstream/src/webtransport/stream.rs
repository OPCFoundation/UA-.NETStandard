//! WebTransport ストリーム (draft-ietf-webtrans-http3-15 Section 4.2, 4.3, 9.3, 9.4)
//!
//! WebTransport データストリームの管理を提供。

use crate::varint::{self, VarInt};

/// WebTransport 単方向ストリームタイプ (0x54)
pub const UNIDIRECTIONAL_STREAM_TYPE: u64 = 0x54;

/// WebTransport 双方向ストリームシグナル値 (WT_STREAM フレーム) (0x41)
pub const BIDIRECTIONAL_SIGNAL_VALUE: u64 = 0x41;

/// 単方向ストリームタイプの VarInt 表現
const UNI_TYPE_VARINT: VarInt = VarInt::from_static(UNIDIRECTIONAL_STREAM_TYPE);

/// 双方向シグナル値の VarInt 表現
const BIDI_SIGNAL_VARINT: VarInt = VarInt::from_static(BIDIRECTIONAL_SIGNAL_VALUE);

/// 可変長整数をデコード (生の `u64` 値を返す)
fn decode_varint(buf: &[u8]) -> Option<(u64, usize)> {
    varint::decode(buf).ok().map(|(v, n)| (v.get(), n))
}

/// `u64` session_id を VarInt に変換する
///
/// session_id は QUIC ストリーム ID 空間 (`<= 2^62 - 1`) に収まることを構造的に
/// 保証している前提で `expect` する。
fn session_id_to_varint(session_id: u64) -> VarInt {
    VarInt::new(session_id).expect("session_id fits in VarInt")
}

/// ストリームヘッダー
///
/// WebTransport ストリームの先頭に付加されるヘッダー。
/// session_id は private であり、構築は検証済みの [`StreamHeader::new`] 経由のみに制限される
/// (値域を逸脱した session_id で encode するパニック経路を構造的に排除するため)。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct StreamHeader {
    session_id: u64,
}

impl StreamHeader {
    /// セッション ID を取得
    pub fn session_id(&self) -> u64 {
        self.session_id
    }
}

/// ストリームヘッダーデコードエラー
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StreamHeaderDecodeError {
    /// バッファ不足
    BufferTooShort,
    /// ヘッダー形式が不正
    InvalidFormat,
    /// Session ID が client-initiated bidirectional stream ID ではない
    ///
    /// 呼び出し側は H3_ID_ERROR で接続を閉じる (draft-ietf-webtrans-http3-15 Section 4)。
    /// 将来のドラフトで変更される可能性がある
    InvalidSessionId,
    /// Session ID が QUIC ストリーム ID 空間の上限 (`2^62 - 1`) を超えている
    ///
    /// (RFC 9000 Section 2.1, Section 16)
    SessionIdOutOfRange,
}

impl StreamHeader {
    /// 新しいストリームヘッダーを作成
    ///
    /// `session_id` は以下を満たさなければならない:
    /// - QUIC ストリーム ID 空間 (`<= 2^62 - 1`) 内
    /// - client-initiated bidirectional stream ID (`session_id % 4 == 0`)
    ///
    /// 不正な値の場合は対応する `StreamHeaderDecodeError` を返す。
    /// Sans I/O 境界として呼び出し側にエラー判定を委ねるため、パニックしない。
    pub fn new(session_id: u64) -> Result<Self, StreamHeaderDecodeError> {
        if VarInt::new(session_id).is_err() {
            return Err(StreamHeaderDecodeError::SessionIdOutOfRange);
        }
        if !session_id.is_multiple_of(4) {
            return Err(StreamHeaderDecodeError::InvalidSessionId);
        }
        Ok(Self { session_id })
    }

    /// 単方向ストリームヘッダーをエンコード
    ///
    /// フォーマット:
    /// ```text
    /// Unidirectional Stream {
    ///     Stream Type (i) = 0x54,
    ///     Session ID (i),
    /// }
    /// ```
    pub fn encode_unidirectional(&self, buf: &mut Vec<u8>) {
        varint::encode_into_vec(buf, UNI_TYPE_VARINT);
        varint::encode_into_vec(buf, session_id_to_varint(self.session_id));
    }

    /// 双方向ストリームヘッダーをエンコード
    ///
    /// フォーマット:
    /// ```text
    /// Bidirectional Stream {
    ///     Signal Value (i) = 0x41,
    ///     Session ID (i),
    /// }
    /// ```
    pub fn encode_bidirectional(&self, buf: &mut Vec<u8>) {
        varint::encode_into_vec(buf, BIDI_SIGNAL_VARINT);
        varint::encode_into_vec(buf, session_id_to_varint(self.session_id));
    }

    /// 単方向ストリームヘッダーをデコード
    ///
    /// # Returns
    ///
    /// デコードしたヘッダーと消費したバイト数、またはバッファが不足している場合は `None`
    pub fn decode_unidirectional(buf: &[u8]) -> Option<(Self, usize)> {
        Self::decode_unidirectional_checked(buf).ok()
    }

    /// 単方向ストリームヘッダーをデコード (エラー種別付き)
    pub fn decode_unidirectional_checked(
        buf: &[u8],
    ) -> Result<(Self, usize), StreamHeaderDecodeError> {
        let mut offset = 0;

        // Stream Type
        let (stream_type, len) =
            decode_varint(&buf[offset..]).ok_or(StreamHeaderDecodeError::BufferTooShort)?;
        offset += len;

        if stream_type != UNIDIRECTIONAL_STREAM_TYPE {
            return Err(StreamHeaderDecodeError::InvalidFormat);
        }

        // Session ID
        let (session_id, len) =
            decode_varint(&buf[offset..]).ok_or(StreamHeaderDecodeError::BufferTooShort)?;
        offset += len;

        // session_id は client-initiated bidirectional stream ID でなければならない
        // (draft-ietf-webtrans-http3-15 Section 4)
        if !session_id.is_multiple_of(4) {
            return Err(StreamHeaderDecodeError::InvalidSessionId);
        }

        Ok((Self { session_id }, offset))
    }

    /// 双方向ストリームヘッダーをデコード
    ///
    /// # Returns
    ///
    /// デコードしたヘッダーと消費したバイト数、またはバッファが不足している場合は `None`
    pub fn decode_bidirectional(buf: &[u8]) -> Option<(Self, usize)> {
        Self::decode_bidirectional_checked(buf).ok()
    }

    /// 双方向ストリームヘッダーをデコード (エラー種別付き)
    pub fn decode_bidirectional_checked(
        buf: &[u8],
    ) -> Result<(Self, usize), StreamHeaderDecodeError> {
        let mut offset = 0;

        // Signal Value
        let (signal_value, len) =
            decode_varint(&buf[offset..]).ok_or(StreamHeaderDecodeError::BufferTooShort)?;
        offset += len;

        if signal_value != BIDIRECTIONAL_SIGNAL_VALUE {
            return Err(StreamHeaderDecodeError::InvalidFormat);
        }

        // Session ID
        let (session_id, len) =
            decode_varint(&buf[offset..]).ok_or(StreamHeaderDecodeError::BufferTooShort)?;
        offset += len;

        // session_id は client-initiated bidirectional stream ID でなければならない
        // (draft-ietf-webtrans-http3-15 Section 4)
        if !session_id.is_multiple_of(4) {
            return Err(StreamHeaderDecodeError::InvalidSessionId);
        }

        Ok((Self { session_id }, offset))
    }

    /// エンコードサイズを計算
    pub fn encoded_size(&self) -> usize {
        // Signal/Type + Session ID
        BIDI_SIGNAL_VARINT.encoded_len() + session_id_to_varint(self.session_id).encoded_len()
    }
}

/// WebTransport ストリーム
#[derive(Debug)]
pub struct Stream {
    /// QUIC ストリーム ID
    stream_id: u64,
    /// セッション ID
    session_id: u64,
    /// 双方向ストリームかどうか
    bidirectional: bool,
    /// ヘッダー送信済み
    header_sent: bool,
    /// ヘッダー受信済み
    header_received: bool,
    /// 送信データ量
    bytes_sent: u64,
    /// 受信データ量
    bytes_received: u64,
}

impl Stream {
    /// 新しいストリームを作成
    pub fn new(stream_id: u64, session_id: u64, bidirectional: bool) -> Self {
        Self {
            stream_id,
            session_id,
            bidirectional,
            header_sent: false,
            header_received: false,
            bytes_sent: 0,
            bytes_received: 0,
        }
    }

    /// QUIC ストリーム ID を取得
    pub fn stream_id(&self) -> u64 {
        self.stream_id
    }

    /// セッション ID を取得
    pub fn session_id(&self) -> u64 {
        self.session_id
    }

    /// 双方向ストリームかどうか
    pub fn is_bidirectional(&self) -> bool {
        self.bidirectional
    }

    /// ヘッダー送信済みかどうか
    pub fn is_header_sent(&self) -> bool {
        self.header_sent
    }

    /// ヘッダー受信済みかどうか
    pub fn is_header_received(&self) -> bool {
        self.header_received
    }

    /// ヘッダー送信済みを設定
    pub fn set_header_sent(&mut self) {
        self.header_sent = true;
    }

    /// ヘッダー受信済みを設定
    pub fn set_header_received(&mut self) {
        self.header_received = true;
    }

    /// 送信データ量を取得
    pub fn bytes_sent(&self) -> u64 {
        self.bytes_sent
    }

    /// 受信データ量を取得
    pub fn bytes_received(&self) -> u64 {
        self.bytes_received
    }

    /// 送信データ量を加算
    pub fn add_bytes_sent(&mut self, bytes: u64) {
        self.bytes_sent = self.bytes_sent.saturating_add(bytes);
    }

    /// 受信データ量を加算
    pub fn add_bytes_received(&mut self, bytes: u64) {
        self.bytes_received = self.bytes_received.saturating_add(bytes);
    }
}

/// 単方向ストリーム分類結果
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClassifiedUniStream {
    /// WebTransport 単方向ストリーム
    WebTransport {
        /// セッション ID
        session_id: u64,
        /// ヘッダー以降のデータ開始オフセット
        data_offset: usize,
    },
    /// HTTP/3 ストリーム
    Http3 {
        /// ストリームタイプ
        stream_type: u64,
        /// ストリームタイプ以降のデータ開始オフセット
        data_offset: usize,
    },
}

/// 単方向ストリームの先頭バイトからストリームタイプを分類する
///
/// バッファ不足の場合は `Err(varint::DecodeError::BufferTooShort)` を返す。
/// WebTransport ストリームの場合は session_id もデコードする必要があるため、
/// session_id のデコードにもバッファが不足している場合は `Err` を返す。
///
/// この関数は最小限のパースのみ行い、session_id の正当性 (`session_id % 4 == 0`) は
/// 検証しない。呼び出し側で検証する必要がある
/// (draft-ietf-webtrans-http3-15 Section 4)。
pub fn classify_uni_stream(buf: &[u8]) -> Result<ClassifiedUniStream, varint::DecodeError> {
    let (stream_type, type_len) = varint::decode(buf)?;
    if stream_type.get() == UNIDIRECTIONAL_STREAM_TYPE {
        let (session_id, session_id_len) = varint::decode(&buf[type_len..])?;
        Ok(ClassifiedUniStream::WebTransport {
            session_id: session_id.get(),
            data_offset: type_len + session_id_len,
        })
    } else {
        Ok(ClassifiedUniStream::Http3 {
            stream_type: stream_type.get(),
            data_offset: type_len,
        })
    }
}

/// 単方向ストリームの先頭バイトからストリームタイプを分類する (session_id 検証付き)
///
/// `classify_uni_stream()` と同様だが、WebTransport ストリームの場合に
/// session_id が client-initiated bidirectional stream ID (`session_id % 4 == 0`)
/// であることを検証する (draft-ietf-webtrans-http3-15 Section 4)。
///
/// 不正な session_id の場合は `Err(StreamHeaderDecodeError::InvalidSessionId)` を返す。
/// 呼び出し側は H3_ID_ERROR で接続を閉じる必要がある。
pub fn classify_uni_stream_checked(
    buf: &[u8],
) -> Result<ClassifiedUniStream, StreamHeaderDecodeError> {
    let (stream_type, type_len) =
        varint::decode(buf).map_err(|_| StreamHeaderDecodeError::BufferTooShort)?;
    if stream_type.get() == UNIDIRECTIONAL_STREAM_TYPE {
        let (session_id, session_id_len) = varint::decode(&buf[type_len..])
            .map_err(|_| StreamHeaderDecodeError::BufferTooShort)?;
        let session_id = session_id.get();
        if !session_id.is_multiple_of(4) {
            return Err(StreamHeaderDecodeError::InvalidSessionId);
        }
        Ok(ClassifiedUniStream::WebTransport {
            session_id,
            data_offset: type_len + session_id_len,
        })
    } else {
        Ok(ClassifiedUniStream::Http3 {
            stream_type: stream_type.get(),
            data_offset: type_len,
        })
    }
}

/// ストリームタイプを判定する補助関数
pub mod stream_type {
    /// QUIC ストリーム ID がクライアント開始かどうか
    pub fn is_client_initiated(stream_id: u64) -> bool {
        stream_id & 0x01 == 0
    }

    /// QUIC ストリーム ID がサーバー開始かどうか
    pub fn is_server_initiated(stream_id: u64) -> bool {
        stream_id & 0x01 != 0
    }

    /// QUIC ストリーム ID が双方向かどうか
    pub fn is_bidirectional(stream_id: u64) -> bool {
        stream_id & 0x02 == 0
    }

    /// QUIC ストリーム ID が単方向かどうか
    pub fn is_unidirectional(stream_id: u64) -> bool {
        stream_id & 0x02 != 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_stream_header_decode_empty_buffer() {
        assert!(StreamHeader::decode_unidirectional(&[]).is_none());
        assert!(StreamHeader::decode_bidirectional(&[]).is_none());
    }

    #[test]
    fn test_stream_header_new_rejects_invalid_session_id() {
        // session_id % 4 != 0 は client-initiated bidi ID ではないので拒否される
        assert_eq!(
            StreamHeader::new(1),
            Err(StreamHeaderDecodeError::InvalidSessionId)
        );
        assert_eq!(
            StreamHeader::new(2),
            Err(StreamHeaderDecodeError::InvalidSessionId)
        );
        assert_eq!(
            StreamHeader::new(3),
            Err(StreamHeaderDecodeError::InvalidSessionId)
        );
        assert!(StreamHeader::new(0).is_ok());
        assert!(StreamHeader::new(4).is_ok());
    }

    #[test]
    fn test_stream_header_new_rejects_session_id_out_of_range() {
        // session_id が VarInt 範囲 (`2^62 - 1`) を超える場合は SessionIdOutOfRange
        // (4 の倍数判定より先に値域判定が走る)
        let too_large = (1u64 << 62) & !0x3;
        assert_eq!(
            StreamHeader::new(too_large),
            Err(StreamHeaderDecodeError::SessionIdOutOfRange)
        );
    }

    #[test]
    fn test_stream_header_session_id_getter() {
        // new 経由のヘッダーは検証済みの session_id を返す
        let header = StreamHeader::new(8).expect("test must succeed");
        assert_eq!(header.session_id(), 8);
    }

    #[test]
    fn test_stream_creation() {
        let stream = Stream::new(4, 0, true);
        assert_eq!(stream.stream_id(), 4);
        assert_eq!(stream.session_id(), 0);
        assert!(stream.is_bidirectional());
        assert!(!stream.is_header_sent());
        assert!(!stream.is_header_received());
        assert_eq!(stream.bytes_sent(), 0);
        assert_eq!(stream.bytes_received(), 0);
    }

    #[test]
    fn test_stream_bytes_tracking() {
        let mut stream = Stream::new(2, 0, false);
        stream.add_bytes_sent(100);
        stream.add_bytes_received(50);
        assert_eq!(stream.bytes_sent(), 100);
        assert_eq!(stream.bytes_received(), 50);

        stream.add_bytes_sent(200);
        assert_eq!(stream.bytes_sent(), 300);
    }

    #[test]
    fn test_stream_type_helpers() {
        // Client-initiated bidirectional (0b00)
        assert!(stream_type::is_client_initiated(0));
        assert!(stream_type::is_bidirectional(0));

        // Server-initiated bidirectional (0b01)
        assert!(stream_type::is_server_initiated(1));
        assert!(stream_type::is_bidirectional(1));

        // Client-initiated unidirectional (0b10)
        assert!(stream_type::is_client_initiated(2));
        assert!(stream_type::is_unidirectional(2));

        // Server-initiated unidirectional (0b11)
        assert!(stream_type::is_server_initiated(3));
        assert!(stream_type::is_unidirectional(3));
    }

    #[test]
    fn test_decode_unidirectional_checked_invalid_session_id() {
        let mut buf = Vec::new();
        varint::encode_into_vec(&mut buf, UNI_TYPE_VARINT);
        varint::encode_into_vec(&mut buf, VarInt::from_static(5));
        let result = StreamHeader::decode_unidirectional_checked(&buf);
        assert_eq!(result, Err(StreamHeaderDecodeError::InvalidSessionId));
    }

    #[test]
    fn test_decode_bidirectional_checked_invalid_session_id() {
        let mut buf = Vec::new();
        varint::encode_into_vec(&mut buf, BIDI_SIGNAL_VARINT);
        varint::encode_into_vec(&mut buf, VarInt::from_static(7));
        let result = StreamHeader::decode_bidirectional_checked(&buf);
        assert_eq!(result, Err(StreamHeaderDecodeError::InvalidSessionId));
    }

    // ---------------------------------------------------------------
    // classify_uni_stream_checked テスト
    // ---------------------------------------------------------------

    #[test]
    fn test_classify_uni_stream_checked_webtransport_valid() {
        let mut buf = Vec::new();
        varint::encode_into_vec(&mut buf, UNI_TYPE_VARINT);
        varint::encode_into_vec(&mut buf, VarInt::ZERO); // session_id = 0 (0 % 4 == 0)
        let expected_offset = buf.len();
        buf.extend_from_slice(b"payload");
        let result = classify_uni_stream_checked(&buf).expect("test must succeed");
        assert_eq!(
            result,
            ClassifiedUniStream::WebTransport {
                session_id: 0,
                data_offset: expected_offset,
            }
        );
    }

    #[test]
    fn test_classify_uni_stream_checked_webtransport_invalid_session_id() {
        let mut buf = Vec::new();
        varint::encode_into_vec(&mut buf, UNI_TYPE_VARINT);
        varint::encode_into_vec(&mut buf, VarInt::from_static(5)); // session_id = 5 (5 % 4 != 0)
        let result = classify_uni_stream_checked(&buf);
        assert_eq!(result, Err(StreamHeaderDecodeError::InvalidSessionId));
    }

    #[test]
    fn test_classify_uni_stream_checked_http3() {
        // HTTP/3 制御ストリーム (type = 0x00)
        let mut buf = Vec::new();
        varint::encode_into_vec(&mut buf, VarInt::ZERO);
        buf.extend_from_slice(b"data");
        let result = classify_uni_stream_checked(&buf).expect("test must succeed");
        assert_eq!(
            result,
            ClassifiedUniStream::Http3 {
                stream_type: 0x00,
                data_offset: 1,
            }
        );
    }

    #[test]
    fn test_classify_uni_stream_checked_buffer_too_short() {
        let result = classify_uni_stream_checked(&[]);
        assert_eq!(result, Err(StreamHeaderDecodeError::BufferTooShort));
    }

    #[test]
    fn test_classify_uni_stream_checked_session_id_buffer_too_short() {
        // ストリームタイプは読めるが session_id が不足
        let mut buf = Vec::new();
        varint::encode_into_vec(&mut buf, UNI_TYPE_VARINT);
        let result = classify_uni_stream_checked(&buf);
        assert_eq!(result, Err(StreamHeaderDecodeError::BufferTooShort));
    }
}
