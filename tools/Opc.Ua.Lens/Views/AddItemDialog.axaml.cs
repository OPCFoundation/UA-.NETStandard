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

        var filterHeader = this.RequiredControl<TextBlock>("FilterHeader");
        var triggerLabel = this.RequiredControl<TextBlock>("TriggerLabel");
        var triggerCombo = this.RequiredControl<ComboBox>("TriggerCombo");
        var deadbandTypeLabel = this.RequiredControl<TextBlock>("DeadbandTypeLabel");
        var deadbandTypeCombo = this.RequiredControl<ComboBox>("DeadbandTypeCombo");
        var deadbandValueLabel = this.RequiredControl<TextBlock>("DeadbandValueLabel");
        var deadbandValueBox = this.RequiredControl<TextBox>("DeadbandValueBox");

        // The DataChangeFilter is only meaningful for value monitored items.
        // Hide the whole section in event mode so the dialog stays clean.
        bool showFilter = !isEvent;
        filterHeader.IsVisible = showFilter;
        triggerLabel.IsVisible = showFilter;
        triggerCombo.IsVisible = showFilter;
        deadbandTypeLabel.IsVisible = showFilter;
        deadbandTypeCombo.IsVisible = showFilter;
        deadbandValueLabel.IsVisible = showFilter;
        deadbandValueBox.IsVisible = showFilter;

        // Greying-out: the deadband value only matters when the type is non-None.
        deadbandTypeCombo.SelectionChanged += (_, _) =>
            deadbandValueBox.IsEnabled = deadbandTypeCombo.SelectedIndex > 0;

        ok.Click += (_, _) =>
        {
            if (!uint.TryParse(samplingMs.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint smp))
            {
                return;
            }

            DataChangeFilter? filter = null;
            if (!isEvent)
            {
                var trigger = (DataChangeTrigger)Math.Max(0, triggerCombo.SelectedIndex);
                var deadbandType = (DeadbandType)Math.Max(0, deadbandTypeCombo.SelectedIndex);
                double deadbandValue = 0.0;
                if (deadbandType != DeadbandType.None)
                {
                    if (!double.TryParse(deadbandValueBox.Text, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out deadbandValue) || deadbandValue < 0.0)
                    {
                        return;
                    }
                    if (deadbandType == DeadbandType.Percent && deadbandValue > 100.0)
                    {
                        return;
                    }
                }

                // Only emit a filter when the user picked something other than
                // the server-default (Trigger=StatusValue, DeadbandType=None).
                if (trigger != DataChangeTrigger.StatusValue || deadbandType != DeadbandType.None)
                {
                    filter = new DataChangeFilter
                    {
                        Trigger = trigger,
                        DeadbandType = (uint)deadbandType,
                        DeadbandValue = deadbandValue
                    };
                }
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
                MonitoringMode = MonitoringMode.Reporting,
                DataChangeFilter = filter
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
