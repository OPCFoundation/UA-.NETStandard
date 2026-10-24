//! HTTP/3 フレームエンコーダー

use crate::varint::{self, VarInt};

use super::{
    DataPayload, Frame, FrameType, GoawayPayload, HeadersPayload, SettingsPayload, UnknownFrame,
};

/// フレームヘッダーをエンコードする
///
/// 成功時はエンコードしたバイト数を返す。
/// 引数は [`VarInt`] 型のため値域は型レベルで保証される (RFC 9000 Section 16)。
/// バッファ不足の場合は `None` を返す。
pub fn encode_frame_header(
    buf: &mut [u8],
    frame_type: VarInt,
    payload_len: VarInt,
) -> Option<usize> {
    let total = frame_type.encoded_len() + payload_len.encoded_len();
    if buf.len() < total {
        return None;
    }

    let mut offset = 0;
    offset += varint::encode(&mut buf[offset..], frame_type).ok()?;
    offset += varint::encode(&mut buf[offset..], payload_len).ok()?;

    Some(offset)
}

/// フレームをエンコードするのに必要なバイト数を計算する
///
/// 各 [`Frame`] のフィールドは構築時に [`VarInt`] 範囲が型レベルで保証されているため、
/// 検査エラーが発生するのはペイロード長 (`payload.len()` または SETTINGS の累計長) を
/// `VarInt` に押し込めない場合、もしくは合計長が `usize` を超える場合のみ。
/// その場合は `None` を返す。
/// 呼び出し側は本値を根拠にバッファを確保するため、嘘の長さを返さない。
pub fn encoded_frame_len(frame: &Frame) -> Option<usize> {
    let (frame_type, payload_len): (VarInt, VarInt) = match frame {
        Frame::Data(p) => (
            VarInt::from_static(FrameType::Data as u64),
            VarInt::new(p.len() as u64).ok()?,
        ),
        Frame::Headers(p) => (
            VarInt::from_static(FrameType::Headers as u64),
            VarInt::new(p.len() as u64).ok()?,
        ),
        Frame::Settings(p) => (
            VarInt::from_static(FrameType::Settings as u64),
            encoded_settings_payload_len(p)?,
        ),
        Frame::Goaway(p) => (
            VarInt::from_static(FrameType::Goaway as u64),
            // VarInt::encoded_len() は 1/2/4/8 のいずれかなので必ず VarInt に収まる
            VarInt::from_static(p.id().encoded_len() as u64),
        ),
        Frame::MaxPushId(id) => (
            VarInt::from_static(FrameType::MaxPushId as u64),
            VarInt::from_static(id.encoded_len() as u64),
        ),
        Frame::Unknown(p) => (p.frame_type(), VarInt::new(p.payload().len() as u64).ok()?),
    };

    let header_len = frame_type.encoded_len() + payload_len.encoded_len();
    // 32bit プラットフォームで payload_len が usize を超える場合は None
    // (FrameHeader::total_len と同じ防御)
    let payload_size = usize::try_from(payload_len.get()).ok()?;
    header_len.checked_add(payload_size)
}

/// SETTINGS ペイロードのエンコード長を計算
///
/// 各 [`Setting`](crate::settings::Setting) の wire 表現が VarInt 範囲内である
/// ことは構築時に保証されているため、本関数は累計長が `usize` および VarInt 範囲に
/// 収まるかだけ検査する。
fn encoded_settings_payload_len(payload: &SettingsPayload) -> Option<VarInt> {
    let mut total: usize = 0;
    for setting in payload.settings() {
        let (id, value) = setting.as_wire();
        total = total.checked_add(id.encoded_len())?;
        total = total.checked_add(value.encoded_len())?;
    }
    VarInt::new(total as u64).ok()
}

/// フレームをエンコードする
///
/// 成功時はエンコードしたバイト数を返す。
/// バッファ不足、または各種ペイロード長が VarInt 範囲を超える場合は `None` を返す。
pub fn encode_frame(buf: &mut [u8], frame: &Frame) -> Option<usize> {
    let required = encoded_frame_len(frame)?;
    if buf.len() < required {
        return None;
    }

    match frame {
        Frame::Data(p) => encode_data_frame(buf, p),
        Frame::Headers(p) => encode_headers_frame(buf, p),
        Frame::Settings(p) => encode_settings_frame(buf, p),
        Frame::Goaway(p) => encode_goaway_frame(buf, p),
        Frame::MaxPushId(value) => encode_max_push_id_frame(buf, *value),
        Frame::Unknown(p) => encode_unknown_frame(buf, p),
    }
}

fn encode_data_frame(buf: &mut [u8], payload: &DataPayload) -> Option<usize> {
    let frame_type = VarInt::from_static(FrameType::Data as u64);
    let payload_len = VarInt::new(payload.len() as u64).ok()?;

    let mut offset = encode_frame_header(buf, frame_type, payload_len)?;
    buf[offset..offset + payload.len()].copy_from_slice(payload.data());
    offset += payload.len();

    Some(offset)
}

fn encode_headers_frame(buf: &mut [u8], payload: &HeadersPayload) -> Option<usize> {
    let frame_type = VarInt::from_static(FrameType::Headers as u64);
    let payload_len = VarInt::new(payload.len() as u64).ok()?;

    let mut offset = encode_frame_header(buf, frame_type, payload_len)?;
    buf[offset..offset + payload.len()].copy_from_slice(payload.encoded_field_section());
    offset += payload.len();

    Some(offset)
}

fn encode_settings_frame(buf: &mut [u8], payload: &SettingsPayload) -> Option<usize> {
    let frame_type = VarInt::from_static(FrameType::Settings as u64);
    let payload_len = encoded_settings_payload_len(payload)?;

    let mut offset = encode_frame_header(buf, frame_type, payload_len)?;

    for setting in payload.settings() {
        let (id, value) = setting.as_wire();
        offset += varint::encode(&mut buf[offset..], id).ok()?;
        offset += varint::encode(&mut buf[offset..], value).ok()?;
    }

    Some(offset)
}

fn encode_goaway_frame(buf: &mut [u8], payload: &GoawayPayload) -> Option<usize> {
    let frame_type = VarInt::from_static(FrameType::Goaway as u64);
    let id = payload.id();
    // VarInt::encoded_len() は 1/2/4/8 のいずれかで必ず VarInt 範囲内
    let payload_len = VarInt::from_static(id.encoded_len() as u64);

    let mut offset = encode_frame_header(buf, frame_type, payload_len)?;
    offset += varint::encode(&mut buf[offset..], id).ok()?;

    Some(offset)
}

fn encode_max_push_id_frame(buf: &mut [u8], id: VarInt) -> Option<usize> {
    let frame_type = VarInt::from_static(FrameType::MaxPushId as u64);
    let payload_len = VarInt::from_static(id.encoded_len() as u64);

    let mut offset = encode_frame_header(buf, frame_type, payload_len)?;
    offset += varint::encode(&mut buf[offset..], id).ok()?;

    Some(offset)
}

fn encode_unknown_frame(buf: &mut [u8], payload: &UnknownFrame) -> Option<usize> {
    let frame_type = payload.frame_type();

    // WT_STREAM (0x41) は長さを持たないフレームであり、HTTP/3 フレームとして送信してはならない
    // (draft-ietf-webtrans-http3-16 Section 4.3: "Endpoints MUST NOT send WT_STREAM as a frame
    // type on HTTP/3 streams other than the very first bytes of a request stream")。
    // コードが表す数値は webtransport ストリームの双方向シグナル値と同じだが、
    // frame モジュールは webtransport 層よりも下位のレイヤーであるためリテラルで扱う。
    if frame_type.get() == 0x41 {
        return None;
    }

    let bytes = payload.payload();
    let payload_len = VarInt::new(bytes.len() as u64).ok()?;

    let mut offset = encode_frame_header(buf, frame_type, payload_len)?;
    buf[offset..offset + bytes.len()].copy_from_slice(bytes);
    offset += bytes.len();

    Some(offset)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encode_frame_header() {
        let mut buf = [0u8; 16];

        // Type=0, Length=0
        let len = encode_frame_header(&mut buf, VarInt::from_static(0), VarInt::from_static(0))
            .expect("test must succeed");
        assert_eq!(len, 2);
        assert_eq!(&buf[..2], &[0x00, 0x00]);

        // Type=4 (SETTINGS), Length=10
        let len = encode_frame_header(&mut buf, VarInt::from_static(4), VarInt::from_static(10))
            .expect("test must succeed");
        assert_eq!(len, 2);
        assert_eq!(&buf[..2], &[0x04, 0x0a]);
    }

    #[test]
    fn test_encode_frame_header_buffer_too_short() {
        // バッファが必要バイト数未満なら None
        let mut buf = [0u8; 1];
        assert_eq!(
            encode_frame_header(&mut buf, VarInt::from_static(0), VarInt::from_static(0)),
            None
        );
    }

    #[test]
    fn test_encode_unknown_frame_wt_stream_is_rejected() {
        // WT_STREAM (0x41) をフレームとしてエンコードすると None
        // (draft-ietf-webtrans-http3-16 Section 4.3)
        let payload = UnknownFrame::new(VarInt::from_static(0x41), vec![0x00, 0x01])
            .expect("test must succeed");
        let frame = Frame::Unknown(payload);
        let mut buf = [0u8; 64];
        assert_eq!(encode_frame(&mut buf, &frame), None);
    }

    #[test]
    fn test_encode_unknown_frame_other_type_is_ok() {
        // 0x41 以外の Unknown フレームは従来どおりエンコードできる
        let payload = UnknownFrame::new(VarInt::from_static(0x42), vec![0xaa, 0xbb])
            .expect("test must succeed");
        let frame = Frame::Unknown(payload);
        let mut buf = [0u8; 64];
        let len = encode_frame(&mut buf, &frame).expect("test must succeed");
        // 0x42 は 63 を超えるため 2 バイト VarInt としてエンコードされる
        assert_eq!(len, 5);
        assert_eq!(&buf[..len], &[0x40, 0x42, 0x02, 0xaa, 0xbb]);
    }
}
