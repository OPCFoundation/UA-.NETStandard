//! WebTransport エラーコード (draft-ietf-webtrans-http3-15 Section 9.5)
//!
//! HTTP/3 エラーコードの WebTransport 拡張を定義。

/// WebTransport エラーコード
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u64)]
pub enum ErrorCode {
    /// バッファされたストリームが拒否された
    ///
    /// 関連するセッションがないため、WebTransport データストリームが拒否された。
    BufferedStreamRejected = 0x3994bd84,

    /// セッションが終了した
    ///
    /// 関連する WebTransport セッションがクローズされたため、
    /// WebTransport データストリームが中断された。
    /// CONNECT ストリームからの読み取りを終了することを示す場合にも使用。
    SessionGone = 0x170d7b68,

    /// フロー制御エラー
    ///
    /// フロー制御エラーが発生したため、WebTransport セッションが中断された。
    FlowControlError = 0x045d4487,

    /// ALPN ネゴシエーションエラー
    ///
    /// アプリケーションプロトコルネゴシエーションに失敗した。
    /// draft-ietf-webtrans-http3-15 Section 3.3, Section 9.5
    /// 将来のドラフトで変更される可能性がある
    AlpnError = 0x0817b3dd,

    /// WebTransport 前提条件未達
    ///
    /// WebTransport に必要な SETTINGS やトランスポートパラメータが揃っていない。
    /// クライアントが HTTP/3 接続を閉じる際に使用する。
    /// draft-ietf-webtrans-http3-15 Section 3.1, Section 9.5
    /// 将来のドラフトで変更される可能性がある
    ///
    /// `from_code` による受信エラーコード変換網羅性のため維持する
    /// (本実装では生成しない)。
    RequirementsNotMet = 0x212c0d48,
}

impl ErrorCode {
    /// エラーコードから `ErrorCode` を作成
    pub fn from_code(code: u64) -> Option<Self> {
        match code {
            0x3994bd84 => Some(Self::BufferedStreamRejected),
            0x170d7b68 => Some(Self::SessionGone),
            0x045d4487 => Some(Self::FlowControlError),
            0x0817b3dd => Some(Self::AlpnError),
            0x212c0d48 => Some(Self::RequirementsNotMet),
            _ => None,
        }
    }

    /// エラーコードの説明を返す
    pub fn description(self) -> &'static str {
        match self {
            Self::BufferedStreamRejected => {
                "WebTransport data stream rejected due to lack of associated session"
            }
            Self::SessionGone => {
                "WebTransport data stream aborted because the associated session has been closed"
            }
            Self::FlowControlError => {
                "WebTransport session aborted because a flow control error was encountered"
            }
            Self::AlpnError => "WebTransport ALPN negotiation failed",
            Self::RequirementsNotMet => {
                "WebTransport requirements not met: missing required SETTINGS or transport parameters"
            }
        }
    }
}

impl core::fmt::Display for ErrorCode {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        write!(f, "{}", self.description())
    }
}

/// WebTransport アプリケーションエラーコード変換
///
/// WebTransport アプリケーションエラーコード (0x00000000-0xffffffff) と
/// HTTP/3 エラーコード (0x52e4a40fa8db-0x52e5ac983162) 間の変換を行う。
///
/// 予約コードポイント (0x1f * N + 0x21) はスキップする。
pub struct ApplicationErrorCode;

impl ApplicationErrorCode {
    /// WebTransport アプリケーションエラーコード範囲の開始
    pub const FIRST: u64 = 0x52e4a40fa8db;

    /// WebTransport アプリケーションエラーコード範囲の終了
    pub const LAST: u64 = 0x52e5ac983162;

    /// WebTransport アプリケーションエラーコードを HTTP/3 エラーコードに変換
    ///
    /// # Arguments
    ///
    /// * `app_code` - WebTransport アプリケーションエラーコード (0-0xffffffff)
    ///
    /// # Returns
    ///
    /// HTTP/3 エラーコード
    pub fn to_http3_code(app_code: u32) -> u64 {
        let n = u64::from(app_code);
        // 予約コードポイントをスキップするため、0x1e ごとに 1 を加算
        Self::FIRST + n + n / 0x1e
    }

    /// HTTP/3 エラーコードを WebTransport アプリケーションエラーコードに変換
    ///
    /// # Arguments
    ///
    /// * `http3_code` - HTTP/3 エラーコード
    ///
    /// # Returns
    ///
    /// WebTransport アプリケーションエラーコード、または範囲外/予約コードの場合は `None`
    pub fn from_http3_code(http3_code: u64) -> Option<u32> {
        // 範囲チェック
        if !(Self::FIRST..=Self::LAST).contains(&http3_code) {
            return None;
        }

        // 予約コードポイントチェック (0x1f * N + 0x21)
        if http3_code.wrapping_sub(0x21).is_multiple_of(0x1f) {
            return None;
        }

        let shifted = http3_code - Self::FIRST;
        let app_code = shifted - shifted / 0x1f;

        // u32 範囲チェック
        u32::try_from(app_code).ok()
    }

    /// HTTP/3 エラーコードが WebTransport アプリケーションエラー範囲内かどうか
    pub fn is_application_error(http3_code: u64) -> bool {
        (Self::FIRST..=Self::LAST).contains(&http3_code)
    }
}

/// WebTransport エラー
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Error {
    /// プロトコルエラー
    Protocol(ErrorCode),
    /// アプリケーションエラー
    Application {
        /// エラーコード (0-0xffffffff)
        code: u32,
        /// エラーメッセージ (最大 1024 バイト)
        message: String,
    },
    /// 不明なエラーコード
    ///
    /// WT_APPLICATION_ERROR 範囲外のエラーコードによる RESET_STREAM/STOP_SENDING の場合、
    /// ストリームはリセットされたものとして扱い、アプリケーションエラーコードへの
    /// マッピングは行わない (draft-ietf-webtrans-http3-15 Section 4.4)。
    /// 将来のドラフトで変更される可能性がある
    Unknown(u64),
}

impl Error {
    /// プロトコルエラーを作成
    pub fn protocol(code: ErrorCode) -> Self {
        Self::Protocol(code)
    }

    /// アプリケーションエラーを作成
    pub fn application(code: u32, message: impl Into<String>) -> Self {
        let mut msg = message.into();
        // メッセージは最大 1024 バイト
        if msg.len() > 1024 {
            // UTF-8 境界で切り詰め
            let mut truncate_at = 1024;
            while truncate_at > 0 && !msg.is_char_boundary(truncate_at) {
                truncate_at -= 1;
            }
            msg.truncate(truncate_at);
        }
        Self::Application { code, message: msg }
    }

    /// HTTP/3 エラーコードから Error を作成
    pub fn from_http3_code(code: u64) -> Self {
        if let Some(error_code) = ErrorCode::from_code(code) {
            Self::Protocol(error_code)
        } else if let Some(app_code) = ApplicationErrorCode::from_http3_code(code) {
            Self::Application {
                code: app_code,
                message: String::new(),
            }
        } else {
            Self::Unknown(code)
        }
    }

    /// 範囲外エラーコードによるストリームリセットかどうか
    ///
    /// WT_APPLICATION_ERROR 範囲外のエラーコードで RESET_STREAM/STOP_SENDING を受信した場合に
    /// `true` を返す。この場合、アプリケーションには「エラーコードなしのストリームリセット」
    /// として通知する (draft-ietf-webtrans-http3-15 Section 4.4)。
    /// 将来のドラフトで変更される可能性がある
    pub fn is_out_of_range_reset(&self) -> bool {
        matches!(self, Self::Unknown(_))
    }

    /// HTTP/3 エラーコードに変換
    pub fn to_http3_code(&self) -> u64 {
        match self {
            Self::Protocol(code) => *code as u64,
            Self::Application { code, .. } => ApplicationErrorCode::to_http3_code(*code),
            Self::Unknown(code) => *code,
        }
    }
}

impl core::fmt::Display for Error {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            Self::Protocol(code) => write!(f, "webtransport protocol error: {code}"),
            Self::Application { code, message } => {
                write!(f, "webtransport application error: {code:#x}")?;
                if !message.is_empty() {
                    write!(f, " ({message})")?;
                }
                Ok(())
            }
            Self::Unknown(code) => write!(f, "webtransport unknown error: {code:#x}"),
        }
    }
}

impl core::error::Error for Error {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_error_code_from_code() {
        assert_eq!(
            ErrorCode::from_code(0x3994bd84),
            Some(ErrorCode::BufferedStreamRejected)
        );
        assert_eq!(
            ErrorCode::from_code(0x170d7b68),
            Some(ErrorCode::SessionGone)
        );
        assert_eq!(
            ErrorCode::from_code(0x045d4487),
            Some(ErrorCode::FlowControlError)
        );
        assert_eq!(ErrorCode::from_code(0x0817b3dd), Some(ErrorCode::AlpnError));
        assert_eq!(
            ErrorCode::from_code(0x212c0d48),
            Some(ErrorCode::RequirementsNotMet)
        );
        assert_eq!(ErrorCode::from_code(0x12345678), None);
    }

    #[test]
    fn test_application_error_code_reserved() {
        // 予約コードポイントはスキップされる
        for i in 0..10 {
            let reserved = 0x1f * i + 0x21;
            assert_eq!(ApplicationErrorCode::from_http3_code(reserved), None);
        }
    }

    #[test]
    fn test_application_error_code_range() {
        assert!(!ApplicationErrorCode::is_application_error(0));
        assert!(!ApplicationErrorCode::is_application_error(
            ApplicationErrorCode::FIRST - 1
        ));
        assert!(ApplicationErrorCode::is_application_error(
            ApplicationErrorCode::FIRST
        ));
        assert!(ApplicationErrorCode::is_application_error(
            ApplicationErrorCode::LAST
        ));
        assert!(!ApplicationErrorCode::is_application_error(
            ApplicationErrorCode::LAST + 1
        ));
    }

    #[test]
    fn test_error_from_http3_code() {
        // プロトコルエラー
        let err = Error::from_http3_code(0x3994bd84);
        assert_eq!(err, Error::Protocol(ErrorCode::BufferedStreamRejected));

        // アプリケーションエラー
        let err = Error::from_http3_code(ApplicationErrorCode::FIRST);
        assert!(matches!(err, Error::Application { code: 0, .. }));

        // 不明なエラー (範囲外リセット)
        let err = Error::from_http3_code(0x12345678);
        assert_eq!(err, Error::Unknown(0x12345678));
        assert!(err.is_out_of_range_reset());
    }

    #[test]
    fn test_is_out_of_range_reset() {
        // プロトコルエラーは範囲外リセットではない
        assert!(!Error::protocol(ErrorCode::SessionGone).is_out_of_range_reset());
        // アプリケーションエラーは範囲外リセットではない
        assert!(!Error::application(0, "").is_out_of_range_reset());
        // Unknown は範囲外リセット
        assert!(Error::Unknown(0x99).is_out_of_range_reset());
    }
}
