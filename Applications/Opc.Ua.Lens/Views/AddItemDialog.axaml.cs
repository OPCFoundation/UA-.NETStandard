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
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// Confirmation dialog for adding the address-space-selected node as a
/// monitored item.  The validation (Variable / Object-with-EventNotifier)
/// already happened upstream in <see cref="MainViewModel.UpdateSelectionAsync"/>;
/// the dialog only collects the sampling interval and turns the chosen
/// node into a <see cref="MonitoredItemConfig"/>.
/// </summary>
internal sealed partial class AddItemDialog : Window
{
    private readonly NodeViewModel m_node;
    private readonly bool m_isEvent;
    public MonitoredItemConfig? Result { get; private set; }

    public AddItemDialog(NodeViewModel node, bool isEvent)
    {
        m_node = node;
        m_isEvent = isEvent;
        InitializeComponent();

        this.RequiredControl<TextBlock>("NodeIdLabel").Text = node.NodeId.ToString() ?? string.Empty;
        this.RequiredControl<TextBlock>("ModeLabel").Text = isEvent
            ? $"Event subscription (default EventFilter) — {node.NodeClass}"
            : $"Value subscription — {node.NodeClass}";

        var samplingMs = this.RequiredControl<TextBox>("SamplingMs");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        ok.Click += (_, _) =>
        {
            if (!uint.TryParse(samplingMs.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint smp))
            {
                return;
            }
            Result = new MonitoredItemConfig
            {
                DisplayName = (m_isEvent ? "event:" : "value:") + m_node.NodeId,
                NodeId = m_node.NodeId,
                AttributeId = m_isEvent ? Attributes.EventNotifier : Attributes.Value,
                SamplingInterval = TimeSpan.FromMilliseconds(smp),
                QueueSize = m_isEvent ? 100u : 1u,
                DiscardOldest = true,
                IsEvent = m_isEvent,
                MonitoringMode = MonitoringMode.Reporting
            };
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
