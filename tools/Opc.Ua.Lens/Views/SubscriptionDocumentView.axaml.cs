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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Views
{
    /// <summary>
    /// Presents one monitor document. Notification ownership and retention remain in the model.
    /// </summary>
    internal sealed partial class SubscriptionDocumentView : UserControl, IDisposable
    {
        public SubscriptionDocumentView(SubscriptionViewModel model)
        {
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
            AvaloniaXamlLoader.Load(this);
            m_plot = this.RequiredControl<ScottPlotView>("DocumentPlot");
            m_animation = this.RequiredControl<AnimationCanvas>("DocumentAnimation");
            m_interval = this.RequiredControl<NumericUpDown>("PublishingIntervalInput");
            m_enabled = this.RequiredControl<CheckBox>("PublishingEnabledCheck");
            m_viewMode = this.RequiredControl<ComboBox>("DocumentViewMode");
            m_values = this.RequiredControl<ListBox>("MonitorValuesList");
            m_timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                (_, _) => RefreshSummary());

            m_animation.GetItems = () => [.. m_model.Items];
            m_animation.GetHeaderText = () => m_model.SubscriptionStatus;
            m_animation.GetGapMetrics = () => m_model.Adapter is { } adapter
                ? (adapter.MissingMessageCount, adapter.RepublishMessageCount, adapter.DroppedNotificationCount)
                : (0, 0, 0);
            this.RequiredControl<Button>("ApplyPublishingButton").Click += async (_, _) =>
                await ApplyPublishingAsync().ConfigureAwait(true);
            m_enabled.IsCheckedChanged += async (_, _) =>
            {
                if (!m_updating)
                {
                    await ApplyPublishingAsync().ConfigureAwait(true);
                }
            };
            m_viewMode.SelectionChanged += (_, _) =>
            {
                if (!m_updating)
                {
                    m_model.DisplayModeIndex = Math.Max(0, m_viewMode.SelectedIndex);
                    BindChart();
                }
            };
            this.RequiredControl<Button>("SubscriptionSettingsButton").Click += async (_, _) =>
                await ShowSubscriptionSettingsAsync().ConfigureAwait(true);
            this.RequiredControl<Button>("AddNodeIdButton").Click += async (_, _) =>
                await AddNodeAsync().ConfigureAwait(true);
            this.RequiredControl<Button>("ItemSettingsButton").Click += async (_, _) =>
                await ShowItemSettingsAsync().ConfigureAwait(true);
            this.RequiredControl<Button>("RemoveSelectedItemButton").Click += async (_, _) =>
            {
                if (SelectedItem() is { } item)
                {
                    await m_model.RemoveItemCommand.ExecuteAsync(item).ConfigureAwait(true);
                }
            };
            this.RequiredControl<Button>("ChartZoomOutButton").Click += (_, _) => Zoom(0.5);
            this.RequiredControl<Button>("ChartZoomInButton").Click += (_, _) => Zoom(2);
            this.RequiredControl<Button>("ChartZoomResetButton").Click += (_, _) =>
            {
                m_model.AnimationTimeScale = 1;
                m_animation.TimeScale = 1;
                m_plot.ResetXZoom();
            };
            RefreshSettings();
            BindChart();
        }

        public Func<(double Cpu, double MemMiB)>? ResourceSample
        {
            get => m_animation.GetResourceSample;
            set => m_animation.GetResourceSample = value;
        }

        public void Dispose()
        {
            m_timer.Stop();
            m_model.PropertyChanged -= OnModelChanged;
            m_plot.Dispose();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            m_model.PropertyChanged += OnModelChanged;
            m_model.OnActivated();
            RefreshSettings();
            BindChart();
            RefreshSummary();
            m_timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            m_timer.Stop();
            m_model.PropertyChanged -= OnModelChanged;
            m_model.OnDeactivated();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SubscriptionViewModel.Subscription) or nameof(SubscriptionViewModel.Adapter))
            {
                RefreshSettings();
            }
            if (e.PropertyName is nameof(SubscriptionViewModel.ShowLegend)
                or nameof(SubscriptionViewModel.ShowXAxis) or nameof(SubscriptionViewModel.ShowYAxis))
            {
                m_plot.SetChartElementsVisible(m_model.ShowLegend, m_model.ShowXAxis, m_model.ShowYAxis);
            }
            m_animation.ShowResourceOverlay = m_model.ShowResourceOverlay;
        }

        private void RefreshSettings()
        {
            m_updating = true;
            try
            {
                m_interval.Value = (decimal)m_model.Subscription.PublishingInterval.TotalMilliseconds;
                m_enabled.IsChecked = m_model.Subscription.PublishingEnabled;
                m_viewMode.SelectedIndex = m_model.DisplayModeIndex;
            }
            finally
            {
                m_updating = false;
            }
        }

        private async Task ApplyPublishingAsync()
        {
            if (m_interval.Value is not { } interval)
            {
                m_model.ErrorText = "Enter a publishing interval in milliseconds.";
                return;
            }
            await m_model.ApplySubscriptionCommand.ExecuteAsync(m_model.Subscription with
            {
                PublishingInterval = TimeSpan.FromMilliseconds((double)interval),
                PublishingEnabled = m_enabled.IsChecked == true
            }).ConfigureAwait(true);
        }

        private async Task AddNodeAsync()
        {
            TextBox input = this.RequiredControl<TextBox>("MonitoredNodeIdInput");
            if (string.IsNullOrWhiteSpace(input.Text) ||
                !NodeId.TryParse(input.Text, out NodeId nodeId) ||
                nodeId.IsNull)
            {
                m_model.ErrorText = "Enter a valid node id, for example i=2258.";
                return;
            }
            await m_model.AddItemCommand.ExecuteAsync(new MonitoredItemConfig
            {
                NodeId = nodeId,
                DisplayName = nodeId.ToString()
            }).ConfigureAwait(true);
            if (string.IsNullOrEmpty(m_model.ErrorText))
            {
                input.Text = string.Empty;
            }
            m_plot.OnItemsChanged([.. m_model.Items]);
        }

        private async Task ShowSubscriptionSettingsAsync()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                throw new InvalidOperationException("The monitor document has no desktop owner.");
            }
            var dialog = new SubscriptionSettingsDialog(m_model.Subscription, m_model.Adapter?.HasWorkerPool ?? true);
            SubscriptionConfig? result = await dialog.ShowDialog<SubscriptionConfig?>(owner).ConfigureAwait(true);
            if (result is not null)
            {
                await m_model.ApplySubscriptionCommand.ExecuteAsync(result).ConfigureAwait(true);
            }
        }

        private async Task ShowItemSettingsAsync()
        {
            if (SelectedItem() is not { } item)
            {
                m_model.ErrorText = "Select a monitored item first.";
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                throw new InvalidOperationException("The monitor document has no desktop owner.");
            }
            var dialog = new MonitoredItemSettingsDialog(new MonitoredItemSettings
            {
                SamplingInterval = item.SamplingInterval,
                QueueSize = item.QueueSize,
                DiscardOldest = item.DiscardOldest,
                MonitoringMode = item.MonitoringMode,
                DataChangeFilter = item.DataChangeFilter
            }, "Only the selected monitored item will change.");
            MonitoredItemSettings? settings = await dialog.ShowDialog<MonitoredItemSettings?>(owner)
                .ConfigureAwait(true);
            if (settings is not null)
            {
                try
                {
                    await m_model.ConfigureItemAsync(item, settings).ConfigureAwait(true);
                    m_model.ErrorText = string.Empty;
                }
                catch (Exception error) when (error is ServiceResultException or ArgumentException
                    or InvalidOperationException)
                {
                    m_model.ErrorText = $"Item settings failed: {error.Message}";
                }
            }
        }

        private MonitoredItemConfig? SelectedItem()
        {
            if (m_values.SelectedItem is not MonitoredItemStatusRow row)
            {
                return null;
            }
            return m_model.Items.FirstOrDefault(item => item.Id == row.Id);
        }

        private void BindChart()
        {
            int index = m_model.DisplayModeIndex;
            bool show = index != 0;
            this.RequiredControl<Grid>("DocumentChart").IsVisible = show;
            this.RequiredControl<Expander>("ChartOptions").IsVisible = show;
            Grid grid = this.RequiredControl<Grid>("DocumentDataGrid");
            grid.RowDefinitions[0].Height = show ? new GridLength(1, GridUnitType.Star) : GridLength.Star;
            grid.RowDefinitions[1].Height = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            AnimationMode mode = index switch
            {
                1 => AnimationMode.Signal,
                3 => AnimationMode.Bars,
                4 => AnimationMode.Lines,
                5 => AnimationMode.Histogram,
                6 => AnimationMode.Heatmap,
                _ => AnimationMode.Dots
            };
            m_model.AnimationMode = mode;
            bool plot = ScottPlotView.IsScottPlotMode(mode);
            m_plot.IsVisible = show && plot;
            m_animation.IsVisible = show && !plot;
            m_animation.TimeScale = m_model.AnimationTimeScale;
            if (plot)
            {
                m_animation.Bind(null, null);
                m_plot.Bind(m_model.Recorder.CreateReader(), [.. m_model.Items], mode);
                m_plot.SetChartElementsVisible(m_model.ShowLegend, m_model.ShowXAxis, m_model.ShowYAxis);
            }
            else
            {
                m_plot.Bind(null, null, mode);
                m_animation.Mode = mode;
                m_animation.Bind(m_model.Recorder.CreateReader(), m_model.Adapter?.Counters);
            }
        }

        private void Zoom(double factor)
        {
            m_model.AnimationTimeScale = Math.Clamp(m_model.AnimationTimeScale * factor, 0.125, 8);
            m_animation.TimeScale = m_model.AnimationTimeScale;
            m_plot.ApplyXZoom(factor);
        }

        private void RefreshSummary()
        {
            m_model.RefreshStatus();
            ISubscriptionAdapter? adapter = m_model.Adapter;
            this.RequiredControl<TextBlock>("DocumentDeliverySummary").Text = adapter is null
                ? $"{m_model.Items.Count} configured items. Collected history is retained."
                : string.Format(CultureInfo.InvariantCulture,
                    "{0} values | {1} publishes | {2} keep-alives | gaps {3} | republished {4} | dropped {5}",
                    adapter.Counters.DataValues + adapter.Counters.EventValues,
                    adapter.Counters.DataMessages + adapter.Counters.EventMessages,
                    adapter.Counters.KeepAlives, adapter.MissingMessageCount,
                    adapter.RepublishMessageCount, adapter.DroppedNotificationCount);
        }

        private readonly SubscriptionViewModel m_model;
        private readonly ScottPlotView m_plot;
        private readonly AnimationCanvas m_animation;
        private readonly NumericUpDown m_interval;
        private readonly CheckBox m_enabled;
        private readonly ComboBox m_viewMode;
        private readonly ListBox m_values;
        private readonly DispatcherTimer m_timer;
        private bool m_updating;
    }
}
