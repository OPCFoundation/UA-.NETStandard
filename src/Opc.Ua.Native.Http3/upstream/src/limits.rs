//! HTTP/3 制限設定
//!
//! ヘッダーサイズ、QPACK 関連の制限値を管理。

/// デフォルトの最大ヘッダーリストサイズ (バイト)
pub const DEFAULT_MAX_FIELD_SECTION_SIZE: u64 = 16 * 1024;

/// デフォルトの QPACK 最大テーブル容量 (バイト) (RFC 9204 Section 3.2.3)
pub const DEFAULT_QPACK_MAX_TABLE_CAPACITY: u64 = 4096;

/// デフォルトの QPACK ブロックストリーム数 (RFC 9204 Section 4.3.1)
pub const DEFAULT_QPACK_BLOCKED_STREAMS: u64 = 100;

/// HTTP/3 制限設定
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Limits {
    /// 最大ヘッダーセクションサイズ (SETTINGS_MAX_FIELD_SECTION_SIZE)
    pub max_field_section_size: u64,
    /// QPACK 最大テーブル容量 (SETTINGS_QPACK_MAX_TABLE_CAPACITY)
    pub qpack_max_table_capacity: u64,
    /// QPACK ブロックストリーム数 (SETTINGS_QPACK_BLOCKED_STREAMS)
    pub qpack_blocked_streams: u64,
}

impl Default for Limits {
    fn default() -> Self {
        Self {
            max_field_section_size: DEFAULT_MAX_FIELD_SECTION_SIZE,
            qpack_max_table_capacity: DEFAULT_QPACK_MAX_TABLE_CAPACITY,
            qpack_blocked_streams: DEFAULT_QPACK_BLOCKED_STREAMS,
        }
    }
}

impl Limits {
    /// 新しい制限設定を作成
    pub fn new() -> Self {
        Self::default()
    }

    /// 最大ヘッダーセクションサイズを設定
    pub fn max_field_section_size(mut self, size: u64) -> Self {
        self.max_field_section_size = size;
        self
    }

    /// QPACK 最大テーブル容量を設定
    pub fn qpack_max_table_capacity(mut self, capacity: u64) -> Self {
        self.qpack_max_table_capacity = capacity;
        self
    }

    /// QPACK ブロックストリーム数を設定
    pub fn qpack_blocked_streams(mut self, streams: u64) -> Self {
        self.qpack_blocked_streams = streams;
        self
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_default_limits() {
        let limits = Limits::default();
        assert_eq!(
            limits.max_field_section_size,
            DEFAULT_MAX_FIELD_SECTION_SIZE
        );
        assert_eq!(
            limits.qpack_max_table_capacity,
            DEFAULT_QPACK_MAX_TABLE_CAPACITY
        );
        assert_eq!(limits.qpack_blocked_streams, DEFAULT_QPACK_BLOCKED_STREAMS);
    }

    #[test]
    fn test_builder() {
        let limits = Limits::new()
            .max_field_section_size(32 * 1024)
            .qpack_max_table_capacity(4096)
            .qpack_blocked_streams(100);

        assert_eq!(limits.max_field_section_size, 32 * 1024);
        assert_eq!(limits.qpack_max_table_capacity, 4096);
        assert_eq!(limits.qpack_blocked_streams, 100);
    }
}
