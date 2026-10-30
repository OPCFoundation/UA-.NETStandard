// Modified by OPC Foundation, 2026-10-16: bounded receive allocation and release; see the proof patch manifest.
//! HTTP/3 ストリーム (RFC 9114 Section 6)
//!
//! HTTP/3 で使用されるストリームの管理とバッファリングを提供。

mod control;
pub mod request;

pub(crate) use control::{ControlStreamRecv, ControlStreamSend};
pub use request::RequestStream;

/// 単方向ストリームタイプ (RFC 9114 Section 6.2)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u64)]
pub enum UniStreamType {
    /// Control Stream (0x00)
    Control = 0x00,
    /// Push Stream (0x01) - 未サポート
    Push = 0x01,
    /// QPACK Encoder Stream (0x02)
    QpackEncoder = 0x02,
    /// QPACK Decoder Stream (0x03)
    QpackDecoder = 0x03,
    /// WebTransport Stream (0x54) - draft-ietf-webtrans-http3-15
    WebTransport = 0x54,
}

impl UniStreamType {
    /// ストリームタイプから `UniStreamType` を作成
    pub fn from_type(t: u64) -> Option<Self> {
        match t {
            0x00 => Some(Self::Control),
            0x01 => Some(Self::Push),
            0x02 => Some(Self::QpackEncoder),
            0x03 => Some(Self::QpackDecoder),
            0x54 => Some(Self::WebTransport),
            _ => None,
        }
    }

    /// 予約されたストリームタイプかどうか (0x1f * N + 0x21)
    pub fn is_reserved(t: u64) -> bool {
        t >= 0x21 && (t - 0x21).is_multiple_of(0x1f)
    }
}

/// ストリーム ID からストリーム種別を判定
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StreamKind {
    /// クライアント開始の双方向ストリーム
    ClientBidi,
    /// サーバー開始の双方向ストリーム
    ServerBidi,
    /// クライアント開始の単方向ストリーム
    ClientUni,
    /// サーバー開始の単方向ストリーム
    ServerUni,
}

impl StreamKind {
    /// ストリーム ID から種別を判定
    ///
    /// QUIC ストリーム ID の下位 2 ビットで判定:
    /// - 0b00: Client-Initiated, Bidirectional
    /// - 0b01: Server-Initiated, Bidirectional
    /// - 0b10: Client-Initiated, Unidirectional
    /// - 0b11: Server-Initiated, Unidirectional
    pub fn from_stream_id(stream_id: u64) -> Self {
        match stream_id & 0x03 {
            0x00 => Self::ClientBidi,
            0x01 => Self::ServerBidi,
            0x02 => Self::ClientUni,
            0x03 => Self::ServerUni,
            _ => unreachable!(),
        }
    }

    /// 双方向ストリームかどうか
    pub fn is_bidirectional(self) -> bool {
        matches!(self, Self::ClientBidi | Self::ServerBidi)
    }

    /// 単方向ストリームかどうか
    pub fn is_unidirectional(self) -> bool {
        matches!(self, Self::ClientUni | Self::ServerUni)
    }

    /// クライアント開始のストリームかどうか
    pub fn is_client_initiated(self) -> bool {
        matches!(self, Self::ClientBidi | Self::ClientUni)
    }

    /// サーバー開始のストリームかどうか
    pub fn is_server_initiated(self) -> bool {
        matches!(self, Self::ServerBidi | Self::ServerUni)
    }
}

/// ストリーム状態
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum StreamState {
    /// オープン
    #[default]
    Open,
    /// ローカル側が FIN 送信済み
    LocalClosed,
    /// リモート側が FIN 送信済み
    RemoteClosed,
    /// 完全にクローズ
    Closed,
    /// リセット済み (RESET_STREAM 受信)
    Reset,
}

impl StreamState {
    /// ローカル側をクローズ
    pub fn close_local(&mut self) {
        *self = match *self {
            Self::Open => Self::LocalClosed,
            Self::RemoteClosed => Self::Closed,
            other => other,
        };
    }

    /// リモート側をクローズ
    pub fn close_remote(&mut self) {
        *self = match *self {
            Self::Open => Self::RemoteClosed,
            Self::LocalClosed => Self::Closed,
            other => other,
        };
    }

    /// リセット状態に遷移
    pub fn reset(&mut self) {
        *self = Self::Reset;
    }

    /// リセット済みかどうか
    pub fn is_reset(self) -> bool {
        matches!(self, Self::Reset)
    }

    /// 送信可能かどうか
    pub fn can_send(self) -> bool {
        matches!(self, Self::Open | Self::RemoteClosed)
    }

    /// 受信可能かどうか
    pub fn can_receive(self) -> bool {
        matches!(self, Self::Open | Self::LocalClosed)
    }
}

/// 送信バッファ
#[derive(Debug, Default)]
pub struct SendBuffer {
    /// バッファデータ
    data: Vec<u8>,
    /// 消費済みオフセット
    consumed: usize,
    /// FIN フラグ
    fin: bool,
    /// FIN が QUIC 層に引き渡し済みか
    fin_sent: bool,
}

impl SendBuffer {
    /// 新しい送信バッファを作成
    pub fn new() -> Self {
        Self::default()
    }

    /// データを追加
    pub fn push(&mut self, data: &[u8]) {
        self.data.extend_from_slice(data);
    }

    /// FIN を設定
    pub fn set_fin(&mut self) {
        self.fin = true;
    }

    /// FIN が設定されているか
    pub fn is_fin(&self) -> bool {
        self.fin
    }

    /// 送信可能なデータを取得
    pub fn peek(&self) -> &[u8] {
        &self.data[self.consumed..]
    }

    /// データを消費
    pub fn consume(&mut self, len: usize) {
        self.consumed += len;
        if self.consumed >= self.data.len() / 2 {
            self.data.drain(..self.consumed);
            self.consumed = 0;
        }
    }

    /// 送信待ちデータと FIN を破棄する
    ///
    /// STOP_SENDING 受信時など、送信方向が閉じられて以降のデータを送れない
    /// 状態になった場合に使用する。破棄後は `is_data_consumed` が true になるため、
    /// 両方向クローズ後に `Connection::remove_stream_if_done` で除去される。
    pub fn discard(&mut self) {
        self.data.clear();
        self.consumed = 0;
        self.fin = false;
        self.fin_sent = false;
    }

    /// 送信待ちデータがあるか (FIN-only も含む)
    pub fn has_pending(&self) -> bool {
        self.consumed < self.data.len() || (self.fin && !self.fin_sent)
    }

    /// データが全て消費済みかどうか
    ///
    /// `has_pending` と違い FIN を含まないデータ専用の判定であり、
    /// 「FIN 設定済み && データ全消費済み && 未交付」の FIN 交付条件に使う。
    pub fn is_data_consumed(&self) -> bool {
        self.consumed >= self.data.len()
    }

    /// FIN を引き渡し済みとしてマークする
    pub fn mark_fin_sent(&mut self) {
        self.fin_sent = true;
    }

    /// FIN が引き渡し済みかどうか
    pub fn is_fin_sent(&self) -> bool {
        self.fin_sent
    }

    /// すべてのデータが送信済みで FIN も送信済みか
    pub fn is_complete(&self) -> bool {
        self.consumed >= self.data.len() && self.fin && self.fin_sent
    }
}

/// 受信バッファ
#[derive(Debug, Default)]
pub struct RecvBuffer {
    /// バッファデータ
    data: Vec<u8>,
    /// 消費済みオフセット
    consumed: usize,
    /// FIN 受信済み
    fin: bool,
}

impl RecvBuffer {
    /// 新しい受信バッファを作成
    pub fn new() -> Self {
        Self::default()
    }

    /// データを追加
    pub fn push(&mut self, data: &[u8]) {
        self.data.reserve_exact(data.len());
        self.data.extend_from_slice(data);
    }

    /// Allocated receive storage retained by this buffer.
    pub(crate) fn capacity(&self) -> usize {
        self.data.capacity()
    }

    /// FIN を受信
    pub fn set_fin(&mut self) {
        self.fin = true;
    }

    /// FIN を受信済みか
    pub fn is_fin(&self) -> bool {
        self.fin
    }

    /// 読み取り可能なデータを取得
    pub fn peek(&self) -> &[u8] {
        &self.data[self.consumed..]
    }

    /// データを消費
    pub fn consume(&mut self, len: usize) {
        self.consumed += len;
        if self.consumed == self.data.len() {
            self.data = Vec::new();
            self.consumed = 0;
            return;
        }
        if self.consumed >= self.data.len() / 2 {
            self.data.drain(..self.consumed);
            self.consumed = 0;
        }
    }

    /// 読み取り可能なデータがあるか
    pub fn has_data(&self) -> bool {
        self.consumed < self.data.len()
    }

    /// すべてのデータを読み取り済みで FIN も受信済みか
    pub fn is_complete(&self) -> bool {
        !self.has_data() && self.fin
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_uni_stream_type() {
        assert_eq!(UniStreamType::from_type(0x00), Some(UniStreamType::Control));
        assert_eq!(UniStreamType::from_type(0x01), Some(UniStreamType::Push));
        assert_eq!(
            UniStreamType::from_type(0x02),
            Some(UniStreamType::QpackEncoder)
        );
        assert_eq!(
            UniStreamType::from_type(0x03),
            Some(UniStreamType::QpackDecoder)
        );
        assert_eq!(
            UniStreamType::from_type(0x54),
            Some(UniStreamType::WebTransport)
        );
        assert_eq!(UniStreamType::from_type(0x99), None);
    }

    #[test]
    fn test_uni_stream_type_reserved() {
        assert!(UniStreamType::is_reserved(0x21)); // 0x1f * 0 + 0x21
        assert!(UniStreamType::is_reserved(0x40)); // 0x1f * 1 + 0x21
        assert!(!UniStreamType::is_reserved(0x00));
        assert!(!UniStreamType::is_reserved(0x20));
    }

    #[test]
    fn test_stream_kind() {
        assert_eq!(StreamKind::from_stream_id(0), StreamKind::ClientBidi);
        assert_eq!(StreamKind::from_stream_id(1), StreamKind::ServerBidi);
        assert_eq!(StreamKind::from_stream_id(2), StreamKind::ClientUni);
        assert_eq!(StreamKind::from_stream_id(3), StreamKind::ServerUni);
        assert_eq!(StreamKind::from_stream_id(4), StreamKind::ClientBidi);
    }

    #[test]
    fn test_stream_kind_properties() {
        assert!(StreamKind::ClientBidi.is_bidirectional());
        assert!(!StreamKind::ClientBidi.is_unidirectional());
        assert!(StreamKind::ClientBidi.is_client_initiated());

        assert!(StreamKind::ServerUni.is_unidirectional());
        assert!(!StreamKind::ServerUni.is_bidirectional());
        assert!(StreamKind::ServerUni.is_server_initiated());
    }

    #[test]
    fn test_stream_state() {
        let mut state = StreamState::Open;
        assert!(state.can_send());
        assert!(state.can_receive());

        state.close_local();
        assert_eq!(state, StreamState::LocalClosed);
        assert!(!state.can_send());
        assert!(state.can_receive());

        state.close_remote();
        assert_eq!(state, StreamState::Closed);
        assert!(!state.can_send());
        assert!(!state.can_receive());
    }

    #[test]
    fn test_stream_state_close_remote_then_local() {
        // close_local → close_remote とは逆順でも Closed に到達する
        let mut state = StreamState::Open;
        state.close_remote();
        assert_eq!(state, StreamState::RemoteClosed);
        assert!(state.can_send());
        assert!(!state.can_receive());

        state.close_local();
        assert_eq!(state, StreamState::Closed);
        assert!(!state.can_send());
        assert!(!state.can_receive());
    }

    #[test]
    fn test_send_buffer() {
        let mut buf = SendBuffer::new();
        assert!(!buf.has_pending());

        buf.push(b"hello");
        assert!(buf.has_pending());
        assert_eq!(buf.peek(), b"hello");
        assert!(!buf.is_data_consumed());

        buf.consume(3);
        assert_eq!(buf.peek(), b"lo");
        assert!(!buf.is_data_consumed());

        buf.set_fin();
        assert!(buf.is_fin());
        assert!(!buf.is_complete());

        buf.consume(2);
        // データは全て消費されたが FIN はまだ引き渡されていない
        assert!(buf.is_data_consumed());
        assert!(!buf.is_complete());
        assert!(buf.has_pending()); // FIN-only が残っている

        buf.mark_fin_sent();
        assert!(buf.is_fin_sent());
        assert!(buf.is_complete());
        assert!(!buf.has_pending());
    }

    #[test]
    fn test_recv_buffer() {
        let mut buf = RecvBuffer::new();
        assert!(!buf.has_data());

        buf.push(b"world");
        assert!(buf.has_data());
        assert_eq!(buf.peek(), b"world");

        buf.consume(2);
        assert_eq!(buf.peek(), b"rld");

        buf.set_fin();
        assert!(buf.is_fin());
        assert!(!buf.is_complete());

        buf.consume(3);
        assert!(buf.is_complete());
    }
}
