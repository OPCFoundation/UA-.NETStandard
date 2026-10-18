//! QPACK エンコーダー (RFC 9204)
//!
//! 静的テーブルと動的テーブルを使用する QPACK エンコーダー。
//!
//! ## 機能
//!
//! - `Encoder`: 静的テーブルのみを使用するシンプルなエンコーダー
//! - `DynamicEncoder`: 動的テーブルも使用する拡張エンコーダー

use std::collections::{HashMap, VecDeque};

use crate::error::QpackError;

use super::dynamic_table::DynamicTable;
use super::header::Header;
use super::integer;
use super::table::find_static_entry;

/// QPACK エンコーダー
/// QPACK エンコーダー DynamicEncoder への統合
///
/// 静的テーブルのみを使用するエンコーダー。`DynamicEncoder` の型エイリアス。
/// `encode` メソッドは Required Insert Count = 0, Delta Base = 0 でエンコードする。
pub type Encoder = DynamicEncoder;

/// ヘッダーリストをエンコードするのに必要なバッファサイズを推定
pub fn estimate_encoded_size(headers: &[Header]) -> usize {
    // 2 bytes for Required Insert Count and Delta Base
    let mut size = 2;

    for header in headers {
        let (exact_match, name_match) = find_static_entry(header.name(), header.value());

        if exact_match.is_some() {
            // Indexed Field Line: 1-2 bytes
            size += 2;
        } else if name_match.is_some() {
            // Literal with Name Reference
            size += 2 + 1 + header.value().len();
        } else {
            // Literal with Literal Name
            size += 2 + header.name().len() + 1 + header.value().len();
        }
    }

    size
}

/// 動的テーブル対応 QPACK エンコーダー (RFC 9204)
///
/// 静的テーブルと動的テーブルの両方を使用してヘッダーを圧縮する。
#[derive(Debug)]
pub struct DynamicEncoder {
    /// 動的テーブル
    table: DynamicTable,
    /// ハフマン符号化を使用するか
    use_huffman: bool,
    /// ピアの最大テーブル容量
    max_table_capacity: u64,
    /// Known Received Count (デコーダーが受信したと確認された挿入カウント)
    known_received_count: u64,
    /// 直前のエンコードで使用された Required Insert Count
    last_required_insert_count: u64,
    /// ピアの SETTINGS_QPACK_BLOCKED_STREAMS (RFC 9204 Section 2.1.2)
    peer_max_blocked_streams: u64,
    /// 未 ack フィールドセクションの Required Insert Count (stream_id → RIC の FIFO)
    /// (RFC 9204 Section 2.1.1, 4.4.1)
    unacked_section_rics: HashMap<u64, VecDeque<u64>>,
}

impl Default for DynamicEncoder {
    fn default() -> Self {
        Self::new()
    }
}

impl DynamicEncoder {
    /// 新しい動的エンコーダーを作成
    pub fn new() -> Self {
        Self {
            table: DynamicTable::new(),
            use_huffman: true,
            max_table_capacity: 0,
            known_received_count: 0,
            last_required_insert_count: 0,
            peer_max_blocked_streams: 0,
            unacked_section_rics: HashMap::new(),
        }
    }

    /// ハフマン符号化の使用を設定
    pub fn use_huffman(mut self, use_huffman: bool) -> Self {
        self.use_huffman = use_huffman;
        self
    }

    /// 最大テーブル容量を設定 (ピアの SETTINGS から)
    pub fn set_max_table_capacity(&mut self, capacity: u64) {
        self.max_table_capacity = capacity;
    }

    /// ピアの SETTINGS_QPACK_BLOCKED_STREAMS を設定 (RFC 9204 Section 2.1.2)
    ///
    /// エンコーダーはブロックし得るストリーム数をこの値以下に制限しなければならない (MUST)。
    pub fn set_peer_max_blocked_streams(&mut self, max: u64) {
        self.peer_max_blocked_streams = max;
    }

    /// ピアの SETTINGS_QPACK_BLOCKED_STREAMS を取得
    pub fn peer_max_blocked_streams(&self) -> u64 {
        self.peer_max_blocked_streams
    }

    /// 動的テーブル容量を設定
    ///
    /// `capacity` が `max_table_capacity` を超える場合は `CapacityExceeded` を返す。
    /// `capacity = 0` は常に許可する (RFC 9204 Section 3.2.3)。
    pub fn set_table_capacity(&mut self, capacity: u64) -> Result<(), QpackError> {
        if capacity > self.max_table_capacity {
            return Err(QpackError::CapacityExceeded);
        }
        self.table.set_capacity(capacity);
        Ok(())
    }

    /// 動的テーブルへの参照を取得
    pub fn table(&self) -> &DynamicTable {
        &self.table
    }

    /// 動的テーブルへの可変参照を取得
    pub fn table_mut(&mut self) -> &mut DynamicTable {
        &mut self.table
    }

    /// Known Received Count を取得
    pub fn known_received_count(&self) -> u64 {
        self.known_received_count
    }

    /// 動的テーブルの挿入カウント (エンコーダーが送信した数) を取得
    pub fn insert_count(&self) -> u64 {
        self.table.insert_count()
    }

    /// Known Received Count を更新 (デコーダーからの確認応答)
    pub fn acknowledge(&mut self, count: u64) {
        if count > self.known_received_count {
            self.known_received_count = count;
        }
        self.update_eviction_limit();
    }

    /// フィールドセクションの送信を記録する (RFC 9204 Section 2.1.1)
    ///
    /// Required Insert Count > 0 のフィールドセクションを送信した際に呼び出す。
    /// Section Acknowledgment でこの RIC が ack されるまで、
    /// 関連する動的テーブルエントリの eviction を防ぐ。
    pub fn track_section(&mut self, stream_id: u64, required_insert_count: u64) {
        if required_insert_count > 0 {
            self.unacked_section_rics
                .entry(stream_id)
                .or_default()
                .push_back(required_insert_count);
            self.update_eviction_limit();
        }
    }

    /// Section Acknowledgment を処理する (RFC 9204 Section 4.4.1)
    ///
    /// 指定ストリームの最も古い未 ack フィールドセクションを解放し、
    /// ack された Required Insert Count で Known Received Count を更新する
    /// (RFC 9204 Section 2.1.4)。
    ///
    /// 全て ack 済みのストリームに対する Section Acknowledgment は
    /// QPACK_DECODER_STREAM_ERROR として扱う (RFC 9204 Section 4.4.1)。
    ///
    /// エラー時は `QpackError::DecodeFailed` を返す。呼び出し元で
    /// `ErrorCode::QpackDecoderStreamError` へマッピングすること。
    pub fn ack_section(&mut self, stream_id: u64) -> Result<(), QpackError> {
        let Some(rics) = self.unacked_section_rics.get_mut(&stream_id) else {
            return Err(QpackError::DecodeFailed);
        };
        let Some(ric) = rics.pop_front() else {
            return Err(QpackError::DecodeFailed);
        };
        if rics.is_empty() {
            self.unacked_section_rics.remove(&stream_id);
        }
        // Section Acknowledgment は、デコーダーがそのフィールドセクションのデコードに
        // 必要な全ての動的テーブル状態を受信したことを意味する (RFC 9204 Section 2.1.4)
        if ric > self.known_received_count {
            self.known_received_count = ric;
        }
        self.update_eviction_limit();
        Ok(())
    }

    /// Stream Cancellation を処理する (RFC 9204 Section 4.4.2)
    ///
    /// 指定ストリームの全ての未 ack フィールドセクションを解放する。
    pub fn cancel_stream(&mut self, stream_id: u64) {
        self.unacked_section_rics.remove(&stream_id);
        self.update_eviction_limit();
    }

    /// 未 ack セクションを持つストリーム数を取得
    pub fn blocked_streams_count(&self) -> usize {
        self.unacked_section_rics.len()
    }

    /// eviction_limit を再計算して動的テーブルに設定する
    ///
    /// eviction_limit = 未 ack フィールドセクションの最大 RIC
    /// (保守的: RIC 未満の全エントリを参照している可能性があるとみなす)
    fn update_eviction_limit(&mut self) {
        let max_ric = self
            .unacked_section_rics
            .values()
            .flat_map(|rics| rics.iter())
            .copied()
            .max()
            .unwrap_or(0);
        self.table.set_eviction_limit(max_ric);
    }

    /// ヘッダーリストをエンコード
    ///
    /// 動的テーブルを使用しない場合は静的テーブルのみで圧縮。
    /// 動的テーブルを使用する場合は Required Insert Count と Base を適切に設定。
    ///
    /// `blocked_streams_count` はブロックし得るストリーム数 (RIC > 0 の未 ack ストリーム数)。
    /// ピアの SETTINGS_QPACK_BLOCKED_STREAMS に達している場合は
    /// 動的テーブル参照を使用せず静的テーブルのみでエンコードする (RFC 9204 Section 2.1.2)。
    pub fn encode(
        &mut self,
        buf: &mut [u8],
        headers: &[Header],
        blocked_streams_count: usize,
    ) -> Option<usize> {
        // 動的テーブルが空の場合は静的テーブルのみを使用
        if self.table.is_empty() {
            self.last_required_insert_count = 0;
            return self.encode_static_only(buf, headers);
        }

        // 動的テーブル容量が 0 (ピア SETTINGS 未受信等) の場合は静的テーブルのみを使用
        // (RFC 9204 Section 3.2.3: max_table_capacity == 0 では動的テーブル参照禁止)
        if self.max_table_capacity == 0 {
            self.last_required_insert_count = 0;
            return self.encode_static_only(buf, headers);
        }

        // ブロック可能ストリーム数が上限に達している場合は動的テーブル参照を抑止
        // (RFC 9204 Section 2.1.2)
        if blocked_streams_count as u64 >= self.peer_max_blocked_streams {
            self.last_required_insert_count = 0;
            return self.encode_static_only(buf, headers);
        }

        self.encode_with_dynamic(buf, headers)
    }

    /// 直前のエンコードで使用された Required Insert Count を取得
    pub fn last_required_insert_count(&self) -> u64 {
        self.last_required_insert_count
    }

    /// 静的テーブルのみを使用してエンコード
    fn encode_static_only(&self, buf: &mut [u8], headers: &[Header]) -> Option<usize> {
        let mut offset = 0;

        // Required Insert Count = 0
        if buf.is_empty() {
            return None;
        }
        buf[offset] = 0x00;
        offset += 1;

        // Delta Base = 0, Sign = 0
        if offset >= buf.len() {
            return None;
        }
        buf[offset] = 0x00;
        offset += 1;

        // ヘッダーをエンコード
        for header in headers {
            let encoded = self.encode_header_static_only(&mut buf[offset..], header)?;
            offset += encoded;
        }

        Some(offset)
    }

    /// 動的テーブルを使用してエンコード
    fn encode_with_dynamic(&mut self, buf: &mut [u8], headers: &[Header]) -> Option<usize> {
        // 参照する動的テーブルエントリの最大絶対インデックスを計算
        let mut required_insert_count = 0u64;

        for header in headers {
            // 動的テーブルで検索
            let (exact, name_only) = self.table.find_entry(header.name(), header.value());
            if let Some(idx) = exact {
                if idx + 1 > required_insert_count {
                    required_insert_count = idx + 1;
                }
            } else if let Some(idx) = name_only
                && idx + 1 > required_insert_count
            {
                required_insert_count = idx + 1;
            }
        }

        self.last_required_insert_count = required_insert_count;

        let mut offset = 0;

        // Required Insert Count をエンコード
        let enc_insert_count = self.encode_required_insert_count(required_insert_count);
        offset += integer::encode_integer(&mut buf[offset..], enc_insert_count, 8, 0x00)?;

        // Base = Required Insert Count (シンプルな実装)
        // Sign = 0, Delta Base = 0
        if offset >= buf.len() {
            return None;
        }
        buf[offset] = 0x00;
        offset += 1;

        let base = required_insert_count;

        // ヘッダーをエンコード
        for header in headers {
            let encoded = self.encode_header_with_dynamic(&mut buf[offset..], header, base)?;
            offset += encoded;
        }

        Some(offset)
    }

    /// Required Insert Count をエンコード (RFC 9204 Section 4.5.1.1)
    ///
    /// max_entries == 0 かつ req_insert_count != 0 は不変条件違反。
    /// 動的テーブルが無効 (max_table_capacity == 0) の場合に動的テーブル参照は発生しない。
    fn encode_required_insert_count(&self, req_insert_count: u64) -> u64 {
        if req_insert_count == 0 {
            return 0;
        }

        let max_entries = self.max_table_capacity / 32;
        debug_assert!(
            max_entries > 0,
            "max_entries == 0 with non-zero req_insert_count is an invariant violation"
        );
        if max_entries == 0 {
            // 防御的フォールバック: 動的テーブル無効時は RIC を 0 として扱う
            return 0;
        }

        let full_range = 2 * max_entries;
        (req_insert_count % full_range) + 1
    }

    /// 静的テーブルのみを使用してヘッダーをエンコード
    fn encode_header_static_only(&self, buf: &mut [u8], header: &Header) -> Option<usize> {
        let (exact_match, name_match) = find_static_entry(header.name(), header.value());

        if let Some(index) = exact_match {
            // Indexed Field Line (静的テーブル)
            self.encode_indexed_field_static(buf, index)
        } else if let Some(index) = name_match {
            // Literal with Name Reference (静的テーブル)
            self.encode_literal_with_name_ref_static(
                buf,
                index,
                header.value(),
                header.never_indexed(),
            )
        } else {
            // Literal with Literal Name
            self.encode_literal_with_literal_name(
                buf,
                header.name(),
                header.value(),
                header.never_indexed(),
            )
        }
    }

    /// 動的テーブルを使用してヘッダーをエンコード
    fn encode_header_with_dynamic(
        &self,
        buf: &mut [u8],
        header: &Header,
        base: u64,
    ) -> Option<usize> {
        // 動的テーブルで検索
        let (dyn_exact, dyn_name) = self.table.find_entry(header.name(), header.value());

        // 静的テーブルで検索
        let (static_exact, static_name) = find_static_entry(header.name(), header.value());

        // 優先順位: 動的完全一致 > 静的完全一致 > 動的名前一致 > 静的名前一致 > リテラル
        if let Some(abs_index) = dyn_exact {
            // Indexed Field Line (動的テーブル)
            self.encode_indexed_field_dynamic(buf, abs_index, base)
        } else if let Some(index) = static_exact {
            // Indexed Field Line (静的テーブル)
            self.encode_indexed_field_static(buf, index)
        } else if let Some(abs_index) = dyn_name {
            // Literal with Name Reference (動的テーブル)
            self.encode_literal_with_name_ref_dynamic(
                buf,
                abs_index,
                header.value(),
                base,
                header.never_indexed(),
            )
        } else if let Some(index) = static_name {
            // Literal with Name Reference (静的テーブル)
            self.encode_literal_with_name_ref_static(
                buf,
                index,
                header.value(),
                header.never_indexed(),
            )
        } else {
            // Literal with Literal Name
            self.encode_literal_with_literal_name(
                buf,
                header.name(),
                header.value(),
                header.never_indexed(),
            )
        }
    }

    /// Indexed Field Line (静的テーブル) をエンコード
    ///
    /// Format: 1TNNNNNN (T=1 for static)
    fn encode_indexed_field_static(&self, buf: &mut [u8], index: usize) -> Option<usize> {
        // 0xc0 = 11000000 (T=1)
        integer::encode_integer(buf, index as u64, 6, 0xc0)
    }

    /// Indexed Field Line (動的テーブル) をエンコード (RFC 9204 Section 4.5.2)
    ///
    /// Format: 1TNNNNNN (T=0 for dynamic)
    /// relative_index = base - absolute_index - 1
    ///
    /// 現在の `encode_with_dynamic` は base = required_insert_count とするため、
    /// absolute_index < base が必ず成立し Post-Base Index 表現 (Section 4.5.3)
    /// には到達しない (RFC 9204 Section 3.2.6)。
    /// Base < Required Insert Count の戦略を導入した場合に Post-Base 表現が必要になる。
    fn encode_indexed_field_dynamic(
        &self,
        buf: &mut [u8],
        absolute_index: u64,
        base: u64,
    ) -> Option<usize> {
        debug_assert!(
            absolute_index < base,
            "absolute_index >= base の Post-Base Index 表現は未対応 (RFC 9204 Section 4.5.3)"
        );
        let relative_index = base - absolute_index - 1;
        // 0x80 = 10000000 (T=0)
        integer::encode_integer(buf, relative_index, 6, 0x80)
    }

    /// Literal with Name Reference (静的テーブル) をエンコード
    ///
    /// Format: 01NTNNNN (N=never index, T=1 for static)
    fn encode_literal_with_name_ref_static(
        &self,
        buf: &mut [u8],
        index: usize,
        value: &[u8],
        never_indexed: bool,
    ) -> Option<usize> {
        // 0x50 = 01010000 (N=0, T=1), never_indexed 時は 0x20 を OR して N=1 (0x70)
        let prefix = if never_indexed { 0x70 } else { 0x50 };
        let mut offset = integer::encode_integer(buf, index as u64, 4, prefix)?;

        // Value
        let value_len = self.encode_string(&mut buf[offset..], value)?;
        offset += value_len;

        Some(offset)
    }

    /// Literal with Name Reference (動的テーブル) をエンコード (RFC 9204 Section 4.5.4)
    ///
    /// Format: 01NTNNNN (N=never index, T=0 for dynamic)
    /// relative_index = base - absolute_index - 1
    ///
    /// 現在の `encode_with_dynamic` は base = required_insert_count とするため、
    /// absolute_index < base が必ず成立し Post-Base Name Reference 表現 (Section 4.5.5)
    /// には到達しない (RFC 9204 Section 3.2.6)。
    /// Base < Required Insert Count の戦略を導入した場合に Post-Base 表現が必要になる。
    fn encode_literal_with_name_ref_dynamic(
        &self,
        buf: &mut [u8],
        absolute_index: u64,
        value: &[u8],
        base: u64,
        never_indexed: bool,
    ) -> Option<usize> {
        debug_assert!(
            absolute_index < base,
            "absolute_index >= base の Post-Base Name Reference 表現は未対応 (RFC 9204 Section 4.5.5)"
        );
        let relative_index = base - absolute_index - 1;
        // 0x40 = 01000000 (N=0, T=0), never_indexed 時は 0x20 を OR して N=1 (0x60)
        let prefix = if never_indexed { 0x60 } else { 0x40 };
        let mut offset = integer::encode_integer(buf, relative_index, 4, prefix)?;

        // Value
        let value_len = self.encode_string(&mut buf[offset..], value)?;
        offset += value_len;

        Some(offset)
    }

    /// Literal with Literal Name をエンコード
    ///
    /// Format (RFC 9204 Section 4.5.6):
    /// ```text
    ///      0   1   2   3   4   5   6   7
    ///    +---+---+---+---+---+---+---+---+
    ///    | 0 | 0 | 1 | N |H|    Name     |
    ///    +---+---+---+---+---+-----------+
    ///    |  Name String (Length octets)  |
    ///    +-------------------------------+
    ///    |H|     Value Length (7+)       |
    ///    +---+---------------------------+
    ///    |  Value String (Length octets) |
    ///    +-------------------------------+
    /// ```
    fn encode_literal_with_literal_name(
        &self,
        buf: &mut [u8],
        name: &[u8],
        value: &[u8],
        never_indexed: bool,
    ) -> Option<usize> {
        let mut offset = 0;

        // 名前のエンコード (3-bit prefix)
        // Prefix: 001N (N=never_indexed), never_indexed 時は 0x10 を OR して N=1 (0x30)
        // H ビット (bit 3) と Name Length (bits 0-2)
        let prefix = if never_indexed { 0x30 } else { 0x20 };
        let name_len = self.encode_string_with_prefix(buf, name, 3, prefix)?;
        offset += name_len;

        // Value (string literal, 7-bit prefix)
        let value_len = self.encode_string(&mut buf[offset..], value)?;
        offset += value_len;

        Some(offset)
    }

    /// 文字列を指定された prefix bits でエンコード
    fn encode_string_with_prefix(
        &self,
        buf: &mut [u8],
        data: &[u8],
        prefix_bits: u8,
        prefix: u8,
    ) -> Option<usize> {
        super::wire::encode_string_with_prefix(buf, data, prefix_bits, prefix, self.use_huffman)
    }

    /// 文字列をエンコード (7-bit prefix)
    fn encode_string(&self, buf: &mut [u8], data: &[u8]) -> Option<usize> {
        super::wire::encode_string(buf, data, self.use_huffman)
    }

    /// エントリを動的テーブルに挿入
    pub fn insert(&mut self, name: Vec<u8>, value: Vec<u8>) -> Option<u64> {
        self.table.insert(name, value)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encode_indexed_field() {
        let mut encoder = Encoder::new().use_huffman(false);
        let headers = vec![Header::new(b":method", b"GET").expect("test must succeed")];

        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode(&mut buf, &headers, 0)
            .expect("test must succeed");

        // Required Insert Count (0) + Delta Base (0) + Indexed Field (17)
        assert!(len >= 3);
        assert_eq!(buf[0], 0x00); // Required Insert Count
        assert_eq!(buf[1], 0x00); // Delta Base
        assert_eq!(buf[2], 0xc0 | 17); // Indexed static table entry 17 (:method GET)
    }

    #[test]
    fn test_encode_literal_with_name_ref() {
        let mut encoder = Encoder::new().use_huffman(false);
        let headers = vec![Header::new(b":status", b"201").expect("test must succeed")];

        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode(&mut buf, &headers, 0)
            .expect("test must succeed");

        assert!(len >= 3);
        assert_eq!(buf[0], 0x00); // Required Insert Count
        assert_eq!(buf[1], 0x00); // Delta Base
        // Name reference to static table (24 is first :status)
    }

    #[test]
    fn test_encode_literal_with_literal_name() {
        let mut encoder = Encoder::new().use_huffman(false);
        let headers = vec![Header::new(b"x-custom", b"value").expect("test must succeed")];

        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode(&mut buf, &headers, 0)
            .expect("test must succeed");

        assert!(len > 4);
        assert_eq!(buf[0], 0x00); // Required Insert Count
        assert_eq!(buf[1], 0x00); // Delta Base
        // Literal with Literal Name: 001N|H|LLL
        // N=0, H=0, LLL=7 (max for 3-bit), 続きに 1 (8-7=1)
        // "x-custom" は 8 バイトなので 3-bit prefix を超える
        assert_eq!(buf[2], 0x27); // 0x20 | 7 = 0x27
        assert_eq!(buf[3], 0x01); // 8 - 7 = 1
    }

    #[test]
    fn test_encode_multiple_headers() {
        let mut encoder = Encoder::new();
        let headers = vec![
            Header::new(b":method", b"GET").expect("test must succeed"),
            Header::new(b":scheme", b"https").expect("test must succeed"),
            Header::new(b":path", b"/").expect("test must succeed"),
            Header::new(b":authority", b"example.com").expect("test must succeed"),
        ];

        let mut buf = vec![0u8; 128];
        let len = encoder
            .encode(&mut buf, &headers, 0)
            .expect("test must succeed");
        assert!(len > 0);
    }

    #[test]
    fn test_static_table_boundary() {
        // Test encoding index >= 64 (needs multi-byte encoding)
        let mut encoder = Encoder::new();

        // Index 98 (x-frame-options: sameorigin)
        let headers =
            vec![Header::new(b"x-frame-options", b"sameorigin").expect("test must succeed")];
        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode(&mut buf, &headers, 0)
            .expect("test must succeed");
        assert!(len > 0);
    }

    #[test]
    fn test_estimate_encoded_size() {
        let headers = vec![
            Header::new(b":method", b"GET").expect("test must succeed"),
            Header::new(b"x-custom", b"value").expect("test must succeed"),
        ];
        let estimate = estimate_encoded_size(&headers);
        assert!(estimate > 0);
    }

    #[test]
    fn test_dynamic_encoder_static_only() {
        let mut encoder = DynamicEncoder::new().use_huffman(false);
        let headers = vec![Header::new(b":method", b"GET").expect("test must succeed")];

        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode_with_dynamic(&mut buf, &headers)
            .expect("test must succeed");

        assert!(len >= 3);
        assert_eq!(buf[0], 0x00); // Required Insert Count
        assert_eq!(buf[1], 0x00); // Delta Base
        assert_eq!(buf[2], 0xc0 | 17); // Indexed static table entry 17
    }

    #[test]
    fn test_dynamic_encoder_with_dynamic_table() {
        let mut encoder = DynamicEncoder::new().use_huffman(false);
        encoder.set_max_table_capacity(4096);
        encoder
            .set_table_capacity(1024)
            .expect("test: capacity within max");
        encoder.set_peer_max_blocked_streams(100);

        // 動的テーブルにエントリを挿入
        encoder.insert(b":authority".to_vec(), b"www.example.com".to_vec());
        assert_eq!(encoder.table().len(), 1);

        // エンコード
        let headers =
            vec![Header::new(b":authority", b"www.example.com").expect("test must succeed")];
        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode_with_dynamic(&mut buf, &headers)
            .expect("test must succeed");

        // Required Insert Count > 0 (動的テーブルを参照)
        assert!(len >= 3);
        // Encoded Insert Count = (1 % (2 * (4096/32))) + 1 = 2
        assert!(buf[0] > 0);
    }

    #[test]
    fn test_dynamic_encoder_blocked_streams_limit() {
        let mut encoder = DynamicEncoder::new().use_huffman(false);
        encoder.set_max_table_capacity(4096);
        encoder
            .set_table_capacity(1024)
            .expect("test: capacity within max");
        encoder.set_peer_max_blocked_streams(2);

        // 動的テーブルにエントリを挿入
        encoder.insert(b":authority".to_vec(), b"www.example.com".to_vec());

        // blocked_streams_count < peer_max_blocked_streams: 動的テーブルを使用
        let headers =
            vec![Header::new(b":authority", b"www.example.com").expect("test must succeed")];
        let mut buf = vec![0u8; 64];
        let _len = encoder
            .encode(&mut buf, &headers, 0)
            .expect("test must succeed");
        assert!(buf[0] > 0); // RIC > 0

        // blocked_streams_count >= peer_max_blocked_streams: 静的テーブルのみ
        let mut buf2 = vec![0u8; 64];
        let len2 = encoder
            .encode(&mut buf2, &headers, 2)
            .expect("test must succeed");
        assert_eq!(buf2[0], 0x00); // RIC = 0
        assert!(len2 > 0);
    }

    #[test]
    fn test_dynamic_encoder_prefer_dynamic() {
        let mut encoder = DynamicEncoder::new().use_huffman(false);
        encoder.set_max_table_capacity(4096);
        encoder
            .set_table_capacity(1024)
            .expect("test: capacity within max");
        encoder.set_peer_max_blocked_streams(100);

        // :method = GET は静的テーブルにある (インデックス 17)
        // しかし、動的テーブルに追加すると動的テーブルが優先される
        encoder.insert(b":method".to_vec(), b"GET".to_vec());

        let headers = vec![Header::new(b":method", b"GET").expect("test must succeed")];
        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode_with_dynamic(&mut buf, &headers)
            .expect("test must succeed");

        assert!(len >= 3);
        // Required Insert Count が設定されている
        assert!(buf[0] > 0);
    }

    #[test]
    fn ack_section_は未追跡ストリームに対してエラーを返す() {
        let mut encoder = DynamicEncoder::new();
        // track_section していないストリーム ID に対する ack_section は
        // QPACK_DECODER_STREAM_ERROR (RFC 9204 Section 4.4.1)
        assert!(encoder.ack_section(42).is_err());
    }

    #[test]
    fn ack_section_は全て_ack_済みのストリームに対してエラーを返す() {
        let mut encoder = DynamicEncoder::new();
        encoder.track_section(1, 5);
        assert!(encoder.ack_section(1).is_ok());
        // 2 回目の ack は全て ack 済みなのでエラー
        assert!(encoder.ack_section(1).is_err());
    }

    #[test]
    fn encode_required_insert_count_は_max_entries_が_0_かつ_ric_が_0_のとき_0_を返す() {
        let encoder = DynamicEncoder::new();
        assert_eq!(encoder.encode_required_insert_count(0), 0);
    }

    #[test]
    #[cfg_attr(debug_assertions, should_panic(expected = "invariant violation"))]
    fn encode_required_insert_count_は_max_entries_が_0_かつ_ric_が非ゼロのとき不変条件違反を検出する()
     {
        // debug ビルド: debug_assert! で panic する
        // release ビルド: 防御的に 0 を返す
        let encoder = DynamicEncoder::new();
        let result = encoder.encode_required_insert_count(5);
        // release ビルドで到達する場合
        assert_eq!(result, 0);
    }

    #[test]
    fn 相対インデックス表現は引き続き正しく動作する() {
        // absolute_index=1, base=3 → relative_index=1
        let encoder = DynamicEncoder::new();
        let mut buf = vec![0u8; 16];
        let len = encoder
            .encode_indexed_field_dynamic(&mut buf, 1, 3)
            .expect("test must succeed");
        assert_eq!(len, 1);
        // 0x80 | 1 = 0x81 (10000001)
        assert_eq!(buf[0], 0x81);
    }
}
