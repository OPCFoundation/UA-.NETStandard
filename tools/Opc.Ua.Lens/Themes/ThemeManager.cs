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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace UaLens.Themes
{
    /// <summary>
    /// Available theme presets.
    /// </summary>
    internal enum ThemePreset
    {
        DarkNavy,
        DarkStandard,
        Light,
        System
    }

    /// <summary>
    /// Manages runtime theme switching by swapping the
    /// <see cref="ResourceDictionary"/> that defines the
    /// semantic brush keys and toggling
    /// <see cref="Application.RequestedThemeVariant"/> between
    /// <see cref="ThemeVariant.Dark"/> and <see cref="ThemeVariant.Light"/>
    /// so FluentTheme control chrome follows suit.
    /// </summary>
    internal static class ThemeManager
    {
        /// <summary>
        /// Current explicit preference. System remains selected when the OS changes appearance.
        /// </summary>
        public static ThemePreset Current { get; private set; } = ThemePreset.System;

        /// <summary>
        /// Raised after the theme is switched so the UI can refresh
        /// anything that reads theme colors imperatively (e.g.
        /// ScottPlot palettes).
        /// </summary>
        public static event Action? ThemeChanged;

        public static Color GetColor(string key, Color headlessDefault)
        {
            if (Application.Current is not { } app)
            {
                return headlessDefault;
            }
            if (app.TryGetResource(key, app.ActualThemeVariant, out object? resource) &&
                resource is ISolidColorBrush brush)
            {
                return brush.Color;
            }
            throw new InvalidOperationException($"The appearance does not define the '{key}' color.");
        }

        /// <summary>
        /// Applies the system appearance immediately, before asynchronous preference loading.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static void Initialize()
        {
            if (Application.Current is not { } app)
            {
                throw new InvalidOperationException("Avalonia must be initialized before applying an appearance.");
            }
            app.ActualThemeVariantChanged -= OnActualThemeChanged;
            app.ActualThemeVariantChanged += OnActualThemeChanged;
            Apply(ThemePreset.System);
        }

        /// <summary>
        /// Loads preferences without blocking the desktop thread.
        /// </summary>
        public static async Task LoadPreferenceAsync(
            AppearancePreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            ThemePreset preset = await preferences.LoadAsync(cancellationToken).ConfigureAwait(true);
            Apply(preset);
        }

        /// <summary>
        /// Persists an explicit choice before applying it. Save failures leave the existing appearance intact.
        /// </summary>
        public static async Task SetThemeAsync(
            ThemePreset preset,
            AppearancePreferences preferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            await s_changeGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await preferences.SaveAsync(preset, cancellationToken).ConfigureAwait(true);
                Apply(preset);
            }
            finally
            {
                s_changeGate.Release();
            }
        }

        private static void Apply(ThemePreset preset)
        {
            if (Application.Current is not { } app)
            {
                throw new InvalidOperationException("Avalonia must be initialized before applying an appearance.");
            }
            if (!Enum.IsDefined(preset))
            {
                throw new ArgumentOutOfRangeException(nameof(preset));
            }
            s_applying = true;
            try
            {
                Current = preset;
                app.RequestedThemeVariant = preset switch
                {
                    ThemePreset.System => ThemeVariant.Default,
                    ThemePreset.Light => ThemeVariant.Light,
                    _ => ThemeVariant.Dark
                };
                ApplyResources(app);
            }
            finally
            {
                s_applying = false;
            }
        }

        private static void OnActualThemeChanged(object? sender, EventArgs args)
        {
            if (!s_applying && Current == ThemePreset.System && sender is Application app)
            {
                ApplyResources(app);
            }
        }

        private static void ApplyResources(Application app)
        {
            ResourceDictionary resources = Current == ThemePreset.DarkNavy
                ? new DarkNavyTheme()
                : app.ActualThemeVariant == ThemeVariant.Light ? new LightTheme() : new DarkStandardTheme();
            if (s_resources is not null)
            {
                app.Resources.MergedDictionaries.Remove(s_resources);
            }
            app.Resources.MergedDictionaries.Add(resources);
            s_resources = resources;
            ThemeChanged?.Invoke();
        }

        private static readonly SemaphoreSlim s_changeGate = new(1, 1);
        private static ResourceDictionary? s_resources;
        private static bool s_applying;
    }
}
