//! QPACK デコーダー (RFC 9204)
//!
//! 静的テーブルと動的テーブルを使用する QPACK デコーダー。
//!
//! ## 機能
//!
//! - `Decoder`: 静的テーブルのみを使用するシンプルなデコーダー
//! - `DynamicDecoder`: 動的テーブルも使用する拡張デコーダー

use std::borrow::Cow;

use crate::error::QpackError;

use super::dynamic_table::DynamicTable;
use super::header::Header;
use super::integer;
use super::table::{STATIC_TABLE_LEN, get_static_entry};

/// QPACK 動的デコードの結果 (RFC 9204 Section 2.1.2)
#[derive(Debug)]
pub enum DecodeOutput {
    /// デコード成功
    Decoded(Vec<Header>),
    /// ブロッキング (Required Insert Count > Insert Count)
    Blocked,
}

/// QPACK デコード由来のバイト列から検査をスキップして `Header` を構築する
///
/// デコード経路は wire 上のバイト列から構築する。受信側の検証は
/// `validation` モジュールで別途実施するため、ここでは構築時検査をスキップする。
fn header_from_decoded(name: Vec<u8>, value: Vec<u8>) -> Header {
    Header::from_validated_parts_internal(Cow::Owned(name), Cow::Owned(value))
}

/// QPACK デコーダー
#[derive(Debug, Default)]
pub struct Decoder {
    /// 最大ヘッダーリストサイズ
    max_field_section_size: u64,
}

impl Decoder {
    /// 新しいデコーダーを作成
    pub fn new() -> Self {
        Self {
            max_field_section_size: 16 * 1024,
        }
    }

    /// 最大ヘッダーリストサイズを設定
    pub fn max_field_section_size(mut self, size: u64) -> Self {
        self.max_field_section_size = size;
        self
    }

    /// エンコードされたフィールドセクションをデコード
    pub fn decode(&self, data: &[u8]) -> Result<Vec<Header>, QpackError> {
        if data.len() < 2 {
            return Err(QpackError::BufferTooShort);
        }

        let mut offset = 0;

        // Required Insert Count
        let (ric, ric_len) = integer::decode_integer(&data[offset..], 8)?;
        offset += ric_len;

        if ric != 0 {
            // 動的テーブルは未サポート
            return Err(QpackError::DecodeFailed);
        }

        // Delta Base (Sign bit + Base)
        if offset >= data.len() {
            return Err(QpackError::BufferTooShort);
        }
        let (_, delta_len) = integer::decode_integer(&data[offset..], 7)?;
        offset += delta_len;

        // ヘッダーをデコード
        let mut headers = Vec::new();
        let mut total_size = 0u64;

        while offset < data.len() {
            let (header, consumed) = self.decode_header(&data[offset..])?;

            total_size += (header.name().len() + header.value().len() + 32) as u64;
            if total_size > self.max_field_section_size {
                return Err(QpackError::DecodeFailed);
            }

            headers.push(header);
            offset += consumed;
        }

        Ok(headers)
    }

    /// 単一のヘッダーをデコード
    fn decode_header(&self, data: &[u8]) -> Result<(Header, usize), QpackError> {
        if data.is_empty() {
            return Err(QpackError::BufferTooShort);
        }

        let first = data[0];

        if first & 0x80 != 0 {
            // Indexed Field Line (1xxxxxxx)
            self.decode_indexed_field(data)
        } else if first & 0x40 != 0 {
            // Literal Field Line with Name Reference (01xxxxxx)
            self.decode_literal_with_name_ref(data)
        } else if first & 0x20 != 0 {
            // Literal Field Line with Literal Name (001xxxxx)
            self.decode_literal_with_literal_name(data)
        } else if first & 0x10 != 0 {
            // Indexed Field Line with Post-Base Index (0001xxxx)
            // 動的テーブルは未サポート
            Err(QpackError::DecodeFailed)
        } else {
            // Literal Field Line with Post-Base Name Reference (0000xxxx)
            // 動的テーブルは未サポート
            Err(QpackError::DecodeFailed)
        }
    }

    /// Indexed Field Line をデコード
    ///
    /// Format: 1TNNNNNN
    fn decode_indexed_field(&self, data: &[u8]) -> Result<(Header, usize), QpackError> {
        let is_static = (data[0] & 0x40) != 0;

        if !is_static {
            // 動的テーブルは未サポート
            return Err(QpackError::DecodeFailed);
        }

        let (index, consumed) = integer::decode_integer(data, 6)?;

        if index as usize >= STATIC_TABLE_LEN {
            return Err(QpackError::InvalidIndex(index));
        }

        let entry = get_static_entry(index as usize).ok_or(QpackError::InvalidIndex(index))?;

        Ok((entry.clone(), consumed))
    }

    /// Literal Field Line with Name Reference をデコード
    ///
    /// Format: 01NTNNNN
    fn decode_literal_with_name_ref(&self, data: &[u8]) -> Result<(Header, usize), QpackError> {
        let is_static = (data[0] & 0x10) != 0;

        if !is_static {
            // 動的テーブルは未サポート
            return Err(QpackError::DecodeFailed);
        }

        let mut offset = 0;

        // Name index
        let (index, index_len) = integer::decode_integer(data, 4)?;
        offset += index_len;

        if index as usize >= STATIC_TABLE_LEN {
            return Err(QpackError::InvalidIndex(index));
        }

        let entry = get_static_entry(index as usize).ok_or(QpackError::InvalidIndex(index))?;
        let name = entry.name().to_vec();

        // Value
        let (value, value_len) = self.decode_string(&data[offset..])?;
        offset += value_len;

        Ok((header_from_decoded(name, value), offset))
    }

    /// Literal Field Line with Literal Name をデコード
    ///
    /// Format: 001NNNNN
    fn decode_literal_with_literal_name(&self, data: &[u8]) -> Result<(Header, usize), QpackError> {
        let mut offset = 0;

        // Skip prefix byte (already validated)
        let (name_len_value, prefix_len) = integer::decode_integer(data, 3)?;
        offset += prefix_len;

        // Decode name
        // Re-read to get huffman flag
        let is_huffman = (data[0] & 0x08) != 0;
        let (name, name_bytes) =
            self.decode_string_with_len(&data[offset..], name_len_value as usize, is_huffman)?;
        offset += name_bytes;

        // Decode value
        let (value, value_len) = self.decode_string(&data[offset..])?;
        offset += value_len;

        Ok((header_from_decoded(name, value), offset))
    }

    /// 文字列をデコード
    fn decode_string(&self, data: &[u8]) -> Result<(Vec<u8>, usize), QpackError> {
        super::wire::decode_string(data)
    }

    /// 長さ指定で文字列をデコード
    fn decode_string_with_len(
        &self,
        data: &[u8],
        length: usize,
        is_huffman: bool,
    ) -> Result<(Vec<u8>, usize), QpackError> {
        super::wire::decode_string_with_len(data, length, is_huffman)
    }
}

/// 動的テーブル対応 QPACK デコーダー (RFC 9204)
///
/// 静的テーブルと動的テーブルの両方を使用してヘッダーをデコードする。
#[derive(Debug)]
pub struct DynamicDecoder {
    /// 動的テーブル
    table: DynamicTable,
    /// 最大フィールドセクションサイズ
    max_field_section_size: u64,
    /// 最大テーブル容量
    max_table_capacity: u64,
    /// 最後にデコードした Required Insert Count
    last_required_insert_count: u64,
}

impl Default for DynamicDecoder {
    fn default() -> Self {
        Self::new()
    }
}

impl DynamicDecoder {
    /// 新しい動的デコーダーを作成
    pub fn new() -> Self {
        Self {
            table: DynamicTable::new(),
            max_field_section_size: 16 * 1024,
            max_table_capacity: 0,
            last_required_insert_count: 0,
        }
    }

    /// 最大フィールドセクションサイズを設定 (ビルダーパターン)
    pub fn max_field_section_size(mut self, size: u64) -> Self {
        self.max_field_section_size = size;
        self
    }

    /// 最大フィールドセクションサイズを設定
    pub fn set_max_field_section_size(&mut self, size: u64) {
        self.max_field_section_size = size;
    }

    /// 最大テーブル容量を設定
    pub fn set_max_table_capacity(&mut self, capacity: u64) {
        self.max_table_capacity = capacity;
    }

    /// 動的テーブル容量を設定
    pub fn set_table_capacity(&mut self, capacity: u64) {
        self.table.set_capacity(capacity);
    }

    /// 動的テーブルへの参照を取得
    pub fn table(&self) -> &DynamicTable {
        &self.table
    }

    /// 動的テーブルへの可変参照を取得
    pub fn table_mut(&mut self) -> &mut DynamicTable {
        &mut self.table
    }

    /// エンコードされたフィールドセクションをデコード
    ///
    /// Required Insert Count > Insert Count の場合は `DecodeOutput::Blocked` を返す
    /// (RFC 9204 Section 2.1.2)。
    pub fn decode(&mut self, data: &[u8]) -> Result<DecodeOutput, QpackError> {
        if data.len() < 2 {
            return Err(QpackError::BufferTooShort);
        }

        let mut offset = 0;

        // Required Insert Count をデコード
        let (enc_insert_count, ric_len) = integer::decode_integer(&data[offset..], 8)?;
        offset += ric_len;

        // Required Insert Count を復元
        let required_insert_count = self.decode_required_insert_count(enc_insert_count)?;
        self.last_required_insert_count = required_insert_count;

        // ブロッキングチェック (RFC 9204 Section 2.1.2)
        if required_insert_count > 0 && self.table.insert_count() < required_insert_count {
            return Ok(DecodeOutput::Blocked);
        }

        // Delta Base をデコード
        if offset >= data.len() {
            return Err(QpackError::BufferTooShort);
        }
        let sign = (data[offset] & 0x80) != 0;
        let (delta_base, delta_len) = integer::decode_integer(&data[offset..], 7)?;
        offset += delta_len;

        // Base を計算
        let base = if sign {
            // Sign = 1: Base = ReqInsertCount - DeltaBase - 1
            if required_insert_count <= delta_base {
                return Err(QpackError::DecodeFailed);
            }
            required_insert_count - delta_base - 1
        } else {
            // Sign = 0: Base = ReqInsertCount + DeltaBase
            required_insert_count
                .checked_add(delta_base)
                .ok_or(QpackError::DecodeFailed)?
        };

        // ヘッダーをデコード
        let mut headers = Vec::new();
        let mut total_size = 0u64;

        while offset < data.len() {
            let (header, consumed) =
                self.decode_header(&data[offset..], base, required_insert_count)?;

            total_size += (header.name().len() + header.value().len() + 32) as u64;
            if total_size > self.max_field_section_size {
                return Err(QpackError::DecodeFailed);
            }

            headers.push(header);
            offset += consumed;
        }

        Ok(DecodeOutput::Decoded(headers))
    }

    /// Required Insert Count をデコード (RFC 9204 Section 4.5.1.1)
    fn decode_required_insert_count(&self, enc_insert_count: u64) -> Result<u64, QpackError> {
        if enc_insert_count == 0 {
            return Ok(0);
        }

        let max_entries = self.max_table_capacity / 32;
        if max_entries == 0 {
            // 動的テーブルが有効でない
            return Err(QpackError::DecodeFailed);
        }

        let full_range = 2 * max_entries;
        if enc_insert_count > full_range {
            return Err(QpackError::DecodeFailed);
        }

        let total_inserts = self.table.insert_count();
        let max_value = total_inserts + max_entries;
        let max_wrapped = (max_value / full_range) * full_range;
        let mut req_insert_count = max_wrapped + enc_insert_count - 1;

        if req_insert_count > max_value {
            if req_insert_count <= full_range {
                return Err(QpackError::DecodeFailed);
            }
            req_insert_count -= full_range;
        }

        if req_insert_count == 0 {
            return Err(QpackError::DecodeFailed);
        }

        Ok(req_insert_count)
    }

    /// 単一のヘッダーをデコード
    fn decode_header(
        &self,
        data: &[u8],
        base: u64,
        required_insert_count: u64,
    ) -> Result<(Header, usize), QpackError> {
        if data.is_empty() {
            return Err(QpackError::BufferTooShort);
        }

        let first = data[0];

        if first & 0x80 != 0 {
            // Indexed Field Line (1xxxxxxx)
            self.decode_indexed_field(data, base, required_insert_count)
        } else if first & 0x40 != 0 {
            // Literal Field Line with Name Reference (01xxxxxx)
            self.decode_literal_with_name_ref(data, base, required_insert_count)
        } else if first & 0x20 != 0 {
            // Literal Field Line with Literal Name (001xxxxx)
            self.decode_literal_with_literal_name(data)
        } else if first & 0x10 != 0 {
            // Indexed Field Line with Post-Base Index (0001xxxx)
            self.decode_indexed_field_post_base(data, base, required_insert_count)
        } else {
            // Literal Field Line with Post-Base Name Reference (0000xxxx)
            self.decode_literal_with_post_base_name_ref(data, base, required_insert_count)
        }
    }

    /// Indexed Field Line をデコード
    ///
    /// Format: 1TNNNNNN
    fn decode_indexed_field(
        &self,
        data: &[u8],
        base: u64,
        required_insert_count: u64,
    ) -> Result<(Header, usize), QpackError> {
        let is_static = (data[0] & 0x40) != 0;

        let (index, consumed) = integer::decode_integer(data, 6)?;

        if is_static {
            // 静的テーブル
            if index as usize >= STATIC_TABLE_LEN {
                return Err(QpackError::InvalidIndex(index));
            }
            let entry = get_static_entry(index as usize).ok_or(QpackError::InvalidIndex(index))?;
            Ok((entry.clone(), consumed))
        } else {
            // 動的テーブル (相対インデックス)
            // absolute_index = base - index - 1
            // RFC 9204 Section 2.2.3: absolute index >= Required Insert Count は
            // QPACK_DECOMPRESSION_FAILED
            if index < base {
                let absolute_index = base - index - 1;
                if absolute_index >= required_insert_count {
                    return Err(QpackError::DecodeFailed);
                }
            }
            let entry = self
                .table
                .get_by_relative_index_repr(index, base)
                .ok_or(QpackError::InvalidIndex(index))?;
            Ok((
                header_from_decoded(entry.name.clone(), entry.value.clone()),
                consumed,
            ))
        }
    }

    /// Indexed Field Line with Post-Base Index をデコード
    ///
    /// Format: 0001NNNN
    fn decode_indexed_field_post_base(
        &self,
        data: &[u8],
        base: u64,
        required_insert_count: u64,
    ) -> Result<(Header, usize), QpackError> {
        let (post_base_index, consumed) = integer::decode_integer(data, 4)?;

        // absolute_index = base + post_base_index
        // RFC 9204 Section 2.2.3: absolute index >= Required Insert Count は
        // QPACK_DECOMPRESSION_FAILED
        let absolute_index = base
            .checked_add(post_base_index)
            .ok_or(QpackError::DecodeFailed)?;
        if absolute_index >= required_insert_count {
            return Err(QpackError::DecodeFailed);
        }

        let entry = self
            .table
            .get_by_post_base_index(post_base_index, base)
            .ok_or(QpackError::InvalidIndex(post_base_index))?;

        Ok((
            header_from_decoded(entry.name.clone(), entry.value.clone()),
            consumed,
        ))
    }

    /// Literal Field Line with Name Reference をデコード
    ///
    /// Format: 01NTNNNN
    fn decode_literal_with_name_ref(
        &self,
        data: &[u8],
        base: u64,
        required_insert_count: u64,
    ) -> Result<(Header, usize), QpackError> {
        let is_static = (data[0] & 0x10) != 0;

        let mut offset = 0;

        // Name index
        let (index, index_len) = integer::decode_integer(data, 4)?;
        offset += index_len;

        let name = if is_static {
            if index as usize >= STATIC_TABLE_LEN {
                return Err(QpackError::InvalidIndex(index));
            }
            let entry = get_static_entry(index as usize).ok_or(QpackError::InvalidIndex(index))?;
            entry.name().to_vec()
        } else {
            // absolute_index = base - index - 1
            // RFC 9204 Section 2.2.3: absolute index >= Required Insert Count は
            // QPACK_DECOMPRESSION_FAILED
            if index < base {
                let absolute_index = base - index - 1;
                if absolute_index >= required_insert_count {
                    return Err(QpackError::DecodeFailed);
                }
            }
            let entry = self
                .table
                .get_by_relative_index_repr(index, base)
                .ok_or(QpackError::InvalidIndex(index))?;
            entry.name.clone()
        };

        // Value
        let (value, value_len) = decode_string(&data[offset..])?;
        offset += value_len;

        Ok((header_from_decoded(name, value), offset))
    }

    /// Literal Field Line with Post-Base Name Reference をデコード
    ///
    /// Format: 0000NNNN
    fn decode_literal_with_post_base_name_ref(
        &self,
        data: &[u8],
        base: u64,
        required_insert_count: u64,
    ) -> Result<(Header, usize), QpackError> {
        let mut offset = 0;

        // Name index (post-base)
        let (post_base_index, index_len) = integer::decode_integer(data, 3)?;
        offset += index_len;

        // absolute_index = base + post_base_index
        // RFC 9204 Section 2.2.3: absolute index >= Required Insert Count は
        // QPACK_DECOMPRESSION_FAILED
        let absolute_index = base
            .checked_add(post_base_index)
            .ok_or(QpackError::DecodeFailed)?;
        if absolute_index >= required_insert_count {
            return Err(QpackError::DecodeFailed);
        }

        let entry = self
            .table
            .get_by_post_base_index(post_base_index, base)
            .ok_or(QpackError::InvalidIndex(post_base_index))?;
        let name = entry.name.clone();

        // Value
        let (value, value_len) = decode_string(&data[offset..])?;
        offset += value_len;

        Ok((header_from_decoded(name, value), offset))
    }

    /// Literal Field Line with Literal Name をデコード
    ///
    /// Format: 001NNNNN
    fn decode_literal_with_literal_name(&self, data: &[u8]) -> Result<(Header, usize), QpackError> {
        let mut offset = 0;

        // Skip prefix byte (already validated)
        let (name_len_value, prefix_len) = integer::decode_integer(data, 3)?;
        offset += prefix_len;

        // Decode name
        let is_huffman = (data[0] & 0x08) != 0;
        let (name, name_bytes) =
            decode_string_with_len(&data[offset..], name_len_value as usize, is_huffman)?;
        offset += name_bytes;

        // Decode value
        let (value, value_len) = decode_string(&data[offset..])?;
        offset += value_len;

        Ok((header_from_decoded(name, value), offset))
    }

    /// エントリを動的テーブルに挿入
    pub fn insert(&mut self, name: Vec<u8>, value: Vec<u8>) -> Option<u64> {
        self.table.insert(name, value)
    }

    /// 最後にデコードした Required Insert Count を取得
    ///
    /// Section Acknowledgement を送信するかどうかの判断に使用する。
    /// 0 より大きい場合、動的テーブルを参照したため確認応答が必要。
    pub fn last_required_insert_count(&self) -> u64 {
        self.last_required_insert_count
    }
}

/// 文字列をデコード
fn decode_string(data: &[u8]) -> Result<(Vec<u8>, usize), QpackError> {
    super::wire::decode_string(data)
}

/// 長さ指定で文字列をデコード
fn decode_string_with_len(
    data: &[u8],
    length: usize,
    is_huffman: bool,
) -> Result<(Vec<u8>, usize), QpackError> {
    super::wire::decode_string_with_len(data, length, is_huffman)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::qpack::encoder::Encoder;
    use crate::qpack::header::Header;

    #[test]
    fn test_decode_indexed_field() {
        let decoder = Decoder::new();

        // Required Insert Count (0) + Delta Base (0) + Indexed Field (17 = :method GET)
        let data = [0x00, 0x00, 0xd1]; // 0xd1 = 0xc0 | 17
        let headers = decoder.decode(&data).expect("test must succeed");

        assert_eq!(headers.len(), 1);
        assert_eq!(headers[0].name(), b":method");
        assert_eq!(headers[0].value(), b"GET");
    }

    #[test]
    fn test_decode_literal_with_name_ref() {
        let decoder = Decoder::new();

        // Required Insert Count (0) + Delta Base (0) + Literal with Name Ref
        // 0x5f 0x09 = 01011111 00001001 = static ref to index 24 (:status)
        //   01 = Literal with Name Ref prefix
        //   0 = N (not never indexed)
        //   1 = T (static table)
        //   1111 = 15 (max for 4-bit prefix)
        //   0x09 = 9 (24 - 15 = 9)
        // 0x03 = length 3 (not huffman)
        // "201" = value
        let data = [0x00, 0x00, 0x5f, 0x09, 0x03, b'2', b'0', b'1'];
        let headers = decoder.decode(&data).expect("test must succeed");

        assert_eq!(headers.len(), 1);
        assert_eq!(headers[0].name(), b":status");
        assert_eq!(headers[0].value(), b"201");
    }

    #[test]
    fn test_encode_decode_roundtrip() {
        let mut encoder = Encoder::new().use_huffman(false);
        let decoder = Decoder::new();

        let original = vec![
            Header::new(b":method", b"GET").expect("test must succeed"),
            Header::new(b":scheme", b"https").expect("test must succeed"),
            Header::new(b":path", b"/").expect("test must succeed"),
        ];

        let mut buf = vec![0u8; 128];
        let len = encoder
            .encode(&mut buf, &original, 0)
            .expect("test must succeed");

        let decoded = decoder.decode(&buf[..len]).expect("test must succeed");

        assert_eq!(decoded.len(), original.len());
        for (dec, orig) in decoded.iter().zip(original.iter()) {
            assert_eq!(dec.name(), orig.name());
            assert_eq!(dec.value(), orig.value());
        }
    }

    #[test]
    fn test_encode_decode_with_huffman() {
        let mut encoder = Encoder::new().use_huffman(true);
        let decoder = Decoder::new();

        let original = vec![
            Header::new(b":method", b"GET").expect("test must succeed"),
            Header::new(b":authority", b"www.example.com").expect("test must succeed"),
        ];

        let mut buf = vec![0u8; 128];
        let len = encoder
            .encode(&mut buf, &original, 0)
            .expect("test must succeed");

        let decoded = decoder.decode(&buf[..len]).expect("test must succeed");

        assert_eq!(decoded.len(), original.len());
        assert_eq!(decoded[0].name(), b":method");
        assert_eq!(decoded[0].value(), b"GET");
        assert_eq!(decoded[1].name(), b":authority");
        assert_eq!(decoded[1].value(), b"www.example.com");
    }

    #[test]
    fn test_decode_buffer_too_short() {
        let decoder = Decoder::new();
        assert!(decoder.decode(&[0x00]).is_err());
    }

    #[test]
    fn test_decode_invalid_index() {
        let decoder = Decoder::new();
        // Indexed field with index 99 (out of range)
        let data = [0x00, 0x00, 0xff, 0x24]; // 0xc0 | 63 + continuation
        let result = decoder.decode(&data);
        assert!(result.is_err());
    }

    #[test]
    fn test_dynamic_decoder_static_only() {
        let mut decoder = DynamicDecoder::new();

        // Required Insert Count (0) + Delta Base (0) + Indexed Field (17 = :method GET)
        let data = [0x00, 0x00, 0xd1]; // 0xd1 = 0xc0 | 17
        let DecodeOutput::Decoded(headers) = decoder.decode(&data).expect("test must succeed")
        else {
            panic!("unexpected Blocked");
        };

        assert_eq!(headers.len(), 1);
        assert_eq!(headers[0].name(), b":method");
        assert_eq!(headers[0].value(), b"GET");
        assert_eq!(decoder.last_required_insert_count(), 0);
    }

    #[test]
    fn test_dynamic_encoder_decoder_roundtrip() {
        use crate::qpack::encoder::DynamicEncoder;

        let mut encoder = DynamicEncoder::new().use_huffman(false);
        let mut decoder = DynamicDecoder::new();

        encoder.set_max_table_capacity(4096);
        encoder
            .set_table_capacity(1024)
            .expect("test: capacity within max");
        decoder.set_max_table_capacity(4096);
        decoder.set_table_capacity(1024);

        // 動的テーブルに同じエントリを挿入
        encoder.insert(b":authority".to_vec(), b"www.example.com".to_vec());
        decoder.insert(b":authority".to_vec(), b"www.example.com".to_vec());

        // エンコード
        let headers =
            vec![Header::new(b":authority", b"www.example.com").expect("test must succeed")];
        let mut buf = vec![0u8; 64];
        let len = encoder
            .encode(&mut buf, &headers, 0)
            .expect("test must succeed");

        // デコード
        let DecodeOutput::Decoded(decoded) =
            decoder.decode(&buf[..len]).expect("test must succeed")
        else {
            panic!("unexpected Blocked");
        };

        assert_eq!(decoded.len(), 1);
        assert_eq!(decoded[0].name(), b":authority");
        assert_eq!(decoded[0].value(), b"www.example.com");
    }

    #[test]
    fn test_dynamic_decoder_with_dynamic_ref() {
        let mut decoder = DynamicDecoder::new();
        decoder.set_max_table_capacity(4096);
        decoder.set_table_capacity(1024);

        // 動的テーブルにエントリを挿入
        decoder.insert(b"custom-header".to_vec(), b"custom-value".to_vec());

        // 手動でエンコードされたデータ:
        // Required Insert Count = 2 (エンコード値)
        // Base = 1, Sign = 0, Delta = 0
        // Indexed Field (dynamic, relative index 0)

        // まず Required Insert Count = 1 をエンコード
        // enc = (1 % (2 * 128)) + 1 = 2
        // Indexed dynamic: 0x80 | 0 = 0x80 (relative index 0, T=0)
        let data = [0x02, 0x00, 0x80];
        let DecodeOutput::Decoded(headers) = decoder.decode(&data).expect("test must succeed")
        else {
            panic!("unexpected Blocked");
        };

        assert_eq!(headers.len(), 1);
        assert_eq!(headers[0].name(), b"custom-header");
        assert_eq!(headers[0].value(), b"custom-value");
        // 動的テーブルを参照したので Required Insert Count > 0
        assert_eq!(decoder.last_required_insert_count(), 1);
    }

    #[test]
    fn test_dynamic_decoder_blocking() {
        let mut decoder = DynamicDecoder::new();
        decoder.set_max_table_capacity(4096);
        decoder.set_table_capacity(1024);

        // 動的テーブルが空 (insert_count = 0) のまま
        // Required Insert Count = 1 を要求するエンコード済みデータ
        // enc_insert_count = 2 (Required Insert Count = 1 の場合)
        let data = [0x02, 0x00, 0x80];
        let result = decoder.decode(&data).expect("test must succeed");

        // insert_count (0) < required_insert_count (1) なのでブロック
        assert!(matches!(result, DecodeOutput::Blocked));
    }

    /// RFC 9204 Section 2.2.3: 動的テーブル参照の absolute index が
    /// Required Insert Count 以上の場合は QPACK_DECOMPRESSION_FAILED
    #[test]
    fn test_dynamic_ref_absolute_index_exceeds_ric() {
        let mut decoder = DynamicDecoder::new();
        decoder.set_max_table_capacity(4096);
        decoder.set_table_capacity(1024);

        // 動的テーブルにエントリを 3 つ挿入 (insert_count = 3)
        decoder.insert(b"name0".to_vec(), b"value0".to_vec()); // abs=0
        decoder.insert(b"name1".to_vec(), b"value1".to_vec()); // abs=1
        decoder.insert(b"name2".to_vec(), b"value2".to_vec()); // abs=2

        // Required Insert Count = 2, Base = 3 (Sign=0, DeltaBase=1)
        // → Base > RIC なので relative_index=0 → absolute=2 >= RIC=2 → エラー
        //
        // enc_insert_count = (2 % (2*128)) + 1 = 3
        // DeltaBase = 3 - 2 = 1, Sign = 0
        // Indexed dynamic: relative_index=0, T=0 → 0x80
        let data = [0x03, 0x01, 0x80];
        let result = decoder.decode(&data);
        assert!(
            result.is_err(),
            "absolute index >= RIC should be QPACK_DECOMPRESSION_FAILED"
        );
    }
}
