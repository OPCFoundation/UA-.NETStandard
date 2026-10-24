//! QPACK ヘッダーフィールド (RFC 9114 Section 4.2 / RFC 9110 Section 5)
//!
//! `Header` は不正な値を持てない構築時検査型として実装されている。
//!
//! # 二段の構築 API
//!
//! - `Header::new(name, value).expect("infallible: implementation bug if this panics")`: ランタイム値の検査つき構築 (`Result<Self, HeaderError>`)
//! - `Header::from_static(name, value)`: `&'static [u8]` の `const fn` 構築。
//!   不正リテラルは `const` / `static` 宣言内ではコンパイル時に検出される
//!
//! `from_static` は const 文脈 (`const` / `static` 宣言) で使われた場合に限り、
//! 不正リテラルがコンパイルエラーになる。実行時文脈 (`let h = Header::from_static(...)`)
//! で呼ぶと panic する点に注意する。
//!
//! # 検査内容
//!
//! - field-name (RFC 9114 Section 4.2 / RFC 9110 Section 5.1, 5.6.2)
//!   - 空でないこと
//!   - lowercase ASCII の token 文字のみ (大文字は MUST NOT)
//! - field-value (RFC 9114 Section 4.2 / RFC 9110 Section 5.5)
//!   - 全バイトが field-vchar (0x21-0x7E | 0x80-0xFF) または SP (0x20) / HTAB (0x09)
//!   - 先頭末尾は field-vchar (前後 SP / HTAB は MUST NOT)
//!   - NUL (0x00) / CR (0x0d) / LF (0x0a) は MUST NOT
//! - 疑似ヘッダー (RFC 9114 Section 4.3 / RFC 8441 Section 4 / RFC 9220)
//!   - 名前が `:method` / `:scheme` / `:authority` / `:path` / `:status` / `:protocol` のいずれか
//!   - 単一フィールドで完結する値の構文検査 (`:method`, `:scheme`, `:status`)
//!
//! ヘッダーリスト全体や他フィールドとの組み合わせに依存する検査
//! (CONNECT の構成、`:path` / `:authority` の構文、疑似ヘッダーの順序/重複/存在等)
//! は `src/validation.rs` 側に残す。

use std::borrow::Cow;

/// ヘッダーフィールドの構築時検査エラー
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum HeaderError {
    /// field-name が空
    /// (RFC 9110 Section 5.1, 5.6.2: `token = 1*tchar`)
    EmptyFieldName,

    /// field-name に lowercase ASCII 英字以外の英字 (大文字) が含まれる
    /// (RFC 9114 Section 4.2: MUST be lowercase)
    UppercaseFieldName { name: Vec<u8> },

    /// field-name に token 文字以外が含まれる
    /// (RFC 9110 Section 5.1, 5.6.2 / RFC 9114 Section 4.2)
    InvalidFieldNameByte { name: Vec<u8>, byte: u8 },

    /// field-value に NUL / CR / LF などの不正バイトが含まれる
    /// (RFC 9114 Section 4.2 MUST NOT / RFC 9110 Section 5.5)
    InvalidFieldValueByte { name: Vec<u8>, byte: u8 },

    /// field-value が先頭または末尾に SP / HTAB を含む
    /// (RFC 9114 Section 4.2 MUST NOT / RFC 9110 Section 5.5)
    FieldValueLeadingOrTrailingWhitespace { name: Vec<u8> },

    /// 疑似ヘッダー名が未定義
    /// (RFC 9114 Section 4.3 / RFC 8441 Section 4 / RFC 9220)
    UnknownPseudoHeader { name: Vec<u8> },

    /// 疑似ヘッダー値の単一フィールド構文検査に失敗
    ///
    /// 検査対象の疑似ヘッダー:
    /// - `:method`: token (RFC 9110 Section 9.1, 5.6.2)
    /// - `:scheme`: scheme (RFC 3986 Section 3.1)
    /// - `:status`: 3DIGIT (RFC 9110 Section 15)
    InvalidPseudoHeaderValue { name: Vec<u8>, value: Vec<u8> },
}

/// `HeaderError` の Display 出力で使用するバイト列の安全な表示
///
/// CR / LF / NUL 等の制御文字をそのままターミナルやログに流すと
/// ログ injection を許してしまうため、`escape_default` で `\x0d` のような
/// 表記に変換する。長すぎる場合は先頭 64 バイトに切り詰めて `...` を付加する
/// (攻撃者制御のメモリ消費を抑える)。
fn write_escaped(f: &mut core::fmt::Formatter<'_>, bytes: &[u8]) -> core::fmt::Result {
    use core::fmt::Write;
    const MAX_DISPLAY_LEN: usize = 64;
    let truncated = bytes.len() > MAX_DISPLAY_LEN;
    let view = if truncated {
        &bytes[..MAX_DISPLAY_LEN]
    } else {
        bytes
    };
    for &b in view {
        for ch in std::ascii::escape_default(b) {
            f.write_char(ch as char)?;
        }
    }
    if truncated {
        f.write_str("...")?;
    }
    Ok(())
}

impl core::fmt::Display for HeaderError {
    fn fmt(&self, f: &mut core::fmt::Formatter<'_>) -> core::fmt::Result {
        match self {
            Self::EmptyFieldName => write!(f, "field name must not be empty"),
            Self::UppercaseFieldName { name } => {
                write!(f, "field name must be lowercase: ")?;
                write_escaped(f, name)
            }
            Self::InvalidFieldNameByte { name, byte } => {
                write!(f, "field name contains invalid byte 0x{byte:02x}: ")?;
                write_escaped(f, name)
            }
            Self::InvalidFieldValueByte { name, byte } => {
                write!(f, "field value contains invalid byte 0x{byte:02x} (name: ")?;
                write_escaped(f, name)?;
                f.write_str(")")
            }
            Self::FieldValueLeadingOrTrailingWhitespace { name } => {
                write!(
                    f,
                    "field value must not start or end with whitespace (name: "
                )?;
                write_escaped(f, name)?;
                f.write_str(")")
            }
            Self::UnknownPseudoHeader { name } => {
                write!(f, "unknown pseudo header: ")?;
                write_escaped(f, name)
            }
            Self::InvalidPseudoHeaderValue { name, value } => {
                write!(f, "invalid pseudo header value for ")?;
                write_escaped(f, name)?;
                write!(f, ": ")?;
                write_escaped(f, value)
            }
        }
    }
}

impl std::error::Error for HeaderError {}

/// 既知の疑似ヘッダー名
///
/// `:method`, `:scheme`, `:authority`, `:path`, `:status` (RFC 9114 Section 4.3),
/// `:protocol` (RFC 8441 Section 4 / RFC 9220).
const KNOWN_PSEUDO_HEADERS: &[&[u8]] = &[
    b":method",
    b":scheme",
    b":authority",
    b":path",
    b":status",
    b":protocol",
];

/// HTTP/3 では field-name は lowercase ASCII の token 文字のみ
/// (RFC 9114 Section 4.2 / RFC 9110 Section 5.6.2)
///
/// `:` 始まりの疑似ヘッダー名のため、`:` は別途許可する。
const fn is_lowercase_token_byte(b: u8) -> bool {
    matches!(b,
        b'!' | b'#' | b'$' | b'%' | b'&' | b'\'' | b'*' | b'+' | b'-' | b'.' |
        b'^' | b'_' | b'`' | b'|' | b'~' |
        b'0'..=b'9' |
        b'a'..=b'z'
    )
}

/// field-vchar 判定 (RFC 9110 Section 5.5)
///
/// field-vchar = VCHAR (%x21-7E) / obs-text (%x80-FF)
const fn is_field_vchar(b: u8) -> bool {
    matches!(b, 0x21..=0x7e | 0x80..=0xff)
}

/// HTTP token の 1 バイトが tchar かを判定する (RFC 9110 Section 5.6.2)
///
/// `:method` 値の検証用。大文字も許可する。
const fn is_tchar(b: u8) -> bool {
    matches!(b,
        b'!' | b'#' | b'$' | b'%' | b'&' | b'\'' | b'*' | b'+' | b'-' | b'.' |
        b'^' | b'_' | b'`' | b'|' | b'~' |
        b'0'..=b'9' |
        b'A'..=b'Z' |
        b'a'..=b'z'
    )
}

/// 検査結果
///
/// `const fn` 内で `Result` を返せないため、専用の内部表現で結果を扱う。
/// (`const Try` は MSRV 1.93 では未安定)
enum CheckResult {
    Ok,
    EmptyFieldName,
    UppercaseFieldName,
    InvalidFieldNameByte(u8),
    InvalidFieldValueByte(u8),
    FieldValueLeadingOrTrailingWhitespace,
    UnknownPseudoHeader,
    InvalidPseudoHeaderValue,
}

/// field-name と field-value を検査する (const fn)
const fn check_header(name: &[u8], value: &[u8]) -> CheckResult {
    // field-name の検査 (RFC 9110 Section 5.1 / RFC 9114 Section 4.2)
    if name.is_empty() {
        return CheckResult::EmptyFieldName;
    }
    let is_pseudo = name[0] == b':';
    let name_start = if is_pseudo { 1 } else { 0 };

    // 疑似ヘッダーの場合は先頭の `:` をスキップし、残りを lowercase token として検査
    let mut i = name_start;
    while i < name.len() {
        let b = name[i];
        // 大文字を含む場合は判別を分けてエラーにする
        if b.is_ascii_uppercase() {
            return CheckResult::UppercaseFieldName;
        }
        if !is_lowercase_token_byte(b) {
            return CheckResult::InvalidFieldNameByte(b);
        }
        i += 1;
    }
    // 疑似ヘッダーで本体が空 (`:` のみ) のケース。`is_known_pseudo_header` で
    // 弾けるが、`UnknownPseudoHeader` バリアントで返すことで意味を明確にする。
    if is_pseudo && name.len() == 1 {
        return CheckResult::UnknownPseudoHeader;
    }

    // 疑似ヘッダー名は既知の集合にのみ属する (RFC 9114 Section 4.3 / RFC 8441 Section 4 / RFC 9220 Section 3)
    if is_pseudo && !is_known_pseudo_header(name) {
        return CheckResult::UnknownPseudoHeader;
    }

    // field-value の検査 (RFC 9110 Section 5.5 / RFC 9114 Section 4.2)
    let mut i = 0;
    while i < value.len() {
        let b = value[i];
        // field-vchar / SP / HTAB のみ許可
        if !(is_field_vchar(b) || b == b' ' || b == b'\t') {
            return CheckResult::InvalidFieldValueByte(b);
        }
        i += 1;
    }
    if !value.is_empty() {
        let first = value[0];
        let last = value[value.len() - 1];
        // 先頭末尾は field-vchar でなければならない
        if !is_field_vchar(first) || !is_field_vchar(last) {
            return CheckResult::FieldValueLeadingOrTrailingWhitespace;
        }
    }

    // 疑似ヘッダー値の単一フィールド構文検査
    if is_pseudo {
        match check_pseudo_value(name, value) {
            CheckResult::Ok => {}
            other => return other,
        }
    }

    CheckResult::Ok
}

/// 既知の疑似ヘッダー名かを判定する
const fn is_known_pseudo_header(name: &[u8]) -> bool {
    let mut i = 0;
    while i < KNOWN_PSEUDO_HEADERS.len() {
        if bytes_eq(KNOWN_PSEUDO_HEADERS[i], name) {
            return true;
        }
        i += 1;
    }
    false
}

/// バイト列の同値比較 (const fn)
const fn bytes_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    let mut i = 0;
    while i < a.len() {
        if a[i] != b[i] {
            return false;
        }
        i += 1;
    }
    true
}

/// 疑似ヘッダー値の単一フィールド構文検査
const fn check_pseudo_value(name: &[u8], value: &[u8]) -> CheckResult {
    if bytes_eq(name, b":method") {
        // method = token = 1*tchar (RFC 9110 Section 9.1)
        if value.is_empty() {
            return CheckResult::InvalidPseudoHeaderValue;
        }
        let mut i = 0;
        while i < value.len() {
            if !is_tchar(value[i]) {
                return CheckResult::InvalidPseudoHeaderValue;
            }
            i += 1;
        }
    } else if bytes_eq(name, b":scheme") {
        // scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) (RFC 3986 Section 3.1)
        if value.is_empty() {
            return CheckResult::InvalidPseudoHeaderValue;
        }
        let first = value[0];
        if !first.is_ascii_alphabetic() {
            return CheckResult::InvalidPseudoHeaderValue;
        }
        let mut i = 1;
        while i < value.len() {
            let b = value[i];
            if !(b.is_ascii_alphanumeric() || b == b'+' || b == b'-' || b == b'.') {
                return CheckResult::InvalidPseudoHeaderValue;
            }
            i += 1;
        }
    } else if bytes_eq(name, b":status") {
        // status-code = 3DIGIT (RFC 9110 Section 15)
        //
        // HTTP/3 が `:status = 101` を拒否する規定 (RFC 9114 Section 4.5) は
        // レスポンスセクションのセマンティクス検査として `src/validation.rs`
        // 側で扱う。ヘッダー単体の構文としては 3DIGIT で受理する。
        if value.len() != 3 {
            return CheckResult::InvalidPseudoHeaderValue;
        }
        let mut i = 0;
        while i < 3 {
            if !value[i].is_ascii_digit() {
                return CheckResult::InvalidPseudoHeaderValue;
            }
            i += 1;
        }
    } else if bytes_eq(name, b":protocol") {
        // protocol = protocol-name [ "/" protocol-version ]
        // protocol-name = token, protocol-version = token (RFC 9110 Section 7.8)
        // RFC 8441 Section 4 / RFC 9220 Section 3 が Extended CONNECT の
        // `:protocol` 値として参照している。
        match check_upgrade_token(value) {
            CheckResult::Ok => {}
            other => return other,
        }
    }
    // `:authority` / `:path` の単一フィールド構文検査は他フィールド
    // (`:method`, `:scheme`) に依存するため validation.rs 側で扱う。
    CheckResult::Ok
}

/// HTTP Upgrade Token 構文を検査する (`const fn`)
///
/// `protocol = protocol-name [ "/" protocol-version ]` を const eval で検査する。
/// 空文字列 / 先頭末尾の `/` / `/` が複数 / token 文字以外を含むケースを拒否する。
const fn check_upgrade_token(value: &[u8]) -> CheckResult {
    if value.is_empty() {
        return CheckResult::InvalidPseudoHeaderValue;
    }
    // 最初の `/` の位置を探す
    let mut slash = usize::MAX;
    let mut i = 0;
    while i < value.len() {
        if value[i] == b'/' {
            slash = i;
            break;
        }
        i += 1;
    }
    if slash == usize::MAX {
        // `/` なし。全体が protocol-name (1*tchar)
        let mut j = 0;
        while j < value.len() {
            if !is_tchar(value[j]) {
                return CheckResult::InvalidPseudoHeaderValue;
            }
            j += 1;
        }
        return CheckResult::Ok;
    }
    // protocol-name 部 (slash の前)
    if slash == 0 {
        return CheckResult::InvalidPseudoHeaderValue;
    }
    let mut j = 0;
    while j < slash {
        if !is_tchar(value[j]) {
            return CheckResult::InvalidPseudoHeaderValue;
        }
        j += 1;
    }
    // protocol-version 部 (slash の後)。空または更に `/` を含むなら不正
    if slash + 1 >= value.len() {
        return CheckResult::InvalidPseudoHeaderValue;
    }
    let mut j = slash + 1;
    while j < value.len() {
        if !is_tchar(value[j]) {
            return CheckResult::InvalidPseudoHeaderValue;
        }
        j += 1;
    }
    CheckResult::Ok
}

/// HTTP/3 のヘッダーフィールド
///
/// 全フィールドが private なので構造体リテラルや直接代入では構築できない。
/// 構築は `Header::new` (ランタイム値) または `Header::from_static` (静的バイト列)
/// を経由する必要がある。
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Header {
    name: Cow<'static, [u8]>,
    value: Cow<'static, [u8]>,
    /// N ビット (never-indexed): RFC 9204 Section 4.5.4 / RFC 7541 Section 6.2.2
    /// true の場合、このヘッダーは中間プロキシで圧縮してはならない。
    never_indexed: bool,
}

impl Header {
    /// ランタイム値から検査つきで構築する
    ///
    /// 不正な値の場合は `Err(HeaderError)` を返す。
    pub fn new(name: impl AsRef<[u8]>, value: impl AsRef<[u8]>) -> Result<Self, HeaderError> {
        let name = name.as_ref();
        let value = value.as_ref();
        match check_header(name, value) {
            CheckResult::Ok => Ok(Self {
                name: Cow::Owned(name.to_vec()),
                value: Cow::Owned(value.to_vec()),
                never_indexed: false,
            }),
            CheckResult::EmptyFieldName => Err(HeaderError::EmptyFieldName),
            CheckResult::UppercaseFieldName => Err(HeaderError::UppercaseFieldName {
                name: name.to_vec(),
            }),
            CheckResult::InvalidFieldNameByte(byte) => Err(HeaderError::InvalidFieldNameByte {
                name: name.to_vec(),
                byte,
            }),
            CheckResult::InvalidFieldValueByte(byte) => Err(HeaderError::InvalidFieldValueByte {
                name: name.to_vec(),
                byte,
            }),
            CheckResult::FieldValueLeadingOrTrailingWhitespace => {
                Err(HeaderError::FieldValueLeadingOrTrailingWhitespace {
                    name: name.to_vec(),
                })
            }
            CheckResult::UnknownPseudoHeader => Err(HeaderError::UnknownPseudoHeader {
                name: name.to_vec(),
            }),
            CheckResult::InvalidPseudoHeaderValue => Err(HeaderError::InvalidPseudoHeaderValue {
                name: name.to_vec(),
                value: value.to_vec(),
            }),
        }
    }

    /// 静的バイト列から検査つきで構築する (`const fn`)
    ///
    /// `const` / `static` 宣言で使った場合、不正リテラルは const 評価時に
    /// `assert!` で fail し、コンパイルエラーとなる。
    /// 実行時文脈 (`let h = Header::from_static(...)`) で呼んだ場合は通常の
    /// ランタイム panic になる。リテラル定数は必ず `const` / `static` 宣言で
    /// 受けることを推奨する。
    ///
    /// # Panics
    ///
    /// `name` / `value` が RFC 9114 / RFC 9110 の構文に違反する場合、
    /// const 評価時にコンパイルエラー、実行時には panic する。
    ///
    /// 大文字を含む field name はコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::Header;
    /// const _BAD: Header = Header::from_static(b"Host", b"example.com");
    /// ```
    ///
    /// CRLF を含む field value はコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::Header;
    /// const _BAD: Header = Header::from_static(b":path", b"/foo\r\nX-Inject: 1");
    /// ```
    ///
    /// 空 field name はコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::Header;
    /// const _BAD: Header = Header::from_static(b"", b"value");
    /// ```
    ///
    /// 不明な疑似ヘッダーはコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::Header;
    /// const _BAD: Header = Header::from_static(b":bogus", b"value");
    /// ```
    ///
    /// token 外の文字を含む field name はコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::Header;
    /// const _BAD: Header = Header::from_static(b"x-hdr with space", b"v");
    /// ```
    ///
    /// 先頭または末尾に空白を含む field value はコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::Header;
    /// const _BAD: Header = Header::from_static(b"x-h", b" leading");
    /// ```
    ///
    /// 疑似ヘッダーの値が構文に違反する場合はコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::Header;
    /// const _BAD: Header = Header::from_static(b":status", b"20");
    /// ```
    #[track_caller]
    pub const fn from_static(name: &'static [u8], value: &'static [u8]) -> Self {
        match check_header(name, value) {
            CheckResult::Ok => {}
            CheckResult::EmptyFieldName => {
                panic!("Header::from_static: field name must not be empty");
            }
            CheckResult::UppercaseFieldName => {
                panic!("Header::from_static: field name must be lowercase");
            }
            CheckResult::InvalidFieldNameByte(_) => {
                panic!("Header::from_static: field name contains invalid byte");
            }
            CheckResult::InvalidFieldValueByte(_) => {
                panic!("Header::from_static: field value contains invalid byte");
            }
            CheckResult::FieldValueLeadingOrTrailingWhitespace => {
                panic!("Header::from_static: field value must not start or end with whitespace");
            }
            CheckResult::UnknownPseudoHeader => {
                panic!("Header::from_static: unknown pseudo header");
            }
            CheckResult::InvalidPseudoHeaderValue => {
                panic!("Header::from_static: invalid pseudo header value");
            }
        }
        Self {
            name: Cow::Borrowed(name),
            value: Cow::Borrowed(value),
            never_indexed: false,
        }
    }

    /// 同 crate 内 (decoder / validation など) で検査をスキップして構築する
    pub(crate) fn from_validated_parts_internal(
        name: Cow<'static, [u8]>,
        value: Cow<'static, [u8]>,
    ) -> Self {
        Self {
            name,
            value,
            never_indexed: false,
        }
    }

    /// ヘッダー名を取得
    #[inline]
    pub fn name(&self) -> &[u8] {
        &self.name
    }

    /// ヘッダー値を取得
    #[inline]
    pub fn value(&self) -> &[u8] {
        &self.value
    }

    /// RFC 9114 Section 4.2.2 の field section size 計算に使うフィールドサイズ
    /// (`name.len() + value.len() + 32`)
    #[inline]
    pub fn size(&self) -> usize {
        self.name.len() + self.value.len() + 32
    }

    /// N ビット (never-indexed) を取得
    ///
    /// RFC 9204 Section 4.5.4 / RFC 7541 Section 6.2.2:
    /// true の場合、このヘッダーは中間プロキシで圧縮してはならない。
    #[inline]
    pub fn never_indexed(&self) -> bool {
        self.never_indexed
    }

    /// N ビット (never-indexed) を設定した新しい Header を返す
    #[inline]
    pub fn with_never_indexed(mut self, never_indexed: bool) -> Self {
        self.never_indexed = never_indexed;
        self
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_new_simple_header() {
        let h = Header::new(b":method", b"GET").expect("test must succeed");
        assert_eq!(h.name(), b":method");
        assert_eq!(h.value(), b"GET");
    }

    #[test]
    fn test_new_empty_value_allowed() {
        let h = Header::new(b":authority", b"").expect("test must succeed");
        assert_eq!(h.value(), b"");
    }

    #[test]
    fn test_new_uppercase_field_name_rejected() {
        assert!(matches!(
            Header::new(b"Host", b"example.com"),
            Err(HeaderError::UppercaseFieldName { .. })
        ));
    }

    #[test]
    fn test_new_invalid_field_name_byte_rejected() {
        assert!(matches!(
            Header::new(b"x hdr", b"v"),
            Err(HeaderError::InvalidFieldNameByte { .. })
        ));
    }

    #[test]
    fn test_new_empty_field_name_rejected() {
        assert!(matches!(
            Header::new(b"", b"v"),
            Err(HeaderError::EmptyFieldName)
        ));
    }

    #[test]
    fn test_new_field_value_with_cr_rejected() {
        assert!(matches!(
            Header::new(b":path", b"/foo\r\nX-Inject: 1"),
            Err(HeaderError::InvalidFieldValueByte { byte: 0x0d, .. })
        ));
    }

    #[test]
    fn test_new_field_value_with_lf_rejected() {
        assert!(matches!(
            Header::new(b"x-h", b"v\nv"),
            Err(HeaderError::InvalidFieldValueByte { byte: 0x0a, .. })
        ));
    }

    #[test]
    fn test_new_field_value_with_nul_rejected() {
        assert!(matches!(
            Header::new(b"x-h", b"v\0v"),
            Err(HeaderError::InvalidFieldValueByte { byte: 0x00, .. })
        ));
    }

    #[test]
    fn test_new_field_value_leading_space_rejected() {
        assert!(matches!(
            Header::new(b"x-h", b" v"),
            Err(HeaderError::FieldValueLeadingOrTrailingWhitespace { .. })
        ));
    }

    #[test]
    fn test_new_field_value_trailing_tab_rejected() {
        assert!(matches!(
            Header::new(b"x-h", b"v\t"),
            Err(HeaderError::FieldValueLeadingOrTrailingWhitespace { .. })
        ));
    }

    #[test]
    fn test_new_unknown_pseudo_header_rejected() {
        assert!(matches!(
            Header::new(b":unknown", b"value"),
            Err(HeaderError::UnknownPseudoHeader { .. })
        ));
    }

    #[test]
    fn test_new_invalid_method_rejected() {
        assert!(matches!(
            Header::new(b":method", b"GET POST"),
            Err(HeaderError::InvalidPseudoHeaderValue { .. })
        ));
    }

    #[test]
    fn test_new_invalid_scheme_rejected() {
        assert!(matches!(
            Header::new(b":scheme", b"1http"),
            Err(HeaderError::InvalidPseudoHeaderValue { .. })
        ));
    }

    #[test]
    fn test_new_invalid_status_rejected() {
        assert!(matches!(
            Header::new(b":status", b"20"),
            Err(HeaderError::InvalidPseudoHeaderValue { .. })
        ));
        assert!(matches!(
            Header::new(b":status", b"abc"),
            Err(HeaderError::InvalidPseudoHeaderValue { .. })
        ));
    }

    #[test]
    fn test_from_static_ok() {
        const H: Header = Header::from_static(b":method", b"GET");
        assert_eq!(H.name(), b":method");
        assert_eq!(H.value(), b"GET");
    }

    #[test]
    fn test_size() {
        let h = Header::new(b":method", b"GET").expect("test must succeed");
        assert_eq!(h.size(), 7 + 3 + 32);
    }

    #[test]
    fn test_from_validated_parts_internal_skips_validation() {
        // デコーダー経路が検査をスキップして不正データを Header に注入できること。
        // 通常 API (`Header::new`) では大文字名で reject されることもあわせて確認。
        let h = Header::from_validated_parts_internal(
            Cow::Borrowed(b"Host"),
            Cow::Borrowed(b"example"),
        );
        assert_eq!(h.name(), b"Host");
        assert!(matches!(
            Header::new(h.name(), h.value()),
            Err(HeaderError::UppercaseFieldName { .. })
        ));
    }

    // -------------------------------------------------------------------------
    // HeaderError::Display の escape / 64 バイト truncate 検査
    // (log injection 対策と攻撃者制御メモリ消費抑制)
    // -------------------------------------------------------------------------

    #[test]
    fn test_display_escapes_crlf_in_name() {
        // CR / LF などの制御文字がそのまま出力されないことを確認 (log injection 対策)
        let err = Header::new(b"x\r\nzz", b"v").unwrap_err();
        let msg = format!("{err}");
        assert!(!msg.contains('\r'), "CR must be escaped: {msg}");
        assert!(!msg.contains('\n'), "LF must be escaped: {msg}");
        assert!(msg.contains("\\r"), "should contain escaped \\r: {msg}");
        assert!(msg.contains("\\n"), "should contain escaped \\n: {msg}");
    }

    #[test]
    fn test_display_truncates_long_name() {
        // 65 バイト以上の name は先頭 64 バイトに切り詰めて "..." を付加する
        let name = vec![b'A'; 65];
        let err = Header::new(&name, b"v").unwrap_err();
        let msg = format!("{err}");
        assert!(msg.ends_with("..."), "long name must be truncated: {msg}");
    }

    #[test]
    fn test_display_does_not_truncate_at_64_bytes() {
        // ちょうど 64 バイトでは "..." を付けない (境界値)
        let name = vec![b'A'; 64];
        let err = Header::new(&name, b"v").unwrap_err();
        let msg = format!("{err}");
        assert!(
            !msg.ends_with("..."),
            "64 bytes name must not be truncated: {msg}"
        );
    }
}
