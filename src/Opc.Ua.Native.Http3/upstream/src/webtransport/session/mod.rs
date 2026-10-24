//! WebTransport セッション (draft-ietf-webtrans-http3-15 Section 3, 6)
//!
//! WebTransport セッションの状態管理を提供。
//! フロー制御型は `flow_control.rs` で管理する。

pub(crate) mod flow_control;

pub(crate) use flow_control::{DataFlowControl, DirectionalStreamFlowControl, SendBlockedState};
pub use flow_control::{FlowControlLimits, FlowControlState};

use std::collections::HashMap;

use super::capsule::{Capsule, MAX_STREAMS_LIMIT};
use super::error::{Error, ErrorCode};
use super::stream::Stream;

/// セッション確立前にバッファリングするストリームの上限 (draft-ietf-webtrans-http3-15 Section 4.6)
pub(crate) const MAX_BUFFERED_STREAMS: usize = 100;

/// セッション確立前にバッファリングするデータグラムの上限 (draft-ietf-webtrans-http3-15 Section 4.6)
pub(crate) const MAX_BUFFERED_DATAGRAMS: usize = 100;

/// バッファリングされたストリームの情報 (draft-ietf-webtrans-http3-15 Section 4.6)
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BufferedStream {
    /// ストリーム ID
    pub stream_id: u64,
    /// 双方向ストリームかどうか
    pub is_bidirectional: bool,
}

/// セッション状態
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum SessionState {
    /// 接続待ち (CONNECT リクエスト送信前)
    #[default]
    Pending,
    /// 確立中 (CONNECT リクエスト送信済み、レスポンス待ち)
    Connecting,
    /// 確立済み
    Established,
    /// ドレイン中 (グレースフルシャットダウン)
    Draining,
    /// クローズ済み
    Closed,
}

impl SessionState {
    /// ストリームを作成可能かどうか
    pub fn can_create_stream(self) -> bool {
        matches!(self, Self::Established | Self::Draining)
    }

    /// データを送信可能かどうか
    pub fn can_send(self) -> bool {
        matches!(self, Self::Established | Self::Draining)
    }

    /// データを受信可能かどうか
    pub fn can_receive(self) -> bool {
        matches!(self, Self::Established | Self::Draining)
    }
}

/// Capsule 処理エラー
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CapsuleProcessError {
    /// セッションレベルのエラー (WT_FLOW_CONTROL_ERROR 等)
    Session(Error),
}

/// WebTransport セッション
#[derive(Debug)]
pub struct Session {
    /// セッション ID (CONNECT ストリーム ID)
    session_id: u64,
    /// セッション状態
    state: SessionState,
    /// 関連ストリーム
    streams: HashMap<u64, Stream>,
    /// リモートからのフロー制御リミット
    remote_limits: FlowControlLimits,
    /// ローカルが設定したフロー制御リミット
    local_limits: FlowControlLimits,
    /// フロー制御状態
    flow_state: FlowControlState,
    /// フロー制御を有効として扱うかどうか (draft-ietf-webtrans-http3-15 Section 5.1)
    flow_control_enabled: bool,
    /// 送信待ち Capsule
    pending_capsules: Vec<Capsule>,
    /// セッション終了時に WT_SESSION_GONE でリセットすべきストリーム ID
    pending_stream_resets: Vec<u64>,
    /// クローズ理由
    close_error: Option<Error>,
    /// セッション確立前のバッファリングされたストリーム (draft-ietf-webtrans-http3-15 Section 4.6)
    buffered_streams: Vec<BufferedStream>,
    /// セッション確立前のバッファリングされたデータグラム (draft-ietf-webtrans-http3-15 Section 4.6)
    buffered_datagrams: Vec<Vec<u8>>,
    /// HTTP/3 GOAWAY を受信したかどうか (draft-ietf-webtrans-http3-15 Section 4.7)
    goaway_received: bool,
    /// WT_CLOSE_SESSION を受信したかどうか (draft-ietf-webtrans-http3-15 Section 6)
    close_session_received: bool,
    /// WT_CLOSE_SESSION を送信したかどうか (draft-ietf-webtrans-http3-15 Section 6)
    close_session_sent: bool,
    /// 受信側ストリームフロー制御 (単方向)
    recv_stream_fc_uni: DirectionalStreamFlowControl,
    /// 受信側ストリームフロー制御 (双方向)
    recv_stream_fc_bidi: DirectionalStreamFlowControl,
    /// 受信側データフロー制御
    recv_data_fc: DataFlowControl,
    /// 送信側ブロック状態追跡
    send_blocked: SendBlockedState,
}

impl Session {
    /// 新しいセッションを作成
    ///
    /// # Arguments
    ///
    /// * `session_id` - CONNECT ストリーム ID
    pub fn new(session_id: u64) -> Self {
        Self {
            session_id,
            state: SessionState::Pending,
            streams: HashMap::new(),
            remote_limits: FlowControlLimits::new(),
            local_limits: FlowControlLimits::new(),
            flow_state: FlowControlState::new(),
            flow_control_enabled: true,
            pending_capsules: Vec::new(),
            pending_stream_resets: Vec::new(),
            close_error: None,
            buffered_streams: Vec::new(),
            buffered_datagrams: Vec::new(),
            goaway_received: false,
            close_session_received: false,
            close_session_sent: false,
            recv_stream_fc_uni: DirectionalStreamFlowControl::new(0),
            recv_stream_fc_bidi: DirectionalStreamFlowControl::new(0),
            recv_data_fc: DataFlowControl::new(0),
            send_blocked: SendBlockedState::default(),
        }
    }

    /// セッション ID を取得
    pub fn session_id(&self) -> u64 {
        self.session_id
    }

    /// セッション状態を取得
    pub fn state(&self) -> SessionState {
        self.state
    }

    /// セッションが確立済みかどうか
    pub fn is_established(&self) -> bool {
        self.state == SessionState::Established
    }

    /// セッションがクローズ済みかどうか
    pub fn is_closed(&self) -> bool {
        self.state == SessionState::Closed
    }

    /// セッションを確立中に遷移
    pub fn set_connecting(&mut self) {
        if self.state == SessionState::Pending {
            self.state = SessionState::Connecting;
        }
    }

    /// セッションを確立済みに遷移
    pub fn set_established(&mut self) {
        if matches!(self.state, SessionState::Pending | SessionState::Connecting) {
            self.state = SessionState::Established;
        }
    }

    /// セッションをドレイン中に遷移
    pub fn set_draining(&mut self) {
        if self.state == SessionState::Established {
            self.state = SessionState::Draining;
        }
    }

    /// セッションをクローズ
    pub fn close(&mut self, error: Option<Error>) {
        if self.state != SessionState::Closed {
            self.pending_stream_resets = self.streams.keys().copied().collect();
        }
        self.state = SessionState::Closed;
        self.close_error = error;
    }

    /// クローズ理由を取得
    pub fn close_error(&self) -> Option<&Error> {
        self.close_error.as_ref()
    }

    /// ストリームを追加
    pub fn add_stream(&mut self, stream: Stream) {
        let _ = self.try_add_stream(stream);
    }

    /// ストリームを追加 (セッション終了後は拒否)
    pub fn try_add_stream(&mut self, stream: Stream) -> bool {
        if self.is_closed() {
            return false;
        }
        self.streams.insert(stream.stream_id(), stream);
        true
    }

    /// ストリームを取得
    pub fn get_stream(&self, stream_id: u64) -> Option<&Stream> {
        self.streams.get(&stream_id)
    }

    /// ストリームを可変参照で取得
    pub fn get_stream_mut(&mut self, stream_id: u64) -> Option<&mut Stream> {
        self.streams.get_mut(&stream_id)
    }

    /// ストリームを削除
    pub fn remove_stream(&mut self, stream_id: u64) -> Option<Stream> {
        self.streams.remove(&stream_id)
    }

    /// ストリーム数を取得
    pub fn stream_count(&self) -> usize {
        self.streams.len()
    }

    /// ストリームのイテレータを取得
    pub fn streams(&self) -> impl Iterator<Item = &Stream> {
        self.streams.values()
    }

    /// リモートからのフロー制御リミットを取得
    pub fn remote_limits(&self) -> &FlowControlLimits {
        &self.remote_limits
    }

    /// リモートからのフロー制御リミットを可変参照で取得
    pub fn remote_limits_mut(&mut self) -> &mut FlowControlLimits {
        &mut self.remote_limits
    }

    /// ローカルが設定したフロー制御リミットを取得
    pub fn local_limits(&self) -> &FlowControlLimits {
        &self.local_limits
    }

    /// フロー制御状態を取得
    pub fn flow_state(&self) -> &FlowControlState {
        &self.flow_state
    }

    /// フロー制御状態を可変参照で取得
    pub fn flow_state_mut(&mut self) -> &mut FlowControlState {
        &mut self.flow_state
    }

    /// フロー制御を有効として扱うかどうかを設定
    pub fn set_flow_control_enabled(&mut self, enabled: bool) {
        self.flow_control_enabled = enabled;
    }

    /// ローカルフロー制御の初期値を設定
    ///
    /// `local_limits` と受信側フロー制御を同時に初期化する。
    /// セッション確立前に 1 回だけ呼ぶこと。
    /// SETTINGS の `wt_initial_max_streams_uni` / `wt_initial_max_streams_bidi` /
    /// `wt_initial_max_data` の値を渡す。
    pub fn initialize_local_limits(&mut self, limits: FlowControlLimits) {
        self.local_limits = limits;
        self.recv_stream_fc_uni = DirectionalStreamFlowControl::new(limits.max_streams_uni);
        self.recv_stream_fc_bidi = DirectionalStreamFlowControl::new(limits.max_streams_bidi);
        self.recv_data_fc = DataFlowControl::new(limits.max_data);
    }

    /// 初期フロー制御カプセルをキューに追加する
    ///
    /// Safari (Network.framework) は draft-07 の SETTINGS で接続しつつ
    /// draft-14 のカプセルベースフロー制御を使う。セッション確立直後に
    /// WT_MAX_STREAMS_BIDI, WT_MAX_STREAMS_UNI, WT_MAX_DATA カプセルを
    /// 送信することで Safari との互換性を確保する。
    ///
    /// `DraftVersion::requires_initial_capsule_flow_control()` が `true` の場合に
    /// 呼び出すこと。
    ///
    /// このメソッドは `local_limits` と受信側フロー制御も同時に初期化する。
    ///
    /// draft-ietf-webtrans-http3-14 Section 5
    /// 将来のドラフトで変更される可能性がある
    pub fn queue_initial_flow_control_capsules(&mut self, limits: FlowControlLimits) {
        self.initialize_local_limits(limits);

        if limits.max_streams_bidi > 0 {
            self.pending_capsules.push(Capsule::MaxStreams {
                bidirectional: true,
                maximum: limits.max_streams_bidi,
            });
        }
        if limits.max_streams_uni > 0 {
            self.pending_capsules.push(Capsule::MaxStreams {
                bidirectional: false,
                maximum: limits.max_streams_uni,
            });
        }
        if limits.max_data > 0 {
            self.pending_capsules.push(Capsule::MaxData {
                maximum: limits.max_data,
            });
        }
    }

    /// 単方向ストリームを作成可能かどうか
    pub fn can_create_unidirectional_stream(&self) -> bool {
        self.state.can_create_stream()
            && (self.flow_state.streams_uni_opened < self.remote_limits.max_streams_uni
                || !self.flow_control_enabled)
    }

    /// 双方向ストリームを作成可能かどうか
    pub fn can_create_bidirectional_stream(&self) -> bool {
        self.state.can_create_stream()
            && (self.flow_state.streams_bidi_opened < self.remote_limits.max_streams_bidi
                || !self.flow_control_enabled)
    }

    /// データを送信可能かどうか (フロー制御考慮)
    pub fn can_send_data(&self, bytes: u64) -> bool {
        self.state.can_send()
            && (self
                .remote_limits
                .max_data
                .saturating_sub(self.flow_state.data_sent)
                >= bytes
                || !self.flow_control_enabled)
    }

    /// ストリームの開設を試行 (送信側)
    ///
    /// `remote_limits` 内であれば opened カウンタを加算して `true` を返す。
    /// ブロックされている場合は WT_STREAMS_BLOCKED カプセルを生成
    /// (同じ maximum に対して 1 回だけ) して `false` を返す。
    /// draft-ietf-webtrans-http3-15 Section 5.6
    /// 将来のドラフトで変更される可能性がある
    pub fn try_open_stream(&mut self, bidirectional: bool) -> bool {
        if !self.state.can_create_stream() {
            return false;
        }

        let (opened, limit, last_blocked) = if bidirectional {
            (
                &mut self.flow_state.streams_bidi_opened,
                self.remote_limits.max_streams_bidi,
                &mut self.send_blocked.last_streams_blocked_bidi,
            )
        } else {
            (
                &mut self.flow_state.streams_uni_opened,
                self.remote_limits.max_streams_uni,
                &mut self.send_blocked.last_streams_blocked_uni,
            )
        };

        // フロー制御無効時は上限なし (draft-ietf-webtrans-http3-16 Section 5.1)
        if !self.flow_control_enabled {
            *opened = opened.saturating_add(1);
            return true;
        }

        if *opened < limit {
            *opened = opened.saturating_add(1);
            return true;
        }

        // ブロック: 同じ maximum に対して重複送信しない
        if self.flow_control_enabled && *last_blocked != Some(limit) {
            *last_blocked = Some(limit);
            self.pending_capsules.push(Capsule::StreamsBlocked {
                bidirectional,
                maximum: limit,
            });
        }
        false
    }

    /// データ送信を試行 (送信側)
    ///
    /// `remote_limits.max_data` 内であれば `data_sent` を加算して `true` を返す。
    /// ブロックされている場合は WT_DATA_BLOCKED カプセルを生成
    /// (同じ maximum に対して 1 回だけ) して `false` を返す。
    /// draft-ietf-webtrans-http3-15 Section 5.6
    /// 将来のドラフトで変更される可能性がある
    pub fn try_send_data(&mut self, bytes: u64) -> bool {
        if !self.state.can_send() {
            return false;
        }

        // フロー制御無効時は上限なし (draft-ietf-webtrans-http3-16 Section 5.1)
        if !self.flow_control_enabled {
            self.flow_state.data_sent = self.flow_state.data_sent.saturating_add(bytes);
            return true;
        }

        if self
            .remote_limits
            .max_data
            .saturating_sub(self.flow_state.data_sent)
            >= bytes
        {
            self.flow_state.data_sent = self.flow_state.data_sent.saturating_add(bytes);
            return true;
        }

        // ブロック: 同じ maximum に対して重複送信しない
        if self.flow_control_enabled {
            let limit = self.remote_limits.max_data;
            if self.send_blocked.last_data_blocked != Some(limit) {
                self.send_blocked.last_data_blocked = Some(limit);
                self.pending_capsules
                    .push(Capsule::DataBlocked { maximum: limit });
            }
        }
        false
    }

    /// 受信データのフロー制御検証
    ///
    /// ピアが `local_limits.max_data` を超過するデータを送信した場合、
    /// `WT_FLOW_CONTROL_ERROR` でセッションを閉じる (MUST)。
    /// draft-ietf-webtrans-http3-15 Section 5.4
    /// 将来のドラフトで変更される可能性がある
    ///
    /// `true` の場合は受信可能。`false` の場合はフロー制御違反。
    pub fn check_received_data(&self, bytes: u64) -> bool {
        if !self.flow_control_enabled {
            return true;
        }
        self.recv_data_fc.check_received(bytes)
    }

    /// 受信データ量を加算
    pub fn add_received_data(&mut self, bytes: u64) {
        self.flow_state.data_received = self.flow_state.data_received.saturating_add(bytes);
        self.recv_data_fc.on_data_received(bytes);
    }

    /// 受信ストリームのフロー制御検証
    ///
    /// ピアがアドバタイズした Maximum Streams を超過してストリームを開いた場合、
    /// `WT_FLOW_CONTROL_ERROR` でセッションを閉じる (MUST)。
    /// draft-ietf-webtrans-http3-15 Section 5.6.2
    /// 将来のドラフトで変更される可能性がある
    ///
    /// `true` の場合は受信可能。`false` の場合はフロー制御違反。
    pub fn check_received_stream(&self, bidirectional: bool) -> bool {
        if !self.flow_control_enabled {
            return true;
        }
        if bidirectional {
            self.recv_stream_fc_bidi.check_received()
        } else {
            self.recv_stream_fc_uni.check_received()
        }
    }

    /// 受信ストリーム数を加算
    pub fn add_received_stream(&mut self, bidirectional: bool) {
        if bidirectional {
            self.flow_state.streams_bidi_received =
                self.flow_state.streams_bidi_received.saturating_add(1);
            self.recv_stream_fc_bidi.on_stream_received();
        } else {
            self.flow_state.streams_uni_received =
                self.flow_state.streams_uni_received.saturating_add(1);
            self.recv_stream_fc_uni.on_stream_received();
        }
    }

    /// ピアが開いたストリームが完全に閉じたことを通知 (受信側)
    ///
    /// 閉じたストリーム数に基づいてウィンドウ更新を判定し、
    /// 必要に応じて WT_MAX_STREAMS カプセルを `pending_capsules` に追加する。
    /// draft-ietf-webtrans-http3-15 Section 5.6
    /// 将来のドラフトで変更される可能性がある
    pub fn on_remote_stream_closed(&mut self, bidirectional: bool) {
        if !self.flow_control_enabled || self.is_closed() {
            return;
        }
        let fc = if bidirectional {
            &mut self.recv_stream_fc_bidi
        } else {
            &mut self.recv_stream_fc_uni
        };
        if let Some(new_max) = fc.on_stream_closed() {
            self.pending_capsules.push(Capsule::MaxStreams {
                bidirectional,
                maximum: new_max,
            });
            // local_limits を同期更新
            if bidirectional {
                self.local_limits.max_streams_bidi = new_max;
            } else {
                self.local_limits.max_streams_uni = new_max;
            }
        }
    }

    /// ピアからの受信データをアプリが消費したことを通知 (受信側)
    ///
    /// 消費量に基づいてウィンドウ更新を判定し、
    /// 必要に応じて WT_MAX_DATA カプセルを `pending_capsules` に追加する。
    /// draft-ietf-webtrans-http3-15 Section 5.6
    /// 将来のドラフトで変更される可能性がある
    pub fn on_data_consumed(&mut self, bytes: u64) {
        if !self.flow_control_enabled || self.is_closed() {
            return;
        }
        if let Some(new_max) = self.recv_data_fc.on_data_consumed(bytes) {
            self.pending_capsules
                .push(Capsule::MaxData { maximum: new_max });
            self.local_limits.max_data = new_max;
        }
    }

    /// 受信データグラム数を加算 (DoS 監視用)
    pub fn add_received_datagram(&mut self) {
        self.flow_state.datagrams_received = self.flow_state.datagrams_received.saturating_add(1);
    }

    /// Capsule を送信キューに追加
    pub fn queue_capsule(&mut self, capsule: Capsule) {
        if self.is_closed() {
            return;
        }
        self.pending_capsules.push(capsule);
    }

    /// 送信待ち Capsule を取得
    pub fn pending_capsules(&self) -> &[Capsule] {
        &self.pending_capsules
    }

    /// 送信待ち Capsule を取り出す
    pub fn take_pending_capsules(&mut self) -> Vec<Capsule> {
        std::mem::take(&mut self.pending_capsules)
    }

    /// WT_CLOSE_SESSION Capsule を送信キューに追加してセッションをクローズ (draft-ietf-webtrans-http3-15 Section 6)
    ///
    /// WT_CLOSE_SESSION 送信後すぐに CONNECT ストリームへ FIN を送信すること (draft-ietf-webtrans-http3-15 Section 6)。
    pub fn close_with_error(&mut self, code: u32, message: impl Into<String>) {
        if self.is_closed() {
            return;
        }
        let mut message = message.into();
        // エラーメッセージは 1024 バイトを超えてはならない
        // (draft-ietf-webtrans-http3-15 Section 6)
        if message.len() > 1024 {
            // UTF-8 境界で安全に切り詰める
            let mut end = 1024;
            while end > 0 && !message.is_char_boundary(end) {
                end -= 1;
            }
            message.truncate(end);
        }
        let application_error = Error::application(code, message.clone());
        let capsule = Capsule::CloseSession {
            error_code: code,
            message,
        };
        self.queue_capsule(capsule);
        self.close_session_sent = true;
        self.close(Some(application_error));
    }

    /// WT_DRAIN_SESSION Capsule を送信キューに追加
    pub fn drain(&mut self) {
        if self.state == SessionState::Established {
            self.queue_capsule(Capsule::DrainSession);
            self.set_draining();
        }
    }

    // =========================================================================
    // Section 4.6: Buffering Incoming Streams and Datagrams
    // =========================================================================

    /// 未確立セッション向けの受信ストリームをバッファリング (draft-ietf-webtrans-http3-15 Section 4.6)
    ///
    /// `true` を返した場合はバッファ済み。
    /// `false` を返した場合は上限超過のため呼び出し元が
    /// `WT_BUFFERED_STREAM_REJECTED` で RESET_STREAM を送信すること。
    pub fn buffer_incoming_stream(&mut self, stream_id: u64, is_bidirectional: bool) -> bool {
        if self.buffered_streams.len() >= MAX_BUFFERED_STREAMS {
            return false;
        }
        self.buffered_streams.push(BufferedStream {
            stream_id,
            is_bidirectional,
        });
        true
    }

    /// バッファリングされたストリームを取り出す (セッション確立後に呼び出す)
    pub fn take_buffered_streams(&mut self) -> Vec<BufferedStream> {
        std::mem::take(&mut self.buffered_streams)
    }

    /// 未確立セッション向けの受信データグラムをバッファリング (draft-ietf-webtrans-http3-15 Section 4.6)
    ///
    /// `true` を返した場合はバッファ済み。
    /// `false` を返した場合は上限超過のためデータグラムを破棄すること。
    pub fn buffer_datagram(&mut self, data: Vec<u8>) -> bool {
        if self.buffered_datagrams.len() >= MAX_BUFFERED_DATAGRAMS {
            return false;
        }
        self.buffered_datagrams.push(data);
        true
    }

    /// バッファリングされたデータグラムを取り出す (セッション確立後に呼び出す)
    pub fn take_buffered_datagrams(&mut self) -> Vec<Vec<u8>> {
        std::mem::take(&mut self.buffered_datagrams)
    }

    // =========================================================================
    // Section 4.7: Interaction with the HTTP/3 GOAWAY frame
    // =========================================================================

    /// HTTP/3 GOAWAY 受信時の処理 (draft-ietf-webtrans-http3-15 Section 4.7)
    ///
    /// 新規セッションの確立不可を記録し、セッションをドレイン中に遷移させる。
    pub fn handle_goaway(&mut self) {
        self.goaway_received = true;
        self.set_draining();
    }

    /// HTTP/3 GOAWAY を受信したかどうか (draft-ietf-webtrans-http3-15 Section 4.7)
    pub fn is_goaway_received(&self) -> bool {
        self.goaway_received
    }

    // =========================================================================
    // Section 6: Session Termination
    // =========================================================================

    /// WT_CLOSE_SESSION を受信したかどうか (draft-ietf-webtrans-http3-15 Section 6)
    pub fn is_close_session_received(&self) -> bool {
        self.close_session_received
    }

    /// WT_CLOSE_SESSION を送信したかどうか (draft-ietf-webtrans-http3-15 Section 6)
    pub fn is_close_session_sent(&self) -> bool {
        self.close_session_sent
    }

    /// セッション終了時にリセットすべきストリーム ID の一覧を返す (draft-ietf-webtrans-http3-15 Section 6)
    ///
    /// セッション終了後、呼び出し元はこれらのストリームを
    /// `WT_SESSION_GONE` エラーコードでリセットすること。
    pub fn stream_ids_to_reset(&self) -> Vec<u64> {
        self.streams.keys().copied().collect()
    }

    /// セッション終了時に WT_SESSION_GONE でリセットすべきストリーム ID を取り出す
    pub fn take_pending_stream_resets(&mut self) -> Vec<u64> {
        std::mem::take(&mut self.pending_stream_resets)
    }

    /// CONNECT ストリームがクリーズクローズされた場合の処理 (draft-ietf-webtrans-http3-15 Section 6)
    ///
    /// WT_CLOSE_SESSION なしで CONNECT ストリームが FIN 受信した場合、
    /// error_code=0, message="" の WT_CLOSE_SESSION と等価。
    pub fn on_connect_stream_closed(&mut self) {
        if !self.close_session_received {
            self.close_session_received = true;
            self.close(Some(Error::application(0, "")));
        }
    }

    /// 受信した Capsule を処理
    pub fn process_capsule(&mut self, capsule: &Capsule) -> Result<(), CapsuleProcessError> {
        // WebTransport over HTTP/3 で禁止される Capsule はセッションエラー
        // (draft-ietf-webtrans-http3-15 Section 5.4: "Endpoints MUST treat
        // receipt of a WT_MAX_STREAM_DATA or a WT_STREAM_DATA_BLOCKED
        // capsule as a session error.")
        // 仕様は具体的なエラーコードを規定していないため、
        // アプリケーションエラーコード 0 でセッションを閉じる。
        // 将来のドラフトで変更される可能性がある
        if capsule.is_prohibited_in_http3() {
            return Err(CapsuleProcessError::Session(Error::application(
                0,
                "prohibited capsule received",
            )));
        }

        // フロー制御が有効化されていない場合は flow control capsules を無視する
        // (draft-ietf-webtrans-http3-15 Section 5.1)
        if !self.flow_control_enabled && capsule.is_flow_control() {
            return Ok(());
        }

        match capsule {
            Capsule::CloseSession {
                error_code,
                message,
            } => {
                self.close_session_received = true;
                self.close(Some(Error::application(*error_code, message.clone())));
            }

            Capsule::DrainSession => {
                self.set_draining();
            }

            Capsule::MaxData { maximum } => {
                // 増加しない値はエラー (draft-16 Section 5.6.4: "does not increase")
                // 将来のドラフトで変更される可能性がある
                if *maximum <= self.remote_limits.max_data {
                    return Err(CapsuleProcessError::Session(Error::Protocol(
                        ErrorCode::FlowControlError,
                    )));
                }
                self.remote_limits.max_data = *maximum;
                // 新しい制限を受け取ったので BLOCKED 状態をリセット
                self.send_blocked.last_data_blocked = None;
            }

            Capsule::MaxStreams {
                bidirectional,
                maximum,
            } => {
                // 2^60 を超える値はセッションエラー
                // (draft-16 Section 5.6.2: "MUST close the WebTransport session
                // with a WT_FLOW_CONTROL_ERROR error code")
                // 将来のドラフトで変更される可能性がある
                if *maximum > MAX_STREAMS_LIMIT {
                    return Err(CapsuleProcessError::Session(Error::Protocol(
                        ErrorCode::FlowControlError,
                    )));
                }
                if *bidirectional {
                    // 増加しない値はエラー (draft-16 Section 5.6.2: "does not increase")
                    // 将来のドラフトで変更される可能性がある
                    if *maximum <= self.remote_limits.max_streams_bidi {
                        return Err(CapsuleProcessError::Session(Error::Protocol(
                            ErrorCode::FlowControlError,
                        )));
                    }
                    self.remote_limits.max_streams_bidi = *maximum;
                    // 新しい制限を受け取ったので BLOCKED 状態をリセット
                    self.send_blocked.last_streams_blocked_bidi = None;
                } else {
                    // 増加しない値はエラー (draft-16 Section 5.6.2: "does not increase")
                    // 将来のドラフトで変更される可能性がある
                    if *maximum <= self.remote_limits.max_streams_uni {
                        return Err(CapsuleProcessError::Session(Error::Protocol(
                            ErrorCode::FlowControlError,
                        )));
                    }
                    self.remote_limits.max_streams_uni = *maximum;
                    // 新しい制限を受け取ったので BLOCKED 状態をリセット
                    self.send_blocked.last_streams_blocked_uni = None;
                }
            }

            Capsule::DataBlocked { .. } => {
                // 情報目的のみ、特に処理は不要
            }

            Capsule::StreamsBlocked { maximum, .. } => {
                // 2^60 を超える値はセッションエラー
                // (draft-16 Section 5.6.3: "MUST close the WebTransport session
                // with a WT_FLOW_CONTROL_ERROR error code")
                // 将来のドラフトで変更される可能性がある
                if *maximum > MAX_STREAMS_LIMIT {
                    return Err(CapsuleProcessError::Session(Error::Protocol(
                        ErrorCode::FlowControlError,
                    )));
                }
                // 情報目的のみ、特に処理は不要
            }

            Capsule::Unknown { .. } => {
                // 不明な Capsule は無視
            }
        }

        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_session_creation() {
        let session = Session::new(0);
        assert_eq!(session.session_id(), 0);
        assert_eq!(session.state(), SessionState::Pending);
        assert!(!session.is_established());
        assert!(!session.is_closed());
    }

    #[test]
    fn test_session_state_transitions() {
        let mut session = Session::new(0);

        session.set_connecting();
        assert_eq!(session.state(), SessionState::Connecting);

        session.set_established();
        assert_eq!(session.state(), SessionState::Established);
        assert!(session.is_established());

        session.set_draining();
        assert_eq!(session.state(), SessionState::Draining);

        session.close(None);
        assert_eq!(session.state(), SessionState::Closed);
        assert!(session.is_closed());
    }

    #[test]
    fn test_session_state_can_create_stream() {
        assert!(!SessionState::Pending.can_create_stream());
        assert!(!SessionState::Connecting.can_create_stream());
        assert!(SessionState::Established.can_create_stream());
        assert!(SessionState::Draining.can_create_stream());
        assert!(!SessionState::Closed.can_create_stream());
    }

    #[test]
    fn test_session_state_can_send() {
        assert!(!SessionState::Pending.can_send());
        assert!(!SessionState::Connecting.can_send());
        assert!(SessionState::Established.can_send());
        assert!(SessionState::Draining.can_send());
        assert!(!SessionState::Closed.can_send());
    }

    #[test]
    fn test_session_direct_establish() {
        // Connecting を経由せず Pending → Established へ遷移できる
        let mut session = Session::new(0);
        assert_eq!(session.state(), SessionState::Pending);

        session.set_established();
        assert_eq!(session.state(), SessionState::Established);

        session.close(None);
        assert_eq!(session.state(), SessionState::Closed);
    }

    #[test]
    fn test_session_stream_management() {
        let mut session = Session::new(0);

        let stream = Stream::new(4, 0, true);
        session.add_stream(stream);

        assert_eq!(session.stream_count(), 1);
        assert!(session.get_stream(4).is_some());

        let removed = session.remove_stream(4);
        assert!(removed.is_some());
        assert_eq!(session.stream_count(), 0);
    }

    #[test]
    fn test_session_flow_control() {
        let mut session = Session::new(0);
        session.set_established();
        session.remote_limits_mut().max_streams_uni = 10;
        session.remote_limits_mut().max_streams_bidi = 5;
        session.remote_limits_mut().max_data = 1024;

        assert!(session.can_create_unidirectional_stream());
        assert!(session.can_create_bidirectional_stream());
        assert!(session.can_send_data(512));
        assert!(session.can_send_data(1024));
        assert!(!session.can_send_data(1025));
    }

    #[test]
    fn test_session_capsule_queue() {
        let mut session = Session::new(0);

        session.queue_capsule(Capsule::DrainSession);
        assert_eq!(session.pending_capsules().len(), 1);

        let capsules = session.take_pending_capsules();
        assert_eq!(capsules.len(), 1);
        assert!(session.pending_capsules().is_empty());
    }

    #[test]
    fn test_session_process_max_data() {
        let mut session = Session::new(0);
        session.set_flow_control_enabled(true);

        // 増加は OK
        session
            .process_capsule(&Capsule::MaxData { maximum: 1000 })
            .expect("test must succeed");
        assert_eq!(session.remote_limits().max_data, 1000);

        // 減少はエラー
        let result = session.process_capsule(&Capsule::MaxData { maximum: 500 });
        assert!(result.is_err());
    }

    #[test]
    fn test_session_process_max_streams() {
        let mut session = Session::new(0);
        session.set_flow_control_enabled(true);

        // 双方向ストリーム
        session
            .process_capsule(&Capsule::MaxStreams {
                bidirectional: true,
                maximum: 100,
            })
            .expect("test must succeed");
        assert_eq!(session.remote_limits().max_streams_bidi, 100);

        // 単方向ストリーム
        session
            .process_capsule(&Capsule::MaxStreams {
                bidirectional: false,
                maximum: 50,
            })
            .expect("test must succeed");
        assert_eq!(session.remote_limits().max_streams_uni, 50);
    }

    #[test]
    fn test_session_close_with_error() {
        let mut session = Session::new(0);
        session.set_established();

        session.close_with_error(42, "test error");

        assert!(session.is_closed());
        assert_eq!(session.pending_capsules().len(), 1);
    }

    #[test]
    fn test_session_close_with_error_on_closed_session() {
        // クローズ済みセッションで close_with_error を呼んでも
        // close_session_sent フラグが誤設定されないことを確認する
        let mut session = Session::new(0);
        session.set_established();

        // ピアからの CloseSession を先に処理してセッションをクローズする
        session
            .process_capsule(&Capsule::CloseSession {
                error_code: 1,
                message: "peer closed".to_string(),
            })
            .expect("CloseSession capsule should be accepted");
        assert!(session.is_closed());
        assert!(!session.is_close_session_sent());

        // クローズ済みセッションに対して close_with_error を呼ぶ
        session.close_with_error(42, "local close");

        // close_session_sent が false のまま (カプセルは送信されていない)
        assert!(!session.is_close_session_sent());
        // 送信キューにカプセルが追加されていない
        assert!(session.pending_capsules().is_empty());
    }

    #[test]
    fn test_session_process_flow_control_capsule_ignored_when_disabled() {
        let mut session = Session::new(0);
        session.set_flow_control_enabled(false);

        session
            .process_capsule(&Capsule::MaxData { maximum: 1000 })
            .expect("test must succeed");

        // 無効時は無視される
        assert_eq!(session.remote_limits().max_data, 0);
    }

    #[test]
    fn test_session_process_prohibited_capsule_returns_error() {
        let mut session = Session::new(0);
        // WT_MAX_STREAM_DATA (0x190B4D3E) は禁止 Capsule
        let result = session.process_capsule(&Capsule::Unknown {
            capsule_type: 0x190B4D3E,
            payload: vec![],
        });
        // 禁止 Capsule はセッションエラー
        // (draft-ietf-webtrans-http3-15 Section 5.4)
        assert_eq!(
            result,
            Err(CapsuleProcessError::Session(Error::application(
                0,
                "prohibited capsule received"
            )))
        );
    }

    #[test]
    fn test_session_process_prohibited_capsule_stream_data_blocked() {
        let mut session = Session::new(0);
        // WT_STREAM_DATA_BLOCKED (0x190B4D42) も禁止 Capsule
        let result = session.process_capsule(&Capsule::Unknown {
            capsule_type: 0x190B4D42,
            payload: vec![],
        });
        // 禁止 Capsule はセッションエラー
        // (draft-ietf-webtrans-http3-15 Section 5.4)
        assert_eq!(
            result,
            Err(CapsuleProcessError::Session(Error::application(
                0,
                "prohibited capsule received"
            )))
        );
    }

    #[test]
    fn test_session_drain() {
        let mut session = Session::new(0);
        session.set_established();

        session.drain();

        assert_eq!(session.state(), SessionState::Draining);
        assert_eq!(session.pending_capsules().len(), 1);
    }

    #[test]
    fn test_session_buffer_incoming_stream() {
        let mut session = Session::new(0);

        // 正常なバッファリング
        assert!(session.buffer_incoming_stream(4, true));
        assert!(session.buffer_incoming_stream(8, false));

        let streams = session.take_buffered_streams();
        assert_eq!(streams.len(), 2);
        assert_eq!(streams[0].stream_id, 4);
        assert!(streams[0].is_bidirectional);
        assert_eq!(streams[1].stream_id, 8);
        assert!(!streams[1].is_bidirectional);

        // take 後は空になる
        let streams = session.take_buffered_streams();
        assert!(streams.is_empty());
    }

    #[test]
    fn test_session_buffer_stream_limit() {
        let mut session = Session::new(0);

        // MAX_BUFFERED_STREAMS まではバッファ可能
        for i in 0..MAX_BUFFERED_STREAMS {
            assert!(session.buffer_incoming_stream(i as u64 * 4, false));
        }

        // 超過した場合は false (RESET_STREAM with WT_BUFFERED_STREAM_REJECTED を送る)
        assert!(!session.buffer_incoming_stream(99999, false));
    }

    #[test]
    fn test_session_buffer_datagram() {
        let mut session = Session::new(0);

        assert!(session.buffer_datagram(vec![1, 2, 3]));
        assert!(session.buffer_datagram(vec![4, 5, 6]));

        let datagrams = session.take_buffered_datagrams();
        assert_eq!(datagrams.len(), 2);
        assert_eq!(datagrams[0], vec![1, 2, 3]);
        assert_eq!(datagrams[1], vec![4, 5, 6]);

        // take 後は空になる
        assert!(session.take_buffered_datagrams().is_empty());
    }

    #[test]
    fn test_session_buffer_datagram_limit() {
        let mut session = Session::new(0);

        // MAX_BUFFERED_DATAGRAMS まではバッファ可能
        for _ in 0..MAX_BUFFERED_DATAGRAMS {
            assert!(session.buffer_datagram(vec![0]));
        }

        // 超過した場合は false (datagram を破棄する)
        assert!(!session.buffer_datagram(vec![0xff]));
    }

    #[test]
    fn test_session_handle_goaway() {
        let mut session = Session::new(0);
        session.set_established();

        assert!(!session.is_goaway_received());

        session.handle_goaway();

        assert!(session.is_goaway_received());
        assert_eq!(session.state(), SessionState::Draining);
    }

    #[test]
    fn test_session_close_session_tracking() {
        let mut session = Session::new(0);
        session.set_established();

        assert!(!session.is_close_session_sent());
        assert!(!session.is_close_session_received());

        // WT_CLOSE_SESSION 送信
        session.close_with_error(42, "bye");
        assert!(session.is_close_session_sent());
        assert!(!session.is_close_session_received());
    }

    #[test]
    fn test_session_process_close_session_capsule() {
        let mut session = Session::new(0);
        session.set_established();

        assert!(!session.is_close_session_received());

        session
            .process_capsule(&Capsule::CloseSession {
                error_code: 0,
                message: String::new(),
            })
            .expect("test must succeed");

        assert!(session.is_close_session_received());
        assert!(session.is_closed());
    }

    #[test]
    fn test_session_on_connect_stream_closed() {
        let mut session = Session::new(0);
        session.set_established();

        assert!(!session.is_close_session_received());

        session.on_connect_stream_closed();

        assert!(session.is_close_session_received());
        assert!(session.is_closed());
    }

    #[test]
    fn test_session_on_connect_stream_closed_idempotent() {
        let mut session = Session::new(0);
        session.set_established();

        // WT_CLOSE_SESSION 受信後はセッションが閉じている
        session
            .process_capsule(&Capsule::CloseSession {
                error_code: 1,
                message: "error".to_string(),
            })
            .expect("test must succeed");
        assert!(session.is_close_session_received());

        // CONNECT ストリームが再度クローズされても既に受信済みフラグは変わらない
        session.on_connect_stream_closed();
        assert!(session.is_close_session_received());
    }

    #[test]
    fn test_session_stream_ids_to_reset() {
        let mut session = Session::new(0);
        session.set_established();

        session.add_stream(Stream::new(4, 0, true));
        session.add_stream(Stream::new(8, 0, false));

        let mut ids = session.stream_ids_to_reset();
        ids.sort();
        assert_eq!(ids, vec![4, 8]);
    }

    #[test]
    fn test_session_take_pending_stream_resets_on_close() {
        let mut session = Session::new(0);
        session.set_established();
        session.add_stream(Stream::new(4, 0, true));
        session.add_stream(Stream::new(8, 0, false));

        session.close(None);

        let mut ids = session.take_pending_stream_resets();
        ids.sort();
        assert_eq!(ids, vec![4, 8]);
        assert!(session.take_pending_stream_resets().is_empty());
    }

    #[test]
    fn test_session_try_add_stream_rejects_after_close() {
        let mut session = Session::new(0);
        session.close(None);
        assert!(!session.try_add_stream(Stream::new(4, 0, true)));
        assert_eq!(session.stream_count(), 0);
    }

    #[test]
    fn test_process_capsule_max_streams_exceeds_limit() {
        let mut session = Session::new(0);
        session.set_established();

        // 2^60 を超える WT_MAX_STREAMS はセッションエラー
        // (draft-16 Section 5.6.2)
        let result = session.process_capsule(&Capsule::MaxStreams {
            bidirectional: true,
            maximum: (1u64 << 60) + 1,
        });
        assert!(matches!(
            result,
            Err(CapsuleProcessError::Session(Error::Protocol(
                ErrorCode::FlowControlError
            )))
        ));
    }

    #[test]
    fn test_process_capsule_max_streams_at_limit() {
        let mut session = Session::new(0);
        session.set_established();

        // 2^60 はちょうど許容
        let result = session.process_capsule(&Capsule::MaxStreams {
            bidirectional: true,
            maximum: 1u64 << 60,
        });
        assert!(result.is_ok());
    }

    #[test]
    fn test_process_capsule_max_streams_decreased() {
        let mut session = Session::new(0);
        session.set_established();

        session
            .process_capsule(&Capsule::MaxStreams {
                bidirectional: false,
                maximum: 10,
            })
            .expect("test must succeed");

        // 値が減少した場合はセッションエラー
        let result = session.process_capsule(&Capsule::MaxStreams {
            bidirectional: false,
            maximum: 5,
        });
        assert!(matches!(result, Err(CapsuleProcessError::Session(_))));
    }

    #[test]
    fn test_check_received_data() {
        let mut session = Session::new(0);
        session.set_established();
        session.initialize_local_limits(FlowControlLimits {
            max_data: 100,
            ..FlowControlLimits::default()
        });

        assert!(session.check_received_data(50));
        session.add_received_data(50);
        assert!(session.check_received_data(50));
        assert!(!session.check_received_data(51));
    }

    #[test]
    fn test_check_received_stream() {
        let mut session = Session::new(0);
        session.set_established();
        session.initialize_local_limits(FlowControlLimits {
            max_streams_uni: 2,
            ..FlowControlLimits::default()
        });

        assert!(session.check_received_stream(false));
        session.add_received_stream(false);
        assert!(session.check_received_stream(false));
        session.add_received_stream(false);
        assert!(!session.check_received_stream(false));
    }

    #[test]
    fn test_received_datagram_counter() {
        let mut session = Session::new(0);
        assert_eq!(session.flow_state().datagrams_received, 0);
        session.add_received_datagram();
        session.add_received_datagram();
        assert_eq!(session.flow_state().datagrams_received, 2);
    }

    // =========================================================================
    // 動的ウィンドウ更新テスト
    // =========================================================================

    #[test]
    fn test_initialize_local_limits() {
        let mut session = Session::new(0);
        session.initialize_local_limits(FlowControlLimits {
            max_streams_uni: 100,
            max_streams_bidi: 50,
            max_data: 1024,
        });

        assert_eq!(session.local_limits().max_streams_uni, 100);
        assert_eq!(session.local_limits().max_streams_bidi, 50);
        assert_eq!(session.local_limits().max_data, 1024);

        // check_received_stream は initialize_local_limits で設定した上限に従う
        assert!(session.check_received_stream(false));
        assert!(session.check_received_stream(true));
    }

    #[test]
    fn test_on_remote_stream_closed_generates_max_streams() {
        let mut session = Session::new(0);
        session.set_established();
        session.initialize_local_limits(FlowControlLimits {
            max_streams_uni: 4,
            ..FlowControlLimits::default()
        });

        // 4 本開いて 3 本閉じる (残りウィンドウ 0 < しきい値 2)
        for _ in 0..4 {
            session.add_received_stream(false);
        }
        // 最初の 2 本を閉じてもまだしきい値以下ではない場合がある
        session.on_remote_stream_closed(false);
        session.on_remote_stream_closed(false);
        // 3 本目を閉じた時点で残りウィンドウ = 0 < threshold = 2
        session.on_remote_stream_closed(false);

        let capsules = session.take_pending_capsules();
        // WT_MAX_STREAMS カプセルが生成されているはず
        let max_streams_capsules: Vec<_> = capsules
            .iter()
            .filter(|c| {
                matches!(
                    c,
                    Capsule::MaxStreams {
                        bidirectional: false,
                        ..
                    }
                )
            })
            .collect();
        assert!(
            !max_streams_capsules.is_empty(),
            "WT_MAX_STREAMS capsule should be generated"
        );

        // advertised_max は増加している
        for c in &max_streams_capsules {
            if let Capsule::MaxStreams { maximum, .. } = c {
                assert!(*maximum > 4, "new max should be greater than initial");
            }
        }
    }

    #[test]
    fn test_on_remote_stream_closed_no_op_when_disabled() {
        let mut session = Session::new(0);
        session.set_established();
        session.set_flow_control_enabled(false);
        session.initialize_local_limits(FlowControlLimits {
            max_streams_uni: 4,
            ..FlowControlLimits::default()
        });

        for _ in 0..4 {
            session.add_received_stream(false);
        }
        for _ in 0..4 {
            session.on_remote_stream_closed(false);
        }

        assert!(session.take_pending_capsules().is_empty());
    }

    #[test]
    fn test_on_remote_stream_closed_no_op_when_closed() {
        let mut session = Session::new(0);
        session.set_established();
        session.initialize_local_limits(FlowControlLimits {
            max_streams_uni: 4,
            ..FlowControlLimits::default()
        });
        session.close(None);

        for _ in 0..4 {
            session.on_remote_stream_closed(false);
        }

        assert!(session.take_pending_capsules().is_empty());
    }

    #[test]
    fn test_try_open_stream_success() {
        let mut session = Session::new(0);
        session.set_established();
        session.remote_limits_mut().max_streams_uni = 3;

        assert!(session.try_open_stream(false));
        assert!(session.try_open_stream(false));
        assert!(session.try_open_stream(false));
        assert!(!session.try_open_stream(false));

        assert_eq!(session.flow_state().streams_uni_opened, 3);
    }

    #[test]
    fn test_try_open_stream_blocked_dedup() {
        let mut session = Session::new(0);
        session.set_established();
        session.remote_limits_mut().max_streams_uni = 0;

        // 1 回目のブロックで STREAMS_BLOCKED が生成される
        assert!(!session.try_open_stream(false));
        // 2 回目は同じ maximum なので重複送信しない
        assert!(!session.try_open_stream(false));

        let capsules = session.take_pending_capsules();
        let blocked_count = capsules
            .iter()
            .filter(|c| matches!(c, Capsule::StreamsBlocked { .. }))
            .count();
        assert_eq!(
            blocked_count, 1,
            "STREAMS_BLOCKED should be sent only once per maximum"
        );
    }

    #[test]
    fn test_try_open_stream_blocked_reset_after_max_streams() {
        let mut session = Session::new(0);
        session.set_established();
        session.remote_limits_mut().max_streams_bidi = 1;

        // 1 本開ける
        assert!(session.try_open_stream(true));
        // ブロック → STREAMS_BLOCKED 生成
        assert!(!session.try_open_stream(true));

        // ピアから新しい WT_MAX_STREAMS を受信
        session
            .process_capsule(&Capsule::MaxStreams {
                bidirectional: true,
                maximum: 2,
            })
            .expect("test must succeed");

        // もう 1 本開ける
        assert!(session.try_open_stream(true));
        // 再度ブロック → 新しい STREAMS_BLOCKED が生成される
        assert!(!session.try_open_stream(true));

        let capsules = session.take_pending_capsules();
        let blocked_count = capsules
            .iter()
            .filter(|c| matches!(c, Capsule::StreamsBlocked { .. }))
            .count();
        assert_eq!(
            blocked_count, 2,
            "STREAMS_BLOCKED should be sent again after new MAX_STREAMS"
        );
    }

    #[test]
    fn test_try_send_data_success() {
        let mut session = Session::new(0);
        session.set_established();
        session.remote_limits_mut().max_data = 100;

        assert!(session.try_send_data(50));
        assert!(session.try_send_data(50));
        assert!(!session.try_send_data(1));

        assert_eq!(session.flow_state().data_sent, 100);
    }

    #[test]
    fn test_try_send_data_blocked_dedup() {
        let mut session = Session::new(0);
        session.set_established();
        session.remote_limits_mut().max_data = 0;

        assert!(!session.try_send_data(1));
        assert!(!session.try_send_data(1));

        let capsules = session.take_pending_capsules();
        let blocked_count = capsules
            .iter()
            .filter(|c| matches!(c, Capsule::DataBlocked { .. }))
            .count();
        assert_eq!(
            blocked_count, 1,
            "DATA_BLOCKED should be sent only once per maximum"
        );
    }

    #[test]
    fn test_on_data_consumed_generates_max_data() {
        let mut session = Session::new(0);
        session.set_established();
        session.initialize_local_limits(FlowControlLimits {
            max_data: 100,
            ..FlowControlLimits::default()
        });

        // 100 バイト受信して 80 バイト消費 → 残りウィンドウ = 20 < threshold = 50
        session.add_received_data(100);
        session.on_data_consumed(80);

        let capsules = session.take_pending_capsules();
        let max_data_capsules: Vec<_> = capsules
            .iter()
            .filter(|c| matches!(c, Capsule::MaxData { .. }))
            .collect();
        assert!(
            !max_data_capsules.is_empty(),
            "WT_MAX_DATA capsule should be generated"
        );

        // 新しい max_data は増加している
        for c in &max_data_capsules {
            if let Capsule::MaxData { maximum } = c {
                assert!(
                    *maximum > 100,
                    "new max_data should be greater than initial"
                );
            }
        }
    }

    #[test]
    fn test_on_data_consumed_no_op_when_disabled() {
        let mut session = Session::new(0);
        session.set_established();
        session.set_flow_control_enabled(false);
        session.initialize_local_limits(FlowControlLimits {
            max_data: 100,
            ..FlowControlLimits::default()
        });

        session.add_received_data(100);
        session.on_data_consumed(80);

        assert!(session.take_pending_capsules().is_empty());
    }

    #[test]
    fn test_advertised_max_does_not_exceed_limit() {
        let mut session = Session::new(0);
        session.set_established();
        // MAX_STREAMS_LIMIT に近い値で初期化
        session.initialize_local_limits(FlowControlLimits {
            max_streams_uni: MAX_STREAMS_LIMIT,
            ..FlowControlLimits::default()
        });

        // 上限まで受信して全部閉じる
        for _ in 0..10 {
            session.add_received_stream(false);
        }
        for _ in 0..10 {
            session.on_remote_stream_closed(false);
        }

        // 生成されたカプセルの maximum が MAX_STREAMS_LIMIT を超えないこと
        for capsule in session.take_pending_capsules() {
            if let Capsule::MaxStreams { maximum, .. } = capsule {
                assert!(maximum <= MAX_STREAMS_LIMIT);
            }
        }
    }

    #[test]
    fn test_queue_initial_flow_control_capsules() {
        let mut session = Session::new(0);
        session.set_established();

        let limits = FlowControlLimits {
            max_streams_bidi: 100,
            max_streams_uni: 50,
            max_data: 8 * 1024 * 1024,
        };
        session.queue_initial_flow_control_capsules(limits);

        // local_limits が更新されること
        assert_eq!(session.local_limits().max_streams_bidi, 100);
        assert_eq!(session.local_limits().max_streams_uni, 50);
        assert_eq!(session.local_limits().max_data, 8 * 1024 * 1024);

        // 3 つのカプセルが生成されること
        let capsules = session.take_pending_capsules();
        assert_eq!(capsules.len(), 3);
        assert_eq!(
            capsules[0],
            Capsule::MaxStreams {
                bidirectional: true,
                maximum: 100
            }
        );
        assert_eq!(
            capsules[1],
            Capsule::MaxStreams {
                bidirectional: false,
                maximum: 50
            }
        );
        assert_eq!(capsules[2], Capsule::MaxData { maximum: 8388608 });
    }

    #[test]
    fn test_queue_initial_flow_control_capsules_zero_values() {
        let mut session = Session::new(0);
        session.set_established();

        let limits = FlowControlLimits {
            max_streams_bidi: 0,
            max_streams_uni: 0,
            max_data: 0,
        };
        session.queue_initial_flow_control_capsules(limits);

        // 0 値ではカプセルが生成されないこと
        assert!(session.take_pending_capsules().is_empty());
    }
}
