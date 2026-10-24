// Modified by OPC Foundation, 2026-10-16: expose receive budget observations; see the proof patch manifest.
//! HTTP/3 クライアント接続

use crate::error::Error;
use crate::event::Event;
use crate::qpack::Header;
use crate::settings::Settings;
use crate::varint::VarInt;

use super::{Connection, H3InitData, Role};

/// HTTP/3 クライアント接続
///
/// クライアント専用の API を提供。
#[derive(Debug)]
pub struct ClientConnection {
    inner: Connection,
}

impl ClientConnection {
    /// 新しいクライアント接続を作成
    pub fn new(settings: Settings) -> Self {
        Self {
            inner: Connection::new(Role::Client, settings),
        }
    }

    /// デフォルト設定でクライアント接続を作成
    pub fn with_default_settings() -> Self {
        Self::new(Settings::default())
    }

    /// 制御ストリーム ID を設定
    ///
    /// 制御ストリームは 1 つのみ許可される (RFC 9114 Section 6.2.1)。
    pub fn set_control_stream_id(&mut self, stream_id: u64) -> Result<(), Error> {
        self.inner.set_control_stream_id(stream_id)
    }

    /// 制御ストリーム・QPACK encoder/decoder ストリームを一括で初期化する
    pub fn init_h3_streams(
        &mut self,
        control_stream_id: u64,
        encoder_stream_id: u64,
        decoder_stream_id: u64,
    ) -> Result<H3InitData, Error> {
        self.inner
            .init_h3_streams(control_stream_id, encoder_stream_id, decoder_stream_id)
    }

    /// QUIC からストリームデータを受信
    pub fn feed_stream(&mut self, stream_id: u64, data: &[u8], fin: bool) -> Result<(), Error> {
        self.inner.feed_stream(stream_id, data, fin)
    }

    /// イベントを取得
    pub fn poll_event(&mut self) -> Result<Option<Event>, Error> {
        self.inner.poll_event()
    }

    /// イベントキューの全イベントを取り出す
    ///
    /// キュー内の全イベントを `Vec` として返し、キューを空にする。
    pub fn drain_events(&mut self) -> Result<Vec<Event>, Error> {
        self.inner.drain_events()
    }

    /// 送信可能なストリームを取得
    pub fn writable_streams(&self) -> impl Iterator<Item = u64> + '_ {
        self.inner.writable_streams()
    }

    /// ストリームの送信データを取得
    ///
    /// FIN はデータが全て消費された後の追加呼び出しで `(空, fin=true)` として交付され、
    /// 交付後は取得できない (FIN は 1 回だけ交付される)。(RFC 9114 Section 4.1)
    pub fn get_stream_data(&mut self, stream_id: u64) -> Option<(&[u8], bool)> {
        self.inner.get_stream_data(stream_id)
    }

    /// ストリームデータを取得して内部バッファから消費する
    ///
    /// FIN を設定していないストリームでは送信バッファの全データが 1 回の呼び出しで返る。
    /// FIN を設定済みのストリームでは、データが全て返った後の追加呼び出しで
    /// `(空, fin=true)` が返り、FIN 交付後は `None` を返す (FIN は 1 回だけ交付される)。
    /// 送信方向クローズ (FIN) を QUIC 層へ渡すためにはデータ消費後にもう一度呼び出すこと
    /// (RFC 9114 Section 4.1)。
    pub fn take_stream_data(&mut self, stream_id: u64) -> Option<(Vec<u8>, bool)> {
        self.inner.take_stream_data(stream_id)
    }

    /// ストリームの送信データを消費
    pub fn consume_stream_data(&mut self, stream_id: u64, len: usize) {
        self.inner.consume_stream_data(stream_id, len);
    }

    /// リクエストを送信
    ///
    /// 新しいストリームを作成し、ヘッダーを送信。
    /// ストリーム ID を返す。
    ///
    /// `fin=true` の場合は送信方向クローズ (FIN) を設定する。FIN はデータが全て
    /// 消費された後に `get_stream_data` / `take_stream_data` を再度呼び出したときに
    /// 交付される (RFC 9114 Section 4.1)。
    pub fn send_request(&mut self, headers: &[Header], fin: bool) -> Result<u64, Error> {
        self.inner.send_request(headers, fin)
    }

    /// ボディを送信
    ///
    /// `fin=true` の場合は送信方向クローズ (FIN) を設定する。FIN はデータが全て
    /// 消費された後に `get_stream_data` / `take_stream_data` を再度呼び出したときに
    /// 交付される (RFC 9114 Section 4.1)。
    pub fn send_body(&mut self, stream_id: u64, data: &[u8], fin: bool) -> Result<(), Error> {
        self.inner.send_body(stream_id, data, fin)
    }

    /// ピア設定を取得
    pub fn peer_settings(&self) -> Option<&Settings> {
        self.inner.peer_settings()
    }

    /// ローカル設定を取得
    pub fn local_settings(&self) -> &Settings {
        self.inner.local_settings()
    }

    /// GOAWAY を送信
    pub fn send_goaway(&mut self, id: VarInt) -> Result<(), Error> {
        self.inner.send_goaway(id)
    }

    /// ストリームをリセット (RESET_STREAM)
    pub fn stream_reset(
        &mut self,
        stream_id: u64,
        error_code: u64,
        final_size: u64,
    ) -> Result<(), Error> {
        self.inner.stream_reset(stream_id, error_code, final_size)
    }

    /// ストリームの送信停止を要求 (STOP_SENDING)
    pub fn stop_sending(&mut self, stream_id: u64, error_code: u64) -> Result<(), Error> {
        self.inner.stop_sending(stream_id, error_code)
    }

    /// QUIC DATAGRAM フレームのペイロードを受信
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.5)
    pub fn feed_datagram(&mut self, data: &[u8]) -> Result<(), Error> {
        self.inner.feed_datagram(data)
    }

    /// WebTransport データグラムを送信用にエンコードする
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.5)
    pub fn send_datagram(&self, session_id: u64, payload: &[u8]) -> Result<Vec<u8>, Error> {
        self.inner.send_datagram(session_id, payload)
    }

    /// ローカル開始の WebTransport ストリームを登録する
    ///
    /// クライアントが自ら開いた WebTransport ストリームをセッションに関連付ける。
    /// 対象は下記の 2 種類:
    ///
    /// - **双方向 (bidi)**: ストリーム ID の下位 2 ビットが 0x00 (RFC 9000 Section 2.1)。
    ///   登録後は `feed_stream` に渡した受信データが WebTransport ストリームとして
    ///   処理され、`BidiStreamData` / `BidiStreamEnd` イベントが発火する
    ///   (draft-ietf-webtrans-http3-16 Section 4.3)
    /// - **単方向 (uni)**: ストリーム ID の下位 2 ビットが 0x02。ローカル uni は
    ///   送信専用でピアからは STOP_SENDING のみ届く。登録することで
    ///   `WebTransportEvent::StreamStopSending` (セッション ID 付き) として通知される
    ///   (draft-ietf-webtrans-http3-16 Section 4.4)
    ///
    /// ストリームを開いた直後に呼び出すこと。
    pub fn register_local_wt_stream(
        &mut self,
        session_id: u64,
        stream_id: u64,
    ) -> Result<(), Error> {
        self.inner.register_local_wt_stream(session_id, stream_id)
    }

    /// 0-RTT 再開時の前回接続のピア WebTransport SETTINGS を注入する
    ///
    /// クライアントが 0-RTT 再開時に、前回接続でキャッシュしたピアの
    /// WebTransport SETTINGS を注入する。SETTINGS フレーム受信時に
    /// フロー制御値の減少を検出して H3_SETTINGS_ERROR で接続を閉じる。
    /// (draft-ietf-webtrans-http3-16 Section 3.2)
    pub fn set_previous_wt_settings(&mut self, settings: crate::webtransport::Settings) {
        self.inner.set_previous_wt_settings(settings);
    }

    /// QUIC transport parameter に基づく WebTransport 前提条件を注入する
    ///
    /// WebTransport CONNECT 送信前に呼び出す必要がある。
    /// (draft-ietf-webtrans-http3-15 Section 3.1)
    pub fn set_webtransport_transport_verified(
        &mut self,
        max_datagram_frame_size_nonzero: bool,
        reset_stream_at_supported: bool,
    ) -> Result<(), Error> {
        self.inner.set_webtransport_transport_verified(
            max_datagram_frame_size_nonzero,
            reset_stream_at_supported,
        )
    }

    /// WebTransport セッションのデータ消費を通知する
    ///
    /// (draft-ietf-webtrans-http3-15 Section 5.6)
    pub fn wt_data_consumed(&mut self, session_id: u64, bytes: u64) {
        self.inner.wt_data_consumed(session_id, bytes);
    }

    /// WebTransport セッションの送信待ちカプセルを取り出す
    ///
    /// 取り出したカプセルは DATA フレームに包んで CONNECT ストリームへ送信すること。
    /// (draft-ietf-webtrans-http3-15 Section 5.6)
    pub fn take_wt_pending_capsules(
        &mut self,
        session_id: u64,
    ) -> Vec<crate::webtransport::Capsule> {
        self.inner.take_wt_pending_capsules(session_id)
    }

    /// WebTransport セッションのフロー制御が有効かどうかを取得する
    pub fn wt_session_flow_control_enabled(&self, session_id: u64) -> bool {
        self.inner.wt_session_flow_control_enabled(session_id)
    }

    /// Allocated capsule capacity across all sessions.
    pub fn wt_buffered_capsule_bytes(&self) -> usize {
        self.inner.wt_buffered_capsule_bytes()
    }

    /// Allocated request framing/body capacity across all streams.
    pub fn buffered_request_bytes(&self) -> usize {
        self.inner.buffered_request_bytes()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::connection::wt_types::{WtSession, WtSessionState};

    #[test]
    fn test_client_connection() {
        let mut client = ClientConnection::with_default_settings();
        client.set_control_stream_id(2).expect("test must succeed");

        let headers = vec![
            Header::new(b":method", b"GET").expect("test must succeed"),
            Header::new(b":path", b"/").expect("test must succeed"),
            Header::new(b":scheme", b"https").expect("test must succeed"),
            Header::new(b":authority", b"example.com").expect("test must succeed"),
        ];

        let stream_id = client
            .send_request(&headers, true)
            .expect("test must succeed");
        assert_eq!(stream_id, 0);
    }

    #[test]
    fn test_client_connection_register_local_wt_stream() {
        // クライアントの公開 API からローカル開始 WT ストリームを登録できる
        let mut client = ClientConnection::with_default_settings();
        client.set_control_stream_id(2).expect("test must succeed");
        let mut session = WtSession::new();
        session.state = WtSessionState::Established;
        client.inner.wt_sessions.insert(0, session);

        // stream_id=4 は 2 番目の client-initiated bidi stream
        client
            .register_local_wt_stream(0, 4)
            .expect("test must succeed");
    }

    #[test]
    fn test_client_connection_wt_flow_control_capsules() {
        // フロー制御を宣言した設定でセッションを確立すると初期カプセルが生成される
        let mut client = ClientConnection::with_default_settings();
        client.set_control_stream_id(2).expect("test must succeed");

        let wt = crate::webtransport::Settings::new()
            .wt_initial_max_data(crate::VarInt::from_static(1024));
        let mut session = WtSession::new();
        session.state = WtSessionState::Established;
        session.flow_control_enabled = true;
        session.initialize_flow_control(&wt, true);
        client.inner.wt_sessions.insert(0, session);

        assert!(
            client.wt_session_flow_control_enabled(0),
            "フロー制御が有効になっていること"
        );
        assert!(
            !client.take_wt_pending_capsules(0).is_empty(),
            "初期フロー制御カプセルが取得できること"
        );
        assert!(
            client.take_wt_pending_capsules(0).is_empty(),
            "カプセルは取り出し後は空になること"
        );

        // データ消費を通知してもパニックしない
        client.wt_data_consumed(0, 10);
    }
}
