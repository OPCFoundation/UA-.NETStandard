//! WebTransport CONNECT レスポンス connect/mod.rs からの分離
//!
//! 拡張 CONNECT レスポンスの構築とバリデーションを担う。
//! (draft-ietf-webtrans-http3-15 Section 3.2)

use crate::qpack::Header;

use super::request::ConnectRequest;
use super::sf_parser::parse_sf_item_string;

#[derive(Debug, Clone)]
pub struct ConnectResponse {
    /// HTTP ステータスコード
    pub status: u16,
    /// `WT-Protocol` ヘッダーで選択されたプロトコル (draft-ietf-webtrans-http3-15 Section 3.3)
    pub selected_protocol: Option<String>,
}

impl ConnectResponse {
    /// 新しい CONNECT レスポンスを作成
    pub fn new(status: u16) -> Self {
        Self {
            status,
            selected_protocol: None,
        }
    }

    /// 選択されたプロトコルを設定
    pub fn with_protocol(mut self, protocol: impl Into<String>) -> Self {
        self.selected_protocol = Some(protocol.into());
        self
    }

    /// CONNECT レスポンスのヘッダー配列を生成する
    ///
    /// `:status` や `wt-protocol` の値は `Header::new` で構築時検査される。
    /// 不正値があれば `Err(HeaderError)` を返す。
    pub fn to_headers(&self) -> Result<Vec<Header>, crate::qpack::HeaderError> {
        let mut headers = vec![Header::new(b":status", self.status.to_string().as_bytes())?];

        if let Some(ref proto) = self.selected_protocol {
            headers.push(Header::new(
                b"wt-protocol",
                format!("\"{}\"", proto.replace('\\', "\\\\").replace('"', "\\\"")).as_bytes(),
            )?);
        }

        Ok(headers)
    }

    /// セッション確立成功かどうか (2xx ステータスコード)
    pub fn is_success(&self) -> bool {
        self.status / 100 == 2
    }

    /// `WT-Protocol` の検証 (draft-ietf-webtrans-http3-15 Section 3.3)
    ///
    /// レスポンスの `WT-Protocol` がリクエストの `WT-Available-Protocols` に
    /// 含まれているかを確認する。
    ///
    /// - リクエストに `WT-Available-Protocols` があり、レスポンスに `WT-Protocol` がない場合: `false`
    ///   (クライアントは WT_ALPN_ERROR でセッションを閉鎖する MUST)
    /// - リクエストに `WT-Available-Protocols` がなく、レスポンスに `WT-Protocol` がある場合: `false`
    /// - `WT-Protocol` が `WT-Available-Protocols` に含まれていない場合: `false`
    ///   (クライアントは WT_ALPN_ERROR でセッションを閉鎖する MUST)
    /// - リクエストに `WT-Available-Protocols` がなく、レスポンスに `WT-Protocol` もない場合: `true`
    /// - `WT-Protocol` が `WT-Available-Protocols` に含まれている場合: `true`
    ///
    /// 将来のドラフトで変更される可能性がある
    pub fn is_protocol_valid(&self, request: &ConnectRequest) -> bool {
        match &self.selected_protocol {
            None => {
                // クライアントがネゴシエーションを要求している場合、
                // レスポンスに WT-Protocol が必須 (draft-15 Section 3.3)
                request.available_protocols.is_empty()
            }
            Some(proto) => {
                if request.available_protocols.is_empty() {
                    false
                } else {
                    request.available_protocols.contains(proto)
                }
            }
        }
    }

    /// `WT-Protocol` ヘッダー値を解析 (draft-ietf-webtrans-http3-15 Section 3.3)
    ///
    /// Structured Fields Item 形式 (RFC 9651) から文字列型のみを抽出する。
    /// 文字列型でない場合は `None` を返す (draft-ietf-webtrans-http3-15 Section 3.3)。
    /// パラメータ (`;` 以降) は無視する (draft-ietf-webtrans-http3-15 Section 3.3)。
    pub fn parse_protocol(header_value: &str) -> Option<String> {
        parse_sf_item_string(header_value)
    }
}
