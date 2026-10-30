//! HTTP/3 イベント
//!
//! Connection から返されるイベントを定義。

use crate::varint::VarInt;

/// WebTransport セッション終了時にリセットすべきストリームの情報
///
/// (draft-ietf-webtrans-http3-15 Section 6 / Section 4.4 / Section 5.4)
///
/// `reliable_size` は draft-ietf-quic-reliable-stream-reset の `RESET_STREAM_AT`
/// に渡す reliable size。WebTransport データストリームの場合は stream header
/// (stream type / signal value + session_id varint) のバイト数以上である
/// 必要がある。`reset_stream_at` transport parameter がネゴシエートされていない
/// 経路 (draft-02/07) では呼び出し側が通常の `RESET_STREAM` にフォールバック
/// することを想定し、本フィールドは 0 でも構わない。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct WtStreamReset {
    /// QUIC ストリーム ID
    pub stream_id: u64,
    /// `RESET_STREAM_AT` の reliable size
    pub reliable_size: u64,
}

/// WebTransport イベント
///
/// WebTransport 関連のイベントを集約した enum。
/// `Event::WebTransport(WebTransportEvent)` として使用する。
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum WebTransportEvent {
    /// 双方向ストリーム開始
    /// (draft-ietf-webtrans-http3-15 Section 4.3)
    BidiStreamOpen {
        /// QUIC ストリーム ID
        stream_id: u64,
        /// WebTransport セッション ID
        session_id: u64,
    },
    /// 双方向ストリームデータ受信
    BidiStreamData {
        /// QUIC ストリーム ID
        stream_id: u64,
        /// データ
        data: Vec<u8>,
    },
    /// 双方向ストリーム終了 (FIN 受信)
    BidiStreamEnd {
        /// QUIC ストリーム ID
        stream_id: u64,
    },
    /// 単方向ストリーム開始
    /// (draft-ietf-webtrans-http3-15 Section 4.2)
    UniStreamOpen {
        /// QUIC ストリーム ID
        stream_id: u64,
        /// WebTransport セッション ID
        session_id: u64,
    },
    /// 単方向ストリームデータ受信
    UniStreamData {
        /// QUIC ストリーム ID
        stream_id: u64,
        /// データ
        data: Vec<u8>,
    },
    /// 単方向ストリーム終了 (FIN 受信)
    UniStreamEnd {
        /// QUIC ストリーム ID
        stream_id: u64,
    },
    /// セッション終了
    /// (draft-ietf-webtrans-http3-15 Section 6)
    ///
    /// セッションが終了した。`reset_streams` に含まれる全ストリームに対して
    /// `error_code` を使用して `RESET_STREAM_AT` (reliable_size を伴う) と
    /// STOP_SENDING を送信すること。`reset_stream_at` transport parameter が
    /// ネゴシエートされていない経路では通常の `RESET_STREAM` にフォールバックする。
    SessionClosed {
        /// セッション ID (CONNECT ストリーム ID)
        session_id: u64,
        /// リセットすべきストリーム情報の一覧 (stream_id と reliable_size)
        reset_streams: Vec<WtStreamReset>,
        /// RESET_STREAM / STOP_SENDING に使用するエラーコード
        /// (WT_SESSION_GONE / WT_ALPN_ERROR 等)
        error_code: u64,
        /// WT_CLOSE_SESSION カプセルのアプリケーションエラーコード
        /// (draft-ietf-webtrans-http3-15 Section 6)
        /// WT_CLOSE_SESSION なしの終了 (FIN / RESET_STREAM) の場合は 0
        close_error_code: u32,
        /// WT_CLOSE_SESSION カプセルのエラーメッセージ
        /// (draft-ietf-webtrans-http3-15 Section 6)
        /// WT_CLOSE_SESSION なしの終了の場合は空文字列
        close_message: String,
    },
    /// セッション確立
    /// (draft-ietf-webtrans-http3-15 Section 3)
    ///
    /// CONNECT ストリームに 200 OK が返された。
    /// バッファリングされていたストリーム/データグラムがあれば配送される。
    SessionEstablished {
        /// セッション ID (CONNECT ストリーム ID)
        session_id: u64,
        /// フロー制御が有効かどうか (Section 5.1)
        ///
        /// 両端が SETTINGS でフロー制御を宣言した場合に `true`。
        /// `true` の場合、接続層がストリーム数/データ量の超過を検知し、
        /// WT_MAX_STREAMS / WT_MAX_DATA カプセルの生成を行う。
        flow_control_enabled: bool,
    },
    /// セッション draining
    /// (draft-ietf-webtrans-http3-15 Section 4.7)
    ///
    /// WT_DRAIN_SESSION カプセルを受信した。
    /// Section 4.7 では MAY continue だが、本実装はアプリ層の早期終了を促すため
    /// 新規ストリームやデータグラムの送信を拒否する。
    /// セッションは即座に終了しないが、グレースフルシャットダウンを開始する。
    SessionDraining {
        /// セッション ID (CONNECT ストリーム ID)
        session_id: u64,
    },
    /// フロー制御カプセル受信
    /// (draft-ietf-webtrans-http3-15 Section 5.6)
    ///
    /// CONNECT ストリーム上でフロー制御カプセルを受信した。
    /// 上位層は `webtransport::Session::process_capsule` に渡すこと。
    Capsule {
        /// WebTransport セッション ID
        session_id: u64,
        /// 受信した Capsule
        capsule: crate::webtransport::Capsule,
    },
    /// データグラム受信
    /// (draft-ietf-webtrans-http3-15 Section 4.5)
    ///
    /// QUIC DATAGRAM フレームから WebTransport データグラムを受信した。
    Datagram {
        /// WebTransport セッション ID
        session_id: u64,
        /// データグラムペイロード
        payload: Vec<u8>,
    },
    /// データストリームのリセット受信
    /// (draft-ietf-webtrans-http3-15 Section 4.4)
    ///
    /// WebTransport セッションに属するデータストリームに対して RESET_STREAM を
    /// 受信した。アプリケーション層はセッション ID と application error code を
    /// 元にアプリへ通知すること。
    StreamReset {
        /// WebTransport セッション ID
        session_id: u64,
        /// QUIC ストリーム ID
        stream_id: u64,
        /// QUIC application error code
        error_code: u64,
        /// QUIC RESET_STREAM の final size (RFC 9000 Section 19.4)
        ///
        /// `reset_stream_at` transport parameter がネゴシエートされている場合、
        /// この値は stream header (stream type / signal value + session_id varint)
        /// のバイト数以上であることが期待される (draft-ietf-webtrans-http3-15
        /// Section 4.4)。
        final_size: u64,
    },
    /// データストリームへの STOP_SENDING 受信
    /// (draft-ietf-webtrans-http3-15 Section 4.4)
    StreamStopSending {
        /// WebTransport セッション ID
        session_id: u64,
        /// QUIC ストリーム ID
        stream_id: u64,
        /// QUIC application error code
        error_code: u64,
    },
    /// バッファリング拒否
    /// (draft-ietf-webtrans-http3-16 Section 4.6)
    ///
    /// 保留していた WebTransport ストリームを WT 経路で処理できないため、
    /// `error_code` を使用して RESET_STREAM / STOP_SENDING を送信すること。
    /// 発火要因:
    /// - バッファリング上限 (ストリーム数 / データ量) を超えた
    /// - セッションが終了済み (`WT_SESSION_GONE` エラーコードで発火する場合あり)
    /// - ピアの SETTINGS が WebTransport 非対応で、保留していた bidi ストリームを
    ///   WT 経路に流せない
    BufferedStreamRejected {
        /// 拒否されたストリーム ID
        stream_id: u64,
        /// RESET_STREAM / STOP_SENDING に使用するエラーコード (WT_BUFFERED_STREAM_REJECTED)
        error_code: u64,
    },
}

impl WebTransportEvent {
    /// ストリーム ID を取得 (存在する場合)
    pub fn stream_id(&self) -> Option<u64> {
        match self {
            Self::BidiStreamOpen { stream_id, .. }
            | Self::BidiStreamData { stream_id, .. }
            | Self::BidiStreamEnd { stream_id }
            | Self::UniStreamOpen { stream_id, .. }
            | Self::UniStreamData { stream_id, .. }
            | Self::UniStreamEnd { stream_id }
            | Self::StreamReset { stream_id, .. }
            | Self::StreamStopSending { stream_id, .. }
            | Self::BufferedStreamRejected { stream_id, .. } => Some(*stream_id),
            Self::SessionClosed { session_id, .. }
            | Self::SessionEstablished { session_id, .. }
            | Self::SessionDraining { session_id }
            | Self::Capsule { session_id, .. }
            | Self::Datagram { session_id, .. } => Some(*session_id),
        }
    }
}

/// HTTP/3 イベント
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Event {
    /// SETTINGS フレーム受信
    SettingsReceived {
        /// ピアから受信した H3 設定
        settings: crate::settings::Settings,
        /// ピアから受信した WebTransport 設定
        wt_settings: Option<crate::webtransport::settings::Settings>,
    },
    /// ヘッダー受信開始
    HeadersBegin {
        /// ストリーム ID
        stream_id: u64,
    },
    /// 個別ヘッダー受信
    Header {
        /// ストリーム ID
        stream_id: u64,
        /// ヘッダー名
        name: Vec<u8>,
        /// ヘッダー値
        value: Vec<u8>,
    },
    /// ヘッダー受信完了
    HeadersEnd {
        /// ストリーム ID
        stream_id: u64,
    },
    /// データ受信
    Data {
        /// ストリーム ID
        stream_id: u64,
        /// データ
        data: Vec<u8>,
    },
    /// ストリーム終了
    StreamEnd {
        /// ストリーム ID
        stream_id: u64,
    },
    /// ストリームリセット (RESET_STREAM 受信)
    StreamReset {
        /// ストリーム ID
        stream_id: u64,
        /// エラーコード
        error_code: u64,
    },
    /// 送信停止要求 (STOP_SENDING 受信)
    StopSending {
        /// ストリーム ID
        stream_id: u64,
        /// エラーコード
        error_code: u64,
    },
    /// GOAWAY 受信
    GoawayReceived {
        /// GOAWAY で指定された ID (RFC 9000 Section 16 の VarInt)
        ///
        /// ロール依存: クライアント受信なら request stream ID、
        /// サーバー受信なら push ID。
        id: VarInt,
    },
    /// WebTransport イベント
    WebTransport(WebTransportEvent),
}

impl Event {
    /// ストリーム ID を取得 (存在する場合)
    pub fn stream_id(&self) -> Option<u64> {
        match self {
            Self::HeadersBegin { stream_id }
            | Self::Header { stream_id, .. }
            | Self::HeadersEnd { stream_id }
            | Self::Data { stream_id, .. }
            | Self::StreamEnd { stream_id }
            | Self::StreamReset { stream_id, .. }
            | Self::StopSending { stream_id, .. } => Some(*stream_id),
            Self::WebTransport(wt) => wt.stream_id(),
            _ => None,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_event_stream_id() {
        let event = Event::HeadersBegin { stream_id: 4 };
        assert_eq!(event.stream_id(), Some(4));

        let event = Event::SettingsReceived {
            settings: crate::settings::Settings::new(),
            wt_settings: None,
        };
        assert_eq!(event.stream_id(), None);
    }

    #[test]
    fn test_webtransport_event_stream_id() {
        // stream_id フィールドを持つバリアント
        let wt = WebTransportEvent::BidiStreamOpen {
            stream_id: 10,
            session_id: 0,
        };
        assert_eq!(wt.stream_id(), Some(10));

        // session_id フィールドを持つバリアント
        let wt = WebTransportEvent::SessionEstablished {
            session_id: 20,
            flow_control_enabled: false,
        };
        assert_eq!(wt.stream_id(), Some(20));

        // Event::WebTransport 経由で取得
        let event = Event::WebTransport(WebTransportEvent::UniStreamEnd { stream_id: 30 });
        assert_eq!(event.stream_id(), Some(30));
    }
}
