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

namespace UaLens.Views
{
    /// <summary>
    /// Editable defaults for every monitored item in a Subscription Bench tab
    /// (or any other host that wants to apply uniform settings across a batch
    /// of items).  Independent of <see cref="Subscriptions.MonitoredItemConfig"/>
    /// because it deliberately omits the node-specific bits (NodeId,
    /// DisplayName, AttributeId, IsEvent) — only the editable knobs are here.
    /// </summary>
    internal sealed record MonitoredItemSettings
    {
        public TimeSpan SamplingInterval { get; init; } = TimeSpan.Zero;
        public uint QueueSize { get; init; } = 1;
        public bool DiscardOldest { get; init; } = true;
        public MonitoringMode MonitoringMode { get; init; } = MonitoringMode.Reporting;

        /// <summary>
        /// Optional <see cref="Opc.Ua.DataChangeFilter"/>. Null means
        /// "use the server default" (Trigger = StatusValue, no deadband).
        /// </summary>
        public DataChangeFilter? DataChangeFilter { get; init; }
    }

    internal sealed partial class MonitoredItemSettingsDialog : Window
    {
        public MonitoredItemSettings? Result { get; private set; }

        public MonitoredItemSettingsDialog(MonitoredItemSettings current, string? scope = null)
        {
            InitializeComponent();
            if (scope is not null)
            {
                this.RequiredControl<TextBlock>("ScopeHint").Text = scope;
            }

            TextBox samplingMs = this.RequiredControl<TextBox>("SamplingMs");
            TextBox queueSize = this.RequiredControl<TextBox>("QueueSizeBox");
            CheckBox discardOldest = this.RequiredControl<CheckBox>("DiscardOldestBox");
            ComboBox monitoringMode = this.RequiredControl<ComboBox>("MonitoringModeCombo");
            ComboBox trigger = this.RequiredControl<ComboBox>("TriggerCombo");
            ComboBox deadbandType = this.RequiredControl<ComboBox>("DeadbandTypeCombo");
            TextBox deadbandValue = this.RequiredControl<TextBox>("DeadbandValueBox");
            Button ok = this.RequiredControl<Button>("OkButton");
            Button cancel = this.RequiredControl<Button>("CancelButton");

            samplingMs.Text = current.SamplingInterval.TotalMilliseconds.ToString("0.###", CultureInfo.CurrentCulture);
            queueSize.Text = current.QueueSize.ToString(CultureInfo.InvariantCulture);
            discardOldest.IsChecked = current.DiscardOldest;
            monitoringMode.SelectedIndex = current.MonitoringMode switch
            {
                MonitoringMode.Disabled => 0,
                MonitoringMode.Sampling => 1,
                _ => 2
            };

            // Pre-fill the filter section from the current value, falling back
            // to "Trigger=StatusValue, DeadbandType=None" when no filter is set.
            DataChangeFilter? f = current.DataChangeFilter;
            trigger.SelectedIndex = f is null
                ? 1
                : (int)f.Trigger;
            deadbandType.SelectedIndex = f is null
                ? 0
                : (int)f.DeadbandType;
            deadbandValue.Text = f is null
                ? "0"
                : f.DeadbandValue.ToString(CultureInfo.CurrentCulture);
            deadbandValue.IsEnabled = deadbandType.SelectedIndex > 0;

            deadbandType.SelectionChanged += (_, _) =>
                deadbandValue.IsEnabled = deadbandType.SelectedIndex > 0;

            ok.Click += (_, _) =>
            {
                TextBlock error = this.RequiredControl<TextBlock>("ValidationError");
                error.Text = string.Empty;
                if (!double.TryParse(samplingMs.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double smp) ||
                    !double.IsFinite(smp) ||
                    smp < -1 ||
                    smp > 3_600_000)
                {
                    error.Text = "Sampling must be -1 (inherit publishing), 0 (fastest), " +
                        "or a positive interval up to one hour.";
                    return;
                }
                if (!uint.TryParse(queueSize.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint q))
                {
                    error.Text = "Queue size must be a non-negative whole number.";
                    return;
                }
                var dt = (DataChangeTrigger)Math.Max(0, trigger.SelectedIndex);
                var db = (DeadbandType)Math.Max(0, deadbandType.SelectedIndex);
                double dbVal = 0.0;
                if (db != DeadbandType.None)
                {
                    if (!double.TryParse(
                        deadbandValue.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dbVal) ||
                        !double.IsFinite(dbVal) ||
                        dbVal < 0.0)
                    {
                        error.Text = "Deadband must be a finite, non-negative number.";
                        return;
                    }
                    if (db == DeadbandType.Percent && dbVal > 100.0)
                    {
                        error.Text = "A percent deadband cannot exceed 100.";
                        return;
                    }
                }
                DataChangeFilter? newFilter = null;
                // Only emit a filter when the user picked something other than
                // the server-default (Trigger=StatusValue, DeadbandType=None).
                if (dt != DataChangeTrigger.StatusValue || db != DeadbandType.None)
                {
                    newFilter = new DataChangeFilter
                    {
                        Trigger = dt,
                        DeadbandType = (uint)db,
                        DeadbandValue = dbVal
                    };
                }
                MonitoringMode mode = monitoringMode.SelectedIndex switch
                {
                    0 => MonitoringMode.Disabled,
                    1 => MonitoringMode.Sampling,
                    _ => MonitoringMode.Reporting
                };
                Result = new MonitoredItemSettings
                {
                    SamplingInterval = TimeSpan.FromMilliseconds(smp),
                    QueueSize = q,
                    DiscardOldest = discardOldest.IsChecked == true,
                    MonitoringMode = mode,
                    DataChangeFilter = newFilter
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
}
