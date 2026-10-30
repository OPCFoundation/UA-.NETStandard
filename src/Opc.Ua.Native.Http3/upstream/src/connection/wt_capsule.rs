// Modified by OPC Foundation, 2026-10-16: bounded capsule storage; see the proof patch manifest.
//! WebTransport Capsule 処理 connection/mod.rs からの分離
//!
//! WebTransport CONNECT ストリーム上の Capsule デコード・処理を担う
//! `Connection` メソッド群。
//! (draft-ietf-webtrans-http3-15 Section 5.6, 6)

use crate::error::{Error, ErrorCode};
use crate::event::{Event, WebTransportEvent};
use crate::webtransport::error::ErrorCode as WtErrorCode;

use super::Connection;
use super::wt_types::WtSessionState;

impl Connection {
    /// WebTransport CONNECT ストリーム上のデータを Capsule としてデコード・処理する
    ///
    /// DATA フレームのペイロードを Capsule デコードバッファに追加し、
    /// 完全な Capsule が得られるまでデコードを試みる。
    /// (draft-ietf-webtrans-http3-15 Section 5.6)
    pub(crate) fn process_wt_capsule_data(
        &mut self,
        session_id: u64,
        data: &[u8],
    ) -> Result<(), Error> {
        // セッションの capsule_buf にデータを追加
        self.append_wt_capsule_data(session_id, data)?;

        // Decode without cloning/draining the entire suffix for every small capsule.
        let mut offset = 0;
        while let Some(session) = self.wt_sessions.get(&session_id) {
            if offset == session.capsule_buf.len() {
                break;
            }
            let buffered_len = session.capsule_buf.len();
            match crate::webtransport::Capsule::decode(&session.capsule_buf[offset..]) {
                Ok(Some((capsule, consumed))) => {
                    offset += consumed;
                    let has_trailing = buffered_len > offset;
                    let is_close_session =
                        matches!(capsule, crate::webtransport::Capsule::CloseSession { .. });

                    // Capsule を処理してイベントに変換
                    self.handle_wt_capsule(session_id, &capsule)?;

                    // WT_CLOSE_SESSION に続く同一 DATA フレーム内の追加バイトは
                    // H3_MESSAGE_ERROR で拒否する (draft-ietf-webtrans-http3-16 Section 6)。
                    // セッション除去で while ループが終了するため、ここで検出する。
                    if is_close_session && has_trailing {
                        return self.reject_receive_buffer(session_id, ErrorCode::MessageError);
                    }
                }
                Ok(None) => {
                    // バッファ不足: 次の DATA フレームを待つ
                    break;
                }
                Err(_) => {
                    // RFC 9297 Section 3.3: malformed Capsule は
                    // HTTP message エラーとして扱う → H3_MESSAGE_ERROR
                    return self.reject_receive_buffer(session_id, ErrorCode::MessageError);
                }
            }
        }

        if offset != 0 && let Some(session) = self.wt_sessions.get_mut(&session_id) {
            if offset == session.capsule_buf.len() {
                session.capsule_buf = Vec::new();
            } else {
                session.capsule_buf.drain(..offset);
                session.capsule_buf.shrink_to_fit();
            }
        }
        Ok(())
    }

    /// デコードされた Capsule を処理してイベントに変換する
    pub(crate) fn handle_wt_capsule(
        &mut self,
        session_id: u64,
        capsule: &crate::webtransport::Capsule,
    ) -> Result<(), Error> {
        use crate::webtransport::Capsule;

        match capsule {
            Capsule::CloseSession {
                error_code,
                message,
            } => {
                // WT_CLOSE_SESSION: セッションを終了し、error_code / message を通知する
                // (draft-ietf-webtrans-http3-15 Section 6)
                //
                // 終了後は tombstone (`closed_wt_sessions`) 経由で追加データを
                // H3_MESSAGE_ERROR として拒否する (draft-ietf-webtrans-http3-16 Section 6)
                self.terminate_wt_session_with(
                    session_id,
                    WtErrorCode::SessionGone as u64,
                    *error_code,
                    message.clone(),
                );
            }
            Capsule::DrainSession => {
                // WT_DRAIN_SESSION: 内部状態を Draining へ遷移し、イベントで通知する
                // (draft-ietf-webtrans-http3-15 Section 4.7)
                // セッションは即座に終了しないが、Connection 層は以後の新規
                // ストリーム/データグラム送信を拒否する。
                if let Some(session) = self.wt_sessions.get_mut(&session_id)
                    && (session.state == WtSessionState::Established
                        || session.state == WtSessionState::Pending)
                {
                    session.state = WtSessionState::Draining;
                    self.events.push_back(Event::WebTransport(
                        WebTransportEvent::SessionDraining { session_id },
                    ));
                }
            }
            Capsule::MaxData { .. }
            | Capsule::MaxStreams { .. }
            | Capsule::DataBlocked { .. }
            | Capsule::StreamsBlocked { .. } => {
                // フロー制御カプセル: セッションのフロー制御有効性を確認
                let fc_enabled = self
                    .wt_sessions
                    .get(&session_id)
                    .is_some_and(|s| s.flow_control_enabled);

                if fc_enabled {
                    // フロー制御有効: 上位層に通知して Session::process_capsule で処理
                    self.events
                        .push_back(Event::WebTransport(WebTransportEvent::Capsule {
                            session_id,
                            capsule: capsule.clone(),
                        }));
                }
                // フロー制御無効時は無視 (Section 5.1)
            }
            Capsule::Unknown { .. } => {
                // 禁止 Capsule (WT_MAX_STREAM_DATA / WT_STREAM_DATA_BLOCKED) は
                // セッションエラーとして扱う
                // (draft-ietf-webtrans-http3-15 Section 5.4: "Endpoints MUST treat
                // receipt of a WT_MAX_STREAM_DATA or a WT_STREAM_DATA_BLOCKED
                // capsule as a session error.")
                // 将来のドラフトで変更される可能性がある
                if capsule.is_prohibited_in_http3() {
                    self.terminate_wt_session_with(
                        session_id,
                        WtErrorCode::SessionGone as u64,
                        0,
                        "prohibited capsule received".to_string(),
                    );
                }
                // その他の不明な Capsule は無視 (draft-ietf-webtrans-http3-15)
            }
        }

        Ok(())
    }

    /// WebTransport CONNECT ストリーム上の DATA フレーム処理 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 5.6)
    /// WT セッションの DATA フレームを処理する。非 WT ストリームは `false` を返す。
    ///
    /// draft 別の扱い:
    /// - draft-07/14/15: Pending 中は楽観的カプセル送信としてバッファリングする
    ///   (draft-ietf-webtrans-http3-16 Section 3.2)
    /// - draft-02: Chrome 互換のため Pending 中の DATA は黙って破棄
    pub(crate) fn handle_wt_data_frame(
        &mut self,
        stream_id: u64,
        data: &[u8],
    ) -> Result<bool, Error> {
        // SETTINGS 未着の WT CONNECT ストリームへの DATA は保留する
        // (draft-ietf-webtrans-http3-16 Section 3.1 / 4.6 / 7.1)
        if self.deferred_wt_connects.contains_key(&stream_id) {
            // DoS 対策: 保留中の DATA に上限を設ける
            const MAX_DEFERRED_DATA_BYTES: usize = 64 * 1024;
            let stream = self
                .streams
                .get(&stream_id)
                .expect("deferred CONNECT stream must exist");
            let current_len = stream.received_body().len();
            if current_len + data.len() > MAX_DEFERRED_DATA_BYTES {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }
            return Ok(true);
        }

        let Some(session) = self.wt_sessions.get(&stream_id) else {
            // 終了済みセッション (tombstone) の CONNECT ストリームへの追加 DATA は
            // H3_MESSAGE_ERROR で拒否する
            // (draft-ietf-webtrans-http3-16 Section 6: WT_CLOSE_SESSION 後の
            //  追加データは H3_MESSAGE_ERROR)
            if self.closed_wt_sessions.contains(&stream_id) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }
            return Ok(false);
        };

        match session.state {
            WtSessionState::Established | WtSessionState::Draining => {
                // Draining 中もカプセル受信は継続する (Section 4.7)
                self.process_wt_capsule_data(stream_id, data)?;
            }
            WtSessionState::Pending => {
                let peer_draft = self.peer_wt_draft_version();
                if !matches!(peer_draft, Some(crate::webtransport::DraftVersion::Draft02)) {
                    // draft-07/14/15: 楽観的カプセル送信としてバッファリングする
                    // (draft-ietf-webtrans-http3-16 Section 3.2)
                    // サーバー側のみ: クライアントは楽観的送信を送信方向にのみ行う
                    if self.role == crate::connection::Role::Server {
                        // Apply both per-session and aggregate storage policy before copying.
                        self.append_wt_capsule_data(stream_id, data)?;
                        return Ok(true);
                    }
                    return self.reject_receive_buffer(stream_id, ErrorCode::MessageError).map(|()| true);
                }
                // draft-02: Pending 中の DATA は破棄する
            }
            WtSessionState::Closed => {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }
        }

        Ok(true)
    }

    /// WebTransport CONNECT ストリームの StreamEnd 処理 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 5.6, 6)
    /// FIN 到着時に未完成 Capsule が残っていれば malformed。
    /// WT セッションの FIN はセッション終了を意味する。
    pub(crate) fn handle_wt_stream_end(&mut self, stream_id: u64) -> Result<bool, Error> {
        // SETTINGS 未着の WT CONNECT ストリームの FIN は保留エントリを破棄する
        if self.deferred_wt_connects.remove(&stream_id).is_some() {
            return Ok(true);
        }
        if let Some(session) = self.wt_sessions.get(&stream_id) {
            if session.state == WtSessionState::Pending {
                // Pending 状態でカプセルバッファが残っている場合は、
                // バッファを破棄してセッションを終了する
                // (draft-ietf-webtrans-http3-16 Section 3.2 / Section 6:
                //  CONNECT ストリームのクローズはセッション終了を意味する)
                self.wt_sessions.remove(&stream_id);
                self.closed_wt_sessions.insert(stream_id);
                return Ok(true);
            }
            if !session.capsule_buf.is_empty() {
                return self.reject_receive_buffer(stream_id, ErrorCode::MessageError).map(|()| true);
            }
            self.terminate_wt_session(stream_id);
            return Ok(true);
        }
        // 終了済みセッション (tombstone) の CONNECT ストリームの FIN は受理して何もしない
        // (WT_CLOSE_SESSION を含む DATA と FIN が同一バッファに連続するのは正常な
        //  終了手順であり、FIN を H3_MESSAGE_ERROR にしてはならない。
        //  draft-ietf-webtrans-http3-16 Section 6)
        if self.closed_wt_sessions.contains(&stream_id) {
            return Ok(true);
        }
        Ok(false)
    }
}
