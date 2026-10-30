//! WebTransport CONNECT エラー型 connect/mod.rs からの分離
//!
//! CONNECT リクエスト/レスポンスのバリデーションエラーと
//! トランスポート能力の検証エラーを定義する。

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ConnectError {
    /// `:scheme` が `https` でない
    InvalidScheme,
    /// `:authority` が欠落している
    MissingAuthority,
    /// `:path` が欠落している
    MissingPath,
    /// `:method` が `CONNECT` でない
    InvalidMethod,
    /// `:protocol` が `webtransport-h3` または `webtransport` でない
    InvalidProtocol,
    /// ヘッダー値が不正な UTF-8
    InvalidEncoding,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CapabilityError {
    /// SETTINGS_WT_ENABLED (= 1) が確認できない
    MissingWebTransportSetting,
    /// SETTINGS_ENABLE_CONNECT_PROTOCOL (= 1) が確認できない
    MissingEnableConnectProtocol,
    /// SETTINGS_H3_DATAGRAM (= 1) が確認できない
    MissingH3Datagram,
    /// max_datagram_frame_size (> 0) が確認できない
    MissingQuicDatagram,
    /// reset_stream_at transport parameter が確認できない
    MissingResetStreamAt,
}

#[derive(Debug, Clone)]
pub struct TransportCapabilities {
    /// SETTINGS_WT_ENABLED が 1 か
    pub wt_enabled: bool,
    /// SETTINGS_ENABLE_CONNECT_PROTOCOL が 1 か
    pub enable_connect_protocol: bool,
    /// SETTINGS_H3_DATAGRAM が 1 か
    pub h3_datagram: bool,
    /// max_datagram_frame_size transport parameter が 0 より大きいか
    pub max_datagram_frame_size: bool,
    /// reset_stream_at transport parameter が存在するか
    pub reset_stream_at: bool,
}

impl Default for TransportCapabilities {
    fn default() -> Self {
        Self::new()
    }
}

impl TransportCapabilities {
    /// 新しい前提機能セットを作成
    pub const fn new() -> Self {
        Self {
            wt_enabled: false,
            enable_connect_protocol: false,
            h3_datagram: false,
            max_datagram_frame_size: false,
            reset_stream_at: false,
        }
    }

    /// WebTransport セッション開始前提を検証
    pub fn validate(&self) -> Result<(), CapabilityError> {
        if !self.wt_enabled {
            return Err(CapabilityError::MissingWebTransportSetting);
        }
        if !self.enable_connect_protocol {
            return Err(CapabilityError::MissingEnableConnectProtocol);
        }
        if !self.h3_datagram {
            return Err(CapabilityError::MissingH3Datagram);
        }
        if !self.max_datagram_frame_size {
            return Err(CapabilityError::MissingQuicDatagram);
        }
        if !self.reset_stream_at {
            return Err(CapabilityError::MissingResetStreamAt);
        }
        Ok(())
    }
}
