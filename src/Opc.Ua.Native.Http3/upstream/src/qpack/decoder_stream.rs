//! QPACK デコーダーストリーム (RFC 9204 Section 4.4)
//!
//! デコーダーからエンコーダーへ動的テーブルの同期情報を通知するための命令を処理。
//!
//! ## 命令
//!
//! - Section Acknowledgment (1 prefix)
//! - Stream Cancellation (01 prefix)
//! - Insert Count Increment (00 prefix)

use crate::error::QpackError;

use super::integer;

/// デコーダーストリーム命令
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DecoderInstruction {
    /// セクション確認応答 (Section 4.4.1)
    ///
    /// フィールドセクションの処理完了を通知
    SectionAcknowledgment { stream_id: u64 },
    /// ストリームキャンセル (Section 4.4.2)
    ///
    /// ストリームのリセットまたは読み取り中止を通知
    StreamCancellation { stream_id: u64 },
    /// 挿入カウントインクリメント (Section 4.4.3)
    ///
    /// Known Received Count を増加
    InsertCountIncrement { increment: u64 },
}

/// デコーダーストリーム
///
/// デコーダー側で使用し、エンコーダーへの確認応答命令を生成・送信する。
#[derive(Debug)]
pub struct DecoderStream {
    /// 送信バッファ
    send_buffer: Vec<u8>,
}

impl Default for DecoderStream {
    fn default() -> Self {
        Self::new()
    }
}

impl DecoderStream {
    /// 新しいデコーダーストリームを作成
    pub fn new() -> Self {
        Self {
            send_buffer: Vec::new(),
        }
    }

    /// 単方向ストリームヘッダーを書き込む (RFC 9114 Section 6.2, RFC 9204 Section 4.2)
    ///
    /// stream type 0x03 を送信バッファの先頭に追加する。
    /// Connection::set_decoder_stream_id() から呼び出される。
    pub fn write_stream_type(&mut self) {
        self.send_buffer.push(0x03);
    }

    /// セクション確認応答をエンコード (Section 4.4.1)
    ///
    /// Format: 1xxxxxxx (7-bit prefix integer)
    pub fn encode_section_acknowledgment(&mut self, stream_id: u64) {
        integer::encode_integer_to_vec(&mut self.send_buffer, stream_id, 7, 0x80);
    }

    /// ストリームキャンセルをエンコード (Section 4.4.2)
    ///
    /// Format: 01xxxxxx (6-bit prefix integer)
    pub fn encode_stream_cancellation(&mut self, stream_id: u64) {
        integer::encode_integer_to_vec(&mut self.send_buffer, stream_id, 6, 0x40);
    }

    /// 挿入カウントインクリメントをエンコード (Section 4.4.3)
    ///
    /// Format: 00xxxxxx (6-bit prefix integer)
    pub fn encode_insert_count_increment(&mut self, increment: u64) {
        integer::encode_integer_to_vec(&mut self.send_buffer, increment, 6, 0x00);
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

/// デコーダーストリームレシーバー
///
/// エンコーダー側で使用し、デコーダーからの命令を受信・処理する。
#[derive(Debug)]
pub struct DecoderStreamReceiver {
    /// 受信バッファ
    recv_buffer: Vec<u8>,
    /// Known Received Count (エンコーダーが知っている、デコーダーが受信した挿入カウント)
    known_received_count: u64,
}

impl Default for DecoderStreamReceiver {
    fn default() -> Self {
        Self::new()
    }
}

impl DecoderStreamReceiver {
    /// 新しいデコーダーストリームレシーバーを作成
    pub fn new() -> Self {
        Self {
            recv_buffer: Vec::new(),
            known_received_count: 0,
        }
    }

    /// Known Received Count を取得
    pub fn known_received_count(&self) -> u64 {
        self.known_received_count
    }

    /// データを受信
    pub fn receive(&mut self, data: &[u8]) {
        self.recv_buffer.extend_from_slice(data);
    }

    /// 命令をデコード
    ///
    /// エンコーダー側で呼び出し、デコーダーからの命令を処理する。
    /// `total_insert_count` はエンコーダーが実際に送った動的テーブルの挿入数。
    /// Insert Count Increment が Known Received Count をこの値を超えて進める場合、
    /// QPACK_DECODER_STREAM_ERROR となる (RFC 9204 Section 4.4.3)。
    ///
    /// QPACK ストリームは unframed な命令列であり、QUIC stream は byte stream なので
    /// 命令が分割到着する (RFC 9204 Section 4.2, RFC 9000 Section 2.2)。
    /// BufferTooShort は部分受信を意味するため Ok(None) として返す。
    pub fn process(
        &mut self,
        total_insert_count: u64,
    ) -> Result<Option<DecoderInstruction>, QpackError> {
        if self.recv_buffer.is_empty() {
            return Ok(None);
        }

        let first = self.recv_buffer[0];

        let result = if first & 0x80 != 0 {
            // Section Acknowledgment (1xxxxxxx)
            self.decode_section_acknowledgment()
        } else if first & 0x40 != 0 {
            // Stream Cancellation (01xxxxxx)
            self.decode_stream_cancellation()
        } else {
            // Insert Count Increment (00xxxxxx)
            self.decode_insert_count_increment(total_insert_count)
        };

        // 部分受信は非エラー: 次のチャンク到着を待つ
        match result {
            Err(QpackError::BufferTooShort) => Ok(None),
            other => other,
        }
    }

    /// Section Acknowledgment をデコード
    fn decode_section_acknowledgment(&mut self) -> Result<Option<DecoderInstruction>, QpackError> {
        let (stream_id, consumed) = integer::decode_integer(&self.recv_buffer, 7)?;

        self.recv_buffer.drain(..consumed);

        Ok(Some(DecoderInstruction::SectionAcknowledgment {
            stream_id,
        }))
    }

    /// Stream Cancellation をデコード
    fn decode_stream_cancellation(&mut self) -> Result<Option<DecoderInstruction>, QpackError> {
        let (stream_id, consumed) = integer::decode_integer(&self.recv_buffer, 6)?;

        self.recv_buffer.drain(..consumed);

        Ok(Some(DecoderInstruction::StreamCancellation { stream_id }))
    }

    /// Insert Count Increment をデコード (RFC 9204 Section 4.4.3)
    fn decode_insert_count_increment(
        &mut self,
        total_insert_count: u64,
    ) -> Result<Option<DecoderInstruction>, QpackError> {
        let (increment, consumed) = integer::decode_integer(&self.recv_buffer, 6)?;

        // インクリメントが 0 の場合はエラー (RFC 9204 Section 4.4.3)
        if increment == 0 {
            return Err(QpackError::DecodeFailed);
        }

        // Known Received Count がエンコーダーの送信した挿入数を超えてはならない
        // (RFC 9204 Section 4.4.3)
        let new_known = self.known_received_count + increment;
        if new_known > total_insert_count {
            return Err(QpackError::DecodeFailed);
        }

        self.recv_buffer.drain(..consumed);
        self.known_received_count = new_known;

        Ok(Some(DecoderInstruction::InsertCountIncrement { increment }))
    }

    /// 受信データを取得
    pub fn buffer(&self) -> &[u8] {
        &self.recv_buffer
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encode_section_acknowledgment() {
        let mut stream = DecoderStream::new();
        stream.encode_section_acknowledgment(4);

        // 1xxxxxxx with 7-bit prefix, value 4
        // 0x80 | 4 = 0x84
        assert_eq!(stream.get_data(), &[0x84]);
    }

    #[test]
    fn test_encode_section_acknowledgment_large() {
        let mut stream = DecoderStream::new();
        // 127 を超える値
        stream.encode_section_acknowledgment(200);

        // 0x80 | 127 = 0xff, then 200 - 127 = 73
        assert_eq!(stream.get_data(), &[0xff, 73]);
    }

    #[test]
    fn test_encode_stream_cancellation() {
        let mut stream = DecoderStream::new();
        stream.encode_stream_cancellation(8);

        // 01xxxxxx with 6-bit prefix, value 8
        // 0x40 | 8 = 0x48
        assert_eq!(stream.get_data(), &[0x48]);
    }

    #[test]
    fn test_encode_insert_count_increment() {
        let mut stream = DecoderStream::new();
        stream.encode_insert_count_increment(3);

        // 00xxxxxx with 6-bit prefix, value 3
        // 0x00 | 3 = 0x03
        assert_eq!(stream.get_data(), &[0x03]);
    }

    #[test]
    fn test_decode_insert_count_increment_zero() {
        let mut receiver = DecoderStreamReceiver::new();
        receiver.receive(&[0x00]);

        // increment が 0 の場合はエラー
        let result = receiver.process(10);
        assert!(result.is_err());
    }

    #[test]
    fn test_decode_insert_count_increment_exceeds_total() {
        let mut receiver = DecoderStreamReceiver::new();
        // increment = 5 だが total_insert_count = 3
        receiver.receive(&[0x05]);

        let result = receiver.process(3);
        assert!(result.is_err());
        // known_received_count は更新されていないこと
        assert_eq!(receiver.known_received_count(), 0);
    }

    #[test]
    fn test_decode_insert_count_increment_valid() {
        let mut receiver = DecoderStreamReceiver::new();
        receiver.receive(&[0x03]);

        let result = receiver.process(5).expect("test must succeed");
        assert_eq!(
            result,
            Some(DecoderInstruction::InsertCountIncrement { increment: 3 })
        );
        assert_eq!(receiver.known_received_count(), 3);
    }
}
