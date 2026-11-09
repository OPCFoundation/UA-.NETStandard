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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Multi-select removal dialog — the user picks one or more
/// <see cref="MonitoredItemConfig"/>s (or ticks <c>All</c>) and clicks
/// Remove.  Returns the chosen list; the caller loops over it to remove
/// each item from the active subscription.
/// </summary>
internal sealed partial class RemoveItemDialog : Window
{
    private readonly IReadOnlyList<MonitoredItemConfig> m_items;
    /// <summary>Suppresses the SelectAll ↔ List feedback loop.</summary>
    private bool m_syncing;

    public IReadOnlyList<MonitoredItemConfig>? Result { get; private set; }

    public RemoveItemDialog(IReadOnlyList<MonitoredItemConfig> items)
    {
        m_items = items;
        InitializeComponent();

        var list = this.RequiredControl<ListBox>("List");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var selectAll = this.RequiredControl<CheckBox>("SelectAll");

        // Display strings keep the same column-aligned layout as before.
        list.ItemsSource = items.Select(c => string.Format(CultureInfo.InvariantCulture,
            " {0,4}  {1,-5}  smp={2,5:0}ms  Q={3}  {4}",
            c.Id, c.IsEvent ? "Event" : "Value",
            c.SamplingInterval.TotalMilliseconds, c.QueueSize, c.NodeId)).ToList();

        if (items.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        selectAll.IsCheckedChanged += (_, _) =>
        {
            if (m_syncing)
            {
                return;
            }
            m_syncing = true;
            try
            {
                if (selectAll.IsChecked == true)
                {
                    list.SelectAll();
                }
                else
                {
                    list.UnselectAll();
                }
            }
            finally
            {
                m_syncing = false;
            }
        };

        list.SelectionChanged += (_, _) =>
        {
            if (m_syncing)
            {
                return;
            }
            m_syncing = true;
            try
            {
                selectAll.IsChecked = list.SelectedItems is { Count: var n } && n == items.Count;
            }
            finally
            {
                m_syncing = false;
            }
        };

        ok.Click += (_, _) =>
        {
            // Map selected indices back to the underlying configs (the list
            // shows formatted strings, so we look at SelectedItems' indices).
            var picked = new List<MonitoredItemConfig>();
            if (list.SelectedItems is { } sel)
            {
                foreach (object? item in sel)
                {
                    int idx = list.Items.IndexOf(item);
                    if (idx >= 0 && idx < m_items.Count)
                    {
                        picked.Add(m_items[idx]);
                    }
                }
            }
            if (picked.Count == 0)
            {
                return;
            }
            Result = picked;
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

