//! WebTransport CONNECT リクエスト connect/mod.rs からの分離
//!
//! 拡張 CONNECT リクエストの構築とバリデーションを担う。
//! (draft-ietf-webtrans-http3-15 Section 3.2, 3.3)

use crate::qpack::Header;

use super::connect_error::ConnectError;
use super::draft::DraftVersion;
use super::sf_parser::parse_sf_list_strings;
use super::{PROTOCOL_WEBTRANSPORT_DRAFT02, PROTOCOL_WEBTRANSPORT_H3};

#[derive(Debug, Clone)]
pub struct ConnectRequest {
    /// ドラフトバージョン (デフォルト: Draft15)
    pub draft_version: DraftVersion,
    /// `:scheme` (MUST be `https` - draft-ietf-webtrans-http3-15 Section 3.2)
    pub scheme: String,
    /// `:authority` (MUST be present - draft-ietf-webtrans-http3-15 Section 3.2)
    pub authority: String,
    /// `:path` (MUST be present - draft-ietf-webtrans-http3-15 Section 3.2)
    pub path: String,
    /// `Origin` ヘッダー (ブラウザクライアントの場合は MUST - draft-ietf-webtrans-http3-15 Section 3.2)
    pub origin: Option<String>,
    /// `WT-Available-Protocols` ヘッダーから解析したプロトコルリスト (draft-ietf-webtrans-http3-15 Section 3.3)
    ///
    /// 優先度順で列挙する。空の場合はプロトコルネゴシエーションなし。
    pub available_protocols: Vec<String>,
}

impl ConnectRequest {
    /// 新しい CONNECT リクエストを作成 (デフォルト: Draft15)
    pub fn new(
        scheme: impl Into<String>,
        authority: impl Into<String>,
        path: impl Into<String>,
    ) -> Self {
        Self {
            draft_version: DraftVersion::Draft15,
            scheme: scheme.into(),
            authority: authority.into(),
            path: path.into(),
            origin: None,
            available_protocols: Vec::new(),
        }
    }

    /// ドラフトバージョンを設定
    pub fn draft_version(mut self, version: DraftVersion) -> Self {
        self.draft_version = version;
        self
    }

    /// `Origin` ヘッダーを設定
    pub fn origin(mut self, origin: impl Into<String>) -> Self {
        self.origin = Some(origin.into());
        self
    }

    /// `WT-Available-Protocols` を設定
    pub fn available_protocols(mut self, protocols: Vec<String>) -> Self {
        self.available_protocols = protocols;
        self
    }

    /// ヘッダーペアから CONNECT リクエストを構築する
    ///
    /// `Event::Header` で受信したヘッダーを集めて渡す。
    /// パースのみ行い、RFC 準拠の検証は `validate()` で別途行う。
    ///
    /// # エラー
    ///
    /// - `ConnectError::InvalidMethod`: `:method` が `CONNECT` でない
    /// - `ConnectError::InvalidProtocol`: `:protocol` が `webtransport-h3` / `webtransport` でない
    /// - `ConnectError::InvalidEncoding`: ヘッダー値が不正な UTF-8
    pub fn from_headers(headers: &[(&[u8], &[u8])]) -> Result<Self, ConnectError> {
        let mut method: Option<&[u8]> = None;
        let mut protocol: Option<&[u8]> = None;
        let mut scheme: Option<String> = None;
        let mut authority: Option<String> = None;
        let mut path: Option<String> = None;
        let mut origin: Option<String> = None;
        let mut wt_available_protocols: Option<String> = None;
        // RFC 9114 Section 4.1.2: 疑似ヘッダーの重複は malformed
        let mut seen_regular = false;

        for &(name, value) in headers {
            let is_pseudo = name.starts_with(b":");
            // RFC 9114 Section 4.1.2: 疑似ヘッダーは通常ヘッダーより前に配置 (MUST)
            if is_pseudo && seen_regular {
                return Err(ConnectError::InvalidEncoding);
            }
            if !is_pseudo {
                seen_regular = true;
            }
            match name {
                b":method" => {
                    if method.is_some() {
                        return Err(ConnectError::InvalidEncoding);
                    }
                    method = Some(value);
                }
                b":protocol" => {
                    if protocol.is_some() {
                        return Err(ConnectError::InvalidEncoding);
                    }
                    protocol = Some(value);
                }
                b":scheme" => {
                    if scheme.is_some() {
                        return Err(ConnectError::InvalidEncoding);
                    }
                    scheme = Some(
                        core::str::from_utf8(value)
                            .map_err(|_| ConnectError::InvalidEncoding)?
                            .to_string(),
                    );
                }
                b":authority" => {
                    if authority.is_some() {
                        return Err(ConnectError::InvalidEncoding);
                    }
                    authority = Some(
                        core::str::from_utf8(value)
                            .map_err(|_| ConnectError::InvalidEncoding)?
                            .to_string(),
                    );
                }
                b":path" => {
                    if path.is_some() {
                        return Err(ConnectError::InvalidEncoding);
                    }
                    path = Some(
                        core::str::from_utf8(value)
                            .map_err(|_| ConnectError::InvalidEncoding)?
                            .to_string(),
                    );
                }
                b"origin" => {
                    origin = Some(
                        core::str::from_utf8(value)
                            .map_err(|_| ConnectError::InvalidEncoding)?
                            .to_string(),
                    );
                }
                b"wt-available-protocols" => {
                    wt_available_protocols = Some(
                        core::str::from_utf8(value)
                            .map_err(|_| ConnectError::InvalidEncoding)?
                            .to_string(),
                    );
                }
                _ => {}
            }
        }

        // :method の検証
        match method {
            Some(b"CONNECT") => {}
            Some(_) => return Err(ConnectError::InvalidMethod),
            None => return Err(ConnectError::InvalidMethod),
        }

        // :protocol の検証とドラフトバージョンの判定
        let draft_version = match protocol {
            Some(p) => {
                let p_str = core::str::from_utf8(p).map_err(|_| ConnectError::InvalidEncoding)?;
                if p_str == PROTOCOL_WEBTRANSPORT_H3 {
                    DraftVersion::Draft15
                } else if p_str == PROTOCOL_WEBTRANSPORT_DRAFT02 {
                    // draft-02 と draft-07 は同じ `:protocol` 値を使用するため
                    // ヘッダーだけでは区別できない。draft-02 として扱う。
                    DraftVersion::Draft02
                } else {
                    return Err(ConnectError::InvalidProtocol);
                }
            }
            None => return Err(ConnectError::InvalidProtocol),
        };

        let mut req = Self {
            draft_version,
            scheme: scheme.unwrap_or_default(),
            authority: authority.unwrap_or_default(),
            path: path.unwrap_or_default(),
            origin,
            available_protocols: Vec::new(),
        };

        if let Some(ref wt_ap) = wt_available_protocols {
            req.available_protocols = Self::parse_available_protocols(wt_ap);
        }

        Ok(req)
    }

    /// CONNECT リクエストのヘッダー配列を生成する
    ///
    /// `:protocol` の値はドラフトバージョンに依存する。
    ///
    /// 各フィールドの値は `Header::new` で構築時検査される。`:authority` や `:path`
    /// などのフィールドに RFC 9110 / RFC 9114 に違反する値が入っていた場合は
    /// `Err(HeaderError)` を返す。
    pub fn to_headers(&self) -> Result<Vec<Header>, crate::qpack::HeaderError> {
        let mut headers = vec![
            Header::new(b":method", b"CONNECT")?,
            Header::new(b":protocol", self.draft_version.protocol_value().as_bytes())?,
            Header::new(b":scheme", self.scheme.as_bytes())?,
            Header::new(b":authority", self.authority.as_bytes())?,
            Header::new(b":path", self.path.as_bytes())?,
        ];

        if let Some(ref origin) = self.origin {
            headers.push(Header::new(b"origin", origin.as_bytes())?);
        }

        if !self.available_protocols.is_empty() {
            let value = self
                .available_protocols
                .iter()
                .map(|p| format!("\"{}\"", p.replace('\\', "\\\\").replace('"', "\\\"")))
                .collect::<Vec<_>>()
                .join(", ");
            headers.push(Header::new(b"wt-available-protocols", value.as_bytes())?);
        }

        Ok(headers)
    }

    /// draft-ietf-webtrans-http3-15 Section 3.2 に従いリクエストを検証
    ///
    /// 以下を検証する:
    /// - `:scheme` が `https` であること
    /// - `:authority` が空でないこと
    /// - `:path` が空でないこと
    ///
    /// `:protocol` が `webtransport-h3` であることは呼び出し元が事前に確認する。
    pub fn validate(&self) -> Result<(), ConnectError> {
        if self.scheme != "https" {
            return Err(ConnectError::InvalidScheme);
        }
        if self.authority.is_empty() {
            return Err(ConnectError::MissingAuthority);
        }
        if self.path.is_empty() {
            return Err(ConnectError::MissingPath);
        }
        Ok(())
    }

    /// `WT-Available-Protocols` ヘッダー値を解析 (draft-ietf-webtrans-http3-15 Section 3.3)
    ///
    /// Structured Fields List 形式 (RFC 9651) から文字列型のアイテムのみを抽出する。
    /// 文字列型以外のアイテムはエラーとして無視する (draft-ietf-webtrans-http3-15 Section 3.3)。
    /// パラメータ (`;` 以降) は無視する (draft-ietf-webtrans-http3-15 Section 3.3)。
    pub fn parse_available_protocols(header_value: &str) -> Vec<String> {
        parse_sf_list_strings(header_value)
    }
}
