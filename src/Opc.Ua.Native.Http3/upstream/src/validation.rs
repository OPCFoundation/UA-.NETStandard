//! HTTP/3 メッセージ検証 (RFC 9114 Section 4.1.2)
//!
//! 送受信するヘッダーセクションが malformed でないかを検証する。
//! ヘッダーフィールド単体の構文検査は `qpack::Header` の構築時検査に統合済み。
//! 本モジュールはヘッダーリスト全体や他フィールドとの組み合わせに依存する検査
//! (CONNECT の構成、`:path` / `:authority` の構文、疑似ヘッダーの順序/重複/存在等)
//! を担当する。
//!
//! `qpack::header` の `check_header` と意味的に等価な検査ロジックを意図的に重複させている。
//! decoder 経由の `from_validated_parts_internal` は構築時検査をスキップするため、
//! wire 上の不正バイト列をここで独立に検査する必要がある。
//! 両者の挙動乖離は PBT (`pbt/tests/prop_validation.rs`) で検出する。

use crate::connection::Role;
use crate::error::{Error, ErrorCode};
use crate::qpack::Header;

/// 接続固有フィールド (RFC 9114 Section 4.2)
///
/// HTTP/3 では接続固有フィールドは禁止されている。
const CONNECTION_SPECIFIC_FIELDS: &[&[u8]] = &[
    b"connection",
    b"keep-alive",
    b"proxy-connection",
    b"transfer-encoding",
    b"upgrade",
];

/// HTTP token の 1 バイトが tchar であるかを検証する (RFC 9110 Section 5.6.2)
///
/// token = 1*tchar
/// tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." /
///         "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
///
/// 大文字・小文字の両方を含む。method 等の検証に使用する。
fn is_tchar(b: u8) -> bool {
    matches!(b,
        b'!' | b'#' | b'$' | b'%' | b'&' | b'\'' | b'*' | b'+' | b'-' | b'.' |
        b'^' | b'_' | b'`' | b'|' | b'~' |
        b'0'..=b'9' |
        b'A'..=b'Z' |
        b'a'..=b'z'
    )
}

/// HTTP method が有効な token であるかを検証する (RFC 9110 Section 9.1)
///
/// method = token = 1*tchar
fn is_valid_method(method: &[u8]) -> bool {
    !method.is_empty() && method.iter().all(|&b| is_tchar(b))
}

/// URI scheme が RFC 3986 Section 3.1 の文法に準拠するかを検証する
///
/// scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
fn is_valid_scheme(scheme: &[u8]) -> bool {
    if scheme.is_empty() {
        return false;
    }
    if !scheme[0].is_ascii_alphabetic() {
        return false;
    }
    scheme[1..]
        .iter()
        .all(|&b| b.is_ascii_alphanumeric() || b == b'+' || b == b'-' || b == b'.')
}

/// `:protocol` の値が HTTP Upgrade Token (RFC 8441 Section 4 / RFC 9220 Section 3)
/// として有効かを検証する
///
/// Upgrade Token は RFC 9110 Section 7.8 で `protocol-name ["/" protocol-version]`
/// と定義される (両構成要素は token = 1*tchar)。空文字列は不正。
///
/// 仕様根拠:
/// - RFC 8441 Section 4: `:protocol` の値は HTTP Upgrade Token Registry の値
/// - RFC 9220 Section 3: HTTP/3 への Extended CONNECT 適用
/// - RFC 9110 Section 7.8: protocol = protocol-name ["/" protocol-version]
fn is_valid_protocol(value: &[u8]) -> bool {
    if value.is_empty() {
        return false;
    }
    // protocol-name と protocol-version は token = 1*tchar
    let mut parts = value.splitn(2, |b| *b == b'/');
    let name = parts.next().unwrap_or(&[]);
    if name.is_empty() || !name.iter().all(|&b| is_tchar(b)) {
        return false;
    }
    if let Some(version) = parts.next()
        && (version.is_empty() || !version.iter().all(|&b| is_tchar(b)))
    {
        return false;
    }
    true
}

/// :path が http/https scheme で有効かを検証する (RFC 9114 Section 4.3.1)
///
/// path-absolute (RFC 3986 Section 3.3) = "/" *( "/" segment ) で始まるか、
/// OPTIONS リクエストの場合は "*" (RFC 9110 Section 7.1) も許可する。
fn is_valid_http_path(path: &[u8], method: &[u8]) -> bool {
    if path.is_empty() {
        return false;
    }
    // OPTIONS の asterisk-form
    if method == b"OPTIONS" && path == b"*" {
        return true;
    }
    // path-absolute: "/" で始まる
    path[0] == b'/'
}

/// フィールド名の 1 バイトが token 文字 (tchar) であるかを検証する (RFC 9110 Section 5.6.2)
///
/// token = 1*tchar
/// tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." /
///         "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
///
/// 大文字の ALPHA は HTTP/3 では禁止 (RFC 9114 Section 4.2) なので含めない。
fn is_valid_field_name_byte(b: u8) -> bool {
    matches!(b,
        b'!' | b'#' | b'$' | b'%' | b'&' | b'\'' | b'*' | b'+' | b'-' | b'.' |
        b'^' | b'_' | b'`' | b'|' | b'~' |
        b'0'..=b'9' |
        b'a'..=b'z'
    )
}

/// フィールド名全体が有効な token かを検証する (RFC 9110 Section 5.1, RFC 9114 Section 4.2)
///
/// - 空のフィールド名は不正
/// - 大文字を含むフィールド名は不正 (RFC 9114 Section 4.2)
/// - token 文字以外を含むフィールド名は不正 (RFC 9114 Section 10.3)
fn is_valid_field_name(name: &[u8]) -> bool {
    !name.is_empty() && name.iter().all(|&b| is_valid_field_name_byte(b))
}

/// フィールド値が field-content に準拠するかを検証する (RFC 9110 Section 5.5, RFC 9114 Section 10.3)
///
/// field-value = *field-content
/// field-content = field-vchar [ 1*( SP / HTAB / field-vchar ) field-vchar ]
/// field-vchar = VCHAR / obs-text
/// VCHAR = %x21-7E
/// obs-text = %x80-FF
///
/// 空のフィールド値は許可 (field-value = *field-content)。
/// 非空の場合、先頭と末尾は field-vchar でなければならない。
/// 途中では SP (0x20) / HTAB (0x09) も許可される。
fn is_valid_field_value(value: &[u8]) -> bool {
    if value.is_empty() {
        return true;
    }

    // field-vchar かどうかの判定
    let is_field_vchar = |b: u8| -> bool { matches!(b, 0x21..=0x7e | 0x80..=0xff) };

    // 先頭と末尾は field-vchar でなければならない
    if !is_field_vchar(value[0]) || !is_field_vchar(value[value.len() - 1]) {
        return false;
    }

    // 途中は field-vchar / SP / HTAB のみ許可
    value
        .iter()
        .all(|&b| is_field_vchar(b) || b == b' ' || b == b'\t')
}

/// URI-host の reg-name として妥当な文字かを判定する (RFC 3986 Section 3.2.2)
///
/// reg-name = *( unreserved / pct-encoded / sub-delims )
/// unreserved = ALPHA / DIGIT / "-" / "." / "_" / "~"
/// sub-delims = "!" / "$" / "&" / "'" / "(" / ")" / "*" / "+" / "," / ";" / "="
fn is_reg_name_char(b: u8) -> bool {
    b.is_ascii_alphanumeric()
        || matches!(
            b,
            b'-' | b'.'
                | b'_'
                | b'~'
                | b'!'
                | b'$'
                | b'&'
                | b'\''
                | b'('
                | b')'
                | b'*'
                | b'+'
                | b','
                | b';'
                | b'='
                | b'%'
        )
}

/// URI-host (reg-name) として妥当かを検証する (RFC 3986 Section 3.2.2)
///
/// pct-encoded の完全な検証 (%XX の 16 進数チェック) も行う。
fn is_valid_reg_name(host: &[u8]) -> bool {
    let mut i = 0;
    while i < host.len() {
        if host[i] == b'%' {
            // pct-encoded = "%" HEXDIG HEXDIG
            if i + 2 >= host.len()
                || !host[i + 1].is_ascii_hexdigit()
                || !host[i + 2].is_ascii_hexdigit()
            {
                return false;
            }
            i += 3;
        } else if is_reg_name_char(host[i]) {
            i += 1;
        } else {
            return false;
        }
    }
    true
}

/// IPv6address の括弧内が妥当な文字で構成されているかを検証する (RFC 3986 Section 3.2.2)
///
/// IP-literal = "[" ( IPv6address / IPvFuture ) "]"
/// 厳密な IPv6 アドレスパースは行わず、許可文字セット (HEXDIG / ":" / ".") のみ検証する。
fn is_valid_ip_literal_content(content: &[u8]) -> bool {
    if content.is_empty() {
        return false;
    }
    // IPvFuture = "v" 1*HEXDIG "." 1*( unreserved / sub-delims / ":" )
    if content[0] == b'v' {
        return content.len() >= 4
            && content[1..].contains(&b'.')
            && content[1..].iter().all(|&b| {
                b.is_ascii_hexdigit()
                    || b == b'.'
                    || b == b':'
                    || b == b'-'
                    || b == b'_'
                    || b == b'~'
                    || matches!(
                        b,
                        b'!' | b'$' | b'&' | b'\'' | b'(' | b')' | b'*' | b'+' | b',' | b';' | b'='
                    )
            });
    }
    // IPv6address: HEXDIG / ":" / "." (IPv4-mapped 用)
    content
        .iter()
        .all(|&b| b.is_ascii_hexdigit() || b == b':' || b == b'.')
}

/// :authority (非 CONNECT / Extended CONNECT) または代替の Host ヘッダーが URI authority
/// として妥当かを検証する (RFC 9114 Section 4.3.1, RFC 9110 Section 7.2, RFC 3986 Section 3.2)
///
/// authority = host [ ":" port ]
/// host = IP-literal / IPv4address / reg-name
/// ポートはオプション。userinfo (@) はいずれの経路でも許可文字に含まれないため拒否される。
fn is_valid_authority(value: &[u8]) -> bool {
    if value.is_empty() {
        return false;
    }

    // IP-literal: [IPv6address] または [IPv6address]:port
    if value[0] == b'[' {
        let Some(bracket_end) = value.iter().position(|&b| b == b']') else {
            return false;
        };
        let literal_content = &value[1..bracket_end];
        if !is_valid_ip_literal_content(literal_content) {
            return false;
        }
        let rest = &value[bracket_end + 1..];
        if rest.is_empty() {
            return true;
        }
        // ]:port の形式
        if rest[0] != b':' {
            return false;
        }
        let port = &rest[1..];
        return port.iter().all(|b| b.is_ascii_digit());
    }

    // host[:port] の形式
    // ':' で分割してポート部分を検出する
    if let Some(colon_pos) = value.iter().rposition(|&b| b == b':') {
        let host = &value[..colon_pos];
        let port = &value[colon_pos + 1..];
        // ':' の後がすべて数字ならポート付き、そうでなければ ':' はホスト名の一部ではない
        // (reg-name に ':' は許可されないので、ポートとして解釈する)
        if !port.is_empty() && port.iter().all(|b| b.is_ascii_digit()) {
            return !host.is_empty() && is_valid_reg_name(host);
        }
    }

    // ポートなし: 全体がホスト名
    is_valid_reg_name(value)
}

/// plain CONNECT の :authority が authority-form (host:port) かを検証する
/// (RFC 9114 Section 4.4, RFC 9110 Section 7.1)
///
/// authority-form = uri-host ":" port
/// IPv6 の場合は [host]:port の形式
fn is_valid_connect_authority(value: &[u8]) -> bool {
    if value.is_empty() {
        return false;
    }

    // IPv6: [host]:port
    if value[0] == b'[' {
        // ']' を探す
        let Some(bracket_end) = value.iter().position(|&b| b == b']') else {
            return false;
        };
        // ']:port' の形式を期待
        if bracket_end + 1 >= value.len() || value[bracket_end + 1] != b':' {
            return false;
        }
        // IP-literal の中身を検証 (RFC 3986 Section 3.2.2)
        let literal_content = &value[1..bracket_end];
        if !is_valid_ip_literal_content(literal_content) {
            return false;
        }
        let port = &value[bracket_end + 2..];
        return !port.is_empty() && port.iter().all(|b| b.is_ascii_digit());
    }

    // IPv4 / ドメイン名: host:port
    // 最後の ':' で分割 (ホスト部に ':' は含まれない)
    let Some(colon_pos) = value.iter().rposition(|&b| b == b':') else {
        return false;
    };

    let host = &value[..colon_pos];
    let port = &value[colon_pos + 1..];

    // host と port がそれぞれ空でないこと、host が URI-host として妥当であること
    !host.is_empty()
        && !port.is_empty()
        && is_valid_reg_name(host)
        && port.iter().all(|b| b.is_ascii_digit())
}

/// リクエストヘッダーを検証 (RFC 9114 Section 4.1.2, 4.3.1, 4.4)
pub fn validate_request_headers(headers: &[Header]) -> Result<(), Error> {
    let mut method: Option<&[u8]> = None;
    let mut scheme: Option<&[u8]> = None;
    let mut path: Option<&[u8]> = None;
    let mut authority: Option<&[u8]> = None;
    let mut protocol: Option<&[u8]> = None;
    let mut host: Option<&[u8]> = None;
    let mut pseudo_done = false;

    for header in headers {
        if header.name().starts_with(b":") {
            // 擬似ヘッダーが通常ヘッダーの後に出現 (RFC 9114 Section 4.3)
            if pseudo_done {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            match header.name() {
                b":method" => {
                    if method.is_some() {
                        // 重複 (RFC 9114 Section 4.3.1)
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    method = Some(header.value());
                }
                b":scheme" => {
                    if scheme.is_some() {
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    scheme = Some(header.value());
                }
                b":path" => {
                    if path.is_some() {
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    path = Some(header.value());
                }
                b":authority" => {
                    if authority.is_some() {
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    authority = Some(header.value());
                }
                b":protocol" => {
                    // Extended CONNECT (RFC 8441, RFC 9220)
                    if protocol.is_some() {
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    protocol = Some(header.value());
                }
                b":status" => {
                    // レスポンス専用の擬似ヘッダー
                    return Err(Error::StreamError(ErrorCode::MessageError));
                }
                _ => {
                    // 未定義の擬似ヘッダー (RFC 9114 Section 4.3)
                    return Err(Error::StreamError(ErrorCode::MessageError));
                }
            }
        } else {
            pseudo_done = true;

            // フィールド名の検証 (RFC 9110 Section 5.1, RFC 9114 Section 4.2, 10.3)
            if !is_valid_field_name(header.name()) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            // 接続固有フィールドの検出 (RFC 9114 Section 4.2)
            if CONNECTION_SPECIFIC_FIELDS.contains(&header.name()) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            // TE ヘッダーは "trailers" のみ許可 (RFC 9114 Section 4.2)
            if header.name() == b"te" && header.value() != b"trailers" {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            // フィールド値の検証 (RFC 9110 Section 5.5, RFC 9114 Section 10.3)
            if !is_valid_field_value(header.value()) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            // Host は単一値フィールドで複数生成は禁止 (RFC 9110 Section 5.3、ABNF は
            // Section 7.2)。:authority の重複拒否 (上記参照) と同じく拒否し、:authority との
            // 一致チェックを最後の Host だけで通過させる迂回を防ぐ。受信側拒否は RFC の MUST
            // ではなく堅牢性向上。
            if header.name() == b"host" {
                if host.is_some() {
                    return Err(Error::StreamError(ErrorCode::MessageError));
                }
                host = Some(header.value());
            }
        }
    }

    // :method は必須かつ有効な token でなければならない (RFC 9114 Section 4.3.1, RFC 9110 Section 9.1)
    let method = method.ok_or(Error::StreamError(ErrorCode::MessageError))?;
    if !is_valid_method(method) {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }

    if method == b"CONNECT" && protocol.is_some() {
        // Extended CONNECT (RFC 8441, RFC 9220)
        // :protocol が存在する場合は :scheme, :path が必須
        if scheme.is_none() || path.is_none() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // :protocol は HTTP Upgrade Token として構文上有効でなければならない
        // (RFC 8441 Section 4 / RFC 9220 Section 3 / RFC 9110 Section 7.8)
        if let Some(p) = protocol
            && !is_valid_protocol(p)
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // :scheme は有効な URI scheme でなければならない (RFC 3986 Section 3.1)
        if let Some(s) = scheme
            && !is_valid_scheme(s)
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // :path は空であってはならない (RFC 9114 Section 4.3.1)
        if let Some(p) = path
            && p.is_empty()
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // mandatory authority component を持つ scheme (http, https) では :authority が必須
        // (RFC 9114 Section 4.3.1, RFC 8441 Section 4)
        let ext_scheme_requires_authority =
            matches!(scheme, Some(s) if s == b"http" || s == b"https");
        if ext_scheme_requires_authority && authority.is_none() && host.is_none() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // 非 http/https スキームでは :authority / Host の有無を制限しない。
        // RFC 9114 Section 4.3.1 の MUST NOT は「scheme が mandatory authority を
        // 持たず、かつリクエストターゲットに authority がない」場合のみ適用される。
        // Sans I/O ライブラリとして任意のスキームの authority 要件を判断できないため、
        // スキーム固有の検証は呼び出し側の責務とする。
        // :authority と host の整合チェック (RFC 9114 Section 4.3.1)
        if let (Some(a), Some(h)) = (authority, host)
            && a != h
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // :authority / host が存在する場合は空であってはならない (RFC 9114 Section 4.3.1)
        if let Some(a) = authority
            && a.is_empty()
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        if let Some(h) = host
            && h.is_empty()
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
    } else if method == b"CONNECT" {
        // 通常の CONNECT リクエスト (RFC 9114 Section 4.4)
        // :scheme と :path は存在してはならない
        if scheme.is_some() || path.is_some() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // :protocol は通常 CONNECT では不正
        // (ここには到達しない: protocol.is_some() は上の分岐で処理済み)

        // :authority は必須かつ authority-form (host:port) でなければならない
        // (RFC 9114 Section 4.4, RFC 9110 Section 7.1)
        match authority {
            None => return Err(Error::StreamError(ErrorCode::MessageError)),
            Some([]) => return Err(Error::StreamError(ErrorCode::MessageError)),
            Some(val) => {
                if !is_valid_connect_authority(val) {
                    return Err(Error::StreamError(ErrorCode::MessageError));
                }
            }
        }
    } else {
        // :protocol は非 CONNECT では不正
        if protocol.is_some() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        // 非 CONNECT リクエスト (RFC 9114 Section 4.3.1)
        // :method, :scheme, :path は必須
        if scheme.is_none() || path.is_none() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // :scheme は有効な URI scheme でなければならない (RFC 3986 Section 3.1)
        if let Some(s) = scheme
            && !is_valid_scheme(s)
        {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // :path の検証 (RFC 9114 Section 4.3.1)
        // http/https では path-absolute ("/" 始まり) または "*" (OPTIONS) でなければならない
        let is_http_or_https = matches!(scheme, Some(s) if s == b"http" || s == b"https");
        if let Some(p) = path {
            if p.is_empty() {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }
            if is_http_or_https && !is_valid_http_path(p, method) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }
        }

        // mandatory authority component を持つ scheme (http, https) では
        // :authority または Host のいずれかが必須 (RFC 9114 Section 4.3.1)
        if is_http_or_https && authority.is_none() && host.is_none() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // 非 http/https スキームでは :authority / Host の有無を制限しない。
        // RFC 9114 Section 4.3.1 の MUST NOT は「scheme が mandatory authority を
        // 持たず、かつリクエストターゲットに authority がない」場合のみ適用される。
        // Sans I/O ライブラリとして任意のスキームの authority 要件を判断できないため、
        // スキーム固有の検証は呼び出し側の責務とする。
    }

    // :authority と host の整合 (RFC 9114 Section 4.3.1)
    if let (Some(a), Some(h)) = (authority, host)
        && a != h
    {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }

    // :authority / host が存在する場合は空であってはならない (RFC 9114 Section 4.3.1)
    if let Some(a) = authority
        && a.is_empty()
        && method != b"CONNECT"
    {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }
    if let Some(h) = host
        && h.is_empty()
    {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }

    // :authority が無く Host のみで authority 情報を運ぶ場合、Host を :authority と同じ
    // 構文 (uri-host[:port]) で検証する (RFC 9110 Section 7.2, RFC 3986 Section 3.2.2)。
    // RFC の MUST ではなく :authority との検証レベルを揃える堅牢性向上。plain CONNECT は
    // :authority 必須 (上記で None を拒否済み) のためこの分岐には入らない。
    if authority.is_none()
        && let Some(h) = host
        && !is_valid_authority(h)
    {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }

    // http / https スキームでは :authority に userinfo を含めてはならない (RFC 9114 Section 4.3.1)
    // 通常の CONNECT は :scheme を持たないため、このチェックは Extended CONNECT と非 CONNECT に適用される
    let is_http_scheme = matches!(scheme, Some(s) if s == b"http" || s == b"https");
    if is_http_scheme
        && let Some(a) = authority
        && a.contains(&b'@')
    {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }

    // :authority が URI authority として有効な形式かを検証する
    // (RFC 9114 Section 4.3.1, RFC 3986 Section 3.2)
    // plain CONNECT は is_valid_connect_authority で別途検証済みなのでここでは除外する
    if (method != b"CONNECT" || protocol.is_some())
        && let Some(a) = authority
        && !is_valid_authority(a)
    {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }

    Ok(())
}

/// レスポンスヘッダーを検証 (RFC 9114 Section 4.1.2, 4.3.2)
pub fn validate_response_headers(headers: &[Header]) -> Result<(), Error> {
    let mut status: Option<&[u8]> = None;
    let mut pseudo_done = false;

    for header in headers {
        if header.name().starts_with(b":") {
            // 擬似ヘッダーが通常ヘッダーの後に出現
            if pseudo_done {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            match header.name() {
                b":status" => {
                    if status.is_some() {
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    // :status は 3 桁の ASCII 数字でなければならない (RFC 9110 status-code = 3DIGIT)
                    if header.value().len() != 3
                        || !header.value().iter().all(|b| b.is_ascii_digit())
                    {
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    // HTTP/3 は 101 (Switching Protocols) をサポートしない (RFC 9114 Section 4.5)
                    if header.value() == b"101" {
                        return Err(Error::StreamError(ErrorCode::MessageError));
                    }
                    status = Some(header.value());
                }
                _ => {
                    // レスポンスではリクエスト擬似ヘッダーも未定義擬似ヘッダーも malformed
                    return Err(Error::StreamError(ErrorCode::MessageError));
                }
            }
        } else {
            pseudo_done = true;

            // フィールド名の検証 (RFC 9110 Section 5.1, RFC 9114 Section 4.2, 10.3)
            if !is_valid_field_name(header.name()) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            // 接続固有フィールドの検出
            if CONNECTION_SPECIFIC_FIELDS.contains(&header.name()) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            // TE ヘッダーはリクエストのみ許可。レスポンスには存在してはならない (RFC 9114 Section 4.2)
            if header.name() == b"te" {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }

            // フィールド値の検証 (RFC 9110 Section 5.5, RFC 9114 Section 10.3)
            if !is_valid_field_value(header.value()) {
                return Err(Error::StreamError(ErrorCode::MessageError));
            }
        }
    }

    // :status は必須 (RFC 9114 Section 4.3.2)
    status.ok_or(Error::StreamError(ErrorCode::MessageError))?;

    Ok(())
}

/// ヘッダーを role に応じて検証
pub fn validate_headers(headers: &[Header], role: Role) -> Result<(), Error> {
    match role {
        // サーバーが受信するのはリクエスト
        Role::Server => validate_request_headers(headers),
        // クライアントが受信するのはレスポンス
        Role::Client => validate_response_headers(headers),
    }
}

/// content-length と受信済み DATA フレームの整合性を検証する (RFC 9114 Section 4.1.2)
///
/// - content-length ヘッダーが存在しない場合: 検証不要 (Ok)
/// - content-length ヘッダーが複数ある場合: malformed
/// - content-length 値が非負整数でない場合: malformed
/// - skip_body_check == false かつ値 != received_body_size の場合: malformed
///
/// HEAD レスポンスや 1xx/204/304 レスポンスは skip_body_check = true を渡すこと。
pub fn validate_content_length(
    headers: &[Header],
    received_body_size: u64,
    skip_body_check: bool,
) -> Result<(), Error> {
    let mut content_length: Option<u64> = None;

    for header in headers {
        if header.name() != b"content-length" {
            continue;
        }

        // 複数の content-length は malformed (RFC 9114 Section 4.1.2)
        if content_length.is_some() {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // 値が 1*DIGIT でなければ malformed (RFC 9110 Section 8.6)。
        // u64 への parse は先頭の + を許容するため、全バイトの数字検査を先に行う。
        let value_str = std::str::from_utf8(header.value())
            .map_err(|_| Error::StreamError(ErrorCode::MessageError))?;
        if !value_str.bytes().all(|b| b.is_ascii_digit()) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
        let value = value_str
            .parse::<u64>()
            .map_err(|_| Error::StreamError(ErrorCode::MessageError))?;
        content_length = Some(value);
    }

    let Some(expected) = content_length else {
        // content-length ヘッダーなし: 検証不要
        return Ok(());
    };

    if skip_body_check {
        // HEAD レスポンス・1xx/204/304 レスポンス: DATA なしでも正当
        return Ok(());
    }

    // content-length と受信済み DATA の合計バイト数が一致しなければ malformed
    if expected != received_body_size {
        return Err(Error::StreamError(ErrorCode::MessageError));
    }

    Ok(())
}

/// トレーラーヘッダーを検証 (RFC 9114 Section 4.1.2, 4.3)
///
/// トレーラーセクションには疑似ヘッダーを含めてはならない (RFC 9114 Section 4.3)。
/// ロールに関わらず同じルールが適用される。
pub fn validate_trailer_headers(headers: &[Header]) -> Result<(), Error> {
    for header in headers {
        // トレーラーに疑似ヘッダーは禁止 (RFC 9114 Section 4.3)
        if header.name().starts_with(b":") {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // フィールド名の検証 (RFC 9110 Section 5.1, RFC 9114 Section 4.2, 10.3)
        if !is_valid_field_name(header.name()) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // 接続固有フィールドの検出 (RFC 9114 Section 4.2)
        if CONNECTION_SPECIFIC_FIELDS.contains(&header.name()) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // TE ヘッダーはリクエストのみ許可。トレーラーには存在してはならない (RFC 9114 Section 4.2)
        if header.name() == b"te" {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }

        // フィールド値の検証 (RFC 9110 Section 5.5, RFC 9114 Section 10.3)
        if !is_valid_field_value(header.value()) {
            return Err(Error::StreamError(ErrorCode::MessageError));
        }
    }

    Ok(())
}

/// フィールドセクションサイズを計算する (RFC 9114 Section 4.2.2)
///
/// サイズは各フィールドの名前長 + 値長 + 32 バイトのオーバーヘッドの合計。
pub fn calculate_field_section_size(headers: &[Header]) -> u64 {
    headers.iter().map(|h| h.size() as u64).sum()
}

/// peer の SETTINGS_MAX_FIELD_SECTION_SIZE を超えていないかチェックする (RFC 9114 Section 4.2.2)
///
/// peer がこの設定を送信していない場合 (None) はチェックしない。
pub fn check_field_section_size(headers: &[Header], peer_max: Option<u64>) -> Result<(), Error> {
    if let Some(max_size) = peer_max {
        let size = calculate_field_section_size(headers);
        if size > max_size {
            return Err(Error::ConnectionError(ErrorCode::InternalError));
        }
    }
    Ok(())
}
