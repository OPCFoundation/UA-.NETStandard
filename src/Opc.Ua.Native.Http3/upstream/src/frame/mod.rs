//! HTTP/3 フレーム (RFC 9114 Section 7.2)
//!
//! HTTP/3 で使用されるフレームの型定義とエンコード/デコード機能を提供。
//!
//! ## サーバープッシュについて
//!
//! このライブラリはサーバープッシュをサポートしない。
//! CANCEL_PUSH, PUSH_PROMISE, MAX_PUSH_ID フレームを受信した場合は
//! H3_FRAME_UNEXPECTED エラーを返す。
//!
//! これは nghttp3 と同様の方針であり、主要なブラウザ (Chrome, Firefox) でも
//! サーバープッシュは無効化されているため、実用上問題ない。

mod decoder;
mod encoder;

pub use decoder::{FrameHeader, decode_frame, decode_frame_header};
pub use encoder::{encode_frame, encode_frame_header, encoded_frame_len};

use core::fmt;
use std::collections::HashSet;

use crate::settings::{Setting, SettingError};
use crate::varint::VarInt;

/// フレームタイプ (RFC 9114 Section 7.2)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u64)]
pub enum FrameType {
    /// DATA フレーム (0x00)
    Data = 0x00,
    /// HEADERS フレーム (0x01)
    Headers = 0x01,
    /// CANCEL_PUSH フレーム (0x03) - サーバープッシュはサポートしない
    CancelPush = 0x03,
    /// SETTINGS フレーム (0x04)
    Settings = 0x04,
    /// PUSH_PROMISE フレーム (0x05) - サーバープッシュはサポートしない
    PushPromise = 0x05,
    /// GOAWAY フレーム (0x07)
    Goaway = 0x07,
    /// MAX_PUSH_ID フレーム (0x0d) - サーバープッシュはサポートしない
    MaxPushId = 0x0d,
}

impl FrameType {
    /// タイプ ID から `FrameType` を作成
    pub fn from_type(t: u64) -> Option<Self> {
        match t {
            0x00 => Some(Self::Data),
            0x01 => Some(Self::Headers),
            0x03 => Some(Self::CancelPush),
            0x04 => Some(Self::Settings),
            0x05 => Some(Self::PushPromise),
            0x07 => Some(Self::Goaway),
            0x0d => Some(Self::MaxPushId),
            _ => None,
        }
    }

    /// HTTP/2 専用フレームかどうか
    pub fn is_http2_only(t: u64) -> bool {
        matches!(t, 0x02 | 0x06 | 0x08 | 0x09)
    }
}

/// HTTP/3 フレーム
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Frame {
    /// DATA フレーム
    Data(DataPayload),
    /// HEADERS フレーム (QPACK エンコード済みヘッダーブロック)
    Headers(HeadersPayload),
    /// SETTINGS フレーム
    Settings(SettingsPayload),
    /// GOAWAY フレーム
    Goaway(GoawayPayload),
    /// MAX_PUSH_ID フレーム (RFC 9114 Section 7.2.7)
    ///
    /// サーバープッシュ自体はサポートしないが、クライアントから control stream 上で
    /// 送信される正当なフレームのため、デコードして単調性検証だけ行う。
    /// 値は wire 上 VarInt のため [`VarInt`] 型で保持する (RFC 9000 Section 16)。
    MaxPushId(VarInt),
    /// 未知のフレーム (RFC 9114 Section 9 拡張 / Section 7.2.8 Reserved Frame Types)
    ///
    /// 既知タイプ / HTTP/2 専用 ID を格納できない不変条件は [`UnknownFrame`] の
    /// 構築 API (`new` / decoder 経路) で担保される。本 variant を受け取った側は
    /// `frame_type` が未知の値であると仮定してよい。
    Unknown(UnknownFrame),
}

impl Frame {
    /// フレームタイプを取得
    ///
    /// RFC 9114 Section 7.2 の各フレームタイプは wire 上 VarInt であり、
    /// RFC 9000 Section 16 の値域 (0..=2^62 - 1) に収まる。
    pub fn frame_type(&self) -> VarInt {
        match self {
            Self::Data(_) => VarInt::from_static(FrameType::Data as u64),
            Self::Headers(_) => VarInt::from_static(FrameType::Headers as u64),
            Self::Settings(_) => VarInt::from_static(FrameType::Settings as u64),
            Self::Goaway(_) => VarInt::from_static(FrameType::Goaway as u64),
            Self::MaxPushId(_) => VarInt::from_static(FrameType::MaxPushId as u64),
            Self::Unknown(p) => p.frame_type(),
        }
    }
}

/// [`Frame::Unknown`] 構築時の検査エラー
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UnknownFrameError {
    /// 既知の HTTP/3 フレームタイプを `Unknown` で構築しようとした
    ///
    /// RFC 9114 Section 7.2 で定義された 7 タイプ (DATA / HEADERS / CANCEL_PUSH /
    /// SETTINGS / PUSH_PROMISE / GOAWAY / MAX_PUSH_ID) は専用 variant で表現する。
    KnownFrameType {
        /// 該当フレームタイプ ID
        frame_type: VarInt,
    },
    /// HTTP/2 専用フレームタイプ (0x02 / 0x06 / 0x08 / 0x09) を構築しようとした
    ///
    /// RFC 9114 Section 11.2.1 Table 2 で Reserved 登録、Section 7.2.8 で受信時
    /// H3_FRAME_UNEXPECTED 必須。
    Http2OnlyFrameType {
        /// 該当フレームタイプ ID
        frame_type: VarInt,
    },
}

impl fmt::Display for UnknownFrameError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::KnownFrameType { frame_type } => {
                write!(
                    f,
                    "known HTTP/3 frame type {:#x} cannot be wrapped in Frame::Unknown",
                    frame_type.get()
                )
            }
            Self::Http2OnlyFrameType { frame_type } => {
                write!(
                    f,
                    "HTTP/2-only frame type {:#x} cannot be wrapped in Frame::Unknown",
                    frame_type.get()
                )
            }
        }
    }
}

impl core::error::Error for UnknownFrameError {}

/// 未知の HTTP/3 フレーム (RFC 9114 Section 9 拡張 / Section 7.2.8 Reserved Frame Types)
///
/// `frame_type` は以下のいずれでもないことを構築時に保証する:
/// - [`FrameType`] enum で表現される既知タイプ (DATA / HEADERS / CANCEL_PUSH / SETTINGS /
///   PUSH_PROMISE / GOAWAY / MAX_PUSH_ID)
/// - HTTP/2 専用 ID (RFC 9114 Section 11.2.1 Table 2: 0x02 / 0x06 / 0x08 / 0x09)
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UnknownFrame {
    frame_type: VarInt,
    payload: Vec<u8>,
}

impl UnknownFrame {
    /// 未知フレームを構築する
    ///
    /// エラー条件は [`UnknownFrameError`] の各バリアントを参照。
    pub fn new(frame_type: VarInt, payload: Vec<u8>) -> Result<Self, UnknownFrameError> {
        let t = frame_type.get();
        if FrameType::from_type(t).is_some() {
            return Err(UnknownFrameError::KnownFrameType { frame_type });
        }
        if FrameType::is_http2_only(t) {
            return Err(UnknownFrameError::Http2OnlyFrameType { frame_type });
        }
        Ok(Self {
            frame_type,
            payload,
        })
    }

    /// フレームタイプを取得
    pub const fn frame_type(&self) -> VarInt {
        self.frame_type
    }

    /// ペイロードバイト列の参照を返す
    pub fn payload(&self) -> &[u8] {
        &self.payload
    }

    /// ペイロードバイト列を所有権付きで取り出す
    pub fn into_payload(self) -> Vec<u8> {
        self.payload
    }

    /// ペイロードバイト数を返す
    pub fn len(&self) -> usize {
        self.payload.len()
    }

    /// ペイロードが空か
    pub fn is_empty(&self) -> bool {
        self.payload.is_empty()
    }
}

/// DATA フレームペイロード (RFC 9114 Section 7.2.1)
///
/// ペイロードは任意のオクテット列で値域検査は無いが、構築後の改ざんを防ぐため
/// フィールドを private にし、アクセサ経由で読み取る。
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DataPayload {
    /// ペイロードデータ
    data: Vec<u8>,
}

impl DataPayload {
    /// 新しい DATA ペイロードを作成
    pub fn new(data: Vec<u8>) -> Self {
        Self { data }
    }

    /// ペイロードバイト列の参照を返す
    pub fn data(&self) -> &[u8] {
        &self.data
    }

    /// ペイロードバイト列を所有権付きで取り出す
    pub fn into_data(self) -> Vec<u8> {
        self.data
    }

    /// ペイロードバイト数を返す
    pub fn len(&self) -> usize {
        self.data.len()
    }

    /// ペイロードが空か
    pub fn is_empty(&self) -> bool {
        self.data.is_empty()
    }
}

/// HEADERS フレームペイロード (RFC 9114 Section 7.2.2)
///
/// QPACK 符号化済みバイト列を保持する。フィールド単位の検査は [`crate::qpack::Header`]
/// で別途行う。構築後の改ざんを防ぐためフィールドを private にする。
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HeadersPayload {
    /// QPACK エンコード済みヘッダーブロック
    encoded_field_section: Vec<u8>,
}

impl HeadersPayload {
    /// 新しい HEADERS ペイロードを作成
    pub fn new(encoded_field_section: Vec<u8>) -> Self {
        Self {
            encoded_field_section,
        }
    }

    /// QPACK 符号化済みヘッダーブロックの参照を返す
    pub fn encoded_field_section(&self) -> &[u8] {
        &self.encoded_field_section
    }

    /// QPACK 符号化済みヘッダーブロックを所有権付きで取り出す
    pub fn into_encoded_field_section(self) -> Vec<u8> {
        self.encoded_field_section
    }

    /// 符号化済みヘッダーブロックのバイト数
    pub fn len(&self) -> usize {
        self.encoded_field_section.len()
    }

    /// 符号化済みヘッダーブロックが空か
    pub fn is_empty(&self) -> bool {
        self.encoded_field_section.is_empty()
    }
}

/// SETTINGS フレームペイロード
///
/// 内部の [`Setting`] は構築時に値検査済みかつ ID 重複が無いことを保証する。
/// wire 表現の `(id, value)` から構築するには [`Setting::from_wire`] で先に
/// [`Setting`] を作って [`SettingsPayload::add`] に渡す。
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct SettingsPayload {
    /// 設定エントリのリスト (構築順を維持し、ID 重複は許容しない)
    settings: Vec<Setting>,
    /// `add` 経由で投入された ID の集合 (重複検出用)
    seen_ids: HashSet<VarInt>,
}

impl SettingsPayload {
    /// 新しい SETTINGS ペイロードを作成
    pub fn new() -> Self {
        Self::default()
    }

    /// 検査済みの [`Setting`] を追加する
    ///
    /// 同一 SETTINGS ID が既に存在する場合は
    /// [`SettingError::DuplicateId`] を返す (RFC 9114 §7.2.4: MUST NOT
    /// occur more than once)。
    pub fn add(&mut self, setting: Setting) -> Result<(), SettingError> {
        let id = setting.id();
        if !self.seen_ids.insert(id) {
            return Err(SettingError::DuplicateId { id });
        }
        self.settings.push(setting);
        Ok(())
    }

    /// 保持する全 [`Setting`] のスライス
    ///
    /// 追加順を維持し、ID 重複は構築時に弾かれているため存在しない。
    pub fn settings(&self) -> &[Setting] {
        &self.settings
    }

    /// エントリ数
    pub fn len(&self) -> usize {
        self.settings.len()
    }

    /// エントリが空かどうか
    pub fn is_empty(&self) -> bool {
        self.settings.is_empty()
    }

    /// Settings から SettingsPayload を作成
    ///
    /// H3 設定と WebTransport 設定の両方を含める。`Settings` のフィールドは
    /// 各 ID と 1 対 1 に対応するため、追加時に [`SettingError::DuplicateId`] が
    /// 発生する可能性は無い (`expect` で握り潰す)。
    ///
    /// RFC 9114 Section 7.2.4.1 に従い、予約設定 (GREASE: `0x1f * N + 0x21`) を
    /// 最低 1 つ含める (SHOULD)。N=0 (`0x21`) を使用し、値は任意として 0 を設定する。
    pub fn from_settings(settings: &crate::settings::Settings) -> Self {
        let mut payload = Self::new();
        for setting in settings.iter() {
            payload
                .add(setting)
                .expect("Settings::iter() yields unique IDs");
        }
        if let Some(wt) = &settings.wt_settings {
            for setting in wt.iter() {
                payload
                    .add(setting)
                    .expect("webtransport::Settings::iter() yields unique IDs");
            }
        }
        // GREASE (RFC 9114 §7.2.4.1)
        let grease = crate::settings::Setting::from_wire(
            crate::varint::VarInt::from_static(0x21),
            crate::varint::VarInt::ZERO,
        )
        .expect("GREASE ID 0x21 は HTTP/2 専用 ID でも予約 ID でもないため from_wire は常に成功する（失敗時は実装バグ）");
        payload.add(grease).expect(
            "GREASE 設定 0x21 は既知 ID 群と競合しないため add は常に成功する（失敗時は実装バグ）",
        );
        payload
    }
}

/// GOAWAY フレームペイロード (RFC 9114 Section 7.2.6)
///
/// `id` の意味は送受信ロールに依存する:
/// - サーバー送信 / クライアント受信: client-initiated bidirectional stream ID
///   (RFC 9000 Section 2.1 で 4 の倍数)
/// - クライアント送信 / サーバー受信: push ID (本実装はサーバープッシュ非対応のため 0 のみ)
///
/// 単調減少制約 (RFC 9114 Section 5.2) と 4 の倍数制約は接続状態依存のため
/// [`crate::Connection::send_goaway`] のランタイム検査で別途行う。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GoawayPayload {
    /// ストリーム ID またはプッシュ ID (RFC 9000 Section 16 の VarInt)
    id: VarInt,
}

impl GoawayPayload {
    /// 新しい GOAWAY ペイロードを作成
    pub const fn new(id: VarInt) -> Self {
        Self { id }
    }

    /// 静的値から GOAWAY ペイロードを構築する (const fn)
    ///
    /// `id` が VarInt 範囲外の場合はコンパイル時にエラーとなる。
    /// ランタイム値からは [`Self::new`] と [`VarInt::new`] を組み合わせて使う。
    ///
    /// VarInt 値域外 (`>= 2^62`) のリテラルはコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::GoawayPayload;
    /// const _BAD: GoawayPayload = GoawayPayload::from_static(1u64 << 62);
    /// ```
    pub const fn from_static(id: u64) -> Self {
        Self {
            id: VarInt::from_static(id),
        }
    }

    /// `id` を取得する
    pub const fn id(&self) -> VarInt {
        self.id
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_frame_frame_type() {
        let data = Frame::Data(DataPayload::new(vec![1, 2, 3]));
        assert_eq!(data.frame_type().get(), 0x00);

        let headers = Frame::Headers(HeadersPayload::new(vec![4, 5, 6]));
        assert_eq!(headers.frame_type().get(), 0x01);

        let settings = Frame::Settings(SettingsPayload::new());
        assert_eq!(settings.frame_type().get(), 0x04);

        let goaway = Frame::Goaway(GoawayPayload::from_static(100));
        assert_eq!(goaway.frame_type().get(), 0x07);

        let unknown = Frame::Unknown(
            UnknownFrame::new(VarInt::from_static(0x21), vec![])
                .expect("0x21 は RFC 9114 Section 7.2.8 の Reserved Frame Type"),
        );
        assert_eq!(unknown.frame_type().get(), 0x21);
    }

    #[test]
    fn test_unknown_frame_rejects_all_known_types() {
        // RFC 9114 Section 7.2 で定義された全 7 タイプは UnknownFrame に格納できない
        for id in [0x00u64, 0x01, 0x03, 0x04, 0x05, 0x07, 0x0d] {
            let frame_type = VarInt::from_static(id);
            let err = UnknownFrame::new(frame_type, vec![]).unwrap_err();
            assert_eq!(err, UnknownFrameError::KnownFrameType { frame_type });
        }
    }

    #[test]
    fn test_unknown_frame_rejects_all_http2_only_types() {
        // RFC 9114 Section 11.2.1 Table 2 で Reserved 登録された HTTP/2 専用 4 タイプ
        for id in [0x02u64, 0x06, 0x08, 0x09] {
            let frame_type = VarInt::from_static(id);
            let err = UnknownFrame::new(frame_type, vec![]).unwrap_err();
            assert_eq!(err, UnknownFrameError::Http2OnlyFrameType { frame_type });
        }
    }

    #[test]
    fn test_unknown_frame_accepts_reserved_frame_type() {
        // RFC 9114 Section 7.2.8 の Reserved Frame Type (0x1f * N + 0x21、N=0 で 0x21)
        let unknown = UnknownFrame::new(VarInt::from_static(0x21), vec![1, 2, 3])
            .expect("0x21 は既知タイプでも HTTP/2 専用でもない");
        assert_eq!(unknown.frame_type().get(), 0x21);
        assert_eq!(unknown.payload(), &[1, 2, 3][..]);
    }

    #[test]
    fn test_empty_settings_frame_roundtrip() {
        let frame = Frame::Settings(SettingsPayload::new());
        let mut buf = vec![0u8; encoded_frame_len(&frame).expect("test must succeed")];
        let encoded_len = encode_frame(&mut buf, &frame).expect("test must succeed");
        let (decoded, decoded_len) = decode_frame(&buf[..encoded_len]).expect("test must succeed");
        assert_eq!(encoded_len, decoded_len);
        if let Frame::Settings(settings) = decoded {
            assert!(settings.is_empty());
        } else {
            panic!("SETTINGS フレームであること");
        }
    }
}
