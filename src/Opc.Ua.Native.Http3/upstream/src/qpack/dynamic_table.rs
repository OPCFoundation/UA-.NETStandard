//! QPACK 動的テーブル (RFC 9204 Section 3.2)
//!
//! HTTP/3 で使用される QPACK の動的テーブルを提供。
//!
//! ## 機能
//!
//! - エントリの挿入と削除 (FIFO 順序)
//! - 容量制限とエビクション
//! - 絶対インデックスと相対インデックスのサポート
//!
//! ## エントリサイズ
//!
//! RFC 9204 Section 3.2.1 に基づき、エントリサイズは:
//! `32 + name.len() + value.len()`

use std::collections::VecDeque;

/// エントリオーバーヘッド (RFC 9204 Section 3.2.1)
const ENTRY_OVERHEAD: u64 = 32;

/// 動的テーブルエントリ
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DynamicEntry {
    /// ヘッダー名
    pub name: Vec<u8>,
    /// ヘッダー値
    pub value: Vec<u8>,
    /// 絶対インデックス (挿入時に割り当て)
    pub absolute_index: u64,
}

impl DynamicEntry {
    /// 新しいエントリを作成
    pub fn new(name: Vec<u8>, value: Vec<u8>, absolute_index: u64) -> Self {
        Self {
            name,
            value,
            absolute_index,
        }
    }

    /// エントリサイズを計算 (RFC 9204 Section 3.2.1)
    ///
    /// サイズ = 32 + name.len() + value.len()
    #[inline]
    pub fn size(&self) -> u64 {
        ENTRY_OVERHEAD + self.name.len() as u64 + self.value.len() as u64
    }
}

/// 動的テーブル (RFC 9204 Section 3.2)
///
/// FIFO 順序でエントリを管理するリングバッファ。
/// 新しいエントリは先頭に挿入され、古いエントリは末尾から削除される。
#[derive(Debug)]
pub struct DynamicTable {
    /// エントリ (VecDeque: 先頭が最新、末尾が最古)
    entries: VecDeque<DynamicEntry>,
    /// 最大容量 (バイト)
    max_capacity: u64,
    /// 現在のサイズ (バイト)
    current_size: u64,
    /// 挿入カウント (次の絶対インデックス)
    insert_count: u64,
    /// 削除されたエントリ数 (dropped count)
    dropped_count: u64,
    /// eviction 制限 (この値未満の absolute_index を持つエントリは evict 不可)
    /// (RFC 9204 Section 2.1.1)
    ///
    /// エンコーダーが設定する。未 ack のフィールドセクションから参照されている
    /// エントリの eviction を防ぐ。
    eviction_limit: u64,
}

impl Default for DynamicTable {
    fn default() -> Self {
        Self::new()
    }
}

impl DynamicTable {
    /// 新しい動的テーブルを作成 (容量 0)
    pub fn new() -> Self {
        Self {
            entries: VecDeque::new(),
            max_capacity: 0,
            current_size: 0,
            insert_count: 0,
            dropped_count: 0,
            eviction_limit: 0,
        }
    }

    /// 指定された容量で動的テーブルを作成
    pub fn with_capacity(capacity: u64) -> Self {
        Self {
            entries: VecDeque::new(),
            max_capacity: capacity,
            current_size: 0,
            insert_count: 0,
            dropped_count: 0,
            eviction_limit: 0,
        }
    }

    /// 最大容量を取得
    #[inline]
    pub fn max_capacity(&self) -> u64 {
        self.max_capacity
    }

    /// 現在のサイズを取得
    #[inline]
    pub fn current_size(&self) -> u64 {
        self.current_size
    }

    /// 挿入カウントを取得 (次の絶対インデックス)
    #[inline]
    pub fn insert_count(&self) -> u64 {
        self.insert_count
    }

    /// 削除されたエントリ数を取得
    #[inline]
    pub fn dropped_count(&self) -> u64 {
        self.dropped_count
    }

    /// エントリ数を取得
    #[inline]
    pub fn len(&self) -> usize {
        self.entries.len()
    }

    /// テーブルが空かどうか
    #[inline]
    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    /// eviction 制限を設定 (RFC 9204 Section 2.1.1)
    ///
    /// この値未満の absolute_index を持つエントリは evict できない。
    /// エンコーダーが未 ack フィールドセクションの参照状態に基づいて設定する。
    pub fn set_eviction_limit(&mut self, limit: u64) {
        self.eviction_limit = limit;
    }

    /// 容量を設定 (RFC 9204 Section 4.3.1)
    ///
    /// 容量が減少した場合、エントリをエビクトする。
    pub fn set_capacity(&mut self, capacity: u64) {
        self.max_capacity = capacity;
        self.evict_to_fit(0);
    }

    /// エントリを挿入 (RFC 9204 Section 3.2.2)
    ///
    /// 新しいエントリは先頭に挿入される。
    /// 容量を超える場合は古いエントリをエビクトする。
    ///
    /// # 戻り値
    ///
    /// 成功した場合は挿入されたエントリの絶対インデックスを返す。
    /// エントリが容量を超える場合は `None` を返す。
    pub fn insert(&mut self, name: Vec<u8>, value: Vec<u8>) -> Option<u64> {
        let entry_size = ENTRY_OVERHEAD + name.len() as u64 + value.len() as u64;

        // エントリが容量を超える場合はエラー
        if entry_size > self.max_capacity {
            return None;
        }

        // 容量に収まるようにエビクト
        self.evict_to_fit(entry_size);

        // evict 後もスペースが不足する場合は挿入不可 (RFC 9204 Section 2.1.1)
        if self.current_size + entry_size > self.max_capacity {
            return None;
        }

        let absolute_index = self.insert_count;
        let entry = DynamicEntry::new(name, value, absolute_index);
        self.current_size += entry.size();
        self.insert_count += 1;
        self.entries.push_front(entry);

        Some(absolute_index)
    }

    /// 複製 (RFC 9204 Section 4.3.4)
    ///
    /// 既存のエントリを複製して新しいエントリとして挿入。
    pub fn duplicate(&mut self, relative_index: u64) -> Option<u64> {
        let entry = self.get_by_relative_index_encoder(relative_index)?;
        let name = entry.name.clone();
        let value = entry.value.clone();
        self.insert(name, value)
    }

    /// 絶対インデックスでエントリを取得
    pub fn get_by_absolute_index(&self, absolute_index: u64) -> Option<&DynamicEntry> {
        if absolute_index < self.dropped_count {
            return None;
        }
        if absolute_index >= self.insert_count {
            return None;
        }

        // entries は先頭が最新 (insert_count - 1)、末尾が最古 (dropped_count)
        let offset = (self.insert_count - 1 - absolute_index) as usize;
        self.entries.get(offset)
    }

    /// エンコーダー命令での相対インデックスでエントリを取得 (RFC 9204 Section 3.2.5)
    ///
    /// 相対インデックス 0 は最新エントリを指す。
    pub fn get_by_relative_index_encoder(&self, relative_index: u64) -> Option<&DynamicEntry> {
        self.entries.get(relative_index as usize)
    }

    /// フィールドライン表現での相対インデックスでエントリを取得 (RFC 9204 Section 3.2.5)
    ///
    /// Base からの相対インデックスで取得。
    /// absolute_index = base - relative_index - 1
    pub fn get_by_relative_index_repr(
        &self,
        relative_index: u64,
        base: u64,
    ) -> Option<&DynamicEntry> {
        if relative_index >= base {
            return None;
        }
        let absolute_index = base - relative_index - 1;
        self.get_by_absolute_index(absolute_index)
    }

    /// Post-Base インデックスでエントリを取得 (RFC 9204 Section 3.2.6)
    ///
    /// absolute_index = base + post_base_index
    pub fn get_by_post_base_index(&self, post_base_index: u64, base: u64) -> Option<&DynamicEntry> {
        let absolute_index = base.checked_add(post_base_index)?;
        self.get_by_absolute_index(absolute_index)
    }

    /// 名前と値のペアで動的テーブルを検索
    ///
    /// 完全一致のインデックス、または名前のみ一致のインデックスを返す。
    pub fn find_entry(&self, name: &[u8], value: &[u8]) -> (Option<u64>, Option<u64>) {
        let mut name_match = None;

        for entry in &self.entries {
            if entry.name == name {
                if entry.value == value {
                    return (Some(entry.absolute_index), Some(entry.absolute_index));
                }
                if name_match.is_none() {
                    name_match = Some(entry.absolute_index);
                }
            }
        }

        (None, name_match)
    }

    /// 指定サイズに収まるようにエビクト (RFC 9204 Section 3.2.2)
    ///
    /// eviction_limit 未満の absolute_index を持つエントリは evict しない
    /// (RFC 9204 Section 2.1.1)。
    fn evict_to_fit(&mut self, required_size: u64) {
        while self.current_size + required_size > self.max_capacity {
            // 末尾エントリが evictable か確認
            if let Some(entry) = self.entries.back() {
                if entry.absolute_index < self.eviction_limit {
                    // evict 不可: 未 ack のフィールドセクションから参照されている可能性がある
                    break;
                }
                let size = entry.size();
                self.entries.pop_back();
                self.current_size -= size;
                self.dropped_count += 1;
            } else {
                break;
            }
        }
    }

    /// すべてのエントリをクリア
    pub fn clear(&mut self) {
        self.dropped_count += self.entries.len() as u64;
        self.entries.clear();
        self.current_size = 0;
    }

    /// エントリのイテレータを取得 (最新から最古の順)
    pub fn iter(&self) -> impl Iterator<Item = &DynamicEntry> {
        self.entries.iter()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_empty_table() {
        let table = DynamicTable::new();
        assert_eq!(table.max_capacity(), 0);
        assert_eq!(table.current_size(), 0);
        assert_eq!(table.insert_count(), 0);
        assert_eq!(table.len(), 0);
        assert!(table.is_empty());
    }

    #[test]
    fn test_insert_entry() {
        let mut table = DynamicTable::with_capacity(1024);

        let idx = table.insert(b":authority".to_vec(), b"www.example.com".to_vec());
        assert_eq!(idx, Some(0));
        assert_eq!(table.len(), 1);
        assert_eq!(table.insert_count(), 1);

        let entry = table.get_by_absolute_index(0).expect("test must succeed");
        assert_eq!(entry.name, b":authority");
        assert_eq!(entry.value, b"www.example.com");
    }

    #[test]
    fn test_insert_multiple_entries() {
        let mut table = DynamicTable::with_capacity(1024);

        let idx1 = table.insert(b"name1".to_vec(), b"value1".to_vec());
        let idx2 = table.insert(b"name2".to_vec(), b"value2".to_vec());
        let idx3 = table.insert(b"name3".to_vec(), b"value3".to_vec());

        assert_eq!(idx1, Some(0));
        assert_eq!(idx2, Some(1));
        assert_eq!(idx3, Some(2));
        assert_eq!(table.len(), 3);

        // 絶対インデックスでアクセス
        assert_eq!(
            table
                .get_by_absolute_index(0)
                .expect("test must succeed")
                .name,
            b"name1"
        );
        assert_eq!(
            table
                .get_by_absolute_index(1)
                .expect("test must succeed")
                .name,
            b"name2"
        );
        assert_eq!(
            table
                .get_by_absolute_index(2)
                .expect("test must succeed")
                .name,
            b"name3"
        );

        // エンコーダー相対インデックスでアクセス (0 = 最新)
        assert_eq!(
            table
                .get_by_relative_index_encoder(0)
                .expect("test must succeed")
                .name,
            b"name3"
        );
        assert_eq!(
            table
                .get_by_relative_index_encoder(1)
                .expect("test must succeed")
                .name,
            b"name2"
        );
        assert_eq!(
            table
                .get_by_relative_index_encoder(2)
                .expect("test must succeed")
                .name,
            b"name1"
        );
    }

    #[test]
    fn test_eviction() {
        // 容量を小さく設定 (エントリ 1 つ分程度)
        let mut table = DynamicTable::with_capacity(50);

        // 最初のエントリを挿入 (32 + 5 + 6 = 43)
        let idx1 = table.insert(b"name1".to_vec(), b"value1".to_vec());
        assert_eq!(idx1, Some(0));
        assert_eq!(table.len(), 1);

        // 2 つ目のエントリを挿入すると 1 つ目がエビクトされる
        let idx2 = table.insert(b"name2".to_vec(), b"value2".to_vec());
        assert_eq!(idx2, Some(1));
        assert_eq!(table.len(), 1);
        assert_eq!(table.dropped_count(), 1);

        // 古いエントリにはアクセスできない
        assert!(table.get_by_absolute_index(0).is_none());
        // 新しいエントリにはアクセスできる
        assert!(table.get_by_absolute_index(1).is_some());
    }

    #[test]
    fn test_entry_too_large() {
        let mut table = DynamicTable::with_capacity(50);

        // 容量を超えるエントリは挿入できない
        let idx = table.insert(b"very_long_name".to_vec(), b"very_long_value".to_vec());
        assert_eq!(idx, None);
        assert!(table.is_empty());
    }

    #[test]
    fn test_set_capacity() {
        let mut table = DynamicTable::with_capacity(200);

        table.insert(b"name1".to_vec(), b"value1".to_vec());
        table.insert(b"name2".to_vec(), b"value2".to_vec());
        table.insert(b"name3".to_vec(), b"value3".to_vec());
        assert_eq!(table.len(), 3);

        // 容量を減らすとエビクトが発生
        table.set_capacity(50);
        assert_eq!(table.len(), 1);
        assert_eq!(table.dropped_count(), 2);
    }

    #[test]
    fn test_find_entry() {
        let mut table = DynamicTable::with_capacity(1024);

        table.insert(b":method".to_vec(), b"GET".to_vec());
        table.insert(b":method".to_vec(), b"POST".to_vec());
        table.insert(b":path".to_vec(), b"/".to_vec());

        // 完全一致
        let (exact, name_only) = table.find_entry(b":method", b"GET");
        assert_eq!(exact, Some(0));
        assert_eq!(name_only, Some(0));

        // 名前のみ一致 (最新の一致を返す)
        let (exact, name_only) = table.find_entry(b":method", b"PUT");
        assert_eq!(exact, None);
        assert_eq!(name_only, Some(1)); // POST が最新

        // 一致なし
        let (exact, name_only) = table.find_entry(b"x-custom", b"value");
        assert_eq!(exact, None);
        assert_eq!(name_only, None);
    }

    #[test]
    fn test_relative_index_repr() {
        let mut table = DynamicTable::with_capacity(1024);

        table.insert(b"name0".to_vec(), b"value0".to_vec()); // abs=0
        table.insert(b"name1".to_vec(), b"value1".to_vec()); // abs=1
        table.insert(b"name2".to_vec(), b"value2".to_vec()); // abs=2
        table.insert(b"name3".to_vec(), b"value3".to_vec()); // abs=3

        // Base = 3 の場合
        // relative=0 -> abs=3-0-1=2 -> name2
        // relative=1 -> abs=3-1-1=1 -> name1
        // relative=2 -> abs=3-2-1=0 -> name0
        assert_eq!(
            table
                .get_by_relative_index_repr(0, 3)
                .expect("test must succeed")
                .name,
            b"name2"
        );
        assert_eq!(
            table
                .get_by_relative_index_repr(1, 3)
                .expect("test must succeed")
                .name,
            b"name1"
        );
        assert_eq!(
            table
                .get_by_relative_index_repr(2, 3)
                .expect("test must succeed")
                .name,
            b"name0"
        );

        // 範囲外
        assert!(table.get_by_relative_index_repr(3, 3).is_none());
    }

    #[test]
    fn test_post_base_index() {
        let mut table = DynamicTable::with_capacity(1024);

        table.insert(b"name0".to_vec(), b"value0".to_vec()); // abs=0
        table.insert(b"name1".to_vec(), b"value1".to_vec()); // abs=1
        table.insert(b"name2".to_vec(), b"value2".to_vec()); // abs=2
        table.insert(b"name3".to_vec(), b"value3".to_vec()); // abs=3

        // Base = 2 の場合
        // post_base=0 -> abs=2+0=2 -> name2
        // post_base=1 -> abs=2+1=3 -> name3
        assert_eq!(
            table
                .get_by_post_base_index(0, 2)
                .expect("test must succeed")
                .name,
            b"name2"
        );
        assert_eq!(
            table
                .get_by_post_base_index(1, 2)
                .expect("test must succeed")
                .name,
            b"name3"
        );

        // 範囲外
        assert!(table.get_by_post_base_index(2, 2).is_none());
    }

    #[test]
    fn test_duplicate() {
        let mut table = DynamicTable::with_capacity(1024);

        table.insert(b"name".to_vec(), b"value".to_vec()); // abs=0, rel=0
        table.insert(b"other".to_vec(), b"data".to_vec()); // abs=1, rel=0, old rel=1

        // 相対インデックス 1 (最古のエントリ) を複製
        let idx = table.duplicate(1);
        assert_eq!(idx, Some(2));
        assert_eq!(table.len(), 3);

        let entry = table.get_by_absolute_index(2).expect("test must succeed");
        assert_eq!(entry.name, b"name");
        assert_eq!(entry.value, b"value");
    }

    #[test]
    fn test_clear() {
        let mut table = DynamicTable::with_capacity(1024);

        table.insert(b"name1".to_vec(), b"value1".to_vec());
        table.insert(b"name2".to_vec(), b"value2".to_vec());
        assert_eq!(table.len(), 2);

        table.clear();
        assert!(table.is_empty());
        assert_eq!(table.current_size(), 0);
        assert_eq!(table.dropped_count(), 2);
        assert_eq!(table.insert_count(), 2);
    }

    /// RFC 9204 Section 2.1.1: eviction_limit 未満のエントリは evict されない
    #[test]
    fn test_eviction_limit_prevents_eviction() {
        // 容量を小さく設定 (エントリ 1 つ分程度)
        let mut table = DynamicTable::with_capacity(50);

        // 最初のエントリを挿入 (32 + 5 + 6 = 43)
        let idx1 = table.insert(b"name1".to_vec(), b"value1".to_vec());
        assert_eq!(idx1, Some(0));

        // eviction_limit を設定: abs_index 0 は evict 不可にする
        // (eviction_limit = 1 なら abs_index < 1 = abs_index 0 が保護される)
        table.set_eviction_limit(1);

        // 2 つ目のエントリを挿入しようとするが、1 つ目を evict できないので失敗
        let idx2 = table.insert(b"name2".to_vec(), b"value2".to_vec());
        assert_eq!(idx2, None);
        assert_eq!(table.len(), 1);
        assert_eq!(table.dropped_count(), 0);

        // eviction_limit を解除
        table.set_eviction_limit(0);

        // 今度は eviction が成功
        let idx3 = table.insert(b"name3".to_vec(), b"value3".to_vec());
        assert_eq!(idx3, Some(1));
        assert_eq!(table.len(), 1);
        assert_eq!(table.dropped_count(), 1);
    }

    /// RFC 9204 Section 2.1.1: eviction_limit は set_capacity にも適用される
    #[test]
    fn test_eviction_limit_on_set_capacity() {
        let mut table = DynamicTable::with_capacity(200);

        table.insert(b"name1".to_vec(), b"value1".to_vec()); // abs=0 (最古、末尾)
        table.insert(b"name2".to_vec(), b"value2".to_vec()); // abs=1
        table.insert(b"name3".to_vec(), b"value3".to_vec()); // abs=2 (最新、先頭)
        assert_eq!(table.len(), 3);

        // abs_index < 1 のエントリ (abs=0) を保護
        table.set_eviction_limit(1);

        // 容量を縮小: eviction は末尾 (最古) から行われる
        // abs=0 は eviction_limit で保護されているので evict できない
        // → 容量オーバーのまま全エントリが残る
        table.set_capacity(50);
        assert_eq!(table.len(), 3);
        assert_eq!(table.dropped_count(), 0);

        // eviction_limit を解除すると eviction が進む
        table.set_eviction_limit(0);
        table.set_capacity(50);
        assert_eq!(table.len(), 1);
        assert_eq!(table.dropped_count(), 2);
    }
}
