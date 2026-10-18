//! HTTP/3 フレームデコーダー

use crate::error::FrameDecodeError;
use crate::settings::Setting;
use crate::varint::{self, VarInt};

use super::{
    DataPayload, Frame, FrameType, GoawayPayload, HeadersPayload, SettingsPayload, UnknownFrame,
};

/// フレームヘッダー (RFC 9114 Section 7.1)
///
/// `frame_type` / `payload_len` は wire 上 VarInt のため
/// [`VarInt`] 型で保持し RFC 9000 Section 16 の値域を型レベルで担保する。
/// `header_len` は decoder が受信した wire 上の実バイト長を保持する。
/// RFC 9000 Section 16 は最小エンコード必須ではない (frame type 例外は §12.4 の
/// QUIC frame type のみで HTTP/3 frame type には適用されない) ため、
/// `frame_type.encoded_len() + payload_len.encoded_len()` (値からの最小長)
/// と一致するとは限らない。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FrameHeader {
    /// フレームタイプ
    frame_type: VarInt,
    /// ペイロード長
    payload_len: VarInt,
    /// ヘッダーのバイト長 (wire 上の実バイト長: type と length の VarInt 消費長の和)
    header_len: usize,
}

impl FrameHeader {
    /// 検証済みの値から構築する (crate 内部専用、decoder が使用)
    ///
    /// `frame_type` / `payload_len` は型レベルで VarInt 範囲が保証されており、
    /// `header_len` は呼び出し側が wire 上の実バイト消費長を渡す責務を負う
    /// (`varint::decode` の返す `consumed` の和)。本検証では下限 (= 最小エンコード長)
    /// と上限 (= 16 バイト、VarInt 2 つ分の最大エンコード長) のみを debug_assert する。
    pub(crate) const fn from_validated_parts(
        frame_type: VarInt,
        payload_len: VarInt,
        header_len: usize,
    ) -> Self {
        debug_assert!(header_len >= frame_type.encoded_len() + payload_len.encoded_len());
        debug_assert!(header_len <= 16);
        Self {
            frame_type,
            payload_len,
            header_len,
        }
    }

    /// フレームタイプを取得
    pub const fn frame_type(&self) -> VarInt {
        self.frame_type
    }

    /// ペイロード長を取得
    pub const fn payload_len(&self) -> VarInt {
        self.payload_len
    }

    /// ヘッダーのバイト長を取得
    pub const fn header_len(&self) -> usize {
        self.header_len
    }

    /// フレーム全体のバイト長
    ///
    /// `payload_len` は最大 `2^62 - 1` (RFC 9000 Section 16) のため、
    /// 32bit プラットフォームでは `usize` に収まらない可能性がある。
    /// その場合は [`None`] を返し、呼び出し側でエラー扱いする。
    pub fn total_len(&self) -> Option<usize> {
        let payload = usize::try_from(self.payload_len.get()).ok()?;
        self.header_len.checked_add(payload)
    }
}

/// フレームヘッダーをデコードする
///
/// 成功時は `FrameHeader` を返す
pub fn decode_frame_header(buf: &[u8]) -> Result<FrameHeader, FrameDecodeError> {
    if buf.is_empty() {
        return Err(FrameDecodeError::BufferTooShort);
    }

    let (frame_type, type_len) =
        varint::decode(buf).map_err(|_| FrameDecodeError::BufferTooShort)?;

    if buf.len() < type_len {
        return Err(FrameDecodeError::BufferTooShort);
    }

    let (payload_len, len_len) =
        varint::decode(&buf[type_len..]).map_err(|_| FrameDecodeError::BufferTooShort)?;

    // HTTP/2 専用フレームのチェック
    if FrameType::is_http2_only(frame_type.get()) {
        return Err(FrameDecodeError::Http2Frame(frame_type));
    }

    // wire 上の実バイト長を保持する (RFC 9000 Section 16 は最小エンコード必須でない
    // ため、type_len + len_len は VarInt 値から計算した最小長と一致するとは限らない)
    Ok(FrameHeader::from_validated_parts(
        frame_type,
        payload_len,
        type_len + len_len,
    ))
}

/// フレームをデコードする
///
/// 成功時は `(Frame, 消費バイト数)` を返す
pub fn decode_frame(buf: &[u8]) -> Result<(Frame, usize), FrameDecodeError> {
    let header = decode_frame_header(buf)?;

    // 32bit プラットフォームで payload_len が usize を超える場合は InvalidLength
    let total_len = header.total_len().ok_or(FrameDecodeError::InvalidLength)?;
    if buf.len() < total_len {
        return Err(FrameDecodeError::BufferTooShort);
    }

    let payload = &buf[header.header_len()..total_len];
    let frame_type_u64 = header.frame_type().get();

    let frame = match FrameType::from_type(frame_type_u64) {
        Some(FrameType::Data) => Frame::Data(DataPayload::new(payload.to_vec())),
        Some(FrameType::Headers) => Frame::Headers(HeadersPayload::new(payload.to_vec())),
        Some(FrameType::Settings) => decode_settings_frame(payload)?,
        Some(FrameType::Goaway) => decode_goaway_frame(payload)?,
        Some(FrameType::MaxPushId) => decode_max_push_id_frame(payload)?,
        Some(FrameType::CancelPush | FrameType::PushPromise) => {
            // サーバープッシュはサポートしない
            //
            // RFC 9114 上の配置ルール:
            //   - CANCEL_PUSH (0x03): control stream でのみ送受信 (Section 7.2.3)
            //   - PUSH_PROMISE (0x05): request stream でのみ送信 (Section 7.2.5)
            //
            // 本実装ではサーバープッシュ機能を提供しないため、これらは stream 種別を
            // 問わず H3_FRAME_UNEXPECTED で拒否する。
            // MAX_PUSH_ID は control stream 上で正当に受信されうるため別経路で扱う
            // (Section 7.2.7)。
            return Err(FrameDecodeError::ServerPushNotSupported(
                header.frame_type(),
            ));
        }
        None => Frame::Unknown(
            UnknownFrame::new(header.frame_type(), payload.to_vec())
                .expect("None arm receives only unknown non-HTTP/2 frame types"),
        ),
    };

    Ok((frame, total_len))
}

fn decode_settings_frame(payload: &[u8]) -> Result<Frame, FrameDecodeError> {
    let mut settings = SettingsPayload::new();
    let mut offset = 0;

    while offset < payload.len() {
        // payload は完全に受信済みなので、途中切れは H3_FRAME_ERROR (RFC 9114 Section 7.1)
        let (id, id_len) =
            varint::decode(&payload[offset..]).map_err(|_| FrameDecodeError::InvalidLength)?;
        offset += id_len;

        let (value, value_len) =
            varint::decode(&payload[offset..]).map_err(|_| FrameDecodeError::InvalidLength)?;
        offset += value_len;

        // 値域 / HTTP2 専用 / 予約 / bool / 重複は全て SettingsPayload::add 経路で検査
        let setting = Setting::from_wire(id, value)?;
        settings.add(setting)?;
    }

    Ok(Frame::Settings(settings))
}

fn decode_goaway_frame(payload: &[u8]) -> Result<Frame, FrameDecodeError> {
    // payload は完全に受信済みなので、varint デコード失敗 (空 payload を含む) は H3_FRAME_ERROR
    // (RFC 9114 Section 7.1)。decode_settings_frame / decode_max_push_id_frame と同じ扱い。
    let (id, consumed) = varint::decode(payload).map_err(|_| FrameDecodeError::InvalidLength)?;
    // ペイロードに余剰バイトがあれば不正 (RFC 9114 Section 7.1)
    if consumed != payload.len() {
        return Err(FrameDecodeError::InvalidLength);
    }
    Ok(Frame::Goaway(GoawayPayload::new(id)))
}

fn decode_max_push_id_frame(payload: &[u8]) -> Result<Frame, FrameDecodeError> {
    // MAX_PUSH_ID ペイロードは push ID の単一 varint (RFC 9114 Section 7.2.7)
    let (id, consumed) = varint::decode(payload).map_err(|_| FrameDecodeError::InvalidLength)?;
    if consumed != payload.len() {
        return Err(FrameDecodeError::InvalidLength);
    }
    Ok(Frame::MaxPushId(id))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_decode_frame_header() {
        // Type=0 (DATA), Length=5
        let buf = [0x00, 0x05];
        let header = decode_frame_header(&buf).expect("test must succeed");
        assert_eq!(header.frame_type().get(), 0);
        assert_eq!(header.payload_len().get(), 5);
        assert_eq!(header.header_len(), 2);

        // Type=4 (SETTINGS), Length=10
        let buf = [0x04, 0x0a];
        let header = decode_frame_header(&buf).expect("test must succeed");
        assert_eq!(header.frame_type().get(), 4);
        assert_eq!(header.payload_len().get(), 10);
    }

    #[test]
    fn test_decode_frame_header_http2_frame() {
        // Type=2 (PRIORITY - HTTP/2 only)
        let buf = [0x02, 0x05];
        let result = decode_frame_header(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::Http2Frame(VarInt::from_static(0x02)))
        );

        // Type=6 (PING - HTTP/2 only)
        let buf = [0x06, 0x08];
        let result = decode_frame_header(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::Http2Frame(VarInt::from_static(0x06)))
        );
    }

    #[test]
    fn test_decode_settings_frame_http2_setting() {
        use crate::settings::SettingError;
        use crate::varint::VarInt;

        // SETTINGS frame with HTTP/2-only setting (ENABLE_PUSH=0x02)
        let buf = [0x04, 0x02, 0x02, 0x01];
        let result = decode_frame(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::InvalidSetting(
                SettingError::Http2OnlyId {
                    id: VarInt::new(0x02).expect("test must succeed")
                }
            ))
        );
    }

    #[test]
    fn test_decode_frame_buffer_too_short() {
        let buf = [0x00]; // Missing length
        assert_eq!(decode_frame(&buf), Err(FrameDecodeError::BufferTooShort));

        let buf = [0x00, 0x05, 0x01, 0x02]; // Payload too short
        assert_eq!(decode_frame(&buf), Err(FrameDecodeError::BufferTooShort));
    }

    #[test]
    fn test_frame_header_total_len() {
        // frame_type 0 (1 バイト VarInt) + payload_len 50 (1 バイト VarInt) + payload 50
        let header =
            FrameHeader::from_validated_parts(VarInt::from_static(0), VarInt::from_static(50), 2);
        assert_eq!(header.header_len(), 2);
        assert_eq!(header.total_len(), Some(52));
    }

    #[test]
    fn test_decode_frame_handles_non_minimal_varint_encoding() {
        // RFC 9000 Section 16: 最小エンコード必須ではない (frame type 例外は QUIC layer のみ)。
        // wire 上で type=0 を 2 バイト (0x40 0x00)、payload_len=3 を 1 バイト (0x03) で
        // エンコードしたフレーム + payload 3 バイトを decode できることを確認する。
        let buf = [0x40, 0x00, 0x03, 0xaa, 0xbb, 0xcc];
        let header = decode_frame_header(&buf).expect("test must succeed");
        assert_eq!(header.frame_type().get(), 0); // DATA
        assert_eq!(header.payload_len().get(), 3);
        assert_eq!(header.header_len(), 3); // 2 バイト + 1 バイト
        assert_eq!(header.total_len(), Some(6));

        let (frame, consumed) = decode_frame(&buf).expect("test must succeed");
        assert_eq!(consumed, 6);
        match frame {
            Frame::Data(p) => assert_eq!(p.data(), &[0xaa, 0xbb, 0xcc][..]),
            other => panic!("expected DATA, got {other:?}"),
        }
    }

    #[test]
    fn test_decode_settings_frame_duplicate_id() {
        use crate::settings::SettingError;
        use crate::varint::VarInt;

        // SETTINGS フレームに同一 ID が 2 回含まれる場合 (RFC 9114 §7.2.4 MUST NOT)
        let buf = [0x04, 0x04, 0x01, 0x04, 0x01, 0x08];
        let result = decode_frame(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::InvalidSetting(
                SettingError::DuplicateId {
                    id: VarInt::new(0x01).expect("test must succeed")
                }
            ))
        );
    }

    #[test]
    fn test_decode_settings_frame_reserved_id() {
        use crate::settings::SettingError;
        use crate::varint::VarInt;

        // SETTINGS フレームに予約 ID 0x00 が含まれる場合 (RFC 9114 §11.2.2 Table 3)
        let buf = [0x04, 0x02, 0x00, 0x00];
        let result = decode_frame(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::InvalidSetting(SettingError::ReservedId {
                id: VarInt::ZERO
            }))
        );
    }

    #[test]
    fn test_decode_settings_frame_invalid_boolean_ecp() {
        use crate::settings::SettingError;
        use crate::varint::VarInt;

        // SETTINGS_ENABLE_CONNECT_PROTOCOL (0x08) に 2 を設定する (RFC 8441 §3 違反)
        let buf = [0x04, 0x02, 0x08, 0x02];
        let result = decode_frame(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::InvalidSetting(
                SettingError::InvalidBooleanValue {
                    id: VarInt::new(0x08).expect("test must succeed"),
                    value: VarInt::new(0x02).expect("test must succeed"),
                }
            ))
        );
    }

    #[test]
    fn test_decode_settings_frame_invalid_boolean_h3_datagram() {
        use crate::settings::SettingError;
        use crate::varint::VarInt;

        // SETTINGS_H3_DATAGRAM (0x33) に 5 を設定する (RFC 9297 §2.1.1 違反)
        let buf = [0x04, 0x02, 0x33, 0x05];
        let result = decode_frame(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::InvalidSetting(
                SettingError::InvalidBooleanValue {
                    id: VarInt::new(0x33).expect("test must succeed"),
                    value: VarInt::new(0x05).expect("test must succeed"),
                }
            ))
        );
    }

    #[test]
    fn test_decode_server_push_frames_not_supported() {
        // CANCEL_PUSH (0x03) - サーバープッシュはサポートしない
        let buf = [0x03, 0x01, 0x00];
        let result = decode_frame(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::ServerPushNotSupported(
                VarInt::from_static(0x03)
            ))
        );

        // PUSH_PROMISE (0x05) - サーバープッシュはサポートしない
        let buf = [0x05, 0x02, 0x00, 0x00];
        let result = decode_frame(&buf);
        assert_eq!(
            result,
            Err(FrameDecodeError::ServerPushNotSupported(
                VarInt::from_static(0x05)
            ))
        );
    }

    #[test]
    fn test_decode_max_push_id_frame() {
        // MAX_PUSH_ID (0x0d): サーバープッシュ非対応でも control stream 上では
        // 受信できなければならない (RFC 9114 Section 7.2.7)。デコード自体は成功する。
        let buf = [0x0d, 0x01, 0x05];
        let result = decode_frame(&buf).expect("test must succeed");
        assert_eq!(result, (Frame::MaxPushId(VarInt::from_static(5)), 3));
    }

    #[test]
    fn test_decode_goaway_frame_empty_payload_is_invalid_length() {
        // payload 長 0 の GOAWAY は途中切れ扱いで H3_FRAME_ERROR (RFC 9114 Section 7.1)。
        // BufferTooShort ではなく InvalidLength を返し、SETTINGS / MAX_PUSH_ID と一貫させる。
        let buf = [0x07, 0x00];
        assert_eq!(decode_frame(&buf), Err(FrameDecodeError::InvalidLength));
    }

    #[test]
    fn test_decode_goaway_frame_trailing_bytes_is_invalid_length() {
        // payload に余剰バイトがある GOAWAY も H3_FRAME_ERROR (RFC 9114 Section 7.1)。
        // length=2 だが ID varint は 1 バイト (0x05) で、余剰バイト 0x99 が残る。
        let buf = [0x07, 0x02, 0x05, 0x99];
        assert_eq!(decode_frame(&buf), Err(FrameDecodeError::InvalidLength));
    }
}
