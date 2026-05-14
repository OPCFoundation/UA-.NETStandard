/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
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
