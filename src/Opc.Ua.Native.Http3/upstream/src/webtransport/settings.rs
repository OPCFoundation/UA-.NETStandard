//! WebTransport SETTINGS (draft-ietf-webtrans-http3-15 Section 9.2)
//!
//! HTTP/3 SETTINGS パラメータの WebTransport 拡張を定義する。
//!
//! ID 別の wire 構造とブール値の検査は [`crate::settings::Setting`] が一手に
//! 担うため、本モジュールは「WebTransport 固有フィールドの型付きコレクション」
//! と「`Setting` から本構造体への流し込み」のみを提供する。

use super::connect::DraftVersion;
use crate::settings::Setting;
use crate::varint::VarInt;

/// WebTransport 設定 (型付きフィールドのコレクション)
///
/// draft-ietf-webtrans-http3-02 / -07 / -14 / -15。
/// 将来のドラフトで変更される可能性がある。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Settings {
    /// WebTransport 有効化 (デフォルト: 0 = WebTransport 無効, 0 より大きければ有効)
    /// draft-ietf-webtrans-http3-15 Section 3.1, Section 9.2
    /// 将来のドラフトで変更される可能性がある
    pub wt_enabled: VarInt,
    /// 初期単方向ストリーム上限 (デフォルト: 0)
    pub wt_initial_max_streams_uni: VarInt,
    /// 初期双方向ストリーム上限 (デフォルト: 0)
    pub wt_initial_max_streams_bidi: VarInt,
    /// 初期データ上限 (デフォルト: 0)
    pub wt_initial_max_data: VarInt,
    /// WebTransport 有効化 (draft-02)
    pub enable_webtransport_draft02: Option<bool>,
    /// WebTransport 最大セッション数 (draft-07)
    pub webtransport_max_sessions_draft07: Option<VarInt>,
    /// WebTransport 最大セッション数 (draft-14)
    /// draft-14 Section 9.2
    pub wt_max_sessions_draft14: Option<VarInt>,
}

impl Default for Settings {
    fn default() -> Self {
        Self::new()
    }
}

impl Settings {
    /// 新しい Settings を作成 (すべてデフォルト値)
    pub const fn new() -> Self {
        Self {
            wt_enabled: VarInt::ZERO,
            wt_initial_max_streams_uni: VarInt::ZERO,
            wt_initial_max_streams_bidi: VarInt::ZERO,
            wt_initial_max_data: VarInt::ZERO,
            enable_webtransport_draft02: None,
            webtransport_max_sessions_draft07: None,
            wt_max_sessions_draft14: None,
        }
    }

    /// WebTransport 有効化を設定
    /// draft-ietf-webtrans-http3-15 Section 3.1, Section 9.2
    /// 将来のドラフトで変更される可能性がある
    pub fn wt_enabled(mut self, value: VarInt) -> Self {
        self.wt_enabled = value;
        self
    }

    /// 初期単方向ストリーム上限を設定
    pub fn wt_initial_max_streams_uni(mut self, max_streams: VarInt) -> Self {
        self.wt_initial_max_streams_uni = max_streams;
        self
    }

    /// 初期双方向ストリーム上限を設定
    pub fn wt_initial_max_streams_bidi(mut self, max_streams: VarInt) -> Self {
        self.wt_initial_max_streams_bidi = max_streams;
        self
    }

    /// 初期データ上限を設定
    pub fn wt_initial_max_data(mut self, max_data: VarInt) -> Self {
        self.wt_initial_max_data = max_data;
        self
    }

    /// WebTransport 有効化 (draft-02) を設定
    pub fn enable_webtransport_draft02(mut self, enable: bool) -> Self {
        self.enable_webtransport_draft02 = Some(enable);
        self
    }

    /// WebTransport 最大セッション数 (draft-07) を設定
    pub fn webtransport_max_sessions_draft07(mut self, max_sessions: VarInt) -> Self {
        self.webtransport_max_sessions_draft07 = Some(max_sessions);
        self
    }

    /// WebTransport 最大セッション数 (draft-14) を設定
    /// draft-14 Section 9.2
    pub fn wt_max_sessions_draft14(mut self, max_sessions: VarInt) -> Self {
        self.wt_max_sessions_draft14 = Some(max_sessions);
        self
    }

    /// `Setting` のスライスから WebTransport 関連設定だけを取り出して構築する
    ///
    /// WebTransport 関連の variant が 1 つも含まれない場合は `None` を返す。
    ///
    /// 呼び出し側は [`crate::frame::SettingsPayload::add`] を経由して構築された
    /// スライス (ID 重複なし) を渡す前提で、本関数は重複検査をしない。直接
    /// `&[Setting]` を渡す内部呼び出しで万一同一 variant が複数現れた場合は
    /// debug ビルドで panic させ、release ビルドは最後の値で上書きする
    /// (本関数の不変条件破壊を `debug_assert!` で検出する)。
    pub(crate) fn from_payload(settings: &[Setting]) -> Option<Self> {
        let mut wt = Self::new();
        let mut found = false;
        let mut seen_wt_ids = std::collections::HashSet::new();
        for setting in settings {
            // 同一 WT variant の重複は呼び出し側 (`SettingsPayload::add`) が
            // 弾いている前提だが、本関数の不変条件を debug ビルドで検証する。
            match *setting {
                Setting::WtEnabled(v) => {
                    debug_assert!(seen_wt_ids.insert(setting.id()));
                    wt.wt_enabled = v;
                    found = true;
                }
                Setting::WtInitialMaxStreamsUni(v) => {
                    debug_assert!(seen_wt_ids.insert(setting.id()));
                    wt.wt_initial_max_streams_uni = v;
                    found = true;
                }
                Setting::WtInitialMaxStreamsBidi(v) => {
                    debug_assert!(seen_wt_ids.insert(setting.id()));
                    wt.wt_initial_max_streams_bidi = v;
                    found = true;
                }
                Setting::WtInitialMaxData(v) => {
                    debug_assert!(seen_wt_ids.insert(setting.id()));
                    wt.wt_initial_max_data = v;
                    found = true;
                }
                Setting::EnableWebTransportDraft02(b) => {
                    debug_assert!(seen_wt_ids.insert(setting.id()));
                    wt.enable_webtransport_draft02 = Some(b);
                    found = true;
                }
                Setting::WebTransportMaxSessionsDraft07(v) => {
                    debug_assert!(seen_wt_ids.insert(setting.id()));
                    wt.webtransport_max_sessions_draft07 = Some(v);
                    found = true;
                }
                Setting::WtMaxSessionsDraft14(v) => {
                    debug_assert!(seen_wt_ids.insert(setting.id()));
                    wt.wt_max_sessions_draft14 = Some(v);
                    found = true;
                }
                // 非 WebTransport variant および Unknown は無視
                Setting::QpackMaxTableCapacity(_)
                | Setting::MaxFieldSectionSize(_)
                | Setting::QpackBlockedStreams(_)
                | Setting::EnableConnectProtocol(_)
                | Setting::H3Datagram(_)
                | Setting::Unknown(_) => {}
            }
        }
        found.then_some(wt)
    }

    /// SETTINGS からドラフトバージョンを検出する
    ///
    /// WebTransport が無効の場合は None を返す。
    ///
    /// 4 パターン (draft-02 / draft-07 / draft-14 / draft-15) を固有の SETTINGS ID で
    /// 判別する。判定は新しいドラフトから順に行う。
    ///
    /// **Safari (Network.framework) の挙動**: draft-07 の ID
    /// `SETTINGS_WEBTRANSPORT_MAX_SESSIONS` と draft-14 の ID `SETTINGS_WT_MAX_SESSIONS`
    /// を **同時** に送ってくるが、サーバーが応答 SETTINGS に draft-14 固有の
    /// `WT_INITIAL_MAX_*` を含めると `H3_REQUEST_CANCELLED` (0x10C) で拒否する。
    /// したがって SETTINGS ネゴシエーションとしては draft-07 を優先し、
    /// draft-14 固有のカプセルベースフロー制御はセッション確立後に別途扱う。
    ///
    /// 判定キー（上から順）:
    /// - draft-15: SETTINGS_WT_ENABLED (0x2c7cf000)
    /// - draft-07: SETTINGS_WEBTRANSPORT_MAX_SESSIONS (0xc671706a)
    /// - draft-14: SETTINGS_WT_MAX_SESSIONS (0x14e9cd29)
    /// - draft-02: SETTINGS_ENABLE_WEBTRANSPORT (0x2b603742)
    ///
    /// draft-ietf-webtrans-http3-02 / -07 / -14 / -15
    /// 将来のドラフトで変更される可能性がある
    pub fn detect_draft_pattern(&self) -> Option<DraftVersion> {
        if self.wt_enabled.get() > 0 {
            return Some(DraftVersion::Draft15);
        }
        if self
            .webtransport_max_sessions_draft07
            .is_some_and(|v| v.get() > 0)
        {
            return Some(DraftVersion::Draft07);
        }
        if self.wt_max_sessions_draft14.is_some_and(|v| v.get() > 0) {
            return Some(DraftVersion::Draft14);
        }
        if self.enable_webtransport_draft02 == Some(true) {
            return Some(DraftVersion::Draft02);
        }
        None
    }

    /// WebTransport が有効かどうか
    ///
    /// draft-02/07/14/15 のいずれかで有効になっていれば true。
    /// 将来のドラフトで変更される可能性がある
    pub fn is_enabled(&self) -> bool {
        self.wt_enabled.get() > 0
            || self.enable_webtransport_draft02 == Some(true)
            || self
                .webtransport_max_sessions_draft07
                .is_some_and(|v| v.get() > 0)
            || self.wt_max_sessions_draft14.is_some_and(|v| v.get() > 0)
    }

    /// フロー制御が有効かどうか
    ///
    /// 以下のいずれかを満たす場合に「フロー制御を使う意図を宣言」する:
    /// - SETTINGS_WT_MAX_SESSIONS > 1 (draft-14 Section 5.1)
    /// - SETTINGS_WT_INITIAL_MAX_STREAMS_UNI != 0
    /// - SETTINGS_WT_INITIAL_MAX_STREAMS_BIDI != 0
    /// - SETTINGS_WT_INITIAL_MAX_DATA != 0
    ///
    /// draft-ietf-webtrans-http3-14 Section 5.1, draft-ietf-webtrans-http3-15 Section 5.1
    /// 将来のドラフトで変更される可能性がある
    pub fn declares_flow_control(&self) -> bool {
        self.wt_max_sessions_draft14.is_some_and(|v| v.get() > 1)
            || self.wt_initial_max_streams_uni.get() != 0
            || self.wt_initial_max_streams_bidi.get() != 0
            || self.wt_initial_max_data.get() != 0
    }

    /// ピアとのネゴシエーション結果としてフロー制御が有効かどうか
    ///
    /// draft-ietf-webtrans-http3-15 Section 5.1:
    /// 両端点がフロー制御を使う意図を宣言した場合のみ有効。
    /// 将来のドラフトで変更される可能性がある
    pub fn flow_control_enabled_with_peer(&self, peer: &Self) -> bool {
        self.declares_flow_control() && peer.declares_flow_control()
    }

    /// 互換性のためにセッション確立直後の初期フロー制御カプセルが必要かどうか
    ///
    /// Safari 26.4 は draft-07 の `SETTINGS_WEBTRANSPORT_MAX_SESSIONS` で
    /// WebTransport をネゴシエートしつつ、`SETTINGS_WT_INITIAL_MAX_*` で
    /// フロー制御の意図を宣言し、セッション確立直後の
    /// `WT_MAX_STREAMS` / `WT_MAX_DATA` カプセルを要求する。
    ///
    /// そのため、ドラフト判定自体は draft-07 のまま維持しつつ、
    /// 初期カプセル要否だけは別判定にする。
    ///
    /// draft-ietf-webtrans-http3-14 Section 5,
    /// Safari 26.4 (Network.framework) 実装互換
    pub fn requires_initial_capsule_flow_control_compat(&self) -> bool {
        match self.detect_draft_pattern() {
            Some(DraftVersion::Draft14) => true,
            Some(DraftVersion::Draft07) => {
                self.wt_initial_max_streams_uni.get() != 0
                    || self.wt_initial_max_streams_bidi.get() != 0
                    || self.wt_initial_max_data.get() != 0
            }
            _ => false,
        }
    }

    /// 設定エントリのイテレータを返す
    ///
    /// 値が 0 / `None` の WebTransport 設定はエントリに含めない。
    pub fn iter(&self) -> impl Iterator<Item = Setting> + '_ {
        let entries = [
            (self.wt_enabled != VarInt::ZERO).then_some(Setting::WtEnabled(self.wt_enabled)),
            (self.wt_initial_max_streams_uni != VarInt::ZERO).then_some(
                Setting::WtInitialMaxStreamsUni(self.wt_initial_max_streams_uni),
            ),
            (self.wt_initial_max_streams_bidi != VarInt::ZERO).then_some(
                Setting::WtInitialMaxStreamsBidi(self.wt_initial_max_streams_bidi),
            ),
            (self.wt_initial_max_data != VarInt::ZERO)
                .then_some(Setting::WtInitialMaxData(self.wt_initial_max_data)),
            self.enable_webtransport_draft02
                .map(Setting::EnableWebTransportDraft02),
            self.webtransport_max_sessions_draft07
                .map(Setting::WebTransportMaxSessionsDraft07),
            self.wt_max_sessions_draft14
                .map(Setting::WtMaxSessionsDraft14),
        ];
        entries.into_iter().flatten()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn v(n: u64) -> VarInt {
        VarInt::new(n).expect("test must succeed")
    }

    #[test]
    fn test_settings_default() {
        let settings = Settings::default();
        assert_eq!(settings.wt_enabled, VarInt::ZERO);
        assert!(!settings.is_enabled());
        assert!(!settings.declares_flow_control());
    }

    #[test]
    fn test_settings_builder() {
        let settings = Settings::new()
            .wt_enabled(v(1))
            .wt_initial_max_streams_uni(v(100))
            .wt_initial_max_streams_bidi(v(50))
            .wt_initial_max_data(v(1024 * 1024));

        assert_eq!(settings.wt_enabled, v(1));
        assert_eq!(settings.wt_initial_max_streams_uni, v(100));
        assert_eq!(settings.wt_initial_max_streams_bidi, v(50));
        assert_eq!(settings.wt_initial_max_data, v(1024 * 1024));
        assert!(settings.is_enabled());
        assert!(settings.declares_flow_control());
    }

    #[test]
    fn test_settings_draft02_07() {
        let settings = Settings::new()
            .enable_webtransport_draft02(true)
            .webtransport_max_sessions_draft07(v(5));

        assert!(settings.is_enabled());
        assert_eq!(settings.enable_webtransport_draft02, Some(true));
        assert_eq!(settings.webtransport_max_sessions_draft07, Some(v(5)));
    }

    #[test]
    fn test_is_enabled_draft02() {
        let settings = Settings::new().enable_webtransport_draft02(true);
        assert!(settings.is_enabled());

        let settings = Settings::new().enable_webtransport_draft02(false);
        assert!(!settings.is_enabled());
    }

    #[test]
    fn test_is_enabled_draft07() {
        let settings = Settings::new().webtransport_max_sessions_draft07(v(1));
        assert!(settings.is_enabled());

        let settings = Settings::new().webtransport_max_sessions_draft07(v(0));
        assert!(!settings.is_enabled());
    }

    #[test]
    fn test_flow_control_enabled() {
        // wt_enabled のみではフロー制御無効 (draft-15: INITIAL_MAX_* が必要)
        let settings = Settings::new().wt_enabled(v(1));
        assert!(!settings.declares_flow_control());

        let settings = Settings::new().wt_enabled(v(2));
        assert!(!settings.declares_flow_control());

        // INITIAL_MAX_* が非ゼロならフロー制御有効
        let settings = Settings::new()
            .wt_enabled(v(1))
            .wt_initial_max_streams_uni(v(10));
        assert!(settings.declares_flow_control());

        let settings = Settings::new()
            .wt_enabled(v(1))
            .wt_initial_max_streams_bidi(v(5));
        assert!(settings.declares_flow_control());

        let settings = Settings::new()
            .wt_enabled(v(1))
            .wt_initial_max_data(v(1024));
        assert!(settings.declares_flow_control());
    }

    #[test]
    fn test_flow_control_enabled_with_peer() {
        let local = Settings::new().wt_initial_max_streams_uni(v(10));
        let peer = Settings::new().wt_enabled(v(1));
        assert!(!local.flow_control_enabled_with_peer(&peer));

        let peer = Settings::new().wt_initial_max_data(v(1));
        assert!(local.flow_control_enabled_with_peer(&peer));
    }

    #[test]
    fn test_settings_iter() {
        let settings = Settings::new()
            .wt_enabled(v(1))
            .wt_initial_max_data(v(4096));

        let entries: Vec<_> = settings.iter().collect();
        assert_eq!(entries.len(), 2);
        assert!(entries.contains(&Setting::WtEnabled(v(1))));
        assert!(entries.contains(&Setting::WtInitialMaxData(v(4096))));
    }

    #[test]
    fn test_settings_iter_with_draft02_07() {
        let settings = Settings::new()
            .wt_enabled(v(1))
            .enable_webtransport_draft02(true)
            .webtransport_max_sessions_draft07(v(3));

        let entries: Vec<_> = settings.iter().collect();
        assert_eq!(entries.len(), 3);
        assert!(entries.contains(&Setting::WtEnabled(v(1))));
        assert!(entries.contains(&Setting::EnableWebTransportDraft02(true)));
        assert!(entries.contains(&Setting::WebTransportMaxSessionsDraft07(v(3))));
    }

    #[test]
    fn test_from_payload_wt_setting() {
        // WebTransport variant のみ反映され、非 WT / Unknown は無視されることを検証
        let unknown = Setting::from_wire(v(0xdead), v(1)).expect("test must succeed");
        let entries = [
            Setting::WtEnabled(v(1)),
            Setting::WtInitialMaxStreamsUni(v(100)),
            Setting::WtInitialMaxStreamsBidi(v(50)),
            Setting::WtInitialMaxData(v(1024)),
            Setting::EnableWebTransportDraft02(true),
            Setting::WebTransportMaxSessionsDraft07(v(3)),
            Setting::WtMaxSessionsDraft14(v(2)),
            Setting::H3Datagram(true), // 非 WT、無視される
            unknown,                   // Unknown、無視される
        ];
        let wt = Settings::from_payload(&entries).expect("test must succeed");
        assert_eq!(wt.wt_enabled, v(1));
        assert_eq!(wt.wt_initial_max_streams_uni, v(100));
        assert_eq!(wt.wt_initial_max_streams_bidi, v(50));
        assert_eq!(wt.wt_initial_max_data, v(1024));
        assert_eq!(wt.enable_webtransport_draft02, Some(true));
        assert_eq!(wt.webtransport_max_sessions_draft07, Some(v(3)));
        assert_eq!(wt.wt_max_sessions_draft14, Some(v(2)));
    }

    #[test]
    fn test_from_payload_returns_none_without_wt_entries() {
        // WT variant が 1 つも含まれない場合は None
        let entries = [Setting::H3Datagram(true)];
        assert!(Settings::from_payload(&entries).is_none());
    }

    #[test]
    fn test_detect_draft_pattern_draft15() {
        let settings = Settings::new().wt_enabled(v(1));
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft15));
    }

    #[test]
    fn test_detect_draft_pattern_draft14() {
        let settings = Settings::new().wt_max_sessions_draft14(v(1));
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft14));
    }

    #[test]
    fn test_detect_draft_pattern_draft07() {
        let settings = Settings::new().webtransport_max_sessions_draft07(v(1));
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft07));
    }

    #[test]
    fn test_detect_draft_pattern_draft02() {
        let settings = Settings::new().enable_webtransport_draft02(true);
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft02));
    }

    #[test]
    fn test_detect_draft_pattern_none() {
        let settings = Settings::new();
        assert_eq!(settings.detect_draft_pattern(), None);

        // draft-02 が false の場合も None
        let settings = Settings::new().enable_webtransport_draft02(false);
        assert_eq!(settings.detect_draft_pattern(), None);

        // draft-14 が 0 の場合も None
        let settings = Settings::new().wt_max_sessions_draft14(v(0));
        assert_eq!(settings.detect_draft_pattern(), None);
    }

    #[test]
    fn test_detect_draft_pattern_priority() {
        // draft-07 と draft-14 を両方送る場合は SETTINGS ネゴシエーションとしては
        // draft-07 を優先する (Safari が draft-14 固有の応答 SETTINGS を拒否するため)
        let settings = Settings::new()
            .webtransport_max_sessions_draft07(v(1))
            .wt_max_sessions_draft14(v(1));
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft07));

        // draft-15 が最優先
        let settings = Settings::new()
            .wt_enabled(v(1))
            .wt_max_sessions_draft14(v(1))
            .webtransport_max_sessions_draft07(v(1));
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft15));
    }

    #[test]
    fn test_detect_draft_pattern_safari_observed() {
        // Safari 26.4 の実測パターン:
        // - draft-07 の SETTINGS_WEBTRANSPORT_MAX_SESSIONS
        // - draft-15 系 ID の SETTINGS_WT_INITIAL_MAX_*
        // draft 自体は draft-07 として扱う。
        let settings = Settings::new()
            .webtransport_max_sessions_draft07(v(1))
            .wt_initial_max_data(v(8_388_608))
            .wt_initial_max_streams_uni(v(100))
            .wt_initial_max_streams_bidi(v(100));
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft07));
    }

    #[test]
    fn test_detect_draft_pattern_safari_legacy_combo() {
        // Safari が draft-07 と draft-14 の ID を併送するケース
        // (実測: Safari 26.4 は 0xc671706a と 0x14e9cd29 を同時送信)。
        // SETTINGS ネゴシエーションとしては draft-07 を優先する。
        // draft-14 固有のカプセルベースフロー制御はセッション確立後に別途扱う。
        let settings = Settings::new()
            .webtransport_max_sessions_draft07(v(1))
            .wt_max_sessions_draft14(v(1))
            .wt_initial_max_data(v(8_388_608))
            .wt_initial_max_streams_uni(v(100))
            .wt_initial_max_streams_bidi(v(100));
        assert_eq!(settings.detect_draft_pattern(), Some(DraftVersion::Draft07));
    }

    #[test]
    fn test_declares_flow_control_draft14_max_sessions() {
        // draft-14: WT_MAX_SESSIONS > 1 でフロー制御宣言
        let settings = Settings::new().wt_max_sessions_draft14(v(2));
        assert!(settings.declares_flow_control());

        // WT_MAX_SESSIONS = 1 ではフロー制御宣言にならない
        let settings = Settings::new().wt_max_sessions_draft14(v(1));
        assert!(!settings.declares_flow_control());
    }

    #[test]
    fn test_requires_initial_capsule_flow_control_compat_safari_observed() {
        let settings = Settings::new()
            .webtransport_max_sessions_draft07(v(1))
            .wt_initial_max_streams_uni(v(100))
            .wt_initial_max_streams_bidi(v(100))
            .wt_initial_max_data(v(8 * 1024 * 1024));
        assert!(settings.requires_initial_capsule_flow_control_compat());
    }

    #[test]
    fn test_requires_initial_capsule_flow_control_compat_draft07_plain() {
        let settings = Settings::new().webtransport_max_sessions_draft07(v(1));
        assert!(!settings.requires_initial_capsule_flow_control_compat());
    }

    #[test]
    fn test_is_enabled_draft14() {
        let settings = Settings::new().wt_max_sessions_draft14(v(1));
        assert!(settings.is_enabled());

        let settings = Settings::new().wt_max_sessions_draft14(v(0));
        assert!(!settings.is_enabled());
    }
}
