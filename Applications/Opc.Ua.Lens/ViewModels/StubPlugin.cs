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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UaLens.ViewModels;

/// <summary>
/// Placeholder <see cref="IPlugin"/> implementation used for
/// any kind whose real implementation is not yet shipped.  Renders a
/// centered card describing what the app will do plus a "🚧 Coming
/// soon" badge.  Auto-numbers titles per kind ("GDS Push 1", "Performance 1", …).
/// </summary>
internal sealed partial class StubPlugin : ObservableObject, IPlugin
{
    private static readonly Dictionary<PluginKind, int> s_perKindCounter = new();

    private readonly PluginRegistration m_reg;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    public StubPlugin(PluginKind kind)
    {
        m_reg = PluginRegistry.For(kind);
        int n;
        lock (s_perKindCounter)
        {
            s_perKindCounter.TryGetValue(kind, out int prev);
            n = prev + 1;
            s_perKindCounter[kind] = n;
        }
        m_title = $"{m_reg.DisplayName} {n}";
    }

    public PluginKind Kind => m_reg.Kind;
    Control? IPlugin.HeaderToolbar => null;

    public string Status => $"● {m_reg.DisplayName} — coming soon";
    public bool SupportsDuplicate => false;

    Control? IPlugin.View => BuildStubView();

    public IReadOnlyList<MenuItem> ContributeMenuItems() => Array.Empty<MenuItem>();

    public void OnActivated() { }
    public void OnDeactivated() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Border BuildStubView()
    {
        var titleBlock = new TextBlock
        {
            Text = $"{m_reg.Glyph}  {m_reg.DisplayName}",
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Foreground = (Application.Current?.FindResource("TextPrimary") as IBrush)
                ?? Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var descBlock = new TextBlock
        {
            Text = m_reg.Description,
            FontSize = 14,
            Foreground = (Application.Current?.FindResource("TextSecondary") as IBrush)
                ?? Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24)
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xF5, 0x9E, 0x0B)),
            BorderBrush = (Application.Current?.FindResource("AccentYellow") as IBrush)
                ?? Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "🚧  Coming soon",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = (Application.Current?.FindResource("AccentYellow") as IBrush)
                    ?? Brushes.Transparent
            }
        };
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleBlock, descBlock, badge }
        };
        return new Border
        {
            Background = (Application.Current?.FindResource("SurfaceBg") as IBrush)
                ?? Brushes.Transparent,
            BorderBrush = (Application.Current?.FindResource("PanelBorder") as IBrush)
                ?? Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(32),
            Child = stack
        };
    }
}
