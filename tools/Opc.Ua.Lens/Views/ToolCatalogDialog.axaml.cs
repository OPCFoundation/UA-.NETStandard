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

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Searchable, grouped catalog for opening a tool document. Returns the chosen
/// <see cref="PluginKind"/>, or null when cancelled. The catalog is driven by
/// <see cref="ToolCatalog"/> so it always reflects the plug-in registry.
/// </summary>
internal sealed partial class ToolCatalogDialog : Window, IAsyncDisposable
{
    public ToolCatalogDialog()
        : this(null)
    {
    }

    public ToolCatalogDialog(PluginHost? host)
    {
        m_host = host;
        InitializeComponent();
        m_search = this.RequiredControl<TextBox>("SearchBox");
        m_groups = this.RequiredControl<ItemsControl>("GroupsList");
        m_empty = this.RequiredControl<TextBlock>("EmptyLabel");
        m_check = this.RequiredControl<Button>("CheckButton");
        this.RequiredControl<Button>("CancelButton").Click += (_, _) => Close(null);
        m_search.TextChanged += (_, _) => Refresh();
        m_groups.AddHandler(Button.ClickEvent, OnToolClicked);
        m_check.Click += async (_, _) =>
        {
            if (!m_checking && !m_closed)
            {
                m_probeWork = CheckCapabilitiesAsync(refresh: true);
                await m_probeWork.ConfigureAwait(true);
            }
        };
        Refresh();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    public ValueTask DisposeAsync()
    {
        if (m_disposal is null)
        {
            m_closed = true;
            if (m_host is { } host)
            {
                host.Connection.StateChanged -= OnAvailabilityChanged;
            }
            m_disposal = DisposeCoreAsync();
        }
        return new ValueTask(m_disposal);
    }

    private void OnToolClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Tag: ToolCatalogEntry entry })
        {
            Close(entry.Kind);
            e.Handled = true;
        }
    }

    private void OnToolGroupChanged(object? sender, EventArgs e)
    {
        if (sender is ItemsControl items)
        {
            items.ItemsSource = items.DataContext is ToolCatalogGroup group ? group.Tools.ToList() : null;
        }
    }

    private void Refresh()
    {
        ArrayOf<ToolCatalogGroup> groups = ToolCatalog.Build(m_search.Text, m_host);
        m_groups.ItemsSource = groups.ToList();
        m_empty.IsVisible = groups.Count == 0;
        bool hasProbe = false;
        foreach (ToolCatalogGroup group in groups)
        {
            foreach (ToolCatalogEntry entry in group.Tools)
            {
                hasProbe |= entry.Capability is not null;
            }
        }
        m_check.IsEnabled = !m_checking && hasProbe && m_host is { Connection.IsConnected: true };
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        m_host ??= (Owner?.DataContext as MainViewModel)?.CreatePluginHost();
        if (m_host is { } host)
        {
            host.Connection.StateChanged += OnAvailabilityChanged;
        }
        m_search.Focus();
        m_probeWork = CheckCapabilitiesAsync(refresh: false);
        await m_probeWork.ConfigureAwait(true);
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        await DisposeAsync().ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await m_lifetime.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await m_probeWork.ConfigureAwait(false);
            }
            finally
            {
                m_lifetime.Dispose();
            }
        }
    }

    private void OnAvailabilityChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!m_closed)
            {
                Refresh();
            }
        });
    }

    private async Task CheckCapabilitiesAsync(bool refresh)
    {
        if (m_host is not { Connection.IsConnected: true } host || m_checking)
        {
            Refresh();
            return;
        }
        m_checking = true;
        if (refresh)
        {
            host.Capabilities.Invalidate();
        }
        Refresh();
        try
        {
            ArrayOf<ToolCatalogGroup> groups = ToolCatalog.Build(m_search.Text, host);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                ArrayOf<ToolCatalogEntry> entries = groups[groupIndex].Tools;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    ToolCatalogEntry entry = entries[entryIndex];
                    if (entry.Capability is { } request)
                    {
                        await host.Capabilities.ProbeAsync(request, m_lifetime.Token).ConfigureAwait(true);
                        if (!m_closed)
                        {
                            Refresh();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (m_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            m_checking = false;
            if (!m_closed)
            {
                Refresh();
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private readonly TextBox m_search;
    private readonly ItemsControl m_groups;
    private readonly TextBlock m_empty;
    private readonly Button m_check;
    private readonly CancellationTokenSource m_lifetime = new();
    private PluginHost? m_host;
    private Task m_probeWork = Task.CompletedTask;
    private Task? m_disposal;
    private bool m_checking;
    private bool m_closed;
}
