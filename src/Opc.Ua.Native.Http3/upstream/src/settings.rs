//! HTTP/3 Settings (RFC 9114 Section 7.2.4)
//!
//! SETTINGS フレームで交換される設定パラメータを管理する。
//!
//! [`Setting`] は既知 SETTINGS パラメータの enum 表現で、各 variant が ID と
//! 型安全な値 ([`VarInt`] または `bool`) を保持する。wire 上の `(id, value)` ペアは
//! [`Setting::from_wire`] で検査つきに [`Setting`] へ変換され、
//! [`Setting::as_wire`] で逆方向に変換される。

use core::fmt;

use crate::limits::Limits;
use crate::varint::VarInt;
use crate::webtransport;

// 既知 SETTINGS パラメータの ID 定数 (RFC 9114 / RFC 9204 / RFC 8441 / RFC 9297,
// draft-ietf-webtrans-http3-02 / -07 / -14 / -15)
const ID_QPACK_MAX_TABLE_CAPACITY: u64 = 0x01;
const ID_MAX_FIELD_SECTION_SIZE: u64 = 0x06;
const ID_QPACK_BLOCKED_STREAMS: u64 = 0x07;
const ID_ENABLE_CONNECT_PROTOCOL: u64 = 0x08;
const ID_H3_DATAGRAM: u64 = 0x33;
const ID_WT_INITIAL_MAX_DATA: u64 = 0x2b61;
const ID_WT_INITIAL_MAX_STREAMS_UNI: u64 = 0x2b64;
const ID_WT_INITIAL_MAX_STREAMS_BIDI: u64 = 0x2b65;
const ID_ENABLE_WEBTRANSPORT_DRAFT02: u64 = 0x2b603742;
const ID_WT_MAX_SESSIONS_DRAFT14: u64 = 0x14e9cd29;
const ID_WT_ENABLED: u64 = 0x2c7cf000;
const ID_WEBTRANSPORT_MAX_SESSIONS_DRAFT07: u64 = 0xc671706a;

/// SETTINGS ID が HTTP/2 専用のものかどうかを判定する
///
/// RFC 9114 §7.2.4.1: 「Setting identifiers that were defined in [HTTP/2] where
/// there is no corresponding HTTP/3 setting have also been reserved
/// (Section 11.2.2). These reserved settings MUST NOT be sent, and their receipt
/// MUST be treated as a connection error of type H3_SETTINGS_ERROR」。
/// §11.2.2 Table 3 で 0x02 / 0x03 / 0x04 / 0x05 が Reserved として列挙される。
///
/// 0x00 も Table 3 で Reserved として列挙されるが §7.2.4.1 の MUST 文には
/// 含まれない (HTTP/2 由来ではない)。本実装は [`SettingError::ReservedId`] で
/// 別途検出する。
pub const fn is_http2_only_id(id: u64) -> bool {
    matches!(id, 0x02..=0x05)
}

/// 既知の SETTINGS パラメータ (RFC 9114 §7.2.4)
///
/// 各 variant が ID と型安全な値を保持する。wire 表現との変換は
/// [`Setting::from_wire`] / [`Setting::as_wire`] で行う。
///
/// `Unknown` variant は [`Setting::from_wire`] 経由でのみ生成され、内部の
/// [`UnknownSetting`] は private フィールドで HTTP/2 専用 / 予約 ID が
/// 混入できない不変条件を保証する。RFC 9114 §7.2.4 末尾「An implementation
/// MUST ignore any parameter with an identifier it does not understand」に対応。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Setting {
    /// QPACK 最大テーブル容量 (RFC 9204 §5, ID = 0x01)
    QpackMaxTableCapacity(VarInt),
    /// 最大ヘッダーセクションサイズ (RFC 9114 §7.2.4.2, ID = 0x06)
    MaxFieldSectionSize(VarInt),
    /// QPACK ブロックストリーム数 (RFC 9204 §5, ID = 0x07)
    QpackBlockedStreams(VarInt),
    /// CONNECT プロトコル有効化 (RFC 8441 §3, RFC 9220 §3, ID = 0x08)
    EnableConnectProtocol(bool),
    /// H3 Datagram 有効化 (RFC 9297 §2.1.1, ID = 0x33)
    H3Datagram(bool),

    /// SETTINGS_WT_ENABLED (draft-ietf-webtrans-http3-15 §3.1, §9.2, ID = 0x2c7cf000)
    ///
    /// 将来のドラフトで変更される可能性がある。
    WtEnabled(VarInt),
    /// SETTINGS_WT_MAX_SESSIONS (draft-ietf-webtrans-http3-14 §9.2, ID = 0x14e9cd29)
    ///
    /// 将来のドラフトで変更される可能性がある。
    WtMaxSessionsDraft14(VarInt),
    /// SETTINGS_ENABLE_WEBTRANSPORT (draft-ietf-webtrans-http3-02 §3.1, ID = 0x2b603742)
    ///
    /// 将来のドラフトで変更される可能性がある。
    EnableWebTransportDraft02(bool),
    /// SETTINGS_WEBTRANSPORT_MAX_SESSIONS (draft-ietf-webtrans-http3-07 §3.2, ID = 0xc671706a)
    ///
    /// 将来のドラフトで変更される可能性がある。
    WebTransportMaxSessionsDraft07(VarInt),
    /// SETTINGS_WT_INITIAL_MAX_DATA (draft-ietf-webtrans-http3-14/15, ID = 0x2b61)
    ///
    /// 将来のドラフトで変更される可能性がある。
    WtInitialMaxData(VarInt),
    /// SETTINGS_WT_INITIAL_MAX_STREAMS_UNI (draft-ietf-webtrans-http3-14/15, ID = 0x2b64)
    ///
    /// 将来のドラフトで変更される可能性がある。
    WtInitialMaxStreamsUni(VarInt),
    /// SETTINGS_WT_INITIAL_MAX_STREAMS_BIDI (draft-ietf-webtrans-http3-14/15, ID = 0x2b65)
    ///
    /// 将来のドラフトで変更される可能性がある。
    WtInitialMaxStreamsBidi(VarInt),

    /// 未知の SETTINGS パラメータ
    ///
    /// 内部の [`UnknownSetting`] は private フィールドで構築経路を
    /// [`Setting::from_wire`] 経由に限定し、HTTP/2 専用 / 予約 ID が
    /// 混入できない不変条件を維持する。
    Unknown(UnknownSetting),
}

/// 未知 SETTINGS パラメータの ID と値
///
/// フィールドは private で、構築は [`Setting::from_wire`] (HTTP/2 専用 / 予約 ID
/// チェック後) を経由する以外の手段は存在しない。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct UnknownSetting {
    id: VarInt,
    value: VarInt,
}

impl UnknownSetting {
    /// crate 内部で `Setting::from_wire` から呼び出すコンストラクタ
    ///
    /// 呼び出し時点で `id` が HTTP/2 専用 / 予約済みでないことを保証する責務は
    /// 呼び出し側 ([`Setting::from_wire`] の検査済みパス) が持つ。
    pub(crate) const fn new(id: VarInt, value: VarInt) -> Self {
        Self { id, value }
    }

    /// 未知パラメータの ID を取得する
    pub fn id(self) -> VarInt {
        self.id
    }

    /// 未知パラメータの値を取得する
    pub fn value(self) -> VarInt {
        self.value
    }
}

/// Setting / SettingsPayload 構築時のエラー (RFC 9114 §7.2.4 / §7.2.4.1)
///
/// いずれのエラーも上位の SETTINGS フレームハンドラで H3_SETTINGS_ERROR に
/// 変換される。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SettingError {
    /// HTTP/2 専用の SETTINGS ID を受信した (0x02, 0x03, 0x04, 0x05)
    ///
    /// RFC 9114 §7.2.4.1 + §11.2.2 Table 3: H3_SETTINGS_ERROR。
    Http2OnlyId {
        /// 受信した ID
        id: VarInt,
    },

    /// 予約済み SETTINGS ID を受信した (0x00)
    ///
    /// RFC 9114 §11.2.2 Table 3 で 0x00 は Reserved として登録されている。
    /// §7.2.4.1 の MUST H3_SETTINGS_ERROR 規定は HTTP/2 由来の予約 ID
    /// (0x02-0x05) を直接の対象とするが、Reserved に分類された 0x00 を受信した
    /// 場合も接続クローズで処理するのが実装として妥当な選択。
    /// 注: 0x02-0x05 は [`SettingError::Http2OnlyId`] で別途検出する。
    ReservedId {
        /// 受信した ID
        id: VarInt,
    },

    /// bool 値の SETTINGS が 0/1 以外の値を持つ
    ///
    /// 対象 ID:
    /// - 0x08 (`SETTINGS_ENABLE_CONNECT_PROTOCOL`, RFC 8441 §3 / RFC 9220 §3)
    /// - 0x33 (`SETTINGS_H3_DATAGRAM`, RFC 9297 §2.1.1)
    /// - 0x2b603742 (`SETTINGS_ENABLE_WEBTRANSPORT`, draft-ietf-webtrans-http3-02 §3.1)
    InvalidBooleanValue {
        /// 対象 ID
        id: VarInt,
        /// 受信した値
        value: VarInt,
    },

    /// 同一 SETTINGS フレーム内に同じ ID が複数含まれる
    ///
    /// RFC 9114 §7.2.4「The same setting identifier MUST NOT occur more than
    /// once in the SETTINGS frame」(送信側 MUST NOT) / 受信側は MAY treat
    /// as H3_SETTINGS_ERROR。本実装は常に H3_SETTINGS_ERROR として扱う。
    DuplicateId {
        /// 重複した ID
        id: VarInt,
    },
}

impl fmt::Display for SettingError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Http2OnlyId { id } => {
                write!(f, "http/2 only settings id received: {:#x}", id.get())
            }
            Self::ReservedId { id } => {
                write!(f, "reserved settings id received: {:#x}", id.get())
            }
            Self::InvalidBooleanValue { id, value } => write!(
                f,
                "boolean settings {:#x} has invalid value {}",
                id.get(),
                value.get()
            ),
            Self::DuplicateId { id } => {
                write!(f, "duplicate settings id: {:#x}", id.get())
            }
        }
    }
}

impl core::error::Error for SettingError {}

impl Setting {
    /// wire 上の `(id, value)` ペアから [`Setting`] を構築する
    ///
    /// 既知パラメータは対応する variant に変換され、値の範囲外なら
    /// [`SettingError`] を返す。HTTP/2 専用 ID (0x02-0x05) と予約済み ID (0x00) は
    /// 拒否する。未知の ID は [`Setting::Unknown`] として受理する
    /// (RFC 9114 §7.2.4.1: 未知パラメータは MUST ignore)。
    pub fn from_wire(id: VarInt, value: VarInt) -> Result<Self, SettingError> {
        let raw = id.get();
        if is_http2_only_id(raw) {
            return Err(SettingError::Http2OnlyId { id });
        }
        // 予約済み ID 0x00 を弾く (RFC 9114 §11.2.2 Table 3)。
        // 0x02-0x05 は Http2OnlyId で先に分岐済み。
        if raw == 0x00 {
            return Err(SettingError::ReservedId { id });
        }
        let setting = match raw {
            ID_QPACK_MAX_TABLE_CAPACITY => Self::QpackMaxTableCapacity(value),
            ID_MAX_FIELD_SECTION_SIZE => Self::MaxFieldSectionSize(value),
            ID_QPACK_BLOCKED_STREAMS => Self::QpackBlockedStreams(value),
            ID_ENABLE_CONNECT_PROTOCOL => Self::EnableConnectProtocol(check_bool(id, value)?),
            ID_H3_DATAGRAM => Self::H3Datagram(check_bool(id, value)?),
            ID_WT_ENABLED => Self::WtEnabled(value),
            ID_WT_MAX_SESSIONS_DRAFT14 => Self::WtMaxSessionsDraft14(value),
            ID_ENABLE_WEBTRANSPORT_DRAFT02 => {
                Self::EnableWebTransportDraft02(check_bool(id, value)?)
            }
            ID_WEBTRANSPORT_MAX_SESSIONS_DRAFT07 => Self::WebTransportMaxSessionsDraft07(value),
            ID_WT_INITIAL_MAX_DATA => Self::WtInitialMaxData(value),
            ID_WT_INITIAL_MAX_STREAMS_UNI => Self::WtInitialMaxStreamsUni(value),
            ID_WT_INITIAL_MAX_STREAMS_BIDI => Self::WtInitialMaxStreamsBidi(value),
            _ => Self::Unknown(UnknownSetting::new(id, value)),
        };
        Ok(setting)
    }

    /// wire 上の `(id, value)` ペアに変換する
    pub fn as_wire(self) -> (VarInt, VarInt) {
        let bool_v = |b: bool| {
            if b {
                VarInt::from_static(1)
            } else {
                VarInt::ZERO
            }
        };
        match self {
            Self::QpackMaxTableCapacity(v) => (VarInt::from_static(ID_QPACK_MAX_TABLE_CAPACITY), v),
            Self::MaxFieldSectionSize(v) => (VarInt::from_static(ID_MAX_FIELD_SECTION_SIZE), v),
            Self::QpackBlockedStreams(v) => (VarInt::from_static(ID_QPACK_BLOCKED_STREAMS), v),
            Self::EnableConnectProtocol(b) => {
                (VarInt::from_static(ID_ENABLE_CONNECT_PROTOCOL), bool_v(b))
            }
            Self::H3Datagram(b) => (VarInt::from_static(ID_H3_DATAGRAM), bool_v(b)),
            Self::WtEnabled(v) => (VarInt::from_static(ID_WT_ENABLED), v),
            Self::WtMaxSessionsDraft14(v) => (VarInt::from_static(ID_WT_MAX_SESSIONS_DRAFT14), v),
            Self::EnableWebTransportDraft02(b) => (
                VarInt::from_static(ID_ENABLE_WEBTRANSPORT_DRAFT02),
                bool_v(b),
            ),
            Self::WebTransportMaxSessionsDraft07(v) => {
                (VarInt::from_static(ID_WEBTRANSPORT_MAX_SESSIONS_DRAFT07), v)
            }
            Self::WtInitialMaxData(v) => (VarInt::from_static(ID_WT_INITIAL_MAX_DATA), v),
            Self::WtInitialMaxStreamsUni(v) => {
                (VarInt::from_static(ID_WT_INITIAL_MAX_STREAMS_UNI), v)
            }
            Self::WtInitialMaxStreamsBidi(v) => {
                (VarInt::from_static(ID_WT_INITIAL_MAX_STREAMS_BIDI), v)
            }
            Self::Unknown(u) => (u.id(), u.value()),
        }
    }

    /// この `Setting` の ID を返す
    pub fn id(self) -> VarInt {
        self.as_wire().0
    }
}

/// bool 値 SETTINGS の値検査
fn check_bool(id: VarInt, value: VarInt) -> Result<bool, SettingError> {
    match value.get() {
        0 => Ok(false),
        1 => Ok(true),
        _ => Err(SettingError::InvalidBooleanValue { id, value }),
    }
}

/// HTTP/3 設定 (型付きフィールドのコレクション)
///
/// `Settings` は SETTINGS フレームで送受信される設定値の論理表現。
/// wire 表現との変換は [`crate::frame::SettingsPayload`] を介する。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Settings {
    /// QPACK 最大テーブル容量
    pub qpack_max_table_capacity: Option<VarInt>,
    /// 最大ヘッダーセクションサイズ
    pub max_field_section_size: Option<VarInt>,
    /// QPACK ブロックストリーム数
    pub qpack_blocked_streams: Option<VarInt>,
    /// CONNECT プロトコル有効化
    pub enable_connect_protocol: Option<bool>,
    /// H3 Datagram 有効化
    pub h3_datagram: Option<bool>,
    /// WebTransport 設定
    pub wt_settings: Option<webtransport::Settings>,
}

impl Default for Settings {
    fn default() -> Self {
        Self::new()
    }
}

impl Settings {
    /// 新しい Settings を作成 (すべて None)
    pub const fn new() -> Self {
        Self {
            qpack_max_table_capacity: None,
            max_field_section_size: None,
            qpack_blocked_streams: None,
            enable_connect_protocol: None,
            h3_datagram: None,
            wt_settings: None,
        }
    }

    /// Limits から Settings を作成
    ///
    /// `Limits` のフィールドは `u64` だが、SETTINGS で送出する値域は
    /// VarInt (RFC 9000 §16) に制約される。`Limits` 側の値域が VarInt 範囲外なら
    /// [`crate::varint::VarIntError::OutOfRange`] を返し panic させない。
    pub fn from_limits(limits: &Limits) -> Result<Self, crate::varint::VarIntError> {
        Ok(Self {
            qpack_max_table_capacity: Some(VarInt::new(limits.qpack_max_table_capacity)?),
            max_field_section_size: Some(VarInt::new(limits.max_field_section_size)?),
            qpack_blocked_streams: Some(VarInt::new(limits.qpack_blocked_streams)?),
            enable_connect_protocol: None,
            h3_datagram: None,
            wt_settings: None,
        })
    }

    /// QPACK 最大テーブル容量を設定
    pub fn qpack_max_table_capacity(mut self, capacity: VarInt) -> Self {
        self.qpack_max_table_capacity = Some(capacity);
        self
    }

    /// 最大ヘッダーセクションサイズを設定
    pub fn max_field_section_size(mut self, size: VarInt) -> Self {
        self.max_field_section_size = Some(size);
        self
    }

    /// QPACK ブロックストリーム数を設定
    pub fn qpack_blocked_streams(mut self, streams: VarInt) -> Self {
        self.qpack_blocked_streams = Some(streams);
        self
    }

    /// CONNECT プロトコル有効化を設定
    pub fn enable_connect_protocol(mut self, enable: bool) -> Self {
        self.enable_connect_protocol = Some(enable);
        self
    }

    /// H3 Datagram 有効化を設定
    pub fn h3_datagram(mut self, enable: bool) -> Self {
        self.h3_datagram = Some(enable);
        self
    }

    /// WebTransport をサーバー側で有効にする
    ///
    /// 以下の設定を自動的に行う:
    /// - SETTINGS_ENABLE_CONNECT_PROTOCOL: 1 (サーバー送信項目)
    /// - SETTINGS_H3_DATAGRAM: 1 (サーバー送信項目)
    ///
    /// `SETTINGS_ENABLE_CONNECT_PROTOCOL` は CONNECT 拡張の受諾を広告する
    /// サーバー側の設定であり、クライアントが送る項目ではない
    /// (RFC 8441 / RFC 9220, draft-ietf-webtrans-http3-15 Section 3.1)。
    /// クライアント側では `enable_webtransport_client()` を使うこと。
    ///
    /// WebTransport 固有の設定 (ストリーム上限、データ上限、ドラフトバージョン等) は
    /// `webtransport::Settings` のビルダーメソッドで事前に構築して渡す。
    ///
    /// # バージョンネゴシエーション (draft-ietf-webtrans-http3-15 Section 7.1)
    ///
    /// - 各ドラフトバージョンは異なる SETTINGS_WT_ENABLED コードポイントを使用する
    /// - 複数バージョン対応時、各バージョンのコードポイントでそれぞれ送信する
    /// - サーバーはクライアントの SETTINGS を受信するまで WebTransport リクエストの処理を待つ
    ///
    /// 将来のドラフトで変更される可能性がある
    pub fn enable_webtransport_server(mut self, wt: webtransport::Settings) -> Self {
        self.enable_connect_protocol = Some(true);
        self.h3_datagram = Some(true);
        self.wt_settings = Some(wt);
        self
    }

    /// WebTransport をクライアント側で有効にする
    ///
    /// 以下の設定を自動的に行う:
    /// - SETTINGS_H3_DATAGRAM: 1 (クライアント送信項目)
    ///
    /// `SETTINGS_ENABLE_CONNECT_PROTOCOL` はサーバーが広告する設定であり、
    /// クライアントは送信しない
    /// (draft-ietf-webtrans-http3-15 Section 3.1 のクライアント送信項目リスト参照)。
    ///
    /// draft バージョンの場合は `SETTINGS_WT_ENABLED` もクライアントから送信する
    /// 必要がある (draft-ietf-webtrans-http3-15 Section 7.1 の MUST)。これは
    /// `wt` の中身で表現する。
    ///
    /// 将来のドラフトで変更される可能性がある
    pub fn enable_webtransport_client(mut self, wt: webtransport::Settings) -> Self {
        self.h3_datagram = Some(true);
        self.wt_settings = Some(wt);
        self
    }

    /// WebTransport が有効かどうか
    ///
    /// draft-02/07/14/15 のいずれかで有効になっていれば true
    pub fn is_webtransport_enabled(&self) -> bool {
        self.wt_settings.as_ref().is_some_and(|wt| wt.is_enabled())
    }

    /// WebTransport のドラフトパターンを返す
    ///
    /// `wt_settings` が `None` または WebTransport として解釈できない場合は `None`。
    pub fn webtransport_draft_pattern(&self) -> Option<webtransport::DraftVersion> {
        self.wt_settings
            .as_ref()
            .and_then(|wt| wt.detect_draft_pattern())
    }

    /// SettingsPayload から Settings を作成
    ///
    /// `SettingsPayload` 内の各 [`Setting`] は構築時に値検査済み (ID 重複なし、
    /// HTTP/2 専用 / 予約 ID / bool 値域外を弾き済み) のため、本関数は以下のみを
    /// 担当する:
    ///
    /// - 既知パラメータ (H3) を [`Settings`] のフィールドにマッピングする
    /// - WebTransport 関連の variant は [`webtransport::Settings::from_payload`] に委譲する
    /// - [`Setting::Unknown`] は MUST ignore (RFC 9114 §7.2.4 末尾) として無視する
    ///
    /// SETTINGS フレームレベルの重複 ID 検出は [`crate::frame::SettingsPayload::add`]
    /// 側で構築時に行うため、本関数は失敗しない。`Setting` enum に新 variant が
    /// 追加された場合は本 `match` がコンパイル時に網羅性チェックで検出する
    /// (ワイルドカードを使わず明示列挙する)。
    pub fn from_payload(payload: &crate::frame::SettingsPayload) -> Self {
        let mut settings = Self::new();

        for setting in payload.settings() {
            match *setting {
                Setting::QpackMaxTableCapacity(v) => {
                    settings.qpack_max_table_capacity = Some(v);
                }
                Setting::MaxFieldSectionSize(v) => {
                    settings.max_field_section_size = Some(v);
                }
                Setting::QpackBlockedStreams(v) => {
                    settings.qpack_blocked_streams = Some(v);
                }
                Setting::EnableConnectProtocol(b) => {
                    settings.enable_connect_protocol = Some(b);
                }
                Setting::H3Datagram(b) => {
                    settings.h3_datagram = Some(b);
                }
                // WebTransport variant は `webtransport::Settings::from_payload` で反映する。
                // ここでは H3 フィールドに反映しないことを明示する。
                Setting::WtEnabled(_)
                | Setting::WtMaxSessionsDraft14(_)
                | Setting::EnableWebTransportDraft02(_)
                | Setting::WebTransportMaxSessionsDraft07(_)
                | Setting::WtInitialMaxData(_)
                | Setting::WtInitialMaxStreamsUni(_)
                | Setting::WtInitialMaxStreamsBidi(_) => {}
                Setting::Unknown(_) => {}
            }
        }

        settings.wt_settings = webtransport::Settings::from_payload(payload.settings());
        settings
    }

    /// H3 設定エントリのイテレータを返す
    ///
    /// WebTransport 設定は含まない。WebTransport 設定は
    /// `wt_settings.iter()` で別途取得する。
    pub fn iter(&self) -> impl Iterator<Item = Setting> + '_ {
        let entries = [
            self.qpack_max_table_capacity
                .map(Setting::QpackMaxTableCapacity),
            self.max_field_section_size
                .map(Setting::MaxFieldSectionSize),
            self.qpack_blocked_streams.map(Setting::QpackBlockedStreams),
            self.enable_connect_protocol
                .map(Setting::EnableConnectProtocol),
            self.h3_datagram.map(Setting::H3Datagram),
        ];
        entries.into_iter().flatten()
    }

    /// 設定エントリの数を返す (WebTransport 設定を含む)
    pub fn len(&self) -> usize {
        let h3_count = self.iter().count();
        let wt_count = self
            .wt_settings
            .as_ref()
            .map(|wt| wt.iter().count())
            .unwrap_or(0);
        h3_count + wt_count
    }

    /// 設定が空かどうか
    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

// `From<SettingError> for crate::error::Error` は提供しない。
// SETTINGS フレーム由来の `SettingError` は decoder で `FrameDecodeError::InvalidSetting`
// に変換され、最終的に `Error::FrameDecode(FrameDecodeError::InvalidSetting(_))` で
// 伝播する。直接 SETTINGS 検証 API (`SettingsPayload::add` 等) を呼ぶ利用者は
// `Result<_, SettingError>` をそのまま受けて扱う。

#[cfg(test)]
mod tests {
    use super::*;

    fn v(n: u64) -> VarInt {
        VarInt::new(n).expect("test must succeed")
    }

    #[test]
    fn test_is_http2_only_id() {
        assert!(is_http2_only_id(0x02));
        assert!(is_http2_only_id(0x03));
        assert!(is_http2_only_id(0x04));
        assert!(is_http2_only_id(0x05));
        assert!(!is_http2_only_id(0x00));
        assert!(!is_http2_only_id(0x01));
        assert!(!is_http2_only_id(0x06));
    }

    #[test]
    fn test_setting_from_wire_known_ids() {
        assert_eq!(
            Setting::from_wire(v(0x01), v(4096)).expect("test must succeed"),
            Setting::QpackMaxTableCapacity(v(4096))
        );
        assert_eq!(
            Setting::from_wire(v(0x06), v(16384)).expect("test must succeed"),
            Setting::MaxFieldSectionSize(v(16384))
        );
        assert_eq!(
            Setting::from_wire(v(0x07), v(50)).expect("test must succeed"),
            Setting::QpackBlockedStreams(v(50))
        );
        assert_eq!(
            Setting::from_wire(v(0x08), v(1)).expect("test must succeed"),
            Setting::EnableConnectProtocol(true)
        );
        assert_eq!(
            Setting::from_wire(v(0x33), v(0)).expect("test must succeed"),
            Setting::H3Datagram(false)
        );
    }

    #[test]
    fn test_setting_from_wire_http2_only_rejected() {
        for id in [0x02u64, 0x03, 0x04, 0x05] {
            let err = Setting::from_wire(v(id), v(0)).unwrap_err();
            assert_eq!(err, SettingError::Http2OnlyId { id: v(id) });
        }
    }

    #[test]
    fn test_setting_from_wire_reserved_id_rejected() {
        let err = Setting::from_wire(v(0x00), v(0)).unwrap_err();
        assert_eq!(err, SettingError::ReservedId { id: v(0x00) });
    }

    #[test]
    fn test_setting_from_wire_invalid_boolean() {
        let err = Setting::from_wire(v(0x08), v(2)).unwrap_err();
        assert_eq!(
            err,
            SettingError::InvalidBooleanValue {
                id: v(0x08),
                value: v(2)
            }
        );
        let err = Setting::from_wire(v(0x33), v(u64::from(u8::MAX))).unwrap_err();
        assert_eq!(
            err,
            SettingError::InvalidBooleanValue {
                id: v(0x33),
                value: v(u64::from(u8::MAX))
            }
        );
    }

    #[test]
    fn test_setting_from_wire_unknown_id() {
        let s = Setting::from_wire(v(0x99), v(42)).expect("test must succeed");
        let Setting::Unknown(u) = s else {
            panic!("expected Unknown");
        };
        assert_eq!(u.id(), v(0x99));
        assert_eq!(u.value(), v(42));
    }

    #[test]
    fn test_setting_from_wire_wt_ids() {
        assert_eq!(
            Setting::from_wire(v(0x2c7cf000), v(1)).expect("test must succeed"),
            Setting::WtEnabled(v(1))
        );
        assert_eq!(
            Setting::from_wire(v(0x14e9cd29), v(1)).expect("test must succeed"),
            Setting::WtMaxSessionsDraft14(v(1))
        );
        assert_eq!(
            Setting::from_wire(v(0x2b603742), v(1)).expect("test must succeed"),
            Setting::EnableWebTransportDraft02(true)
        );
        assert_eq!(
            Setting::from_wire(v(0xc671706a), v(3)).expect("test must succeed"),
            Setting::WebTransportMaxSessionsDraft07(v(3))
        );
        assert_eq!(
            Setting::from_wire(v(0x2b61), v(1024)).expect("test must succeed"),
            Setting::WtInitialMaxData(v(1024))
        );
        assert_eq!(
            Setting::from_wire(v(0x2b64), v(100)).expect("test must succeed"),
            Setting::WtInitialMaxStreamsUni(v(100))
        );
        assert_eq!(
            Setting::from_wire(v(0x2b65), v(50)).expect("test must succeed"),
            Setting::WtInitialMaxStreamsBidi(v(50))
        );
    }

    #[test]
    fn test_setting_as_wire_roundtrip() {
        let cases = [
            Setting::QpackMaxTableCapacity(v(4096)),
            Setting::MaxFieldSectionSize(v(16384)),
            Setting::QpackBlockedStreams(v(100)),
            Setting::EnableConnectProtocol(true),
            Setting::EnableConnectProtocol(false),
            Setting::H3Datagram(true),
            Setting::H3Datagram(false),
            Setting::WtEnabled(v(1)),
            Setting::WtMaxSessionsDraft14(v(7)),
            Setting::EnableWebTransportDraft02(true),
            Setting::WebTransportMaxSessionsDraft07(v(3)),
            Setting::WtInitialMaxData(v(1024)),
            Setting::WtInitialMaxStreamsUni(v(100)),
            Setting::WtInitialMaxStreamsBidi(v(50)),
            Setting::from_wire(v(0xdead_beef), v(42)).expect("test must succeed"),
        ];
        for setting in cases {
            let (id, value) = setting.as_wire();
            let restored = Setting::from_wire(id, value).expect("test must succeed");
            assert_eq!(restored, setting);
        }
    }

    #[test]
    fn test_setting_id() {
        assert_eq!(Setting::QpackMaxTableCapacity(v(4096)).id(), v(0x01));
        assert_eq!(Setting::H3Datagram(true).id(), v(0x33));
        let unknown = Setting::from_wire(v(0xfeed), v(0)).expect("test must succeed");
        assert_eq!(unknown.id(), v(0xfeed));
    }

    #[test]
    fn test_setting_error_display() {
        let err = SettingError::Http2OnlyId { id: v(0x02) };
        let s = format!("{err}");
        assert!(s.contains("0x2"));

        let err = SettingError::ReservedId { id: v(0x00) };
        let s = format!("{err}");
        assert!(s.contains("0x0"));

        let err = SettingError::InvalidBooleanValue {
            id: v(0x08),
            value: v(2),
        };
        let s = format!("{err}");
        assert!(s.contains("0x8"));
        assert!(s.contains('2'));
    }

    #[test]
    fn test_settings_default() {
        let settings = Settings::default();
        assert!(settings.is_empty());
    }

    #[test]
    fn test_settings_builder() {
        let settings = Settings::new()
            .qpack_max_table_capacity(v(4096))
            .max_field_section_size(v(16384))
            .qpack_blocked_streams(v(100))
            .enable_connect_protocol(true)
            .h3_datagram(false);

        assert_eq!(settings.qpack_max_table_capacity, Some(v(4096)));
        assert_eq!(settings.max_field_section_size, Some(v(16384)));
        assert_eq!(settings.qpack_blocked_streams, Some(v(100)));
        assert_eq!(settings.enable_connect_protocol, Some(true));
        assert_eq!(settings.h3_datagram, Some(false));
        assert_eq!(settings.len(), 5);
    }

    #[test]
    fn test_settings_from_limits() {
        let limits = Limits::new()
            .qpack_max_table_capacity(4096)
            .max_field_section_size(32768)
            .qpack_blocked_streams(50);

        let settings = Settings::from_limits(&limits).expect("test must succeed");
        assert_eq!(settings.qpack_max_table_capacity, Some(v(4096)));
        assert_eq!(settings.max_field_section_size, Some(v(32768)));
        assert_eq!(settings.qpack_blocked_streams, Some(v(50)));
    }

    #[test]
    fn test_settings_iter() {
        let settings = Settings::new()
            .qpack_max_table_capacity(v(4096))
            .max_field_section_size(v(16384));

        let entries: Vec<_> = settings.iter().collect();
        assert_eq!(entries.len(), 2);
        assert!(entries.contains(&Setting::QpackMaxTableCapacity(v(4096))));
        assert!(entries.contains(&Setting::MaxFieldSectionSize(v(16384))));
    }

    #[test]
    fn test_enable_webtransport() {
        let wt = webtransport::Settings::new()
            .wt_enabled(v(1))
            .enable_webtransport_draft02(true)
            .webtransport_max_sessions_draft07(v(1))
            .wt_initial_max_streams_bidi(v(100))
            .wt_initial_max_streams_uni(v(100))
            .wt_initial_max_data(v(1_048_576));

        let settings = Settings::new().enable_webtransport_server(wt);

        assert_eq!(settings.enable_connect_protocol, Some(true));
        assert_eq!(settings.h3_datagram, Some(true));
        assert!(settings.is_webtransport_enabled());

        let wt = settings.wt_settings.expect("test must succeed");
        assert_eq!(wt.wt_enabled, v(1));
        assert_eq!(wt.enable_webtransport_draft02, Some(true));
        assert_eq!(wt.webtransport_max_sessions_draft07, Some(v(1)));
        assert_eq!(wt.wt_initial_max_streams_bidi, v(100));
        assert_eq!(wt.wt_initial_max_streams_uni, v(100));
        assert_eq!(wt.wt_initial_max_data, v(1_048_576));
    }

    #[test]
    fn test_is_webtransport_enabled() {
        let settings = Settings::new();
        assert!(!settings.is_webtransport_enabled());

        let wt = webtransport::Settings::new().wt_enabled(v(1));
        let settings = Settings::new().enable_webtransport_server(wt);
        assert!(settings.is_webtransport_enabled());
    }

    #[test]
    fn test_len_includes_wt_settings() {
        let wt = webtransport::Settings::new()
            .wt_enabled(v(1))
            .wt_initial_max_streams_bidi(v(100));

        let settings = Settings::new()
            .qpack_max_table_capacity(v(4096))
            .enable_webtransport_server(wt);

        // H3: qpack_max_table_capacity, enable_connect_protocol, h3_datagram = 3
        // WT: wt_enabled, wt_initial_max_streams_bidi = 2
        assert_eq!(settings.len(), 5);
    }

    #[test]
    fn test_add_duplicate_rejected_at_payload() {
        // 重複 ID 検出は SettingsPayload::add の責務 (構築時)
        use crate::frame::SettingsPayload;
        let mut payload = SettingsPayload::new();
        payload
            .add(Setting::QpackMaxTableCapacity(v(4096)))
            .expect("test must succeed");
        let err = payload
            .add(Setting::QpackMaxTableCapacity(v(8192)))
            .unwrap_err();
        assert_eq!(err, SettingError::DuplicateId { id: v(0x01) });
    }

    #[test]
    fn test_from_payload_ignores_unknown() {
        use crate::frame::SettingsPayload;

        // Setting::Unknown は from_wire 経由でしか作れないため、wire からの構築をシミュレート
        let unknown = Setting::from_wire(v(0xdead), v(1)).expect("test must succeed");
        let mut payload = SettingsPayload::new();
        payload.add(unknown).expect("test must succeed");
        payload
            .add(Setting::H3Datagram(true))
            .expect("test must succeed");
        let settings = Settings::from_payload(&payload);
        assert_eq!(settings.h3_datagram, Some(true));
        assert!(settings.qpack_max_table_capacity.is_none());
    }

    #[test]
    fn test_setting_error_duplicate_id_display() {
        let err = SettingError::DuplicateId { id: v(0x01) };
        let s = format!("{err}");
        assert!(s.contains("duplicate"));
        assert!(s.contains("0x1"));
    }

    #[test]
    fn test_from_limits_out_of_range_qpack_max_table_capacity() {
        let mut limits = Limits::new();
        limits.qpack_max_table_capacity = 1u64 << 62;
        let err = Settings::from_limits(&limits).unwrap_err();
        assert!(matches!(err, crate::varint::VarIntError::OutOfRange { .. }));
    }

    #[test]
    fn test_from_limits_out_of_range_max_field_section_size() {
        let mut limits = Limits::new();
        limits.max_field_section_size = 1u64 << 62;
        let err = Settings::from_limits(&limits).unwrap_err();
        assert!(matches!(err, crate::varint::VarIntError::OutOfRange { .. }));
    }

    #[test]
    fn test_from_limits_out_of_range_qpack_blocked_streams() {
        let mut limits = Limits::new();
        limits.qpack_blocked_streams = 1u64 << 62;
        let err = Settings::from_limits(&limits).unwrap_err();
        assert!(matches!(err, crate::varint::VarIntError::OutOfRange { .. }));
    }
}
