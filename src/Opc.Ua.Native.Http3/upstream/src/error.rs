//! HTTP/3 エラー型
//!
//! RFC 9114 Section 8 で定義されるエラーコードと内部エラー型を提供。

use core::fmt;

use crate::settings::SettingError;
use crate::varint::VarInt;
// `Error::Settings(SettingError)` は dead code であったため削除した。
// SETTINGS 検査エラーは `FrameDecodeError::InvalidSetting(SettingError)` 経由で伝播する。

/// HTTP/3 エラーコード (RFC 9114 Section 8.1)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u64)]
pub enum ErrorCode {
    /// 正常終了 (0x100)
    ///
    /// 受信したエラーコードの `from_code` 変換網羅性のため維持する (本実装では生成しない)。
    NoError = 0x100,
    /// プロトコル全般のエラー (0x101)
    GeneralProtocolError = 0x101,
    /// 内部エラー (0x102)
    InternalError = 0x102,
    /// ストリーム作成エラー (0x103)
    StreamCreationError = 0x103,
    /// クローズドクリティカルストリーム (0x104)
    ClosedCriticalStream = 0x104,
    /// フレーム不正 (0x105)
    FrameUnexpected = 0x105,
    /// フレームエラー (0x106)
    FrameError = 0x106,
    /// 過剰な負荷 (0x107)
    ///
    /// 受信したエラーコードの `from_code` 変換網羅性のため維持する (本実装では生成しない)。
    ExcessiveLoad = 0x107,
    /// ID エラー (0x108)
    IdError = 0x108,
    /// 設定エラー (0x109)
    SettingsError = 0x109,
    /// リクエスト不完全 (0x10a)
    MissingSettings = 0x10a,
    /// リクエスト拒否 (0x10b)
    ///
    /// サーバーがリクエストを処理せずに拒否する場合に使用 (RFC 9114 Section 4.1.1)。
    /// このコードで拒否されたリクエストはクライアントが安全に再送できる。
    RequestRejected = 0x10b,
    /// リクエストキャンセル (0x10c)
    ///
    /// 受信したエラーコードの `from_code` 変換網羅性のため維持する (本実装では生成しない)。
    RequestCancelled = 0x10c,
    /// リクエスト不完全 (0x10d)
    ///
    /// 受信したエラーコードの `from_code` 変換網羅性のため維持する (本実装では生成しない)。
    RequestIncomplete = 0x10d,
    /// メッセージエラー (0x10e)
    MessageError = 0x10e,
    /// QPACK デコンプレッションエラー (0x200)
    QpackDecompressionFailed = 0x200,
    /// QPACK エンコーダーストリームエラー (0x201)
    QpackEncoderStreamError = 0x201,
    /// QPACK デコーダーストリームエラー (0x202)
    QpackDecoderStreamError = 0x202,
    /// HTTP Datagram エラー (0x33)
    ///
    /// RFC 9297 Section 5.2: Quarter Stream ID が不正な HTTP Datagram を
    /// 受信した場合に接続を閉じるためのエラーコード (Section 2.1)。
    H3DatagramError = 0x33,
}

impl ErrorCode {
    /// エラーコードから `ErrorCode` を作成
    pub fn from_code(code: u64) -> Option<Self> {
        match code {
            0x100 => Some(Self::NoError),
            0x101 => Some(Self::GeneralProtocolError),
            0x102 => Some(Self::InternalError),
            0x103 => Some(Self::StreamCreationError),
            0x104 => Some(Self::ClosedCriticalStream),
            0x105 => Some(Self::FrameUnexpected),
            0x106 => Some(Self::FrameError),
            0x107 => Some(Self::ExcessiveLoad),
            0x108 => Some(Self::IdError),
            0x109 => Some(Self::SettingsError),
            0x10a => Some(Self::MissingSettings),
            0x10b => Some(Self::RequestRejected),
            0x10c => Some(Self::RequestCancelled),
            0x10d => Some(Self::RequestIncomplete),
            0x10e => Some(Self::MessageError),
            0x200 => Some(Self::QpackDecompressionFailed),
            0x201 => Some(Self::QpackEncoderStreamError),
            0x202 => Some(Self::QpackDecoderStreamError),
            0x33 => Some(Self::H3DatagramError),
            _ => None,
        }
    }

    /// 数値コードを取得
    pub fn code(self) -> u64 {
        self as u64
    }
}

impl fmt::Display for ErrorCode {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::NoError => write!(f, "H3_NO_ERROR"),
            Self::GeneralProtocolError => write!(f, "H3_GENERAL_PROTOCOL_ERROR"),
            Self::InternalError => write!(f, "H3_INTERNAL_ERROR"),
            Self::StreamCreationError => write!(f, "H3_STREAM_CREATION_ERROR"),
            Self::ClosedCriticalStream => write!(f, "H3_CLOSED_CRITICAL_STREAM"),
            Self::FrameUnexpected => write!(f, "H3_FRAME_UNEXPECTED"),
            Self::FrameError => write!(f, "H3_FRAME_ERROR"),
            Self::ExcessiveLoad => write!(f, "H3_EXCESSIVE_LOAD"),
            Self::IdError => write!(f, "H3_ID_ERROR"),
            Self::SettingsError => write!(f, "H3_SETTINGS_ERROR"),
            Self::MissingSettings => write!(f, "H3_MISSING_SETTINGS"),
            Self::RequestRejected => write!(f, "H3_REQUEST_REJECTED"),
            Self::RequestCancelled => write!(f, "H3_REQUEST_CANCELLED"),
            Self::RequestIncomplete => write!(f, "H3_REQUEST_INCOMPLETE"),
            Self::MessageError => write!(f, "H3_MESSAGE_ERROR"),
            Self::QpackDecompressionFailed => write!(f, "QPACK_DECOMPRESSION_FAILED"),
            Self::QpackEncoderStreamError => write!(f, "QPACK_ENCODER_STREAM_ERROR"),
            Self::QpackDecoderStreamError => write!(f, "QPACK_DECODER_STREAM_ERROR"),
            Self::H3DatagramError => write!(f, "H3_DATAGRAM_ERROR"),
        }
    }
}

/// HTTP/3 エラー
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Error {
    /// 接続エラー (接続をクローズすべき)
    ConnectionError(ErrorCode),
    /// ストリームエラー (ストリームをリセットすべき)
    StreamError(ErrorCode),
    /// ストリームが見つからない
    StreamNotFound(u64),
    /// ストリームがクローズ済み
    StreamClosed(u64),
    /// フレームデコードエラー
    FrameDecode(FrameDecodeError),
    /// QPACK エラー
    Qpack(QpackError),
    /// WebTransport セッションが draining 状態
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.7)
    /// `WT_DRAIN_SESSION` 受信後、または GOAWAY 受信によりセッションが
    /// グレースフルシャットダウン中の場合に、新規ストリーム/データグラムの
    /// 送信を試みると返される (Section 4.7 の MAY に対し本実装は送信を拒否する)。
    WtSessionDraining(u64),
    /// WebTransport 前提条件未達
    ///
    /// WebTransport CONNECT を送信するために必要な SETTINGS / transport parameter /
    /// ドラフトバージョンの前提条件が揃っていない場合に返される。
    /// 呼び出し順序や設定不足の問題であり、接続自体のエラーではない。
    WtSetup(WtSetupError),
}

/// WebTransport 前提条件エラーの詳細
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum WtSetupError {
    /// peer の SETTINGS を未受信
    PeerSettingsNotReceived,
    /// peer が WebTransport を有効化していない
    WebTransportNotEnabled,
    /// peer のドラフトバージョンが不明
    UnknownDraftVersion,
    /// ENABLE_CONNECT_PROTOCOL が未設定 (draft-07/14/15 で必須)
    EnableConnectProtocolMissing,
    /// H3_DATAGRAM が未設定
    H3DatagramNotEnabled,
    /// QUIC transport parameter の検証が未完了
    TransportNotVerified,
    /// reset_stream_at transport parameter が未サポート (draft-14/15 で必須)
    ResetStreamAtNotSupported,
    /// :protocol がネゴシエートしたドラフトと不一致
    ProtocolMismatch,
}

impl fmt::Display for WtSetupError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::PeerSettingsNotReceived => write!(f, "peer SETTINGS not received"),
            Self::WebTransportNotEnabled => write!(f, "WebTransport not enabled by peer"),
            Self::UnknownDraftVersion => write!(f, "unknown WebTransport draft version"),
            Self::EnableConnectProtocolMissing => {
                write!(f, "ENABLE_CONNECT_PROTOCOL not set by peer")
            }
            Self::H3DatagramNotEnabled => write!(f, "H3_DATAGRAM not enabled by peer"),
            Self::TransportNotVerified => {
                write!(f, "QUIC transport parameters not verified")
            }
            Self::ResetStreamAtNotSupported => {
                write!(f, "reset_stream_at transport parameter not supported")
            }
            Self::ProtocolMismatch => {
                write!(f, ":protocol does not match negotiated draft version")
            }
        }
    }
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::ConnectionError(code) => write!(f, "connection error: {code}"),
            Self::StreamError(code) => write!(f, "stream error: {code}"),
            Self::StreamNotFound(id) => write!(f, "stream not found: {id}"),
            Self::StreamClosed(id) => write!(f, "stream closed: {id}"),
            Self::FrameDecode(e) => write!(f, "frame decode error: {e}"),
            Self::Qpack(e) => write!(f, "qpack error: {e}"),
            Self::WtSessionDraining(id) => write!(f, "webtransport session draining: {id}"),
            Self::WtSetup(e) => write!(f, "webtransport setup error: {e}"),
        }
    }
}

impl core::error::Error for Error {
    fn source(&self) -> Option<&(dyn core::error::Error + 'static)> {
        match self {
            Self::FrameDecode(e) => Some(e),
            Self::Qpack(e) => Some(e),
            _ => None,
        }
    }
}

/// フレームデコードエラー
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FrameDecodeError {
    /// バッファ不足
    BufferTooShort,
    /// 無効なフレーム長
    InvalidLength,
    /// SETTINGS パラメータの構築時検査でエラーが発生した
    ///
    /// 内部に [`SettingError`] を保持し、ID / 値 / 重複の詳細を伝える
    /// (RFC 9114 §7.2.4 / §7.2.4.1)。
    InvalidSetting(SettingError),
    /// HTTP/2 専用フレームの検出
    Http2Frame(VarInt),
    /// サーバープッシュはサポートしない (CANCEL_PUSH, PUSH_PROMISE, MAX_PUSH_ID)
    ServerPushNotSupported(VarInt),
}

impl fmt::Display for FrameDecodeError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::BufferTooShort => write!(f, "buffer too short"),
            Self::InvalidLength => write!(f, "invalid frame length"),
            Self::InvalidSetting(e) => write!(f, "invalid settings parameter: {e}"),
            Self::Http2Frame(t) => write!(f, "http/2 frame not allowed: {:#x}", t.get()),
            Self::ServerPushNotSupported(t) => {
                write!(f, "server push not supported: {:#x}", t.get())
            }
        }
    }
}

impl core::error::Error for FrameDecodeError {
    fn source(&self) -> Option<&(dyn core::error::Error + 'static)> {
        match self {
            Self::InvalidSetting(e) => Some(e),
            _ => None,
        }
    }
}

impl From<SettingError> for FrameDecodeError {
    fn from(e: SettingError) -> Self {
        Self::InvalidSetting(e)
    }
}

/// QPACK エラー
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum QpackError {
    /// バッファ不足
    BufferTooShort,
    /// 無効なインデックス
    InvalidIndex(u64),
    /// 無効なハフマン符号
    InvalidHuffman,
    /// デコード失敗
    DecodeFailed,
    /// 動的テーブルが無効 (最大容量 0)
    ///
    /// RFC 9204 Section 3.2.3: 最大テーブル容量が 0 の間、
    /// エンコーダーは動的テーブルへの挿入も encoder stream 命令の送信も禁止される。
    DynamicTableDisabled,
    /// 容量が上限を超過
    ///
    /// RFC 9204 Section 3.2.3: エンコーダーは最大テーブル容量を超える
    /// 動的テーブル容量を設定してはならない。
    CapacityExceeded,
}

impl fmt::Display for QpackError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::BufferTooShort => write!(f, "buffer too short"),
            Self::InvalidIndex(idx) => write!(f, "invalid index: {idx}"),
            Self::InvalidHuffman => write!(f, "invalid huffman encoding"),
            Self::DecodeFailed => write!(f, "decode failed"),
            Self::DynamicTableDisabled => {
                write!(f, "dynamic table disabled (max capacity is zero)")
            }
            Self::CapacityExceeded => write!(f, "capacity exceeds maximum table capacity"),
        }
    }
}

impl core::error::Error for QpackError {}

impl From<FrameDecodeError> for Error {
    fn from(e: FrameDecodeError) -> Self {
        Self::FrameDecode(e)
    }
}

impl From<QpackError> for Error {
    fn from(e: QpackError) -> Self {
        Self::Qpack(e)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_error_code_from_code() {
        assert_eq!(ErrorCode::from_code(0x100), Some(ErrorCode::NoError));
        assert_eq!(
            ErrorCode::from_code(0x101),
            Some(ErrorCode::GeneralProtocolError)
        );
        assert_eq!(
            ErrorCode::from_code(0x200),
            Some(ErrorCode::QpackDecompressionFailed)
        );
        assert_eq!(
            ErrorCode::from_code(0x10b),
            Some(ErrorCode::RequestRejected)
        );
        assert_eq!(ErrorCode::from_code(0x999), None);
    }

    #[test]
    fn test_error_code_display() {
        assert_eq!(format!("{}", ErrorCode::NoError), "H3_NO_ERROR");
        assert_eq!(format!("{}", ErrorCode::FrameError), "H3_FRAME_ERROR");
        assert_eq!(
            format!("{}", ErrorCode::RequestRejected),
            "H3_REQUEST_REJECTED"
        );
    }

    #[test]
    fn test_error_display() {
        let e = Error::ConnectionError(ErrorCode::FrameError);
        assert_eq!(format!("{e}"), "connection error: H3_FRAME_ERROR");
    }
}
