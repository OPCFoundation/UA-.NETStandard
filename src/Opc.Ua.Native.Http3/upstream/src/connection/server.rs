// Modified by OPC Foundation, 2026-10-16: expose receive budget observations; see the proof patch manifest.
//! HTTP/3 サーバー接続

use crate::error::Error;
use crate::event::Event;
use crate::qpack::Header;
use crate::settings::Settings;
use crate::varint::VarInt;

use super::{Connection, H3InitData, Role};

/// HTTP/3 サーバー接続
///
/// サーバー専用の API を提供。
#[derive(Debug)]
pub struct ServerConnection {
    inner: Connection,
}

impl ServerConnection {
    /// 新しいサーバー接続を作成
    pub fn new(settings: Settings) -> Self {
        Self {
            inner: Connection::new(Role::Server, settings),
        }
    }

    /// デフォルト設定でサーバー接続を作成
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

    /// レスポンスを送信
    ///
    /// 指定されたストリームにヘッダーを送信。
    ///
    /// `fin=true` の場合は送信方向クローズ (FIN) を設定する。FIN はデータが全て
    /// 消費された後に `get_stream_data` / `take_stream_data` を再度呼び出したときに
    /// 交付される (RFC 9114 Section 4.1)。
    ///
    /// WebTransport CONNECT に対する 2xx レスポンスでは、セッション確立時に
    /// バッファリング済みカプセルの検証に失敗した場合
    /// `Error::StreamError(ErrorCode::MessageError)` (H3_MESSAGE_ERROR) を返す。
    /// エラー時は encode 済みのレスポンスがストリームの送信バッファに残るため
    /// `take_stream_data` で取り出さず、必ず該当ストリームを H3_MESSAGE_ERROR で
    /// reset して破棄すること (draft-ietf-webtrans-http3-16 Section 6)。
    pub fn send_response(
        &mut self,
        stream_id: u64,
        headers: &[Header],
        fin: bool,
    ) -> Result<(), Error> {
        self.inner.send_response(stream_id, headers, fin)
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
    /// サーバーが自ら開いた WebTransport ストリームをセッションに関連付ける。
    /// 対象は下記の 2 種類:
    ///
    /// - **双方向 (bidi)**: ストリーム ID の下位 2 ビットが 0x01 (RFC 9000 Section 2.1)。
    ///   登録後は `feed_stream` に渡した受信データが WebTransport ストリームとして
    ///   処理され、`BidiStreamData` / `BidiStreamEnd` イベントが発火する
    ///   (draft-ietf-webtrans-http3-16 Section 4.3)
    /// - **単方向 (uni)**: ストリーム ID の下位 2 ビットが 0x03。ローカル uni は
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

    /// QUIC transport parameter に基づく WebTransport 前提条件を注入する
    ///
    /// WebTransport CONNECT 受信処理前に呼び出す必要がある。
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
    fn test_server_connection() {
        let mut server = ServerConnection::with_default_settings();
        server.set_control_stream_id(3).expect("test must succeed");

        // 制御ストリームの送信データを取得
        let (data, fin) = server.get_stream_data(3).expect("test must succeed");
        assert!(!data.is_empty());
        assert!(!fin);
    }

    #[test]
    fn test_server_receive_request() {
        let server = ServerConnection::with_default_settings();

        // テスト: サーバーがリクエストを受信できることを確認
        assert!(server.peer_settings().is_none()); // SETTINGS 未受信
    }

    #[test]
    fn test_server_connection_register_local_wt_stream() {
        // サーバーの公開 API からローカル開始 WT ストリームを登録できる
        let mut server = ServerConnection::with_default_settings();
        server.set_control_stream_id(3).expect("test must succeed");
        let mut session = WtSession::new();
        session.state = WtSessionState::Established;
        server.inner.wt_sessions.insert(0, session);

        // stream_id=1 は 1 番目の server-initiated bidi stream
        server
            .register_local_wt_stream(0, 1)
            .expect("test must succeed");
    }

    #[test]
    fn test_server_connection_wt_flow_control_capsules() {
        // フロー制御を宣言した設定でセッションを確立すると初期カプセルが生成される
        let mut server = ServerConnection::with_default_settings();
        server.set_control_stream_id(3).expect("test must succeed");

        let wt = crate::webtransport::Settings::new()
            .wt_initial_max_data(crate::VarInt::from_static(1024));
        let mut session = WtSession::new();
        session.state = WtSessionState::Established;
        session.flow_control_enabled = true;
        session.initialize_flow_control(&wt, true);
        server.inner.wt_sessions.insert(0, session);

        assert!(
            server.wt_session_flow_control_enabled(0),
            "フロー制御が有効になっていること"
        );
        assert!(
            !server.take_wt_pending_capsules(0).is_empty(),
            "初期フロー制御カプセルが取得できること"
        );

        // データ消費を通知してもパニックしない
        server.wt_data_consumed(0, 10);
    }
}
