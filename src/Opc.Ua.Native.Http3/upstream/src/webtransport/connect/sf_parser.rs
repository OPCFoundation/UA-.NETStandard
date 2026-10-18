//! Structured Fields パーサー (RFC 9651 簡易実装)
//!
//! WebTransport の `wt-available-protocols` ヘッダーの解析に使用する。
//! connect.rs から分離したモジュール。

/// Structured Fields List 形式 (RFC 9651) から文字列型のアイテムのみを抽出する。
///
/// - 全アイテムがクォート文字列の場合のみ結果を返す
/// - 文字列型以外 (Integer, Token, Boolean 等) を含む場合はフィールド全体を無視する
///   (draft-ietf-webtrans-http3-15 Section 3.3)
/// - パラメータ (`;` 以降) は無視
/// - DoS 対策: 入力長・要素数・制御文字の制限 (RFC 9651 Section 4.2.5 推奨)
pub(crate) fn parse_sf_list_strings(value: &str) -> Vec<String> {
    // DoS 対策: 入力長上限 (RFC 9651 Section 4.2.5 推奨)
    const MAX_SF_INPUT_LEN: usize = 8192;
    if value.len() > MAX_SF_INPUT_LEN {
        return Vec::new();
    }
    // DoS 対策: 制御文字 (0x00-0x1f, 0x7f) を含む入力は拒否
    if value.bytes().any(|b| b < 0x20 || b == 0x7f) {
        return Vec::new();
    }
    let mut result = Vec::new();
    // DoS 対策: 要素数上限
    const MAX_SF_ELEMENTS: usize = 256;
    for item in value.split(',') {
        if result.len() >= MAX_SF_ELEMENTS {
            return Vec::new();
        }
        let item = item.trim();
        if item.is_empty() {
            continue;
        }
        match parse_sf_item_string(item) {
            Some(s) => result.push(s),
            None => {
                // 非 String 要素を検出: フィールド全体を無視
                // (draft-ietf-webtrans-http3-15 Section 3.3)
                return Vec::new();
            }
        }
    }
    result
}

/// Structured Fields Item から文字列型を抽出 (RFC 9651 簡易実装)
///
/// フォーマット: `"<string>"[;<params>]`
/// - クォート文字列でない場合は `None` を返す
/// - パラメータ (`;` 以降、クォート外) は無視する
pub(crate) fn parse_sf_item_string(value: &str) -> Option<String> {
    let value = value.trim();

    // クォート外のパラメータを除去 (`;` 以降)
    let value = strip_sf_parameters(value);
    let value = value.trim();

    // クォート文字列の解析 (RFC 9651 Section 3.3.3, Section 4.2.5)
    if value.starts_with('"') && value.ends_with('"') && value.len() >= 2 {
        let inner = &value[1..value.len() - 1];
        // RFC 9651: バックスラッシュエスケープ (\\ → \, \" → ")
        // 一時的に \\ を置換して \" の誤処理を防ぐ
        let unescaped = inner
            .replace("\\\\", "\x00")
            .replace("\\\"", "\"")
            .replace('\x00', "\\");
        Some(unescaped)
    } else {
        // クォートされていない = 文字列型ではない
        None
    }
}

/// Structured Fields のパラメータを除去 (クォート外の `;` 以降を削除)
fn strip_sf_parameters(value: &str) -> &str {
    let bytes = value.as_bytes();
    let mut in_string = false;
    let mut escaped = false;

    for (i, &b) in bytes.iter().enumerate() {
        if escaped {
            escaped = false;
            continue;
        }
        match b {
            b'"' => in_string = !in_string,
            b'\\' if in_string => escaped = true,
            b';' if !in_string => return &value[..i],
            _ => {}
        }
    }
    value
}
