// Modified by OPC Foundation, 2026-10-16: transfer deferred capsule ownership; see the proof patch manifest.
//! WebTransport セッション管理 connection/mod.rs からの分離
//!
//! WebTransport の能力ネゴシエーション、セッションライフサイクル管理、
//! フロー制御判定を担う `Connection` メソッド群。
//! (draft-ietf-webtrans-http3-15 Section 3, 4.6, 4.7, 5, 6, 7.1)

use crate::error::{Error, ErrorCode, WtSetupError};
use crate::event::{Event, WebTransportEvent, WtStreamReset};
use crate::qpack::Header;
use crate::webtransport::DraftVersion;
use crate::webtransport::error::ErrorCode as WtErrorCode;

use super::wt_types::{
    AssocOutcome, BufferedStreamEntry, WT_MAX_PENDING_SESSIONS, WtSession, WtSessionState,
};
use super::{Connection, Role};

/// RFC 9297 Section 3.2 の Capsule Protocol 併用禁止ヘッダー (Content-Length / Content-Type)
///
/// Capsule Protocol を使用するメッセージに付与してはならない (MUST NOT)。
/// Transfer-Encoding は接続固有ヘッダーとして全リクエストで既に拒否される。
fn has_forbidden_capsule_headers(headers: &[Header]) -> bool {
    headers
        .iter()
        .any(|h| h.name() == b"content-length" || h.name() == b"content-type")
}

/// RFC 9297 Section 3.2 の Capsule Protocol 併用禁止ステータス (204 / 205 / 206)
///
/// Capsule Protocol を使用するレスポンスに付与してはならない (MUST NOT)。
fn is_forbidden_capsule_status(headers: &[Header]) -> bool {
    headers.iter().any(|h| {
        h.name() == b":status"
            && (h.value() == b"204" || h.value() == b"205" || h.value() == b"206")
    })
}

impl Connection {
    pub(crate) fn peer_requires_initial_wt_capsules(&self) -> bool {
        self.peer_settings
            .as_ref()
            .and_then(|s| s.wt_settings.as_ref())
            .is_some_and(|wt| wt.requires_initial_capsule_flow_control_compat())
    }

    /// 0-RTT 再開時の前回接続のピア WebTransport SETTINGS を注入する
    ///
    /// クライアントが 0-RTT 再開時に、前回接続でキャッシュしたピアの
    /// WebTransport SETTINGS を注入する。SETTINGS フレーム受信時に
    /// フロー制御値の減少を検出して H3_SETTINGS_ERROR で接続を閉じる。
    /// (draft-ietf-webtrans-http3-16 Section 3.2)
    pub fn set_previous_wt_settings(&mut self, settings: crate::webtransport::Settings) {
        self.previous_wt_settings = Some(settings);
    }

    /// WebTransport transport parameter の検証結果を注入する
    ///
    /// WebTransport CONNECT の送信/受信前に呼び出す必要がある。
    /// 呼び出さない場合、WebTransport セッションの確立は拒否される。
    pub fn set_webtransport_transport_verified(
        &mut self,
        max_datagram_frame_size_nonzero: bool,
        reset_stream_at_supported: bool,
    ) -> Result<(), Error> {
        if !max_datagram_frame_size_nonzero {
            return Err(Error::ConnectionError(ErrorCode::InternalError));
        }
        self.wt_transport_verified = true;
        self.wt_reset_stream_at_supported = reset_stream_at_supported;
        Ok(())
    }

    /// WebTransport transport parameter が検証済みかを取得する
    pub fn is_webtransport_transport_verified(&self) -> bool {
        self.wt_transport_verified
    }

    /// バッファリングされたストリーム/データグラムを配送する バッファリングの共通化
    ///
    /// `emit_header_events` と `send_response` の重複コードを共通化する。
    /// フロー制御違反があった場合は `true` を返す。
    /// (draft-ietf-webtrans-http3-15 Section 4.6, 5.4, 5.6)
    pub(crate) fn deliver_buffered_streams(&mut self, session_id: u64) -> bool {
        let Some(session) = self.wt_sessions.get_mut(&session_id) else {
            return false;
        };

        let buffered = session.take_buffered_streams();
        let mut buffered_events: Vec<Event> = Vec::new();
        let mut closed_buffered: Vec<(u64, bool)> = Vec::new();
        let mut fc_violation = false;

        for &buffered_stream_id in &buffered {
            let Some(entry) = session.take_buffered_stream_entry(buffered_stream_id) else {
                continue;
            };
            let is_bidi = entry.is_bidi;
            // ストリーム数 FC はデータ移動前にチェックする (違反時に entry を復元するため)
            if !session.check_received_stream(is_bidi) {
                // フロー制御違反: 違反したストリーム自身を含む未配送ストリームを
                // バッファに戻す (終了時の RESET 対象として残す)
                session.restore_buffered_stream(buffered_stream_id, entry);
                for &remaining_id in buffered
                    .iter()
                    .skip_while(|&&id| id != buffered_stream_id)
                    .skip(1)
                {
                    if let Some(remaining_entry) = session.take_buffered_stream_entry(remaining_id)
                    {
                        session.restore_buffered_stream(remaining_id, remaining_entry);
                    }
                }
                fc_violation = true;
                break;
            }
            let entry_data = entry.data;
            let entry_fin = entry.fin;
            session.add_received_stream(is_bidi);
            session.associate_stream(buffered_stream_id);
            // Open
            buffered_events.push(if is_bidi {
                Event::WebTransport(WebTransportEvent::BidiStreamOpen {
                    stream_id: buffered_stream_id,
                    session_id,
                })
            } else {
                Event::WebTransport(WebTransportEvent::UniStreamOpen {
                    stream_id: buffered_stream_id,
                    session_id,
                })
            });
            // Data
            if !entry_data.is_empty() {
                let data_len = entry_data.len() as u64;
                if !session.check_received_data(data_len) {
                    // フロー制御違反: このストリームの Data イベントは発火済みのため、
                    // 後続の未配送ストリームをバッファに戻す
                    for &remaining_id in buffered
                        .iter()
                        .skip_while(|&&id| id != buffered_stream_id)
                        .skip(1)
                    {
                        if let Some(remaining_entry) =
                            session.take_buffered_stream_entry(remaining_id)
                        {
                            session.restore_buffered_stream(remaining_id, remaining_entry);
                        }
                    }
                    // 違反したストリーム自身もバッファに戻す
                    // (entry_data は既に移動済みのため新規エントリを構築する)
                    {
                        let mut restore_entry = BufferedStreamEntry::new(is_bidi);
                        restore_entry.data = entry_data;
                        restore_entry.fin = entry_fin;
                        session.restore_buffered_stream(buffered_stream_id, restore_entry);
                    }
                    fc_violation = true;
                    break;
                }
                session.add_received_data_and_track(buffered_stream_id, data_len);
                buffered_events.push(if is_bidi {
                    Event::WebTransport(WebTransportEvent::BidiStreamData {
                        stream_id: buffered_stream_id,
                        data: entry_data,
                    })
                } else {
                    Event::WebTransport(WebTransportEvent::UniStreamData {
                        stream_id: buffered_stream_id,
                        data: entry_data,
                    })
                });
            }
            // End
            if entry_fin {
                session.on_remote_stream_closed(is_bidi);
                // 計上済み量の追跡を掃除する (ピア開始は FIN で登録解除される
                // ため、FIN 後の RESET は WT 経路に到達せず二重計上の機会がない)
                session.remove_stream_received_data(buffered_stream_id);
                closed_buffered.push((buffered_stream_id, is_bidi));
                buffered_events.push(if is_bidi {
                    Event::WebTransport(WebTransportEvent::BidiStreamEnd {
                        stream_id: buffered_stream_id,
                    })
                } else {
                    Event::WebTransport(WebTransportEvent::UniStreamEnd {
                        stream_id: buffered_stream_id,
                    })
                });
            }
        }

        // イベントを配送
        for ev in buffered_events {
            self.events.push_back(ev);
        }
        for (sid, is_bidi) in closed_buffered {
            if is_bidi {
                self.wt_bidi_streams.remove(&sid);
            } else {
                self.wt_uni_streams.remove(&sid);
            }
        }

        // バッファリングされていたデータグラムを配送 (Section 4.6)
        if !fc_violation {
            let buffered_datagrams = session.take_buffered_datagrams();
            for payload in buffered_datagrams {
                self.events
                    .push_back(Event::WebTransport(WebTransportEvent::Datagram {
                        session_id,
                        payload,
                    }));
            }
        }

        fc_violation
    }

    /// WebTransport SETTINGS を受信していない場合は `None`。
    /// (draft-ietf-webtrans-http3-02/07/14/15 Section 3.1)
    pub(crate) fn peer_wt_draft_version(&self) -> Option<DraftVersion> {
        self.peer_settings
            .as_ref()
            .and_then(|s| s.wt_settings.as_ref())
            .and_then(|wt| wt.detect_draft_pattern())
    }

    /// ローカルとピアが共に広告している中で最も新しい WebTransport ドラフトを返す
    ///
    /// バージョンネゴシエーションは「両エンドポイントが広告する集合の交差から
    /// 最も新しいものを選ぶ」(draft-ietf-webtrans-http3-15 Section 7.1)。
    /// 将来のドラフトで変更される可能性がある
    pub(crate) fn negotiated_wt_draft_version(&self) -> Option<DraftVersion> {
        self.mutually_advertised_wt_drafts().into_iter().next()
    }

    /// ローカルとピアが共に広告している WebTransport ドラフトを新しい順に返す
    ///
    /// (draft-ietf-webtrans-http3-15 Section 7.1)
    /// 将来のドラフトで変更される可能性がある
    pub(crate) fn mutually_advertised_wt_drafts(&self) -> Vec<DraftVersion> {
        let Some(local) = self.local_settings.wt_settings.as_ref() else {
            return Vec::new();
        };
        let Some(peer) = self
            .peer_settings
            .as_ref()
            .and_then(|s| s.wt_settings.as_ref())
        else {
            return Vec::new();
        };
        let advertises = |s: &crate::webtransport::Settings, d: DraftVersion| -> bool {
            match d {
                DraftVersion::Draft15 => s.wt_enabled.get() > 0,
                DraftVersion::Draft14 => s.wt_max_sessions_draft14.is_some_and(|v| v.get() > 0),
                DraftVersion::Draft07 => s
                    .webtransport_max_sessions_draft07
                    .is_some_and(|v| v.get() > 0),
                DraftVersion::Draft02 => s.enable_webtransport_draft02 == Some(true),
            }
        };
        [
            DraftVersion::Draft15,
            DraftVersion::Draft14,
            DraftVersion::Draft07,
            DraftVersion::Draft02,
        ]
        .into_iter()
        .filter(|d| advertises(local, *d) && advertises(peer, *d))
        .collect()
    }

    /// WebTransport の能力ネゴシエーションが完了しているかどうかを判定する
    ///
    /// (draft-ietf-webtrans-http3-02/07/14/15 Section 3.1)
    pub(crate) fn is_wt_fully_negotiated(&self) -> bool {
        // ローカルが WebTransport を有効にしているか
        if !self.local_settings.is_webtransport_enabled() {
            return false;
        }
        // peer の SETTINGS を受信済みか
        let peer = match self.peer_settings.as_ref() {
            Some(p) => p,
            None => return false,
        };
        // H3_DATAGRAM が有効か (両端共通の必須条件)
        if peer.h3_datagram != Some(true) {
            return false;
        }
        // QUIC transport parameter が検証済みか
        if !self.wt_transport_verified {
            return false;
        }
        // クライアント側: peer (サーバー) が WebTransport を広告し、ENABLE_CONNECT_PROTOCOL
        // と必要なら reset_stream_at もそろっているかを検証する。
        if self.role == Role::Client {
            if !peer.is_webtransport_enabled() {
                return false;
            }
            let draft = match self.peer_wt_draft_version() {
                Some(v) => v,
                None => return false,
            };
            if draft.requires_enable_connect_protocol()
                && peer.enable_connect_protocol != Some(true)
            {
                return false;
            }
            if draft.requires_reset_stream_at() && !self.wt_reset_stream_at_supported {
                return false;
            }
            return true;
        }
        // サーバー側: ローカルが draft-15 を採用している場合のみ peer の WT 広告を要求する
        // (Section 7.1: 双方が SETTINGS_WT_ENABLED を送る MUST)。
        // draft-14 以前は Safari 等の interop のため peer 広告を要求しない
        // (nghttp3 lib/nghttp3_conn.c の TODO コメント参照)。
        let local_draft = self.local_settings.webtransport_draft_pattern();
        if matches!(local_draft, Some(DraftVersion::Draft15)) && !peer.is_webtransport_enabled() {
            return false;
        }
        true
    }

    /// WebTransport フロー制御が両端で有効かどうかを判定する
    ///
    /// (draft-ietf-webtrans-http3-15 Section 5.1)
    pub(crate) fn is_wt_flow_control_enabled(&self) -> bool {
        let local_wt = self.local_settings.wt_settings.as_ref();
        let peer_wt = self
            .peer_settings
            .as_ref()
            .and_then(|s| s.wt_settings.as_ref());

        match (local_wt, peer_wt) {
            (Some(local), Some(peer)) => local.flow_control_enabled_with_peer(peer),
            _ => false,
        }
    }

    /// WebTransport セッションを終了する
    ///
    /// CONNECT stream の FIN、RESET_STREAM、WT_CLOSE_SESSION 受信時に呼ばれる。
    /// セッションエントリを `wt_sessions` から除去し、終了済みセッション ID を
    /// tombstone に記録する (draft-ietf-webtrans-http3-16 Section 6)。
    pub(crate) fn terminate_wt_session_with(
        &mut self,
        session_id: u64,
        error_code: u64,
        close_error_code: u32,
        close_message: String,
    ) {
        if let Some(session) = self.wt_sessions.get_mut(&session_id) {
            if session.state == WtSessionState::Closed {
                return;
            }
            session.state = WtSessionState::Closed;

            // 関連する全ストリーム ID を収集
            let associated_stream_ids: Vec<u64> =
                session.associated_streams.iter().copied().collect();

            // バッファリングされたストリームも対象に含める
            let buffered_stream_ids = session.take_buffered_streams();

            // バッファリングされたストリームのエントリを除去する
            for sid in &buffered_stream_ids {
                session.buffered_stream_entries.remove(sid);
            }

            // wt_uni_streams / wt_bidi_streams から除去する前に reliable size を計算する。
            // (draft-ietf-webtrans-http3-15 Section 6 / Section 4.4 / Section 5.4)
            let mut reset_streams: Vec<WtStreamReset> =
                Vec::with_capacity(associated_stream_ids.len() + buffered_stream_ids.len());
            for &sid in &associated_stream_ids {
                let reliable_size = self.wt_stream_header_len(sid);
                reset_streams.push(WtStreamReset {
                    stream_id: sid,
                    reliable_size,
                });
            }
            for sid in &associated_stream_ids {
                self.wt_uni_streams.remove(sid);
                self.wt_bidi_streams.remove(sid);
            }
            for sid in buffered_stream_ids {
                reset_streams.push(WtStreamReset {
                    stream_id: sid,
                    reliable_size: 0,
                });
            }

            self.events
                .push_back(Event::WebTransport(WebTransportEvent::SessionClosed {
                    session_id,
                    reset_streams,
                    error_code,
                    close_error_code,
                    close_message,
                }));
        }

        // セッションエントリを除去し、終了済みセッション ID を tombstone に記録する
        // 終了後に届く DATA / FIN / RESET / 新規ストリーム / データグラムの拒否・破棄と
        // zombie Pending セッションの再生成防止に使う
        // (draft-ietf-webtrans-http3-16 Section 6)
        if self.wt_sessions.remove(&session_id).is_some() {
            self.closed_wt_sessions.insert(session_id);
        }
    }

    /// WebTransport セッションを WT_SESSION_GONE で終了する
    ///
    /// (draft-ietf-webtrans-http3-15 Section 6)
    pub(crate) fn terminate_wt_session(&mut self, session_id: u64) {
        self.terminate_wt_session_with(
            session_id,
            WtErrorCode::SessionGone as u64,
            0,
            String::new(),
        );
    }

    /// WebTransport ストリームをセッションに関連付ける、またはバッファリングする
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.6, 6)
    pub(crate) fn associate_or_buffer_stream(
        &mut self,
        stream_id: u64,
        session_id: u64,
        is_bidi: bool,
    ) -> Result<AssocOutcome, ()> {
        if let Some(session) = self.wt_sessions.get_mut(&session_id) {
            match session.state {
                WtSessionState::Established => {
                    session.associate_stream(stream_id);
                    Ok(AssocOutcome::Established)
                }
                WtSessionState::Pending => {
                    if session.buffer_stream(stream_id, is_bidi) {
                        Ok(AssocOutcome::Buffered)
                    } else {
                        Ok(AssocOutcome::BufferOverflow)
                    }
                }
                WtSessionState::Draining => Err(()),
                WtSessionState::Closed => Err(()),
            }
        } else {
            // クライアントは自身が開始していない session_id を拒否する
            // (draft-ietf-webtrans-http3-15 Section 4.6)
            if self.role == Role::Client {
                return Err(());
            }

            // 終了済みセッションへの新規ストリームは拒否する
            // (draft-ietf-webtrans-http3-16 Section 6: 終了済みセッションには
            //  新規ストリームを送ってはならない (MUST NOT open any new streams)。
            //  zombie Pending セッションの再生成を防ぐ)
            if self.closed_wt_sessions.contains(&session_id) {
                return Err(());
            }

            // サーバーが GOAWAY を送信済みの場合、その境界以降の session_id に対する
            // 新規 WebTransport セッションは受け入れない
            // (draft-ietf-webtrans-http3-15 Section 4.7)。
            if let Some(last_id) = self.last_sent_goaway_id
                && session_id >= last_id.get()
            {
                return Err(());
            }

            // 接続単位の Pending セッション上限を超過した場合は拒否する
            // (draft-ietf-webtrans-http3-15 Section 4.6 / DoS 対策)
            if self.count_pending_wt_sessions() >= WT_MAX_PENDING_SESSIONS {
                return Ok(AssocOutcome::BufferOverflow);
            }

            // サーバー側: セッション未登録だがストリームが先に到着した
            // 新規 Pending セッションを作成してバッファリング (Section 4.6)
            let mut session = WtSession::new();
            let buffered = session.buffer_stream(stream_id, is_bidi);
            self.wt_sessions.insert(session_id, session);
            if buffered {
                Ok(AssocOutcome::Buffered)
            } else {
                Ok(AssocOutcome::BufferOverflow)
            }
        }
    }

    /// 現在 Pending 状態の WebTransport セッション数を数える
    ///
    /// `WT_MAX_PENDING_SESSIONS` の上限判定に使用する。
    pub(crate) fn count_pending_wt_sessions(&self) -> usize {
        self.wt_sessions
            .values()
            .filter(|s| s.state == WtSessionState::Pending)
            .count()
    }

    /// 現在 active な WebTransport セッション数を数える (Pending + Established + Draining)
    ///
    /// draft-ietf-webtrans-http3-15 Section 5.1 / 5.2 の
    /// 「フロー制御無効時は同時に 1 セッションまで」の判定に使用する。
    /// 将来のドラフトで定義が変更される可能性がある。
    pub(crate) fn count_active_wt_sessions(&self) -> usize {
        self.wt_sessions
            .values()
            .filter(|s| {
                s.state == WtSessionState::Pending
                    || s.state == WtSessionState::Established
                    || s.state == WtSessionState::Draining
            })
            .count()
    }

    /// WebTransport セッションのデータ消費を通知する
    ///
    /// (draft-ietf-webtrans-http3-15 Section 5.6)
    pub fn wt_data_consumed(&mut self, session_id: u64, bytes: u64) {
        if let Some(session) = self.wt_sessions.get_mut(&session_id) {
            session.on_data_consumed(bytes);
        }
    }

    /// WebTransport セッションの送信待ちカプセルを取り出す
    ///
    /// (draft-ietf-webtrans-http3-15 Section 5.6)
    pub fn take_wt_pending_capsules(
        &mut self,
        session_id: u64,
    ) -> Vec<crate::webtransport::Capsule> {
        if let Some(session) = self.wt_sessions.get_mut(&session_id) {
            session.take_pending_capsules()
        } else {
            Vec::new()
        }
    }

    /// WebTransport セッションのフロー制御が有効かどうかを取得する
    pub fn wt_session_flow_control_enabled(&self, session_id: u64) -> bool {
        self.wt_sessions
            .get(&session_id)
            .is_some_and(|s| s.flow_control_enabled)
    }

    /// RESET_STREAM 受信時の WebTransport 伝播処理 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-16 Section 4.4 / Section 6)
    /// CONNECT stream の RESET_STREAM はセッション終了、データストリームの
    /// RESET_STREAM はセッションに通知する。SETTINGS 未着で保留中の 0x41 bidi は
    /// 保留エントリを破棄して `ignored_pre_negotiation_wt_bidi` に記録し `true` を返す。
    /// 既に拒否済みで `ignored_pre_negotiation_wt_bidi` に登録済みの 0x41 bidi は
    /// 静かに吸収して `true` を返す。WT データストリームとして登録済みの
    /// ローカル開始 uni への RESET_STREAM は RFC 9000 Section 19.4 の
    /// STREAM_STATE_ERROR に相当する不正入力として静かに吸収し `true` を返す。
    /// 非 WT ストリームは `false` を返す。
    pub(crate) fn handle_wt_stream_reset(
        &mut self,
        stream_id: u64,
        error_code: u64,
        final_size: u64,
    ) -> bool {
        if self.wt_sessions.contains_key(&stream_id) {
            self.terminate_wt_session(stream_id);
            return true;
        }
        // 終了済みセッションの CONNECT ストリームへの RESET_STREAM は静かに無視する
        // (RFC 9000 Section 4.4: RESET_STREAM 受信時はストリームの状態を破棄し、
        //  以降のデータを無視する)
        if self.closed_wt_sessions.contains(&stream_id) {
            return true;
        }

        // SETTINGS 未着の WT CONNECT ストリームの RESET は保留エントリを破棄する
        if self.deferred_wt_connects.remove(&stream_id).is_some() {
            return true;
        }

        // SETTINGS 未着の 0x41 bidi ストリームの RESET は保留エントリを破棄する。
        // 保留を破棄したあと SETTINGS 受信時の `process_pending_wt_bidi_pre_negotiation`
        // で再ディスパッチされないようにし、`ignored_pre_negotiation_wt_bidi` に記録して
        // 後続チャンクも破棄する
        if self
            .pending_wt_bidi_pre_negotiation
            .remove(&stream_id)
            .is_some()
        {
            self.ignored_pre_negotiation_wt_bidi.insert(stream_id);
            return true;
        }
        // 既に上限拒否等で ignored 化されているストリームの RESET も静かに吸収する
        // (RFC 9000 Section 4.4)
        if self.ignored_pre_negotiation_wt_bidi.contains(&stream_id) {
            return true;
        }

        // バッファリング中ストリームの RESET: バッファからエントリを除去する
        // (draft-ietf-webtrans-http3-16 Section 4.4 / 4.6)
        for session in self.wt_sessions.values_mut() {
            if session.remove_buffered_stream(stream_id) {
                // バッファから除去した場合は StreamReset イベントを発火しない
                // (RESET_STREAM は受信データの破棄を意味し、上位層への通知は不要)
                return true;
            }
        }

        // 除去前に session_id とストリーム種別を解決する
        // (データ FC 計上とヘッダー長の計算に必要なため)
        let Some(session_id) = self
            .wt_uni_streams
            .get(&stream_id)
            .copied()
            .or_else(|| self.wt_bidi_streams.get(&stream_id).copied())
        else {
            return false;
        };
        let is_bidi = self.wt_bidi_streams.contains_key(&stream_id);

        // WT データストリームとして登録済みのローカル開始 uni (ピアは受信専用) への
        // RESET_STREAM は RFC 9000 Section 19.4 の STREAM_STATE_ERROR に相当する不正入力。
        // QUIC 層で拒否されるのが通常経路だが、統合層の不具合や将来の QUIC 実装変更で
        // 到達しても不正に処理しないよう、ここで静かに吸収する。ピアが開いていない
        // ストリームの WT_MAX_STREAMS クレジットを回復してはならない
        // (draft-ietf-webtrans-http3-16 Section 5.3)
        let kind = crate::stream::StreamKind::from_stream_id(stream_id);
        if self.is_local_initiated_uni(kind) {
            return true;
        }

        // データ FC の計上と自動消費 (draft-ietf-webtrans-http3-16 Section 5.4)
        // 超過でセッション終了した場合は StreamReset イベントを発火しない
        // (SessionClosed の reset_streams に含まれるため)
        if self.account_wt_stream_reset(session_id, stream_id, final_size) {
            return true;
        }

        // ストリーム数クレジットの回復 (ピア開始のみ。WT_MAX_STREAMS は
        // ピアが開くストリーム数の制限。draft-ietf-webtrans-http3-16 Section 5.3)
        let local_initiated = self.is_local_initiated_bidi(kind);
        if let Some(session) = self.wt_sessions.get_mut(&session_id) {
            if !local_initiated {
                session.on_remote_stream_closed(is_bidi);
            }
            // 計上済み量の追跡を掃除し、ストリーム関連付けを解除する
            session.remove_stream_received_data(stream_id);
            session.disassociate_stream(stream_id);
        }

        // 登録を除去する
        self.wt_uni_streams.remove(&stream_id);
        self.wt_bidi_streams.remove(&stream_id);
        self.events
            .push_back(Event::WebTransport(WebTransportEvent::StreamReset {
                session_id,
                stream_id,
                error_code,
                final_size,
            }));
        true
    }

    /// RESET_STREAM 受信時のデータフロー制御の計上
    ///
    /// final_size からストリームヘッダー長と既計上分を引いた残差を受信側
    /// データ FC に計上し、RESET により破棄扱いとなるデータを自動消費として
    /// 扱ってウィンドウ (WT_MAX_DATA) を回復する (RFC 9000 Section 19.4)。
    /// 残差が FC 超過の場合は WT_FLOW_CONTROL_ERROR でセッション終了し、
    /// `true` を返す (draft-ietf-webtrans-http3-16 Section 5.6.4)。
    fn account_wt_stream_reset(
        &mut self,
        session_id: u64,
        stream_id: u64,
        final_size: u64,
    ) -> bool {
        // ヘッダー減算はピアがヘッダーを送った場合のみ適用する
        // (ローカル開始 bidi は開始側 (ローカル) がヘッダーを送るため、
        //  ピアの final_size にヘッダーは含まれない。ローカル開始 uni は
        //  呼び出し元の早期 return で本関数に到達しない)
        let kind = crate::stream::StreamKind::from_stream_id(stream_id);
        let header_len = if self.is_local_initiated_bidi(kind) {
            0
        } else {
            self.wt_stream_header_len(stream_id)
        };
        // 既計上分を引いた残差 (二重計上を防ぐ。受信済みが final_size を
        // 超えるケースは QUIC 層の不正 (RFC 9000 Section 4.5 の
        // FINAL_SIZE_ERROR) であり、残差 0 として扱う)
        let accounted = self
            .wt_sessions
            .get(&session_id)
            .map(|s| s.get_stream_received_data(stream_id))
            .unwrap_or(0);
        let residual = final_size
            .saturating_sub(header_len)
            .saturating_sub(accounted);

        let Some(session) = self.wt_sessions.get_mut(&session_id) else {
            return false;
        };
        // 超過検出 (draft-ietf-webtrans-http3-16 Section 5.6.4)
        if !session.check_received_data(residual) {
            self.terminate_wt_session_with(
                session_id,
                WtErrorCode::FlowControlError as u64,
                0,
                String::new(),
            );
            return true;
        }
        session.add_received_data(residual);
        // RESET により破棄扱いとなりアプリが消費を報告しないため、
        // 残差を自動消費してウィンドウを回復する (RFC 9000 Section 19.4)
        session.on_data_consumed(residual);
        false
    }

    /// STOP_SENDING 受信時の WebTransport 伝播処理 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-16 Section 4.4 / Section 6)
    /// CONNECT stream の STOP_SENDING はセッション終了、データストリームの
    /// STOP_SENDING はセッションに通知する。SETTINGS 未着で保留中の 0x41 bidi、
    /// および既に拒否済みの 0x41 bidi ストリームは静かに `true` を返して吸収する。
    /// 非 WT ストリームは `false` を返す。
    pub(crate) fn handle_wt_stop_sending(&mut self, stream_id: u64, error_code: u64) -> bool {
        if self.wt_sessions.contains_key(&stream_id) {
            self.terminate_wt_session(stream_id);
            return true;
        }
        // 終了済みセッションの CONNECT ストリームへの STOP_SENDING は静かに無視する
        // (draft-ietf-webtrans-http3-16 Section 6)
        if self.closed_wt_sessions.contains(&stream_id) {
            return true;
        }
        // SETTINGS 未着で保留中の 0x41 bidi、または上限拒否等で既に ignored 化された
        // 0x41 bidi ストリームへの STOP_SENDING は静かに吸収する (上位層に見えていない
        // ストリームへの STOP_SENDING を汎用イベントで通知しないため)
        if self
            .pending_wt_bidi_pre_negotiation
            .contains_key(&stream_id)
            || self.ignored_pre_negotiation_wt_bidi.contains(&stream_id)
        {
            return true;
        }
        if let Some(session_id) = self
            .wt_uni_streams
            .get(&stream_id)
            .copied()
            .or_else(|| self.wt_bidi_streams.get(&stream_id).copied())
        {
            self.events
                .push_back(Event::WebTransport(WebTransportEvent::StreamStopSending {
                    session_id,
                    stream_id,
                    error_code,
                }));
            return true;
        }
        false
    }

    /// WebTransport CONNECT リクエストの前提条件検証 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 3.1, 4.6)
    /// クライアントが WT CONNECT を送信する前に peer の WebTransport サポートを確認する。
    /// 非 WT CONNECT の場合は `Ok(())` を返す。
    pub(crate) fn validate_wt_connect_request(
        &self,
        headers: &[crate::qpack::Header],
    ) -> Result<(), Error> {
        if !super::is_webtransport_connect(headers) {
            return Ok(());
        }

        // RFC 9297 Section 3.2: Capsule Protocol を使用するメッセージに
        // Content-Length / Content-Type ヘッダーを付与してはならない (MUST NOT)。
        // 送信側も違反メッセージを生成しない
        if has_forbidden_capsule_headers(headers) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        let peer = self
            .peer_settings
            .as_ref()
            .ok_or(Error::WtSetup(WtSetupError::PeerSettingsNotReceived))?;
        if !peer.is_webtransport_enabled() {
            return Err(Error::WtSetup(WtSetupError::WebTransportNotEnabled));
        }
        let draft = self
            .peer_wt_draft_version()
            .ok_or(Error::WtSetup(WtSetupError::UnknownDraftVersion))?;
        if draft.requires_enable_connect_protocol() && peer.enable_connect_protocol != Some(true) {
            return Err(Error::WtSetup(WtSetupError::EnableConnectProtocolMissing));
        }
        if peer.h3_datagram != Some(true) {
            return Err(Error::WtSetup(WtSetupError::H3DatagramNotEnabled));
        }
        if !self.wt_transport_verified {
            return Err(Error::WtSetup(WtSetupError::TransportNotVerified));
        }
        if draft.requires_reset_stream_at() && !self.wt_reset_stream_at_supported {
            return Err(Error::WtSetup(WtSetupError::ResetStreamAtNotSupported));
        }
        let expected_proto = draft.protocol_value().as_bytes();
        let proto_ok = headers
            .iter()
            .any(|h| h.name() == b":protocol" && h.value() == expected_proto);
        if !proto_ok {
            return Err(Error::WtSetup(WtSetupError::ProtocolMismatch));
        }
        if !self.is_wt_flow_control_enabled() && self.count_active_wt_sessions() >= 1 {
            return Err(Error::StreamError(ErrorCode::RequestRejected));
        }

        Ok(())
    }

    /// サーバー側: WebTransport CONNECT リクエストの前提条件検証 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 3.1, 7.1)
    /// 非 WT CONNECT の場合は `Ok(())` を返す。
    pub(crate) fn validate_wt_connect_request_server(
        &self,
        stream_id: u64,
        headers: &[crate::qpack::Header],
    ) -> Result<(), Error> {
        if self.role != Role::Server || !super::is_webtransport_connect(headers) {
            return Ok(());
        }

        // RFC 9297 Section 3.2: Capsule Protocol を使用するメッセージに
        // Content-Length / Content-Type ヘッダーを付与してはならない (MUST NOT)。
        // 違反は malformed として H3_MESSAGE_ERROR で拒否する。
        // (Transfer-Encoding は接続固有ヘッダーとして全リクエストで既に拒否される)
        if has_forbidden_capsule_headers(headers) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        if !self.local_settings.is_webtransport_enabled() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // peer_settings 未着時は検証をスキップする (emit_header_events 側で保留する)
        // (draft-ietf-webtrans-http3-16 Section 3.1 / 4.6 / 7.1)
        let Some(peer) = self.peer_settings.as_ref() else {
            return Ok(());
        };
        let local_draft = self.local_settings.webtransport_draft_pattern();
        if matches!(
            local_draft,
            Some(crate::webtransport::DraftVersion::Draft15)
        ) && !peer.is_webtransport_enabled()
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        if peer.h3_datagram != Some(true) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        if !self.wt_transport_verified {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        let draft = self.negotiated_wt_draft_version();
        if draft.is_some_and(|d| d.requires_reset_stream_at()) && !self.wt_reset_stream_at_supported
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        if draft.is_some() {
            let proto_header = headers
                .iter()
                .find(|h| h.name() == b":protocol")
                .map(|h| h.value())
                .ok_or(Error::StreamError(ErrorCode::MessageError))?;
            let mutual = self.mutually_advertised_wt_drafts();
            let proto_ok = mutual
                .iter()
                .any(|d| d.protocol_value().as_bytes() == proto_header);
            if !proto_ok {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }
        }
        let scheme_is_https = headers
            .iter()
            .any(|h| h.name() == b":scheme" && h.value() == b"https");
        if !scheme_is_https {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        if !self.is_wt_flow_control_enabled() {
            let already_has_pending_for_this = self
                .wt_sessions
                .get(&stream_id)
                .is_some_and(|s| s.state == WtSessionState::Pending);
            let active = self.count_active_wt_sessions();
            let other_active = if already_has_pending_for_this {
                active.saturating_sub(1)
            } else {
                active
            };
            if other_active >= 1 {
                return Err(Error::StreamError(ErrorCode::RequestRejected));
            }
        }

        Ok(())
    }

    /// サーバー側: WebTransport CONNECT セッションの Pending 登録 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 3, 3.3, 4.6)
    /// 非 WT CONNECT の場合は何もしない。
    pub(crate) fn register_wt_connect_session(
        &mut self,
        stream_id: u64,
        headers: &[crate::qpack::Header],
    ) {
        if self.role != Role::Server || !super::is_webtransport_connect(headers) {
            return;
        }

        let session = self
            .wt_sessions
            .entry(stream_id)
            .or_insert_with(WtSession::new);
        for h in headers {
            if h.name() == b"wt-available-protocols" {
                if let Ok(value) = std::str::from_utf8(h.value()) {
                    session.available_protocols =
                        crate::webtransport::ConnectRequest::parse_available_protocols(value);
                }
                break;
            }
        }
        if let Some(stream) = self.streams.get_mut(&stream_id) {
            stream.set_connect();
            // WT CONNECT ストリームの DATA は Capsule データであり recv_body に累積しない
            stream.set_wt_connect();
        }
    }

    /// クライアント側: WebTransport CONNECT の 2xx レスポンス処理 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 3, 3.3, 5.1, 5.5, 5.6)
    /// WT-Protocol 検証、セッション確立、フロー制御初期化、バッファリング配送を行う。
    /// 非 WT セッションまたは非 2xx の場合は何もしない。
    pub(crate) fn handle_wt_connect_response(
        &mut self,
        stream_id: u64,
        headers: &[crate::qpack::Header],
    ) -> Result<(), Error> {
        if self.role != Role::Client || !super::is_success_status(headers) {
            return Ok(());
        }

        // RFC 9297 Section 3.2: Capsule Protocol を使用するレスポンスに
        // Content-Length / Content-Type ヘッダーを付与してはならない (MUST NOT)。
        // 違反は malformed として H3_MESSAGE_ERROR で拒否する。
        // (WT セッションのレスポンスのみが対象)
        if self.wt_sessions.contains_key(&stream_id) && has_forbidden_capsule_headers(headers) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // RFC 9297 Section 3.2: HTTP status codes 204 (No Content) / 205 (Reset
        // Content) / 206 (Partial Content) は Capsule Protocol を使用する
        // レスポンスに付与してはならない (MUST NOT)。違反は malformed。
        // (WT セッションのレスポンスのみが対象。通常 HTTP の 204/205/206 は
        //  この制約の対象外)
        if self.wt_sessions.contains_key(&stream_id) && is_forbidden_capsule_status(headers) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // WT-Protocol 検証 (Section 3.3)
        let wt_protocol_invalid = if let Some(session) = self.wt_sessions.get(&stream_id) {
            if session.state == WtSessionState::Pending {
                let selected = headers.iter().find_map(|h| {
                    if h.name() == b"wt-protocol" {
                        std::str::from_utf8(h.value())
                            .ok()
                            .and_then(crate::webtransport::ConnectResponse::parse_protocol)
                    } else {
                        None
                    }
                });
                if session.available_protocols.is_empty() {
                    selected.is_some()
                } else {
                    match &selected {
                        None => true,
                        Some(proto) => !session.available_protocols.contains(proto),
                    }
                }
            } else {
                false
            }
        } else {
            false
        };

        if wt_protocol_invalid {
            self.terminate_wt_session_with(
                stream_id,
                WtErrorCode::AlpnError as u64,
                0,
                String::new(),
            );
            return Ok(());
        }

        let fc_enabled = self.is_wt_flow_control_enabled();
        let queue_initial_capsules = fc_enabled && self.peer_requires_initial_wt_capsules();
        let mut session_established = false;

        if let Some(session) = self.wt_sessions.get_mut(&stream_id)
            && session.state == WtSessionState::Pending
        {
            session.flow_control_enabled = fc_enabled;
            if let Some(wt) = &self.local_settings.wt_settings {
                session.initialize_flow_control(wt, queue_initial_capsules);
            }
            session.state = WtSessionState::Established;
            session_established = true;
        }

        if session_established {
            if let Some(stream) = self.streams.get_mut(&stream_id) {
                stream.set_connect();
            }
            self.events
                .push_back(Event::WebTransport(WebTransportEvent::SessionEstablished {
                    session_id: stream_id,
                    flow_control_enabled: fc_enabled,
                }));
            let fc_violation = self.deliver_buffered_streams(stream_id);
            if fc_violation {
                self.terminate_wt_session_with(
                    stream_id,
                    WtErrorCode::FlowControlError as u64,
                    0,
                    String::new(),
                );
            }
        }

        Ok(())
    }

    /// サーバー側: WebTransport 2xx レスポンスの WT-Protocol 検証 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 3.3)
    /// 非 WT セッションまたは非 2xx の場合は `Ok(())` を返す。
    pub(crate) fn validate_wt_response_protocol(
        &self,
        stream_id: u64,
        headers: &[crate::qpack::Header],
    ) -> Result<(), Error> {
        if !super::is_success_status(headers) {
            return Ok(());
        }
        let Some(session) = self.wt_sessions.get(&stream_id) else {
            return Ok(());
        };

        // RFC 9297 Section 3.2: Capsule Protocol を使用するレスポンスに
        // Content-Length / Content-Type ヘッダーを付与してはならない (MUST NOT)。
        // 送信側も違反メッセージを生成しない (受信側は malformed として拒否する)
        if has_forbidden_capsule_headers(headers) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // RFC 9297 Section 3.2: HTTP status codes 204 / 205 / 206 は Capsule
        // Protocol を使用するレスポンスに付与してはならない (MUST NOT)。
        // 送信側も違反メッセージを生成しない
        if is_forbidden_capsule_status(headers) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        let selected = headers.iter().find_map(|h| {
            if h.name() == b"wt-protocol" {
                std::str::from_utf8(h.value())
                    .ok()
                    .and_then(crate::webtransport::ConnectResponse::parse_protocol)
            } else {
                None
            }
        });

        if session.available_protocols.is_empty() {
            if selected.is_some() {
                return Err(Error::ConnectionError(ErrorCode::InternalError));
            }
        } else {
            match &selected {
                None => {
                    return Err(Error::ConnectionError(ErrorCode::InternalError));
                }
                Some(proto) => {
                    if !session.available_protocols.contains(proto) {
                        return Err(Error::ConnectionError(ErrorCode::InternalError));
                    }
                }
            }
        }

        Ok(())
    }

    /// サーバー側: WebTransport CONNECT に対する 2xx レスポンス送信時のセッション確立
    /// WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 3, 5.1, 5.5, 5.6)
    /// 非 WT セッションまたは非 2xx の場合は何もしない。
    ///
    /// 楽観的カプセル送信でバッファリングされたデータが不正な場合
    /// (`WT_CLOSE_SESSION` 後の追加バイト等) は `Error::StreamError(MessageError)` を
    /// 返す。呼び出し側は CONNECT ストリームを `H3_MESSAGE_ERROR` で reset すること
    /// (draft-ietf-webtrans-http3-16 Section 6)。
    pub(crate) fn establish_wt_session_server(
        &mut self,
        stream_id: u64,
        headers: &[crate::qpack::Header],
    ) -> Result<(), Error> {
        if self.role != Role::Server || !super::is_success_status(headers) {
            return Ok(());
        }

        let fc_enabled = self.is_wt_flow_control_enabled();
        let queue_initial_capsules = fc_enabled && self.peer_requires_initial_wt_capsules();
        let mut session_established = false;

        if let Some(session) = self.wt_sessions.get_mut(&stream_id)
            && session.state == WtSessionState::Pending
        {
            session.flow_control_enabled = fc_enabled;
            if let Some(wt) = &self.local_settings.wt_settings {
                session.initialize_flow_control(wt, queue_initial_capsules);
            }
            session.state = WtSessionState::Established;
            session_established = true;
        }

        if session_established {
            self.events
                .push_back(Event::WebTransport(WebTransportEvent::SessionEstablished {
                    session_id: stream_id,
                    flow_control_enabled: fc_enabled,
                }));
            let fc_violation = self.deliver_buffered_streams(stream_id);
            if fc_violation {
                self.terminate_wt_session_with(
                    stream_id,
                    WtErrorCode::FlowControlError as u64,
                    0,
                    String::new(),
                );
                return Ok(());
            }

            // 楽観的カプセル送信でバッファリングされたデータを処理する
            // (draft-ietf-webtrans-http3-16 Section 3.2)
            let has_buffered_capsules = self
                .wt_sessions
                .get(&stream_id)
                .is_some_and(|session| !session.capsule_buf.is_empty());
            if has_buffered_capsules && let Err(e) = self.process_wt_capsule_data(stream_id, &[]) {
                // 失敗したセッションを終了し、呼び出し元が CONNECT ストリームを
                // H3_MESSAGE_ERROR で reset できるようエラーを伝播する
                // (draft-ietf-webtrans-http3-16 Section 6)。
                self.terminate_wt_session(stream_id);
                return Err(e);
            }
        }

        Ok(())
    }

    /// SETTINGS 受信前に保留した WT CONNECT を処理する
    /// (draft-ietf-webtrans-http3-16 Section 3.1 / 4.6 / 7.1)
    pub(crate) fn process_deferred_wt_connects(&mut self) -> Result<(), Error> {
        let deferred: Vec<_> = self.deferred_wt_connects.drain().collect();
        for (stream_id, headers) in deferred {
            match self.validate_wt_connect_request_server(stream_id, &headers) {
                Ok(()) => {
                    self.register_wt_connect_session(stream_id, &headers);
                    // 保留中に届いた DATA を処理する
                    if let Some(stream) = self.streams.get_mut(&stream_id) {
                        let body = stream.take_received_body();
                        if !body.is_empty() {
                            // データをセッションの capsule_buf に移動する
                            self.append_wt_capsule_data(stream_id, &body)?;
                        }
                    }
                    // ヘッダーイベントを発行する
                    self.events.push_back(Event::HeadersBegin { stream_id });
                    for header in &headers {
                        self.events.push_back(Event::Header {
                            stream_id,
                            name: header.name().to_vec(),
                            value: header.value().to_vec(),
                        });
                    }
                    self.events.push_back(Event::HeadersEnd { stream_id });
                }
                Err(e) => {
                    // 検証失敗: ストリームエラーを返す
                    return Err(e);
                }
            }
        }
        Ok(())
    }
}
