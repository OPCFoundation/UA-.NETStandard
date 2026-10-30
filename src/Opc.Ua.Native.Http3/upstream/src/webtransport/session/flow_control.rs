//! WebTransport フロー制御 (draft-ietf-webtrans-http3-15 Section 5)
//!
//! セッションレベルのフロー制御 (ストリーム数・データ量) を管理する。
//! session.rs から分離したモジュール。

use super::super::capsule::MAX_STREAMS_LIMIT;

/// # 運用ガイダンス (draft-ietf-webtrans-http3-15 Section 5.6)
///
/// - WT_DATA_BLOCKED / WT_STREAMS_BLOCKED を待ってから WT_MAX_DATA / WT_MAX_STREAMS を
///   送信してはならない (MUST NOT)。待つと少なくとも 1 RTT のブロックが発生する。
/// - WT_MAX_DATA / WT_MAX_STREAMS はデータ消費やストリームクローズに応じて送信する (SHOULD)。
/// - 将来のドラフトで変更される可能性がある
#[derive(Debug, Clone, Copy, Default)]
pub struct FlowControlLimits {
    /// 最大単方向ストリーム数 (ローカルが開けるリモートからの制限)
    pub max_streams_uni: u64,
    /// 最大双方向ストリーム数 (ローカルが開けるリモートからの制限)
    pub max_streams_bidi: u64,
    /// 最大データ量 (ローカルが送れるリモートからの制限)
    pub max_data: u64,
}

impl FlowControlLimits {
    /// 新しいフロー制御リミットを作成
    pub fn new() -> Self {
        Self::default()
    }
}

/// フロー制御状態
#[derive(Debug, Clone, Copy, Default)]
pub struct FlowControlState {
    /// 開いた単方向ストリーム数
    pub streams_uni_opened: u64,
    /// 開いた双方向ストリーム数
    pub streams_bidi_opened: u64,
    /// 送信した Stream Body データ量の合計 (バイト)
    ///
    /// カウント対象: セッションに関連する全ストリームの Stream Body データ長の合計。
    /// カウント対象外: カプセル、Signal Value、Stream Type、Session ID フィールド。
    /// draft-ietf-webtrans-http3-15 Section 5.4
    /// 将来のドラフトで変更される可能性がある
    pub data_sent: u64,
    /// 受信した単方向ストリーム数
    pub streams_uni_received: u64,
    /// 受信した双方向ストリーム数
    pub streams_bidi_received: u64,
    /// 受信した Stream Body データ量の合計 (バイト)
    ///
    /// カウント対象/対象外は `data_sent` と同一。
    /// ピアが `local_limits.max_data` を超過した場合は WT_FLOW_CONTROL_ERROR。
    /// draft-ietf-webtrans-http3-15 Section 5.4
    /// 将来のドラフトで変更される可能性がある
    pub data_received: u64,
    /// 受信したデータグラム数 (DoS 監視用)
    ///
    /// draft-ietf-webtrans-http3-15 Section 4.5
    /// 将来のドラフトで変更される可能性がある
    pub datagrams_received: u64,
}

impl FlowControlState {
    /// 新しいフロー制御状態を作成
    pub fn new() -> Self {
        Self::default()
    }
}

/// 単一方向のストリームフロー制御 (受信側)
///
/// ピアが開くストリームに対するウィンドウ管理を行う。
/// RFC 9000 Section 4.6 と同様のウィンドウ方式で、
/// ストリームの close に応じて WT_MAX_STREAMS の更新を判定する。
/// draft-ietf-webtrans-http3-15 Section 5.6
/// 将来のドラフトで変更される可能性がある
#[derive(Debug, Clone)]
pub(crate) struct DirectionalStreamFlowControl {
    /// 同時許可数 (しきい値計算用)
    concurrent_limit: u64,
    /// ピアに最後に通知した累積上限 (減少不可)
    advertised_max: u64,
    /// ピアが開いて完全に閉じたストリーム総数
    total_closed: u64,
    /// ピアが開いたストリーム総数
    total_received: u64,
}

impl DirectionalStreamFlowControl {
    /// 新しいフロー制御を作成
    ///
    /// `concurrent_limit` が初期の `advertised_max` になる。
    pub(crate) fn new(concurrent_limit: u64) -> Self {
        Self {
            concurrent_limit,
            advertised_max: concurrent_limit,
            total_closed: 0,
            total_received: 0,
        }
    }

    /// ピアがストリームを開く余地があるかどうか
    pub(crate) fn check_received(&self) -> bool {
        self.total_received < self.advertised_max
    }

    /// ピアがストリームを開いたことを記録
    pub(crate) fn on_stream_received(&mut self) {
        self.total_received = self.total_received.saturating_add(1);
    }

    /// ピアが開いたストリームが完全に閉じたことを記録
    ///
    /// しきい値を下回った場合、新しい `advertised_max` を返す。
    /// 呼び出し側は返された値で WT_MAX_STREAMS カプセルを生成する。
    pub(crate) fn on_stream_closed(&mut self) -> Option<u64> {
        self.total_closed = self.total_closed.saturating_add(1);

        // concurrent_limit が 0 の場合はウィンドウ更新不要
        if self.concurrent_limit == 0 {
            return None;
        }

        let remaining = self.advertised_max.saturating_sub(self.total_received);
        let threshold = self.concurrent_limit / 2;

        if remaining <= threshold {
            let new_max = self
                .total_closed
                .saturating_add(self.concurrent_limit)
                .min(MAX_STREAMS_LIMIT);
            if new_max > self.advertised_max {
                self.advertised_max = new_max;
                return Some(new_max);
            }
        }

        None
    }
}

/// セッションレベルのデータフロー制御 (受信側)
///
/// RFC 9000 Section 4.2 と同様のウィンドウ方式で、
/// データ消費に応じて WT_MAX_DATA の更新を判定する。
/// draft-ietf-webtrans-http3-15 Section 5.6
/// 将来のドラフトで変更される可能性がある
#[derive(Debug, Clone)]
pub(crate) struct DataFlowControl {
    /// 初期ウィンドウサイズ (しきい値計算用)
    initial_window: u64,
    /// ピアに最後に通知した max_data (累積値、減少不可)
    advertised_max: u64,
    /// アプリが消費済みのデータ量
    total_consumed: u64,
    /// ピアから受信したデータ量
    total_received: u64,
}

impl DataFlowControl {
    /// 新しいデータフロー制御を作成
    pub(crate) fn new(initial_window: u64) -> Self {
        Self {
            initial_window,
            advertised_max: initial_window,
            total_consumed: 0,
            total_received: 0,
        }
    }

    /// ピアが送信するデータを受信可能かどうか
    pub(crate) fn check_received(&self, bytes: u64) -> bool {
        // checked_sub でオーバーフローを回避: advertised_max - total_received の残り枠と比較
        self.advertised_max.saturating_sub(self.total_received) >= bytes
    }

    /// ピアからのデータ受信を記録
    pub(crate) fn on_data_received(&mut self, bytes: u64) {
        self.total_received = self.total_received.saturating_add(bytes);
    }

    /// アプリがデータを消費したことを記録
    ///
    /// しきい値を下回った場合、新しい `advertised_max` を返す。
    /// 呼び出し側は返された値で WT_MAX_DATA カプセルを生成する。
    pub(crate) fn on_data_consumed(&mut self, bytes: u64) -> Option<u64> {
        self.total_consumed = self.total_consumed.saturating_add(bytes);

        // initial_window が 0 の場合はウィンドウ更新不要
        if self.initial_window == 0 {
            return None;
        }

        let remaining = self.advertised_max.saturating_sub(self.total_received);
        let threshold = self.initial_window / 2;

        if remaining <= threshold {
            let new_max = self.total_consumed.saturating_add(self.initial_window);
            if new_max > self.advertised_max {
                self.advertised_max = new_max;
                return Some(new_max);
            }
        }

        None
    }
}

/// 送信側のブロック状態追跡
///
/// WT_STREAMS_BLOCKED / WT_DATA_BLOCKED の重複送信を防止する。
/// 同じ maximum に対しては 1 回だけカプセルを送信する。
/// draft-ietf-webtrans-http3-15 Section 5.6
/// 将来のドラフトで変更される可能性がある
#[derive(Debug, Clone, Default)]
pub(crate) struct SendBlockedState {
    /// 最後に WT_STREAMS_BLOCKED (uni) を送信した時の maximum
    pub(crate) last_streams_blocked_uni: Option<u64>,
    /// 最後に WT_STREAMS_BLOCKED (bidi) を送信した時の maximum
    pub(crate) last_streams_blocked_bidi: Option<u64>,
    /// 最後に WT_DATA_BLOCKED を送信した時の maximum
    pub(crate) last_data_blocked: Option<u64>,
}
