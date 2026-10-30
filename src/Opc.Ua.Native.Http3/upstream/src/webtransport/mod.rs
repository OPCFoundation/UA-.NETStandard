//! WebTransport over HTTP/3 (draft-ietf-webtrans-http3-15)
//!
//! Sans I/O WebTransport プロトコル実装。
//!
//! # 概要
//!
//! WebTransport は HTTP/3 上で動作するプロトコルで、Web アプリケーションに
//! 双方向・単方向ストリームとデータグラムを提供する。
//!
//! HTTP/3 上では 2 つのモードが利用可能 (draft-ietf-webtrans-http3-15 Section 2.1.2):
//! - `webtransport-h3`: ネイティブ QUIC ストリーム + データグラム (本実装)
//! - `webtransport`: カプセルベース (head-of-line blocking、unreliable delivery 不可)
//!
//! # 機能
//!
//! - セッション管理 (確立、ドレイン、クローズ)
//! - 双方向/単方向ストリーム
//! - フロー制御 (ストリーム数、データ量)
//! - Capsule Protocol
//! - CONNECT リクエスト/レスポンス検証
//! - HTTP Datagram (Quarter Stream ID)
//!
//! # 参照
//!
//! - [draft-ietf-webtrans-http3-15](https://datatracker.ietf.org/doc/draft-ietf-webtrans-http3/)

pub mod capsule;
pub mod connect;
pub mod datagram;
pub mod error;
pub mod session;
pub mod settings;
pub mod stream;

pub use capsule::{
    Capsule, CapsuleType, CapsuleValidationError, H3_DATAGRAM_ERROR, MAX_STREAMS_LIMIT,
    PROHIBITED_WT_MAX_STREAM_DATA_CAPSULE_TYPE, PROHIBITED_WT_STREAM_DATA_BLOCKED_CAPSULE_TYPE,
};
pub use connect::{
    CapabilityError, ConnectError, ConnectRequest, ConnectResponse, DraftVersion,
    PROTOCOL_WEBTRANSPORT_DRAFT02, PROTOCOL_WEBTRANSPORT_H3, ServerSettingsParams,
    TransportCapabilities,
};
pub use datagram::Datagram;
pub use error::{ApplicationErrorCode, Error, ErrorCode};
pub use session::{
    BufferedStream, CapsuleProcessError, FlowControlLimits, FlowControlState, Session, SessionState,
};
pub use settings::Settings;
pub use stream::{
    BIDIRECTIONAL_SIGNAL_VALUE, ClassifiedUniStream, Stream, StreamHeader, StreamHeaderDecodeError,
    UNIDIRECTIONAL_STREAM_TYPE, classify_uni_stream, classify_uni_stream_checked, stream_type,
};
