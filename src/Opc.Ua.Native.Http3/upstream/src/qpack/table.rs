//! QPACK 静的テーブル (RFC 9204 Appendix A)
//!
//! 99 エントリの静的テーブルを提供する。エントリはすべて
//! `Header::from_static` で組み立てるため、RFC 違反のリテラルがあれば
//! コンパイル時 (`const` 評価) に検出される。

use super::header::Header;

/// 静的テーブル (99 エントリ, RFC 9204 Appendix A)
pub static STATIC_TABLE: &[Header] = &[
    // 0
    Header::from_static(b":authority", b""),
    // 1
    Header::from_static(b":path", b"/"),
    // 2
    Header::from_static(b"age", b"0"),
    // 3
    Header::from_static(b"content-disposition", b""),
    // 4
    Header::from_static(b"content-length", b"0"),
    // 5
    Header::from_static(b"cookie", b""),
    // 6
    Header::from_static(b"date", b""),
    // 7
    Header::from_static(b"etag", b""),
    // 8
    Header::from_static(b"if-modified-since", b""),
    // 9
    Header::from_static(b"if-none-match", b""),
    // 10
    Header::from_static(b"last-modified", b""),
    // 11
    Header::from_static(b"link", b""),
    // 12
    Header::from_static(b"location", b""),
    // 13
    Header::from_static(b"referer", b""),
    // 14
    Header::from_static(b"set-cookie", b""),
    // 15
    Header::from_static(b":method", b"CONNECT"),
    // 16
    Header::from_static(b":method", b"DELETE"),
    // 17
    Header::from_static(b":method", b"GET"),
    // 18
    Header::from_static(b":method", b"HEAD"),
    // 19
    Header::from_static(b":method", b"OPTIONS"),
    // 20
    Header::from_static(b":method", b"POST"),
    // 21
    Header::from_static(b":method", b"PUT"),
    // 22
    Header::from_static(b":scheme", b"http"),
    // 23
    Header::from_static(b":scheme", b"https"),
    // 24
    Header::from_static(b":status", b"103"),
    // 25
    Header::from_static(b":status", b"200"),
    // 26
    Header::from_static(b":status", b"304"),
    // 27
    Header::from_static(b":status", b"404"),
    // 28
    Header::from_static(b":status", b"503"),
    // 29
    Header::from_static(b"accept", b"*/*"),
    // 30
    Header::from_static(b"accept", b"application/dns-message"),
    // 31
    Header::from_static(b"accept-encoding", b"gzip, deflate, br"),
    // 32
    Header::from_static(b"accept-ranges", b"bytes"),
    // 33
    Header::from_static(b"access-control-allow-headers", b"cache-control"),
    // 34
    Header::from_static(b"access-control-allow-headers", b"content-type"),
    // 35
    Header::from_static(b"access-control-allow-origin", b"*"),
    // 36
    Header::from_static(b"cache-control", b"max-age=0"),
    // 37
    Header::from_static(b"cache-control", b"max-age=2592000"),
    // 38
    Header::from_static(b"cache-control", b"max-age=604800"),
    // 39
    Header::from_static(b"cache-control", b"no-cache"),
    // 40
    Header::from_static(b"cache-control", b"no-store"),
    // 41
    Header::from_static(b"cache-control", b"public, max-age=31536000"),
    // 42
    Header::from_static(b"content-encoding", b"br"),
    // 43
    Header::from_static(b"content-encoding", b"gzip"),
    // 44
    Header::from_static(b"content-type", b"application/dns-message"),
    // 45
    Header::from_static(b"content-type", b"application/javascript"),
    // 46
    Header::from_static(b"content-type", b"application/json"),
    // 47
    Header::from_static(b"content-type", b"application/x-www-form-urlencoded"),
    // 48
    Header::from_static(b"content-type", b"image/gif"),
    // 49
    Header::from_static(b"content-type", b"image/jpeg"),
    // 50
    Header::from_static(b"content-type", b"image/png"),
    // 51
    Header::from_static(b"content-type", b"text/css"),
    // 52
    Header::from_static(b"content-type", b"text/html; charset=utf-8"),
    // 53
    Header::from_static(b"content-type", b"text/plain"),
    // 54
    Header::from_static(b"content-type", b"text/plain;charset=utf-8"),
    // 55
    Header::from_static(b"range", b"bytes=0-"),
    // 56
    Header::from_static(b"strict-transport-security", b"max-age=31536000"),
    // 57
    Header::from_static(
        b"strict-transport-security",
        b"max-age=31536000; includesubdomains",
    ),
    // 58
    Header::from_static(
        b"strict-transport-security",
        b"max-age=31536000; includesubdomains; preload",
    ),
    // 59
    Header::from_static(b"vary", b"accept-encoding"),
    // 60
    Header::from_static(b"vary", b"origin"),
    // 61
    Header::from_static(b"x-content-type-options", b"nosniff"),
    // 62
    Header::from_static(b"x-xss-protection", b"1; mode=block"),
    // 63
    Header::from_static(b":status", b"100"),
    // 64
    Header::from_static(b":status", b"204"),
    // 65
    Header::from_static(b":status", b"206"),
    // 66
    Header::from_static(b":status", b"302"),
    // 67
    Header::from_static(b":status", b"400"),
    // 68
    Header::from_static(b":status", b"403"),
    // 69
    Header::from_static(b":status", b"421"),
    // 70
    Header::from_static(b":status", b"425"),
    // 71
    Header::from_static(b":status", b"500"),
    // 72
    Header::from_static(b"accept-language", b""),
    // 73
    Header::from_static(b"access-control-allow-credentials", b"FALSE"),
    // 74
    Header::from_static(b"access-control-allow-credentials", b"TRUE"),
    // 75
    Header::from_static(b"access-control-allow-headers", b"*"),
    // 76
    Header::from_static(b"access-control-allow-methods", b"get"),
    // 77
    Header::from_static(b"access-control-allow-methods", b"get, post, options"),
    // 78
    Header::from_static(b"access-control-allow-methods", b"options"),
    // 79
    Header::from_static(b"access-control-expose-headers", b"content-length"),
    // 80
    Header::from_static(b"access-control-request-headers", b"content-type"),
    // 81
    Header::from_static(b"access-control-request-method", b"get"),
    // 82
    Header::from_static(b"access-control-request-method", b"post"),
    // 83
    Header::from_static(b"alt-svc", b"clear"),
    // 84
    Header::from_static(b"authorization", b""),
    // 85
    Header::from_static(
        b"content-security-policy",
        b"script-src 'none'; object-src 'none'; base-uri 'none'",
    ),
    // 86
    Header::from_static(b"early-data", b"1"),
    // 87
    Header::from_static(b"expect-ct", b""),
    // 88
    Header::from_static(b"forwarded", b""),
    // 89
    Header::from_static(b"if-range", b""),
    // 90
    Header::from_static(b"origin", b""),
    // 91
    Header::from_static(b"purpose", b"prefetch"),
    // 92
    Header::from_static(b"server", b""),
    // 93
    Header::from_static(b"timing-allow-origin", b"*"),
    // 94
    Header::from_static(b"upgrade-insecure-requests", b"1"),
    // 95
    Header::from_static(b"user-agent", b""),
    // 96
    Header::from_static(b"x-forwarded-for", b""),
    // 97
    Header::from_static(b"x-frame-options", b"deny"),
    // 98
    Header::from_static(b"x-frame-options", b"sameorigin"),
];

/// 静的テーブルのエントリ数
pub const STATIC_TABLE_LEN: usize = 99;

/// インデックスから静的テーブルエントリを取得する
#[inline]
pub fn get_static_entry(index: usize) -> Option<&'static Header> {
    STATIC_TABLE.get(index)
}

/// 名前と値のペアで静的テーブルを検索する
///
/// 完全一致のインデックスと、名前のみ一致した最初のインデックスを返す。
pub fn find_static_entry(name: &[u8], value: &[u8]) -> (Option<usize>, Option<usize>) {
    let mut name_match = None;

    for (index, entry) in STATIC_TABLE.iter().enumerate() {
        if entry.name() == name {
            if entry.value() == value {
                return (Some(index), Some(index));
            }
            if name_match.is_none() {
                name_match = Some(index);
            }
        }
    }

    (None, name_match)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_static_table_len() {
        assert_eq!(STATIC_TABLE.len(), STATIC_TABLE_LEN);
        assert_eq!(STATIC_TABLE_LEN, 99);
    }

    #[test]
    fn test_get_static_entry() {
        let entry = get_static_entry(0).expect("test must succeed");
        assert_eq!(entry.name(), b":authority");
        assert_eq!(entry.value(), b"");

        let entry = get_static_entry(17).expect("test must succeed");
        assert_eq!(entry.name(), b":method");
        assert_eq!(entry.value(), b"GET");

        let entry = get_static_entry(25).expect("test must succeed");
        assert_eq!(entry.name(), b":status");
        assert_eq!(entry.value(), b"200");

        let entry = get_static_entry(98).expect("test must succeed");
        assert_eq!(entry.name(), b"x-frame-options");
        assert_eq!(entry.value(), b"sameorigin");

        assert!(get_static_entry(99).is_none());
    }

    #[test]
    fn test_find_static_entry_exact_match() {
        let (exact, name_only) = find_static_entry(b":method", b"GET");
        assert_eq!(exact, Some(17));
        assert_eq!(name_only, Some(17));

        let (exact, name_only) = find_static_entry(b":status", b"200");
        assert_eq!(exact, Some(25));
        assert_eq!(name_only, Some(25));
    }

    #[test]
    fn test_find_static_entry_name_only() {
        let (exact, name_only) = find_static_entry(b":method", b"PATCH");
        assert_eq!(exact, None);
        assert_eq!(name_only, Some(15)); // First :method entry

        let (exact, name_only) = find_static_entry(b":status", b"201");
        assert_eq!(exact, None);
        assert_eq!(name_only, Some(24)); // First :status entry
    }

    #[test]
    fn test_find_static_entry_not_found() {
        let (exact, name_only) = find_static_entry(b"x-custom-header", b"value");
        assert_eq!(exact, None);
        assert_eq!(name_only, None);
    }
}
