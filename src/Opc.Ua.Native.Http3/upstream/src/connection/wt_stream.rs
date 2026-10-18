//! WebTransport ストリーム処理 connection/mod.rs からの分離
//!
//! WebTransport の単方向・双方向ストリームとデータグラムの処理を担う
//! `Connection` メソッド群。
//! (draft-ietf-webtrans-http3-15 Section 4.2, 4.3, 4.5, 4.6)

use crate::error::{Error, ErrorCode};
use crate::event::{Event, WebTransportEvent};
use crate::varint::VarInt;
use crate::webtransport::error::ErrorCode as WtErrorCode;

use super::wt_types::{AssocOutcome, WT_MAX_PENDING_SESSIONS, WtSession, WtSessionState};
use super::{Connection, Role};

impl Connection {
    /// QUIC DATAGRAM フレームのペイロードを受信
    ///
    /// Sans I/O パターンに基づき、QUIC スタックから受信した DATAGRAM フレームの
    /// ペイロードを Connection に注入する。セッション ID によるルーティング、
    /// バッファリング (Section 4.6)、セッション終了後の破棄を行う。
    /// (draft-ietf-webtrans-http3-15 Section 4.5)
    pub fn feed_datagram(&mut self, data: &[u8]) -> Result<(), Error> {
        if let Some(ref err) = self.error {
            return Err(err.clone());
        }

        // WebTransport ネゴシエーション完了を bidi / uni stream 経路と同じ
        // 一次関数で確認する (draft-ietf-webtrans-http3-15 Section 4.2 / 7.1)。
        // SETTINGS_H3_DATAGRAM の両端合意も `is_wt_fully_negotiated()` の中で
        // 確認している。未確立で受信した datagram は静かに破棄する (RFC 9297 と整合)。
        if !self.is_wt_fully_negotiated() {
            return Ok(());
        }

        // HTTP Datagram フォーマットをデコード
        // RFC 9297 Section 2.1: Quarter Stream ID が不正な (短すぎる、もしくは
        // 2^60-1 を超える) Datagram を受信した場合は H3_DATAGRAM_ERROR で接続を
        // 閉じなければならない。
        let datagram = match crate::webtransport::Datagram::decode(data) {
            Ok((d, _)) => d,
            Err(_) => {
                return Err(Error::ConnectionError(ErrorCode::H3DatagramError));
            }
        };

        let session_id = datagram.session_id;

        // session_id は client-initiated bidirectional stream ID でなければならない
        // (draft-ietf-webtrans-http3-15 Section 4.5)
        if session_id & 0x03 != 0x00 {
            return Err(Error::ConnectionError(ErrorCode::H3DatagramError));
        }

        // セッション状態に応じてルーティング
        if let Some(session) = self.wt_sessions.get_mut(&session_id) {
            match session.state {
                WtSessionState::Established | WtSessionState::Draining => {
                    // Draining 状態でも既存セッションのデータグラム受信は許可する
                    // (draft-ietf-webtrans-http3-15 Section 6)
                    self.events
                        .push_back(Event::WebTransport(WebTransportEvent::Datagram {
                            session_id,
                            payload: datagram.payload,
                        }));
                }
                WtSessionState::Pending => {
                    // セッション未確立: バッファリング (Section 4.6)
                    // 上限超過時は破棄 (ストリームと異なり RESET 不要)
                    session.buffer_datagram(datagram.payload);
                }
                WtSessionState::Closed => {
                    // セッション終了済み: 破棄 (Section 6)
                }
            }
        } else {
            // 終了済みセッションへのデータグラムは破棄する
            // (draft-ietf-webtrans-http3-16 Section 6 は終了後に新規データグラムを
            //  送らない送信者義務を定める (MUST NOT send any new datagrams)。
            //  受信側の扱いは実装判断として破棄し、zombie Pending セッションの
            //  再生成を防ぐ)
            if self.closed_wt_sessions.contains(&session_id) {
                return Ok(());
            }
            // クライアントは自身が開始していない session_id を拒否する
            // (draft-ietf-webtrans-http3-15 Section 4.6)
            if self.role == Role::Client {
                // 破棄 (ストリームと異なりデータグラムは RESET 不要)
            } else if let Some(last_id) = self.last_sent_goaway_id
                && session_id >= last_id.get()
            {
                // サーバーが GOAWAY を送信済みの場合、その境界以降の session_id に
                // 対する新規 WebTransport セッションは受け入れない
                // (draft-ietf-webtrans-http3-15 Section 4.7 / nghttp3
                //  lib/nghttp3_conn.c と整合)。datagram は破棄するだけでよい。
            } else if self.count_pending_wt_sessions() >= WT_MAX_PENDING_SESSIONS {
                // 接続単位の Pending セッション上限を超過: 破棄
                // (draft-ietf-webtrans-http3-15 Section 4.6 / DoS 対策)
            } else {
                // サーバー側: セッション未登録だがデータグラムが先に到着
                // 新規 Pending セッションを作成してバッファリング (Section 4.6)
                let mut session = WtSession::new();
                session.buffer_datagram(datagram.payload);
                self.wt_sessions.insert(session_id, session);
            }
        }

        Ok(())
    }

    /// WebTransport データグラムを送信用にエンコードする
    ///
    /// 指定されたセッションのデータグラムを HTTP Datagram フォーマットにエンコードして返す。
    /// 呼び出し側は返されたバイト列を QUIC DATAGRAM フレームで送信すること。
    /// (draft-ietf-webtrans-http3-15 Section 4.5)
    ///
    /// セッションが存在しないか Established でない場合はエラーを返す。
    pub fn send_datagram(&self, session_id: u64, payload: &[u8]) -> Result<Vec<u8>, Error> {
        // SETTINGS_H3_DATAGRAM のネゴシエーション確認 (RFC 9297 Section 2.1.1)
        // ローカルとピアの両方が SETTINGS_H3_DATAGRAM=1 を送受信済みでなければならない
        let local_datagram = self.local_settings.h3_datagram == Some(true);
        let peer_datagram = self
            .peer_settings
            .as_ref()
            .is_some_and(|s| s.h3_datagram == Some(true));
        if !local_datagram || !peer_datagram {
            return Err(Error::ConnectionError(ErrorCode::GeneralProtocolError));
        }

        // session_id の検証
        if session_id & 0x03 != 0x00 {
            return Err(Error::ConnectionError(ErrorCode::GeneralProtocolError));
        }

        let session = self
            .wt_sessions
            .get(&session_id)
            .ok_or(Error::ConnectionError(ErrorCode::GeneralProtocolError))?;

        // Draining 状態では新規データグラム送信を拒否する
        // (draft-ietf-webtrans-http3-15 Section 6)
        if session.state == WtSessionState::Draining {
            return Err(Error::WtSessionDraining(session_id));
        }
        if session.state != WtSessionState::Established {
            return Err(Error::ConnectionError(ErrorCode::GeneralProtocolError));
        }

        // session_id は wt_sessions に登録済み = 既に 4 の倍数として検証済み
        let datagram = crate::webtransport::Datagram::new(session_id, payload.to_vec())
            .map_err(|_| Error::ConnectionError(ErrorCode::InternalError))?;
        let mut buf = Vec::new();
        datagram.encode(&mut buf);
        Ok(buf)
    }

    /// ローカル開始の WebTransport ストリームを登録する
    ///
    /// アプリが自ら開いたストリームをセッションに関連付ける。対象は下記の 2 種類:
    ///
    /// - **双方向 (bidi)**: クライアント側は 0x00、サーバー側は 0x01 の下位 2 ビット
    ///   を持つストリーム ID (RFC 9000 Section 2.1)。登録後は `feed_stream` に渡した
    ///   受信データが HTTP/3 リクエストではなく WebTransport ストリームとして処理され、
    ///   `BidiStreamData` / `BidiStreamEnd` イベントが発火する
    /// - **単方向 (uni)**: クライアント側は 0x02、サーバー側は 0x03 の下位 2 ビット
    ///   を持つストリーム ID。ローカル開始 uni はローカルにとって送信専用のため
    ///   受信データは存在しない。登録することでピアからの STOP_SENDING を
    ///   `StreamStopSending` イベントとしてセッション ID 付きで通知できる
    ///   (draft-ietf-webtrans-http3-16 Section 4.4)。
    ///   ローカル開始 uni に対する STREAM (受信データ) / FIN の到着は QUIC 層で
    ///   STREAM_STATE_ERROR となり sans-I/O へは到達しない (RFC 9000 Section 19.8)。
    ///   登録済みローカル開始 uni への STREAM / FIN / RESET_STREAM が統合層の不具合等で
    ///   到達した場合は `handle_wt_uni_stream_data` / `handle_wt_uni_stream_fin` /
    ///   `handle_wt_stream_reset` が防御的に静かに吸収する (RFC 9000 Section 19.4 / 19.8)。
    ///
    /// 受信データにはストリームヘッダー (signal value + session ID) が含まれない。
    /// ヘッダーは開始側がストリーム先頭で 1 回だけ送信する
    /// (draft-ietf-webtrans-http3-16 Section 4.3)。
    ///
    /// 登録はセッション終了時まで維持され、`wt_stream_header_len` はヘッダー長を返し続ける。
    /// 自側が送信したヘッダーを含む RESET_STREAM_AT の reliable size 計算に必要なため
    /// (draft-ietf-webtrans-http3-16 Section 4.4)。セッション終了時、登録された bidi / uni は
    /// `WebTransportEvent::SessionClosed { reset_streams }` に stream_id と reliable_size 付きで
    /// 含まれ、統合層は RESET_STREAM_AT を送出する。
    ///
    /// bidi の FIN で WT_MAX_STREAMS クレジットは返却されない (WT_MAX_STREAMS はピアが
    /// 開くストリーム数の制限。Section 5.3)。
    ///
    /// 登録 API はストリームを開いた直後に呼び出すこと (bidi の場合、登録前に受信データが
    /// 到着すると HTTP/3 リクエストとして誤処理される可能性がある)。
    ///
    /// エラーになる条件:
    /// - ストリーム ID がローカル開始でない (ピア開始 bidi・ピア開始 uni)
    /// - セッション ID が `wt_sessions` に存在しない、または Established でない
    ///   (Pending セッションへの登録は拒否する。仮に登録すると受信データが
    ///   Section 4.6 のバッファリング経路に乗り、セッション確立時に受信
    ///   ストリームとして誤カウントされるため。draft-ietf-webtrans-http3-16
    ///   Section 4.6)
    /// - 同一ストリーム ID の二重登録 (bidi / uni 両方の登録先を確認する)
    /// - 既に HTTP/3 ストリームとして作成済み (`streams` に存在)
    ///
    /// 本 API はドラフト仕様 (draft-ietf-webtrans-http3-16) に基づくため、
    /// 将来変更される可能性がある。
    pub fn register_local_wt_stream(
        &mut self,
        session_id: u64,
        stream_id: u64,
    ) -> Result<(), Error> {
        let kind = crate::stream::StreamKind::from_stream_id(stream_id);

        // 双方向 / 単方向のいずれもローカル開始でない場合 (ピア開始) は登録対象外。
        // 双方向のピア開始ストリームはヘッダーが付くリクエストストリームとして
        // `feed_stream` が処理する。単方向のピア開始ストリームはピアが送信するので
        // 受信データを持ち登録の対象になるが、これは `resolve_wt_uni_stream_session_id`
        // が扱う。
        let is_local_bidi = self.is_local_initiated_bidi(kind);
        let is_local_uni = self.is_local_initiated_uni(kind);
        if !is_local_bidi && !is_local_uni {
            return Err(Error::ConnectionError(ErrorCode::IdError));
        }

        // 二重登録を拒否する (bidi / uni 両方の登録先を確認する)
        if self.wt_bidi_streams.contains_key(&stream_id)
            || self.wt_uni_streams.contains_key(&stream_id)
        {
            return Err(Error::ConnectionError(ErrorCode::StreamCreationError));
        }

        // 登録前に受信データが到着して HTTP/3 ストリームとして作成済みの場合、
        // 誤解析の結果を巻き戻せないため拒否する
        if self.streams.contains_key(&stream_id) {
            return Err(Error::ConnectionError(ErrorCode::StreamCreationError));
        }

        // セッションが存在し Established 状態である必要がある
        let session = self
            .wt_sessions
            .get_mut(&session_id)
            .ok_or(Error::ConnectionError(ErrorCode::GeneralProtocolError))?;
        if session.state != WtSessionState::Established {
            return Err(Error::ConnectionError(ErrorCode::GeneralProtocolError));
        }

        // セッション終了時の RESET 対象 (reliable size 計算を含む) に含める
        session.associate_stream(stream_id);
        if is_local_bidi {
            self.wt_bidi_streams.insert(stream_id, session_id);
        } else {
            // ローカル開始の単方向 WT ストリーム。ピアは受信専用となるため、
            // ピアからは STOP_SENDING のみ届く (RFC 9000 Section 3.5)。
            // ここに登録することで `handle_wt_stop_sending` が
            // `WebTransportEvent::StreamStopSending` をセッション ID 付きで発火する
            // (draft-ietf-webtrans-http3-16 Section 4.4)。
            self.wt_uni_streams.insert(stream_id, session_id);
        }
        Ok(())
    }

    /// ストリーム種別がローカル開始の双方向ストリームかどうかを判定する
    ///
    /// ストリーム ID の下位ビット (RFC 9000 Section 2.1) から求めた種別と
    /// ロールから判定する。クライアント側はクライアント開始 bidi (0x00)、
    /// サーバー側はサーバー開始 bidi (0x01) がローカル開始になる。
    pub(crate) fn is_local_initiated_bidi(&self, kind: crate::stream::StreamKind) -> bool {
        match self.role {
            Role::Client => kind == crate::stream::StreamKind::ClientBidi,
            Role::Server => kind == crate::stream::StreamKind::ServerBidi,
        }
    }

    /// ストリーム種別がローカル開始の単方向ストリームかどうかを判定する
    ///
    /// クライアント側はクライアント開始 uni (0x02)、サーバー側はサーバー開始 uni (0x03)
    /// がローカル開始になる (RFC 9000 Section 2.1)。
    pub(crate) fn is_local_initiated_uni(&self, kind: crate::stream::StreamKind) -> bool {
        match self.role {
            Role::Client => kind == crate::stream::StreamKind::ClientUni,
            Role::Server => kind == crate::stream::StreamKind::ServerUni,
        }
    }

    /// WebTransport 単方向ストリームのセッション ID を解決
    ///
    /// ストリームタイプ (0x54) が確定した後、セッション ID (varint) をパースする。
    /// varint が不完全な場合は `pending_wt_uni_streams` にバッファリングする。
    /// (draft-ietf-webtrans-http3-15 Section 4.2)
    pub(crate) fn resolve_wt_uni_stream_session_id(
        &mut self,
        stream_id: u64,
        data: &[u8],
    ) -> Result<(), Error> {
        let buf = if let Some(pending) = self.pending_wt_uni_streams.get_mut(&stream_id) {
            pending.extend_from_slice(data);
            pending.clone()
        } else {
            data.to_vec()
        };

        if buf.is_empty() {
            // データなし: 次のチャンクを待つ
            self.pending_wt_uni_streams.entry(stream_id).or_default();
            return Ok(());
        }

        match crate::varint::decode(&buf) {
            Ok((session_id, id_len)) => {
                let session_id = session_id.get();
                // session_id は client-initiated bidirectional stream ID でなければならない
                // (draft-ietf-webtrans-http3-15 Section 4.2)
                // RFC 9000 Section 2.1: client-initiated bidi は stream_id % 4 == 0
                if session_id & 0x03 != 0x00 {
                    return Err(Error::ConnectionError(ErrorCode::IdError));
                }
                self.pending_wt_uni_streams.remove(&stream_id);
                self.wt_uni_streams.insert(stream_id, session_id);

                // セッション関連付けとバッファリング (draft-ietf-webtrans-http3-15 Section 4.6)
                let outcome = match self.associate_or_buffer_stream(stream_id, session_id, false) {
                    Ok(o) => o,
                    Err(()) => {
                        // セッション終了済み: WT_SESSION_GONE で拒否 (Section 6)
                        self.wt_uni_streams.remove(&stream_id);
                        self.events.push_back(Event::WebTransport(
                            WebTransportEvent::BufferedStreamRejected {
                                stream_id,
                                error_code: WtErrorCode::SessionGone as u64,
                            },
                        ));
                        return Ok(());
                    }
                };
                if outcome == AssocOutcome::BufferOverflow {
                    self.wt_uni_streams.remove(&stream_id);
                    self.events.push_back(Event::WebTransport(
                        WebTransportEvent::BufferedStreamRejected {
                            stream_id,
                            error_code: WtErrorCode::BufferedStreamRejected as u64,
                        },
                    ));
                    return Ok(());
                }

                let remaining = &buf[id_len..];

                if outcome == AssocOutcome::Buffered {
                    // Pending セッション: Open / Data はセッション確立まで保留する
                    // (draft-ietf-webtrans-http3-15 Section 4.6)
                    if !remaining.is_empty()
                        && let Some(session) = self.wt_sessions.get_mut(&session_id)
                        && !session.append_buffered_stream_data(stream_id, remaining)
                    {
                        // バッファ上限超過: WT_BUFFERED_STREAM_REJECTED 相当
                        self.wt_uni_streams.remove(&stream_id);
                        let _ = session.take_buffered_stream_entry(stream_id);
                        self.events.push_back(Event::WebTransport(
                            WebTransportEvent::BufferedStreamRejected {
                                stream_id,
                                error_code: WtErrorCode::BufferedStreamRejected as u64,
                            },
                        ));
                        return Ok(());
                    }
                    return Ok(());
                }

                // Established セッションのストリーム数フロー制御 (Section 5.6)
                if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                    if !session.check_received_stream(false) {
                        self.wt_uni_streams.remove(&stream_id);
                        self.terminate_wt_session_with(
                            session_id,
                            WtErrorCode::FlowControlError as u64,
                            0,
                            String::new(),
                        );
                        return Ok(());
                    }
                    session.add_received_stream(false);
                }

                self.events
                    .push_back(Event::WebTransport(WebTransportEvent::UniStreamOpen {
                        stream_id,
                        session_id,
                    }));
                // セッション ID の後にデータがあればイベントで通知
                if !remaining.is_empty() {
                    // データフロー制御 (Section 5.4)
                    if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                        let data_len = remaining.len() as u64;
                        if !session.check_received_data(data_len) {
                            self.terminate_wt_session_with(
                                session_id,
                                WtErrorCode::FlowControlError as u64,
                                0,
                                String::new(),
                            );
                            return Ok(());
                        }
                        session.add_received_data_and_track(stream_id, data_len);
                    }
                    self.events
                        .push_back(Event::WebTransport(WebTransportEvent::UniStreamData {
                            stream_id,
                            data: remaining.to_vec(),
                        }));
                }
                Ok(())
            }
            Err(crate::varint::DecodeError::BufferTooShort) => {
                // varint 不完全: 次のチャンクを待つ
                self.pending_wt_uni_streams.entry(stream_id).or_insert(buf);
                Ok(())
            }
        }
    }

    /// WebTransport 双方向ストリームを処理
    ///
    /// server-initiated (または client-initiated で signal value 0x41 付き) の
    /// bidi stream を処理する。先頭の signal value (0x41) と session_id (varint) を
    /// パースし、確定後はアプリケーションペイロードをイベントで通知する。
    /// (draft-ietf-webtrans-http3-15 Section 4.3)
    pub(crate) fn handle_wt_bidi_stream(
        &mut self,
        stream_id: u64,
        data: &[u8],
        fin: bool,
    ) -> Result<(), Error> {
        // 既に確定済みの WT bidi stream: データ/FIN を処理
        if let Some(&session_id) = self.wt_bidi_streams.get(&stream_id) {
            // Pending セッション中はバッファに記録するだけ
            // (draft-ietf-webtrans-http3-15 Section 4.6)
            let pending = self
                .wt_sessions
                .get(&session_id)
                .is_some_and(|s| s.state == WtSessionState::Pending);
            if pending {
                if !data.is_empty()
                    && let Some(session) = self.wt_sessions.get_mut(&session_id)
                    && !session.append_buffered_stream_data(stream_id, data)
                {
                    self.wt_bidi_streams.remove(&stream_id);
                    let _ = session.take_buffered_stream_entry(stream_id);
                    self.events.push_back(Event::WebTransport(
                        WebTransportEvent::BufferedStreamRejected {
                            stream_id,
                            error_code: WtErrorCode::BufferedStreamRejected as u64,
                        },
                    ));
                    return Ok(());
                }
                if fin && let Some(session) = self.wt_sessions.get_mut(&session_id) {
                    session.mark_buffered_stream_fin(stream_id);
                }
                return Ok(());
            }
            if !data.is_empty() {
                // データフロー制御 (Section 5.4)
                if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                    let data_len = data.len() as u64;
                    if !session.check_received_data(data_len) {
                        self.terminate_wt_session_with(
                            session_id,
                            WtErrorCode::FlowControlError as u64,
                            0,
                            String::new(),
                        );
                        return Ok(());
                    }
                    session.add_received_data_and_track(stream_id, data_len);
                }
                self.events
                    .push_back(Event::WebTransport(WebTransportEvent::BidiStreamData {
                        stream_id,
                        data: data.to_vec(),
                    }));
            }
            if fin {
                // ピア開始ストリームは FIN で登録解除し、WT_MAX_STREAMS クレジットを
                // 返却する (WT_MAX_STREAMS はピアが開くストリーム数の制限。
                //  draft-ietf-webtrans-http3-16 Section 5.3)。
                // ローカル開始ストリームは自側がヘッダーを送信済みのため、FIN 後も
                // RESET_STREAM_AT の reliable size (ヘッダー長) 計算に登録が必要
                // (draft-ietf-webtrans-http3-16 Section 4.4)。
                if !self
                    .is_local_initiated_bidi(crate::stream::StreamKind::from_stream_id(stream_id))
                {
                    self.wt_bidi_streams.remove(&stream_id);
                    if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                        session.on_remote_stream_closed(true);
                        // ピア開始は登録解除されるため FIN 後の RESET は WT 経路に
                        // 到達しない。計上済み量の追跡をここで掃除する。
                        session.remove_stream_received_data(stream_id);
                    }
                }
                // ローカル開始は登録解除をセッション終了時 (terminate_wt_session_with)
                // に委ね、計上済み量も FIN 後の RESET (RFC 9000 Section 3.2: FIN 受信
                // 後に RESET_STREAM を受信しうる) で二重計上しないよう残す。
                // BidiStreamEnd イベントの発火はローカル開始でも行う
                // (ピアの送信方向終了の通知はアプリのストリームクローズ判断に必要)
                self.events
                    .push_back(Event::WebTransport(WebTransportEvent::BidiStreamEnd {
                        stream_id,
                    }));
            }
            return Ok(());
        }

        // signal value + session_id の解決を試みる
        self.resolve_wt_bidi_stream_header(stream_id, data)?;

        // FIN チェック: ヘッダー解決中に FIN が来た場合
        if fin {
            if let Some(&session_id) = self.wt_bidi_streams.get(&stream_id) {
                let pending = self
                    .wt_sessions
                    .get(&session_id)
                    .is_some_and(|s| s.state == WtSessionState::Pending);
                if pending {
                    if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                        session.mark_buffered_stream_fin(stream_id);
                    }
                } else {
                    self.wt_bidi_streams.remove(&stream_id);
                    // ストリーム閉鎖: WT_MAX_STREAMS 更新判定 (Section 5.6)
                    if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                        session.on_remote_stream_closed(true);
                        // ピア開始は登録解除されるため RESET は WT 経路に到達しない。
                        // 計上済み量の追跡を掃除する。
                        session.remove_stream_received_data(stream_id);
                    }
                    self.events
                        .push_back(Event::WebTransport(WebTransportEvent::BidiStreamEnd {
                            stream_id,
                        }));
                }
            }
            self.pending_wt_bidi_streams.remove(&stream_id);
        }

        Ok(())
    }

    /// WebTransport データストリームの stream header エンコード長を計算する
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.2 / 5.4)
    ///
    /// - 双方向ストリーム: signal value (0x41) varint + session_id varint
    /// - 単方向ストリーム: stream type (0x54) varint + session_id varint
    ///
    /// 上位層は本値を `RESET_STREAM_AT` の reliable size の下限として使用する。
    /// `stream_id` が WebTransport データストリームとして登録されていない場合は
    /// 0 を返す (CONNECT stream や非 WT ストリーム)。将来のドラフトで stream
    /// header フォーマットが変更される可能性がある。
    pub fn wt_stream_header_len(&self, stream_id: u64) -> u64 {
        use crate::webtransport::stream::{BIDIRECTIONAL_SIGNAL_VALUE, UNIDIRECTIONAL_STREAM_TYPE};
        // signal value / stream type は WebTransport プロトコルの定数なので const 文脈で
        // VarInt 構築できる (`from_static` でコンパイル時検査)。
        const BIDI_SIGNAL: VarInt = VarInt::from_static(BIDIRECTIONAL_SIGNAL_VALUE);
        const UNI_TYPE: VarInt = VarInt::from_static(UNIDIRECTIONAL_STREAM_TYPE);
        if let Some(&session_id) = self.wt_bidi_streams.get(&stream_id) {
            // session_id は QUIC ストリーム ID 空間 (`<= 2^62 - 1`) で構造的に保証される。
            let session_id = VarInt::new(session_id).expect("session_id fits in VarInt");
            return (BIDI_SIGNAL.encoded_len() + session_id.encoded_len()) as u64;
        }
        if let Some(&session_id) = self.wt_uni_streams.get(&stream_id) {
            let session_id = VarInt::new(session_id).expect("session_id fits in VarInt");
            return (UNI_TYPE.encoded_len() + session_id.encoded_len()) as u64;
        }
        0
    }

    /// WebTransport 双方向ストリームのヘッダー (signal value + session_id) を解決
    ///
    /// 先頭の signal value (0x41) と session_id (varint) をパースする。
    /// varint が不完全な場合は `pending_wt_bidi_streams` にバッファリングする。
    /// (draft-ietf-webtrans-http3-15 Section 4.3)
    pub(crate) fn resolve_wt_bidi_stream_header(
        &mut self,
        stream_id: u64,
        data: &[u8],
    ) -> Result<(), Error> {
        let buf = if let Some(pending) = self.pending_wt_bidi_streams.get_mut(&stream_id) {
            pending.extend_from_slice(data);
            pending.clone()
        } else {
            data.to_vec()
        };

        if buf.is_empty() {
            // データなし: 次のチャンクを待つ
            self.pending_wt_bidi_streams.entry(stream_id).or_default();
            return Ok(());
        }

        // signal value をパース (varint)
        let (signal_value, signal_len) = match crate::varint::decode(&buf) {
            Ok(v) => v,
            Err(crate::varint::DecodeError::BufferTooShort) => {
                self.pending_wt_bidi_streams.entry(stream_id).or_insert(buf);
                return Ok(());
            }
        };

        // signal value は 0x41 (WT_STREAM) でなければならない
        // (draft-ietf-webtrans-http3-15 Section 4.3)
        if signal_value.get() != 0x41 {
            // 0x41 以外の signal value はリクエストストリームの先頭以外での WT_STREAM 受信、
            // または不正な signal value。H3_FRAME_ERROR として接続エラー。
            return Err(Error::ConnectionError(ErrorCode::FrameError));
        }

        // session_id をパース (varint)
        let remaining = &buf[signal_len..];
        match crate::varint::decode(remaining) {
            Ok((session_id, id_len)) => {
                let session_id = session_id.get();
                // session_id は client-initiated bidirectional stream ID でなければならない
                // (draft-ietf-webtrans-http3-15 Section 4.2)
                if session_id & 0x03 != 0x00 {
                    return Err(Error::ConnectionError(ErrorCode::IdError));
                }
                self.pending_wt_bidi_streams.remove(&stream_id);
                self.wt_bidi_streams.insert(stream_id, session_id);

                // セッション関連付けとバッファリング (draft-ietf-webtrans-http3-15 Section 4.6)
                let outcome = match self.associate_or_buffer_stream(stream_id, session_id, true) {
                    Ok(o) => o,
                    Err(()) => {
                        // セッション終了済み: WT_SESSION_GONE で拒否 (Section 6)
                        self.wt_bidi_streams.remove(&stream_id);
                        self.events.push_back(Event::WebTransport(
                            WebTransportEvent::BufferedStreamRejected {
                                stream_id,
                                error_code: WtErrorCode::SessionGone as u64,
                            },
                        ));
                        return Ok(());
                    }
                };
                if outcome == AssocOutcome::BufferOverflow {
                    self.wt_bidi_streams.remove(&stream_id);
                    self.events.push_back(Event::WebTransport(
                        WebTransportEvent::BufferedStreamRejected {
                            stream_id,
                            error_code: WtErrorCode::BufferedStreamRejected as u64,
                        },
                    ));
                    return Ok(());
                }

                let payload = &remaining[id_len..];

                if outcome == AssocOutcome::Buffered {
                    // Pending セッション: Open / Data はセッション確立まで保留する
                    // (draft-ietf-webtrans-http3-15 Section 4.6)
                    if !payload.is_empty()
                        && let Some(session) = self.wt_sessions.get_mut(&session_id)
                        && !session.append_buffered_stream_data(stream_id, payload)
                    {
                        self.wt_bidi_streams.remove(&stream_id);
                        let _ = session.take_buffered_stream_entry(stream_id);
                        self.events.push_back(Event::WebTransport(
                            WebTransportEvent::BufferedStreamRejected {
                                stream_id,
                                error_code: WtErrorCode::BufferedStreamRejected as u64,
                            },
                        ));
                        return Ok(());
                    }
                    return Ok(());
                }

                // Established セッションのストリーム数フロー制御 (Section 5.6)
                if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                    if !session.check_received_stream(true) {
                        self.wt_bidi_streams.remove(&stream_id);
                        self.terminate_wt_session_with(
                            session_id,
                            WtErrorCode::FlowControlError as u64,
                            0,
                            String::new(),
                        );
                        return Ok(());
                    }
                    session.add_received_stream(true);
                }

                self.events
                    .push_back(Event::WebTransport(WebTransportEvent::BidiStreamOpen {
                        stream_id,
                        session_id,
                    }));
                // session_id の後にデータがあればイベントで通知
                if !payload.is_empty() {
                    // データフロー制御 (Section 5.4)
                    if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                        let data_len = payload.len() as u64;
                        if !session.check_received_data(data_len) {
                            self.terminate_wt_session_with(
                                session_id,
                                WtErrorCode::FlowControlError as u64,
                                0,
                                String::new(),
                            );
                            return Ok(());
                        }
                        session.add_received_data_and_track(stream_id, data_len);
                    }
                    self.events
                        .push_back(Event::WebTransport(WebTransportEvent::BidiStreamData {
                            stream_id,
                            data: payload.to_vec(),
                        }));
                }
                Ok(())
            }
            Err(crate::varint::DecodeError::BufferTooShort) => {
                // session_id の varint 不完全: 次のチャンクを待つ
                self.pending_wt_bidi_streams.entry(stream_id).or_insert(buf);
                Ok(())
            }
        }
    }

    /// WebTransport 単方向ストリームのデータ処理 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.6, 5.4)
    /// Pending セッション中はバッファに追記、Established 後はイベント発火。
    /// 登録済みローカル開始 uni への STREAM は防御的に静かに吸収し `Ok(true)` を
    /// 返す (RFC 9000 Section 19.8)。非 WT ストリームは `false` を返す。
    pub(crate) fn handle_wt_uni_stream_data(
        &mut self,
        stream_id: u64,
        data: &[u8],
    ) -> Result<bool, Error> {
        let Some(&session_id) = self.wt_uni_streams.get(&stream_id) else {
            return Ok(false);
        };

        // WT データストリームとして登録済みのローカル開始 uni (ピアは受信専用) への
        // STREAM は RFC 9000 Section 19.8 の STREAM_STATE_ERROR に相当する不正入力。
        // QUIC 層で拒否されるのが通常経路だが、統合層の不具合や将来の QUIC 実装変更で
        // 到達しても不正に処理しないよう、ここで静かに吸収する
        let kind = crate::stream::StreamKind::from_stream_id(stream_id);
        if self.is_local_initiated_uni(kind) {
            return Ok(true);
        }

        let buffered = self
            .wt_sessions
            .get(&session_id)
            .is_some_and(|s| s.state == WtSessionState::Pending);

        if buffered {
            if let Some(session) = self.wt_sessions.get_mut(&session_id)
                && !session.append_buffered_stream_data(stream_id, data)
            {
                self.wt_uni_streams.remove(&stream_id);
                let _ = session.take_buffered_stream_entry(stream_id);
                self.events.push_back(Event::WebTransport(
                    WebTransportEvent::BufferedStreamRejected {
                        stream_id,
                        error_code: WtErrorCode::BufferedStreamRejected as u64,
                    },
                ));
            }
        } else {
            if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                let data_len = data.len() as u64;
                if !session.check_received_data(data_len) {
                    self.terminate_wt_session_with(
                        session_id,
                        WtErrorCode::FlowControlError as u64,
                        0,
                        String::new(),
                    );
                    return Ok(true);
                }
                session.add_received_data_and_track(stream_id, data_len);
            }
            self.events
                .push_back(Event::WebTransport(WebTransportEvent::UniStreamData {
                    stream_id,
                    data: data.to_vec(),
                }));
        }

        Ok(true)
    }

    /// WebTransport 単方向ストリームの FIN 処理 WebTransport 混在関数の抽出
    ///
    /// (draft-ietf-webtrans-http3-15 Section 4.6, 5.6)
    /// Pending セッション中はバッファに記録、Established 後はイベント発火。
    /// 登録済みローカル開始 uni への FIN は登録を維持したまま防御的に静かに吸収し
    /// `true` を返す (RFC 9000 Section 19.8)。非 WT ストリームは `false` を返す。
    pub(crate) fn handle_wt_uni_stream_fin(&mut self, stream_id: u64) -> bool {
        let Some(&session_id) = self.wt_uni_streams.get(&stream_id) else {
            return false;
        };

        // WT データストリームとして登録済みのローカル開始 uni (ピアは受信専用) への
        // FIN は RFC 9000 Section 19.8 の STREAM_STATE_ERROR に相当する不正入力。
        // 登録を維持し、クレジット回復・登録除去・UniStreamEnd 発火を行わない
        let kind = crate::stream::StreamKind::from_stream_id(stream_id);
        if self.is_local_initiated_uni(kind) {
            return true;
        }

        self.wt_uni_streams.remove(&stream_id);

        let pending = self
            .wt_sessions
            .get(&session_id)
            .is_some_and(|s| s.state == WtSessionState::Pending);

        if pending {
            if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                session.mark_buffered_stream_fin(stream_id);
            }
            self.wt_uni_streams.insert(stream_id, session_id);
        } else {
            if let Some(session) = self.wt_sessions.get_mut(&session_id) {
                session.on_remote_stream_closed(false);
                // 計上済み量の追跡を掃除する (ピア開始は FIN で登録解除される
                // ため、FIN 後の RESET は WT 経路に到達せず二重計上の機会がない)
                session.remove_stream_received_data(stream_id);
            }
            self.events
                .push_back(Event::WebTransport(WebTransportEvent::UniStreamEnd {
                    stream_id,
                }));
        }

        true
    }
}
