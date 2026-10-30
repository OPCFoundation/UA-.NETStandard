//! QPACK エンコーダーストリーム (RFC 9204 Section 4.3)
//!
//! エンコーダーからデコーダーへ動的テーブルの更新を通知するための命令を処理。
//!
//! ## 命令
//!
//! - Set Dynamic Table Capacity (001 prefix)
//! - Insert with Name Reference (1x prefix)
//! - Insert with Literal Name (01 prefix)
//! - Duplicate (000 prefix)

use crate::error::QpackError;

use super::dynamic_table::DynamicTable;
use super::huffman;
use super::integer;
use super::table::STATIC_TABLE;

/// エンコーダーストリーム命令
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum EncoderInstruction {
    /// 動的テーブル容量を設定 (Section 4.3.1)
    SetDynamicTableCapacity { capacity: u64 },
    /// 名前参照で挿入 (Section 4.3.2)
    InsertWithNameReference {
        is_static: bool,
        name_index: u64,
        value: Vec<u8>,
    },
    /// リテラル名で挿入 (Section 4.3.3)
    InsertWithLiteralName { name: Vec<u8>, value: Vec<u8> },
    /// 複製 (Section 4.3.4)
    Duplicate { relative_index: u64 },
}

/// エンコーダーストリーム
///
/// エンコーダー側で使用し、動的テーブルの更新命令を生成・送信する。
#[derive(Debug)]
pub struct EncoderStream {
    /// 送信バッファ
    send_buffer: Vec<u8>,
    /// 最大テーブル容量 (ピアの SETTINGS から)
    max_table_capacity: u64,
}

impl Default for EncoderStream {
    fn default() -> Self {
        Self::new()
    }
}

impl EncoderStream {
    /// 新しいエンコーダーストリームを作成
    pub fn new() -> Self {
        Self {
            send_buffer: Vec::new(),
            max_table_capacity: 0,
        }
    }

    /// 単方向ストリームヘッダーを書き込む (RFC 9114 Section 6.2, RFC 9204 Section 4.2)
    ///
    /// stream type 0x02 を送信バッファの先頭に追加する。
    /// Connection::set_encoder_stream_id() から呼び出される。
    pub fn write_stream_type(&mut self) {
        self.send_buffer.push(0x02);
    }

    /// 最大テーブル容量を設定
    pub fn set_max_table_capacity(&mut self, capacity: u64) {
        self.max_table_capacity = capacity;
    }

    /// 動的テーブル容量を設定する命令をエンコード (RFC 9204 Section 4.3.1)
    ///
    /// Format: 001xxxxx (5-bit prefix integer)
    ///
    /// # エラー
    ///
    /// - `DynamicTableDisabled`: 最大テーブル容量が 0 の場合 (RFC 9204 Section 3.2.3)
    /// - `CapacityExceeded`: 指定容量が最大テーブル容量を超える場合 (RFC 9204 Section 3.2.3)
    pub fn encode_set_capacity(&mut self, capacity: u64) -> Result<(), QpackError> {
        if self.max_table_capacity == 0 {
            return Err(QpackError::DynamicTableDisabled);
        }
        if capacity > self.max_table_capacity {
            return Err(QpackError::CapacityExceeded);
        }
        integer::encode_integer_to_vec(&mut self.send_buffer, capacity, 5, 0x20);
        Ok(())
    }

    /// 名前参照で挿入する命令をエンコード (RFC 9204 Section 4.3.2)
    ///
    /// Format: 1T (6-bit prefix integer for index) + value string
    ///
    /// # エラー
    ///
    /// - `DynamicTableDisabled`: 最大テーブル容量が 0 の場合 (RFC 9204 Section 3.2.3)
    pub fn encode_insert_with_name_ref(
        &mut self,
        is_static: bool,
        name_index: u64,
        value: &[u8],
    ) -> Result<(), QpackError> {
        if self.max_table_capacity == 0 {
            return Err(QpackError::DynamicTableDisabled);
        }
        let prefix = if is_static { 0xc0 } else { 0x80 };
        integer::encode_integer_to_vec(&mut self.send_buffer, name_index, 6, prefix);
        encode_string(&mut self.send_buffer, value);
        Ok(())
    }

    /// リテラル名で挿入する命令をエンコード (RFC 9204 Section 4.3.3)
    ///
    /// Format: 01 (6-bit prefix string for name) + (8-bit prefix string for value)
    ///
    /// # エラー
    ///
    /// - `DynamicTableDisabled`: 最大テーブル容量が 0 の場合 (RFC 9204 Section 3.2.3)
    pub fn encode_insert_with_literal_name(
        &mut self,
        name: &[u8],
        value: &[u8],
    ) -> Result<(), QpackError> {
        if self.max_table_capacity == 0 {
            return Err(QpackError::DynamicTableDisabled);
        }
        // Name string with 5-bit prefix, 0x40 base
        encode_string_with_prefix(&mut self.send_buffer, name, 5, 0x40);
        // Value string with 7-bit prefix
        encode_string(&mut self.send_buffer, value);
        Ok(())
    }

    /// 複製する命令をエンコード (RFC 9204 Section 4.3.4)
    ///
    /// Format: 000xxxxx (5-bit prefix integer)
    ///
    /// # エラー
    ///
    /// - `DynamicTableDisabled`: 最大テーブル容量が 0 の場合 (RFC 9204 Section 3.2.3)
    pub fn encode_duplicate(&mut self, relative_index: u64) -> Result<(), QpackError> {
        if self.max_table_capacity == 0 {
            return Err(QpackError::DynamicTableDisabled);
        }
        integer::encode_integer_to_vec(&mut self.send_buffer, relative_index, 5, 0x00);
        Ok(())
    }

    /// 送信データを取得
    pub fn get_data(&self) -> &[u8] {
        &self.send_buffer
    }

    /// 送信データを消費
    pub fn consume_data(&mut self, len: usize) {
        if len >= self.send_buffer.len() {
            self.send_buffer.clear();
        } else {
            self.send_buffer.drain(..len);
        }
    }

    /// 送信待ちデータがあるか
    pub fn has_pending(&self) -> bool {
        !self.send_buffer.is_empty()
    }
}

/// エンコーダーストリームレシーバー
///
/// デコーダー側で使用し、エンコーダーからの命令を受信・処理する。
#[derive(Debug)]
pub struct EncoderStreamReceiver {
    /// 受信バッファ
    recv_buffer: Vec<u8>,
    /// 最大テーブル容量 (ローカルの SETTINGS)
    max_table_capacity: u64,
}

impl Default for EncoderStreamReceiver {
    fn default() -> Self {
        Self::new()
    }
}

impl EncoderStreamReceiver {
    /// 新しいエンコーダーストリームレシーバーを作成
    pub fn new() -> Self {
        Self {
            recv_buffer: Vec::new(),
            max_table_capacity: 0,
        }
    }

    /// 最大テーブル容量を設定
    pub fn set_max_table_capacity(&mut self, capacity: u64) {
        self.max_table_capacity = capacity;
    }

    /// データを受信
    pub fn receive(&mut self, data: &[u8]) {
        self.recv_buffer.extend_from_slice(data);
    }

    /// 命令をデコードして動的テーブルを更新
    ///
    /// QPACK ストリームは unframed な命令列であり、QUIC stream は byte stream なので
    /// 命令が分割到着する (RFC 9204 Section 4.2, RFC 9000 Section 2.2)。
    /// BufferTooShort は部分受信を意味するため Ok(None) として返す。
    pub fn process(
        &mut self,
        table: &mut DynamicTable,
    ) -> Result<Option<EncoderInstruction>, QpackError> {
        if self.recv_buffer.is_empty() {
            return Ok(None);
        }

        let first = self.recv_buffer[0];

        let result = if first & 0x80 != 0 {
            // Insert with Name Reference (1xxxxxxx)
            self.decode_insert_with_name_ref(table)
        } else if first & 0x40 != 0 {
            // Insert with Literal Name (01xxxxxx)
            self.decode_insert_with_literal_name(table)
        } else if first & 0x20 != 0 {
            // Set Dynamic Table Capacity (001xxxxx)
            self.decode_set_capacity(table)
        } else {
            // Duplicate (000xxxxx)
            self.decode_duplicate(table)
        };

        // 部分受信は非エラー: 次のチャンク到着を待つ
        match result {
            Err(QpackError::BufferTooShort) => Ok(None),
            other => other,
        }
    }

    /// Set Dynamic Table Capacity をデコード
    fn decode_set_capacity(
        &mut self,
        table: &mut DynamicTable,
    ) -> Result<Option<EncoderInstruction>, QpackError> {
        let (capacity, consumed) = integer::decode_integer(&self.recv_buffer, 5)?;

        // 最大容量を超えていないかチェック
        if capacity > self.max_table_capacity {
            return Err(QpackError::DecodeFailed);
        }

        self.recv_buffer.drain(..consumed);
        table.set_capacity(capacity);

        Ok(Some(EncoderInstruction::SetDynamicTableCapacity {
            capacity,
        }))
    }

    /// Insert with Name Reference をデコード
    fn decode_insert_with_name_ref(
        &mut self,
        table: &mut DynamicTable,
    ) -> Result<Option<EncoderInstruction>, QpackError> {
        let is_static = (self.recv_buffer[0] & 0x40) != 0;
        let (name_index, mut consumed) = integer::decode_integer(&self.recv_buffer, 6)?;

        // Value をデコード
        let (value, value_len) = decode_string(&self.recv_buffer[consumed..])?;
        consumed += value_len;

        // テーブル参照・テーブル操作を先に行い、成功後に drain
        let name = if is_static {
            STATIC_TABLE
                .get(name_index as usize)
                .ok_or(QpackError::InvalidIndex(name_index))?
                .name()
                .to_vec()
        } else {
            table
                .get_by_relative_index_encoder(name_index)
                .ok_or(QpackError::InvalidIndex(name_index))?
                .name
                .clone()
        };

        // insert 失敗時もバッファを drain しないと同一バイト列が再処理され
        // infinite loop になる (RFC 9204 Section 4.3.2)
        if table.insert(name, value.clone()).is_none() {
            self.recv_buffer.drain(..consumed);
            return Err(QpackError::DecodeFailed);
        }

        self.recv_buffer.drain(..consumed);

        Ok(Some(EncoderInstruction::InsertWithNameReference {
            is_static,
            name_index,
            value,
        }))
    }

    /// Insert with Literal Name をデコード
    fn decode_insert_with_literal_name(
        &mut self,
        table: &mut DynamicTable,
    ) -> Result<Option<EncoderInstruction>, QpackError> {
        // Name をデコード (5-bit prefix)
        let (name, mut consumed) = decode_string_with_prefix(&self.recv_buffer, 5)?;

        // Value をデコード (7-bit prefix)
        let (value, value_len) = decode_string(&self.recv_buffer[consumed..])?;
        consumed += value_len;

        // テーブル操作を先に行い、成功後に drain
        // insert 失敗時もバッファを drain しないと同一バイト列が再処理され
        // infinite loop になる (RFC 9204 Section 4.3.3)
        if table.insert(name.clone(), value.clone()).is_none() {
            self.recv_buffer.drain(..consumed);
            return Err(QpackError::DecodeFailed);
        }

        self.recv_buffer.drain(..consumed);

        Ok(Some(EncoderInstruction::InsertWithLiteralName {
            name,
            value,
        }))
    }

    /// Duplicate をデコード
    fn decode_duplicate(
        &mut self,
        table: &mut DynamicTable,
    ) -> Result<Option<EncoderInstruction>, QpackError> {
        let (relative_index, consumed) = integer::decode_integer(&self.recv_buffer, 5)?;

        // テーブル操作を先に行い、成功後に drain
        // duplicate 失敗時もバッファを drain しないと同一バイト列が再処理され
        // infinite loop になる (RFC 9204 Section 4.3.4)
        if table.duplicate(relative_index).is_none() {
            self.recv_buffer.drain(..consumed);
            return Err(QpackError::InvalidIndex(relative_index));
        }

        self.recv_buffer.drain(..consumed);

        Ok(Some(EncoderInstruction::Duplicate { relative_index }))
    }

    /// 受信データを取得
    pub fn buffer(&self) -> &[u8] {
        &self.recv_buffer
    }
}

// ヘルパー関数

/// 文字列をエンコード (7-bit prefix)
fn encode_string(buf: &mut Vec<u8>, data: &[u8]) {
    encode_string_with_prefix(buf, data, 7, 0x00);
}

/// 文字列をエンコード (指定 prefix)
///
/// H ビットは prefix ビットの直上に配置される:
/// - 7-bit prefix: H は bit 7 (0x80)
/// - 6-bit prefix: H は bit 6 (0x40)
/// - 5-bit prefix: H は bit 5 (0x20)
fn encode_string_with_prefix(buf: &mut Vec<u8>, data: &[u8], prefix_bits: u8, base: u8) {
    let huffman_len = huffman::encoded_len(data);
    if huffman_len < data.len() {
        // ハフマン符号化を使用
        // H ビットは prefix ビットの直上
        let huffman_flag = 1u8 << prefix_bits;
        integer::encode_integer_to_vec(buf, huffman_len as u64, prefix_bits, base | huffman_flag);
        let start = buf.len();
        buf.resize(start + huffman_len, 0);
        huffman::encode(&mut buf[start..], data);
    } else {
        // リテラル
        integer::encode_integer_to_vec(buf, data.len() as u64, prefix_bits, base);
        buf.extend_from_slice(data);
    }
}

/// 文字列をデコード (7-bit prefix)
fn decode_string(data: &[u8]) -> Result<(Vec<u8>, usize), QpackError> {
    decode_string_with_prefix(data, 7)
}

/// 文字列をデコード (指定 prefix)
///
/// H ビットは prefix ビットの直上に配置される:
/// - 7-bit prefix: H は bit 7 (0x80)
/// - 6-bit prefix: H は bit 6 (0x40)
/// - 5-bit prefix: H は bit 5 (0x20)
fn decode_string_with_prefix(data: &[u8], prefix_bits: u8) -> Result<(Vec<u8>, usize), QpackError> {
    if data.is_empty() {
        return Err(QpackError::BufferTooShort);
    }

    // H ビットは prefix ビットの直上
    let huffman_flag = 1u8 << prefix_bits;
    let is_huffman = (data[0] & huffman_flag) != 0;
    let (length, prefix_len) = integer::decode_integer(data, prefix_bits)?;

    let total_len = prefix_len + length as usize;
    if data.len() < total_len {
        return Err(QpackError::BufferTooShort);
    }

    let encoded = &data[prefix_len..total_len];

    let decoded = if is_huffman {
        huffman::decode(encoded)?
    } else {
        encoded.to_vec()
    };

    Ok((decoded, total_len))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encode_set_capacity() {
        let mut stream = EncoderStream::new();
        stream.set_max_table_capacity(4096);
        stream.encode_set_capacity(220).expect("test must succeed");

        // 001xxxxx with 5-bit prefix
        // 220 = 31 + 189, 189 = 0xbd
        // 0x3f (001_11111) + 0xbd (10111101) + 0x01
        assert_eq!(stream.get_data(), &[0x3f, 0xbd, 0x01]);
    }

    #[test]
    fn test_encode_set_capacity_exceeds_max() {
        let mut stream = EncoderStream::new();
        stream.set_max_table_capacity(100);
        assert_eq!(
            stream.encode_set_capacity(200),
            Err(QpackError::CapacityExceeded)
        );
        assert!(stream.get_data().is_empty());
    }

    #[test]
    fn test_encode_set_capacity_when_disabled() {
        let mut stream = EncoderStream::new();
        // max_table_capacity はデフォルト 0
        assert_eq!(
            stream.encode_set_capacity(100),
            Err(QpackError::DynamicTableDisabled)
        );
        assert!(stream.get_data().is_empty());
    }

    #[test]
    fn test_encode_insert_with_name_ref_static() {
        let mut stream = EncoderStream::new();
        stream.set_max_table_capacity(4096);
        // Static table index 0 (:authority), value "www.example.com"
        stream
            .encode_insert_with_name_ref(true, 0, b"www.example.com")
            .expect("test must succeed");

        let data = stream.get_data();
        // 0xc0 = 11000000 (static, index 0)
        assert_eq!(data[0], 0xc0);
    }

    #[test]
    fn test_encode_insert_when_disabled() {
        let mut stream = EncoderStream::new();
        // max_table_capacity はデフォルト 0
        assert_eq!(
            stream.encode_insert_with_name_ref(true, 0, b"value"),
            Err(QpackError::DynamicTableDisabled)
        );
        assert_eq!(
            stream.encode_insert_with_literal_name(b"name", b"value"),
            Err(QpackError::DynamicTableDisabled)
        );
        assert_eq!(
            stream.encode_duplicate(0),
            Err(QpackError::DynamicTableDisabled)
        );
        assert!(stream.get_data().is_empty());
    }

    #[test]
    fn test_encode_insert_with_literal_name() {
        let mut stream = EncoderStream::new();
        stream.set_max_table_capacity(4096);
        stream
            .encode_insert_with_literal_name(b"custom-key", b"custom-value")
            .expect("test must succeed");

        let data = stream.get_data();
        // 01xxxxxx prefix
        assert_eq!(data[0] & 0xc0, 0x40);
    }

    #[test]
    fn test_encode_duplicate() {
        let mut stream = EncoderStream::new();
        stream.set_max_table_capacity(4096);
        stream.encode_duplicate(5).expect("test must succeed");

        // 000xxxxx with 5-bit prefix
        assert_eq!(stream.get_data(), &[0x05]);
    }

    #[test]
    fn test_decode_insert_with_name_ref_static() {
        let mut table = DynamicTable::with_capacity(4096);
        let mut receiver = EncoderStreamReceiver::new();
        receiver.set_max_table_capacity(4096);

        // Insert with static table ref (index 0 = :authority)
        // 0xc0 = static, index 0
        // 0x0f = length 15 (not huffman)
        // "www.example.com"
        let mut data = vec![0xc0, 0x0f];
        data.extend_from_slice(b"www.example.com");
        receiver.receive(&data);

        let instruction = receiver
            .process(&mut table)
            .expect("test must succeed")
            .expect("test must succeed");
        match instruction {
            EncoderInstruction::InsertWithNameReference {
                is_static,
                name_index,
                value,
            } => {
                assert!(is_static);
                assert_eq!(name_index, 0);
                assert_eq!(value, b"www.example.com");
            }
            _ => panic!("Unexpected instruction"),
        }

        // テーブルにエントリが追加されている
        assert_eq!(table.len(), 1);
        let entry = table.get_by_absolute_index(0).expect("test must succeed");
        assert_eq!(entry.name, b":authority");
        assert_eq!(entry.value, b"www.example.com");
    }
}
