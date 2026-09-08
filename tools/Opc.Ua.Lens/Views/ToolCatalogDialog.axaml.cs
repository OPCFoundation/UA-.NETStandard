/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Searchable, grouped catalog for opening a tool document. Returns the chosen
/// <see cref="PluginKind"/>, or null when cancelled. The catalog is driven by
/// <see cref="ToolCatalog"/> so it always reflects the plug-in registry.
/// </summary>
internal sealed partial class ToolCatalogDialog : Window
{
    public ToolCatalogDialog()
    {
        InitializeComponent();
        m_search = this.RequiredControl<TextBox>("SearchBox");
        m_groups = this.RequiredControl<ItemsControl>("GroupsList");
        m_empty = this.RequiredControl<TextBlock>("EmptyLabel");
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
        m_search.TextChanged += (_, _) => Refresh();
        m_groups.AddHandler(Button.ClickEvent, OnToolClicked);
        Refresh();
        Opened += (_, _) =>
        {
            m_search.Focus();
        };
    }

    private void OnToolClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Tag: ToolCatalogEntry entry })
        {
            Close(entry.Kind);
            e.Handled = true;
        }
    }

    private void Refresh()
    {
        IReadOnlyList<ToolCatalogGroup> groups = ToolCatalog.Build(m_search.Text);
        m_groups.ItemsSource = groups;
        m_empty.IsVisible = groups.Count == 0;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly TextBox m_search;
    private readonly ItemsControl m_groups;
    private readonly TextBlock m_empty;
}
