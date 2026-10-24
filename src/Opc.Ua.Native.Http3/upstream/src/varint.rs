//! QUIC 可変長整数 (RFC 9000 Section 16)
//!
//! QUIC の可変長整数は 1, 2, 4, 8 バイトでエンコードされ、最大 2^62-1 まで表現可能。
//! 値域 `0..=2^62 - 1` を型で表現するため [`VarInt`] 構造体を提供し、
//! エンコード / デコード関数は引数 / 戻り値に [`VarInt`] を取り扱う。

use core::fmt;

/// QUIC VarInt (RFC 9000 Section 16)
///
/// `0..=2^62 - 1` の整数を表現する。エンコード長は値域に応じて 1 / 2 / 4 / 8 バイト。
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct VarInt(u64);

/// VarInt 構築時のエラー
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum VarIntError {
    /// 値が VarInt の最大値 (`2^62 - 1`) を超えている
    OutOfRange {
        /// 範囲外と判定された元の値
        value: u64,
    },
}

impl fmt::Display for VarIntError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::OutOfRange { value } => {
                write!(
                    f,
                    "varint value {value} exceeds the maximum allowed by RFC 9000 Section 16 (2^62 - 1)"
                )
            }
        }
    }
}

impl std::error::Error for VarIntError {}

impl VarInt {
    /// VarInt が表現可能な最大値 (`2^62 - 1`)
    ///
    /// RFC 9000 Section 16 Table 4 の 8-byte エンコード上限。
    pub const MAX: Self = Self((1u64 << 62) - 1);

    /// VarInt の最小値 (`0`)
    pub const ZERO: Self = Self(0);

    /// ランタイム値から検査つきで構築する
    ///
    /// 値が [`VarInt::MAX`] を超える場合は [`VarIntError::OutOfRange`] を返す。
    /// `const fn` のため `const VAL: Result<VarInt, _> = VarInt::new(64);` のように
    /// `const` 宣言でも使える。
    pub const fn new(value: u64) -> Result<Self, VarIntError> {
        if value > Self::MAX.get() {
            Err(VarIntError::OutOfRange { value })
        } else {
            Ok(Self(value))
        }
    }

    /// 静的リテラル定数から検査つきで構築する (`const fn`)
    ///
    /// 用途は `const` / `static` 宣言で「リテラルが VarInt 範囲内であること」を
    /// コンパイル時に検証することに限られる。範囲外なら `const` 評価時の panic と
    /// なりコンパイルエラーになる。
    ///
    /// ランタイム値には [`VarInt::new`] を使うこと。本関数を `let x = ...;` の
    /// ような実行時文脈で呼ぶと、範囲外時にランタイム panic になる。
    ///
    /// `#[track_caller]` はランタイム panic 時の呼び出し位置を保持するために
    /// 付けている (const 評価時の panic 表示には影響しない)。
    ///
    /// VarInt 値域外 (`>= 2^62`) のリテラルはコンパイル時に弾かれる:
    ///
    /// ```compile_fail
    /// use shiguredo_http3::VarInt;
    /// const _BAD: VarInt = VarInt::from_static(1u64 << 62);
    /// ```
    #[track_caller]
    pub const fn from_static(value: u64) -> Self {
        assert!(value <= Self::MAX.get(), "VarInt value must be <= 2^62 - 1");
        Self(value)
    }

    /// 内部値を取得する
    pub const fn get(self) -> u64 {
        self.0
    }

    /// wire 表現のエンコード長 (1 / 2 / 4 / 8 バイト) を返す
    ///
    /// 値域と prefix の対応 (RFC 9000 Section 16 Table 4):
    ///   - `0 ..= 2^6 - 1` → 1 バイト (00 prefix)
    ///   - `2^6 ..= 2^14 - 1` → 2 バイト (01 prefix)
    ///   - `2^14 ..= 2^30 - 1` → 4 バイト (10 prefix)
    ///   - `2^30 ..= 2^62 - 1` → 8 バイト (11 prefix)
    pub const fn encoded_len(self) -> usize {
        if self.0 < 64 {
            1
        } else if self.0 < 16_384 {
            2
        } else if self.0 < 1_073_741_824 {
            4
        } else {
            8
        }
    }

    /// 検証済みの `u64` から検査をスキップして構築する (crate 内部専用)
    ///
    /// **release ビルドでは `debug_assert!` が除去される**ため、呼び出し側が
    /// 値の正当性 (`value <= 2^62 - 1`) を構造的に保証できていることが必須。
    /// 検証されていない `u64` を渡すと crate 全体の `VarInt` 不変条件を破壊し、
    /// `encoded_len` の `unreachable` 仮定や `Display` 出力で予期せぬ挙動を起こしうる。
    ///
    /// 現状の利用は [`decode`] のみで、wire の最上位 2 ビットがエンコード長を示す
    /// 構造により mask 後の値が必ず `2^62 - 1` 以下となることを利用している
    /// (RFC 9000 Section 16 Table 4)。
    pub(crate) const fn from_validated_parts_internal(value: u64) -> Self {
        debug_assert!(
            value <= Self::MAX.get(),
            "VarInt invariant violated: value exceeds 2^62 - 1"
        );
        Self(value)
    }
}

impl fmt::Display for VarInt {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Display::fmt(&self.0, f)
    }
}

impl From<u8> for VarInt {
    fn from(value: u8) -> Self {
        Self(u64::from(value))
    }
}

impl From<u16> for VarInt {
    fn from(value: u16) -> Self {
        Self(u64::from(value))
    }
}

impl From<u32> for VarInt {
    fn from(value: u32) -> Self {
        Self(u64::from(value))
    }
}

impl TryFrom<u64> for VarInt {
    type Error = VarIntError;

    fn try_from(value: u64) -> Result<Self, Self::Error> {
        Self::new(value)
    }
}

impl TryFrom<usize> for VarInt {
    type Error = VarIntError;

    fn try_from(value: usize) -> Result<Self, Self::Error> {
        // 32 / 64 bit いずれの環境でも `usize` は `u64` に widen 可能。
        // (Rust 公式サポートターゲットは pointer width 16/32/64 で、いずれも u64 に収まる)
        Self::new(value as u64)
    }
}

/// 可変長整数デコードエラー
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DecodeError {
    /// バッファが不足している
    BufferTooShort,
}

/// 可変長整数エンコードエラー
///
/// 値域は [`VarInt`] が保証するため、ここでは出力バッファ不足だけを表現する。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EncodeError {
    /// バッファが不足している
    BufferTooShort,
}

/// 可変長整数を `Vec` に追記する
///
/// 必要なバイト数 (`value.encoded_len()`) を確保してから [`encode`] を呼ぶため、
/// `BufferTooShort` は発生しない。`Vec::resize` のアロケーション失敗
/// (OS のメモリ枯渇) 以外で panic することはない。
pub fn encode_into_vec(buf: &mut Vec<u8>, value: VarInt) {
    let len = value.encoded_len();
    let start = buf.len();
    buf.resize(start + len, 0);
    encode(&mut buf[start..], value).expect("buffer is correctly sized");
}

/// 可変長整数をエンコードする
///
/// 成功時はエンコードしたバイト数を返す。
pub fn encode(buf: &mut [u8], value: VarInt) -> Result<usize, EncodeError> {
    let len = value.encoded_len();
    if buf.len() < len {
        return Err(EncodeError::BufferTooShort);
    }

    let raw = value.get();
    match len {
        1 => {
            buf[0] = raw as u8;
        }
        2 => {
            let v = (raw as u16) | 0x4000;
            buf[..2].copy_from_slice(&v.to_be_bytes());
        }
        4 => {
            let v = (raw as u32) | 0x8000_0000;
            buf[..4].copy_from_slice(&v.to_be_bytes());
        }
        8 => {
            let v = raw | 0xc000_0000_0000_0000;
            buf[..8].copy_from_slice(&v.to_be_bytes());
        }
        _ => unreachable!(),
    }

    Ok(len)
}

/// 可変長整数をデコードする
///
/// 成功時は `(VarInt, 消費バイト数)` を返す。
pub fn decode(buf: &[u8]) -> Result<(VarInt, usize), DecodeError> {
    if buf.is_empty() {
        return Err(DecodeError::BufferTooShort);
    }

    let prefix = buf[0] >> 6;
    let len = 1 << prefix;

    if buf.len() < len {
        return Err(DecodeError::BufferTooShort);
    }

    let value = match len {
        1 => u64::from(buf[0] & 0x3f),
        2 => {
            let mut bytes = [0u8; 2];
            bytes.copy_from_slice(&buf[..2]);
            u64::from(u16::from_be_bytes(bytes) & 0x3fff)
        }
        4 => {
            let mut bytes = [0u8; 4];
            bytes.copy_from_slice(&buf[..4]);
            u64::from(u32::from_be_bytes(bytes) & 0x3fff_ffff)
        }
        8 => {
            let mut bytes = [0u8; 8];
            bytes.copy_from_slice(&buf[..8]);
            u64::from_be_bytes(bytes) & 0x3fff_ffff_ffff_ffff
        }
        _ => unreachable!(),
    };

    // wire 表現の最上位 2 ビットがエンコード長を示すため、組み立て後の値は
    // 必ず 2^62 - 1 以下になる (RFC 9000 Section 16 Table 4)。
    Ok((VarInt::from_validated_parts_internal(value), len))
}

/// バッファの先頭バイトから可変長整数のバイト長を取得する
///
/// バッファが空の場合は `None` を返す。
#[inline]
pub fn peek_len(buf: &[u8]) -> Option<usize> {
    buf.first().map(|&b| 1 << (b >> 6))
}

#[cfg(test)]
mod tests {
    use super::*;

    // 値域・ラウンドトリップ・境界・From/TryFrom のラウンドトリップは
    // `pbt/tests/prop_varint.rs` 側で PBT 化している (CLAUDE.md: PBT で実現できる
    // ものを単体テストで書かない)。
    // 本モジュールには PBT で表現しにくいケース (const 文脈の正常系、エラー詳細の
    // 文面検査、`from_validated_parts_internal` の境界、`u64::MAX` のエラー) のみ残す。

    #[test]
    fn test_new_rejects_out_of_range() {
        // VarInt::MAX + 1 と u64::MAX が共に同種のエラーを返すこと
        assert_eq!(
            VarInt::new(VarInt::MAX.get() + 1),
            Err(VarIntError::OutOfRange {
                value: VarInt::MAX.get() + 1,
            })
        );
        assert_eq!(
            VarInt::new(u64::MAX),
            Err(VarIntError::OutOfRange { value: u64::MAX })
        );
    }

    #[test]
    fn test_from_static_const_context() {
        // const 宣言で `from_static` が使えること (リテラルが範囲内ならコンパイル成功)
        const ZERO: VarInt = VarInt::from_static(0);
        const ONE_BYTE_BOUNDARY: VarInt = VarInt::from_static(63);
        const TWO_BYTE_BOUNDARY: VarInt = VarInt::from_static(16_383);
        const FOUR_BYTE_BOUNDARY: VarInt = VarInt::from_static(1_073_741_823);
        const MAX: VarInt = VarInt::from_static((1u64 << 62) - 1);
        assert_eq!(ZERO, VarInt::ZERO);
        assert_eq!(ONE_BYTE_BOUNDARY.encoded_len(), 1);
        assert_eq!(TWO_BYTE_BOUNDARY.encoded_len(), 2);
        assert_eq!(FOUR_BYTE_BOUNDARY.encoded_len(), 4);
        assert_eq!(MAX, VarInt::MAX);
    }

    #[test]
    fn test_error_display_contains_rfc_reference() {
        let err = VarIntError::OutOfRange {
            value: VarInt::MAX.get() + 1,
        };
        let s = format!("{err}");
        assert!(s.contains(&format!("{}", VarInt::MAX.get() + 1)));
        assert!(s.contains("RFC 9000"));
        assert!(s.contains("exceeds"));
    }

    #[test]
    fn test_try_from_u64_max_is_error() {
        // PBT は u64 全域をサンプリングするが、u64::MAX 境界は明示的に固定する
        assert!(VarInt::try_from(u64::MAX).is_err());
        assert!(VarInt::try_from(VarInt::MAX.get()).is_ok());
    }

    #[cfg(target_pointer_width = "64")]
    #[test]
    fn test_try_from_usize_rejects_out_of_range_on_64bit() {
        // 64bit 環境でのみ意味のあるテスト (32bit usize は VarInt::MAX を超えられない)
        let too_large = (VarInt::MAX.get() + 1) as usize;
        assert!(VarInt::try_from(too_large).is_err());
    }

    #[test]
    fn test_from_validated_parts_internal_boundaries() {
        // crate 内部からのみ呼ばれるが、境界値で正しく組み立てられることを確認
        assert_eq!(VarInt::from_validated_parts_internal(0).get(), 0);
        assert_eq!(
            VarInt::from_validated_parts_internal(VarInt::MAX.get()),
            VarInt::MAX,
        );
    }

    #[test]
    fn test_decode_buffer_too_short() {
        assert_eq!(decode(&[]), Err(DecodeError::BufferTooShort));
        assert_eq!(decode(&[0x40]), Err(DecodeError::BufferTooShort));
    }

    // `encode` の BufferTooShort は `pbt/tests/prop_varint.rs::prop_short_buffer_returns_buffer_too_short`
    // で網羅検査する (CLAUDE.md: PBT で実現できるものは PBT で書く)。
}
