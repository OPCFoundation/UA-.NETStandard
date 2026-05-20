/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace UaLens.Themes;

/// <summary>
/// Available theme presets.
/// </summary>
internal enum ThemePreset
{
    DarkNavy,
    DarkStandard,
    Light
}

/// <summary>
/// Manages runtime theme switching by swapping the
/// <see cref="Avalonia.Controls.ResourceDictionary"/> that defines the
/// semantic brush keys and toggling
/// <see cref="Application.RequestedThemeVariant"/> between
/// <see cref="ThemeVariant.Dark"/> and <see cref="ThemeVariant.Light"/>
/// so FluentTheme control chrome follows suit.
/// </summary>
internal static class ThemeManager
{
    private static readonly string s_settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UaLens", "theme.json");

    /// <summary>Current active preset.</summary>
    public static ThemePreset Current { get; private set; } = ThemePreset.DarkNavy;

    /// <summary>
    /// Raised after the theme is switched so the UI can refresh
    /// anything that reads theme colors imperatively (e.g.
    /// ScottPlot palettes).
    /// </summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// Loads the persisted theme preference (if any) and applies it.
    /// Call once during startup, after <c>App.InitializeComponent()</c>.
    /// </summary>
    public static void Initialize()
    {
        ThemePreset preset = LoadPreference();
        Apply(preset);
    }

    /// <summary>
    /// Switch to the given preset at runtime.
    /// </summary>
    public static void SetTheme(ThemePreset preset)
    {
        if (preset == Current)
        {
            return;
        }
        Apply(preset);
        SavePreference(preset);
    }

    private static void Apply(ThemePreset preset)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        // Load the new resource dictionary.
        string uri = preset switch
        {
            ThemePreset.DarkStandard => "avares://UaLens/Themes/DarkStandard.axaml",
            ThemePreset.Light => "avares://UaLens/Themes/Light.axaml",
            _ => "avares://UaLens/Themes/DarkNavy.axaml"
        };
        var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri));

        // Remove any previously-loaded theme dictionary (the first
        // MergedDictionary entry after the FluentTheme styles is ours).
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0 && merged[0] is ResourceDictionary prev
            && prev != dict)
        {
            merged.RemoveAt(0);
        }
        if (merged.Count == 0 || merged[0] != dict)
        {
            merged.Insert(0, dict);
        }

        // Toggle the Avalonia theme variant so FluentTheme control
        // chrome (buttons, text boxes, scroll bars, …) follows suit.
        app.RequestedThemeVariant = preset == ThemePreset.Light
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        Current = preset;
        ThemeChanged?.Invoke();
    }

    // ----- Persistence -----

    private static ThemePreset LoadPreference()
    {
        try
        {
            if (File.Exists(s_settingsPath))
            {
                string json = File.ReadAllText(s_settingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("theme", out JsonElement el)
                    && Enum.TryParse<ThemePreset>(el.GetString(), ignoreCase: true, out ThemePreset p))
                {
                    return p;
                }
            }
        }
        catch
        {
            // Corrupted or inaccessible — fall back to default.
        }
        return ThemePreset.DarkNavy;
    }

    private static void SavePreference(ThemePreset preset)
    {
        try
        {
            string? dir = Path.GetDirectoryName(s_settingsPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(new { theme = preset.ToString() });
            File.WriteAllText(s_settingsPath, json);
        }
        catch
        {
            // Best-effort; non-fatal.
        }
    }
}
