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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.Diagnostics;
using UaLens.Diagnostics.Capture;

namespace UaLens.Views
{
    /// <summary>
    /// Explicit capture/replay UI over the workspace-owned module. Opening the window never records traffic.
    /// </summary>
    internal sealed partial class DiagnosticCaptureWindow : Window, IAsyncDisposable
    {
        /// <summary>
        /// Borrows the workspace capture module and owns only transient UI operations.
        /// </summary>
        public DiagnosticCaptureWindow(DiagnosticCaptureWorkspace captures)
        {
            m_captures = captures ?? throw new ArgumentNullException(nameof(captures));
            AvaloniaXamlLoader.Load(this);
            this.RequiredControl<Button>("StartCaptureButton").Click += async (_, _) =>
                await ExecuteAsync(StartAsync).ConfigureAwait(true);
            this.RequiredControl<Button>("ReplayButton").Click += async (_, _) =>
                await ExecuteAsync(ReplayAsync).ConfigureAwait(true);
            this.RequiredControl<Button>("ExportCaptureButton").Click += async (_, _) =>
                await ExecuteAsync(ExportAsync).ConfigureAwait(true);
            this.RequiredControl<Button>("ClearCaptureButton").Click += async (_, _) =>
                await ExecuteAsync(_ => m_captures.ClearAsync()).ConfigureAwait(true);
            this.RequiredControl<Button>("StopCaptureButton").Click += async (_, _) =>
            {
                m_stop = StopFromViewAsync();
                await m_stop.ConfigureAwait(true);
                Refresh();
            };
            this.RequiredControl<CheckBox>("PacketConsent").IsCheckedChanged += (_, _) => Refresh();
            this.RequiredControl<CheckBox>("ExportConsent").IsCheckedChanged += (_, _) => Refresh();
            this.RequiredControl<ComboBox>("SourceCombo").SelectionChanged += (_, _) =>
            {
                this.RequiredControl<CheckBox>("PacketConsent").IsChecked = false;
                Refresh();
            };
            m_timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
                (_, _) => Refresh());
            Opened += (_, _) => m_timer.Start();
            Refresh();
        }

        /// <summary>
        /// Gets the active UI operation so the owner and native interaction tests can await actual completion.
        /// </summary>
        internal Task Operation => m_operation;

        /// <summary>
        /// Gets the last visible operation error, also available to the owner after the dialog closes.
        /// </summary>
        internal string OperationError { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispatcher.UIThread.VerifyAccess();
            m_disposal ??= DisposeCoreAsync();
            return new ValueTask(m_disposal);
        }

        /// <inheritdoc/>
        protected override void OnClosed(EventArgs e)
        {
            m_closed = true;
            m_timer.Stop();
            m_closeCancellation = m_lifetime.CancelAsync();
            base.OnClosed(e);
        }

        private async Task DisposeCoreAsync()
        {
            await Task.CompletedTask.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
            m_closed = true;
            m_timer.Stop();
            if (IsVisible)
            {
                Close();
            }
            await m_lifetime.CancelAsync().ConfigureAwait(true);
            try
            {
                try
                {
                    await StopCoreAsync().ConfigureAwait(true);
                }
                finally
                {
                    await m_stop.ConfigureAwait(true);
                }
            }
            finally
            {
                m_action?.Dispose();
                m_lifetime.Dispose();
            }
        }

        private Task ExecuteAsync(Func<CancellationToken, Task> action)
        {
            if (!m_operation.IsCompleted || m_closed)
            {
                return m_operation;
            }
            m_action?.Dispose();
            m_action = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
            m_operation = ExecuteCoreAsync(action, m_action.Token);
            Refresh();
            return m_operation;
        }

        private async Task ExecuteCoreAsync(Func<CancellationToken, Task> action, CancellationToken token)
        {
            await Task.CompletedTask.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
            OperationError = string.Empty;
            try
            {
                await action(token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                OperationError = "The requested operation was canceled.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or AggregateException or NotSupportedException or
                OverflowException or FormatException or ServiceResultException)
            {
                OperationError = CorrelatedDiagnostics.Failure(error);
            }
            finally
            {
                Refresh();
            }
        }

        private async Task StartAsync(CancellationToken token)
        {
            if (this.RequiredControl<ComboBox>("SourceCombo").SelectedItem is not DiagnosticCaptureSource source)
            {
                throw new InvalidOperationException("Select an available capture source.");
            }
            bool consent = this.RequiredControl<CheckBox>("PacketConsent").IsChecked == true;
            this.RequiredControl<CheckBox>("PacketConsent").IsChecked = false;
            this.RequiredControl<CheckBox>("ExportConsent").IsChecked = false;
            await m_captures.StartAsync(source.Id, Limits(), consent, token).ConfigureAwait(true);
        }

        private async Task ReplayAsync(CancellationToken token)
        {
            bool consent = this.RequiredControl<CheckBox>("PacketConsent").IsChecked == true;
            this.RequiredControl<CheckBox>("PacketConsent").IsChecked = false;
            this.RequiredControl<CheckBox>("ExportConsent").IsChecked = false;
            if (!consent)
            {
                throw new InvalidOperationException("Explicit packet-data consent is required.");
            }
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open bounded PCAP for offline replay (no network send)",
                AllowMultiple = false,
                FileTypeFilter = [s_pcapType]
            }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                return;
            }
            using IStorageFile file = files[0];
            token.ThrowIfCancellationRequested();
            Stream stream = await file.OpenReadAsync().ConfigureAwait(true);
            await using (stream.ConfigureAwait(true))
            {
                await m_captures.ReplayAsync(stream, Limits(), consent, token).ConfigureAwait(true);
            }
        }

        private async Task ExportAsync(CancellationToken token)
        {
            Guid id = m_captures.Snapshot().Id;
            bool consent = this.RequiredControl<CheckBox>("ExportConsent").IsChecked == true;
            this.RequiredControl<CheckBox>("ExportConsent").IsChecked = false;
            if (!consent)
            {
                throw new InvalidOperationException("Explicit raw packet-data export consent is required.");
            }
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export raw packet data to a new PCAP file",
                SuggestedFileName = "ualens-capture.pcap",
                DefaultExtension = "pcap",
                FileTypeChoices = [s_pcapType]
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            using (file)
            {
                token.ThrowIfCancellationRequested();
                string path = file.TryGetLocalPath() ??
                    throw new NotSupportedException("Atomic raw export requires a new local filesystem destination.");
                await m_captures.ExportAsync(id, path, consent, token).ConfigureAwait(true);
            }
        }

        private async Task StopCoreAsync()
        {
            await m_closeCancellation.ConfigureAwait(true);
            if (m_action is not null)
            {
                await m_action.CancelAsync().ConfigureAwait(true);
            }
            try
            {
                await m_captures.StopAsync().ConfigureAwait(true);
            }
            finally
            {
                await m_operation.ConfigureAwait(true);
            }
        }

        private async Task StopFromViewAsync()
        {
            try
            {
                await StopCoreAsync().ConfigureAwait(true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or AggregateException or OperationCanceledException or
                NotSupportedException or OverflowException or FormatException or ServiceResultException)
            {
                OperationError = CorrelatedDiagnostics.Failure(error);
            }
        }

        private DiagnosticCaptureLimits Limits()
        {
            return new DiagnosticCaptureLimits(
                (int)(this.RequiredControl<NumericUpDown>("DurationInput").Value ?? 10),
                (int)(this.RequiredControl<NumericUpDown>("FramesInput").Value ?? 256),
                (long)(this.RequiredControl<NumericUpDown>("BytesInput").Value ?? 1024 * 1024));
        }

        private void Refresh()
        {
            if (m_closed || m_refreshing)
            {
                return;
            }
            m_refreshing = true;
            try
            {
                DiagnosticCaptureSnapshot snapshot = m_captures.Snapshot();
                DiagnosticCaptureSource[] sources = m_captures.Sources.ToArray() ?? [];
                var combo = this.RequiredControl<ComboBox>("SourceCombo");
                if (!sources.SequenceEqual(m_sources))
                {
                    string? selected = (combo.SelectedItem as DiagnosticCaptureSource)?.Id;
                    m_sources = sources;
                    combo.ItemsSource = sources;
                    combo.SelectedItem = sources.FirstOrDefault(source => source.Id == selected) ??
                        sources.FirstOrDefault();
                }
                bool available = !snapshot.IsBusy && m_operation.IsCompleted && m_stop.IsCompleted;
                bool allowed = this.RequiredControl<CheckBox>("PacketConsent").IsChecked == true;
                combo.IsEnabled = available;
                this.RequiredControl<NumericUpDown>("DurationInput").IsEnabled = available;
                this.RequiredControl<NumericUpDown>("FramesInput").IsEnabled = available;
                this.RequiredControl<NumericUpDown>("BytesInput").IsEnabled = available;
                this.RequiredControl<CheckBox>("PacketConsent").IsEnabled = available;
                this.RequiredControl<CheckBox>("ExportConsent").IsEnabled =
                    available && snapshot.Phase == DiagnosticCapturePhase.Completed;
                this.RequiredControl<Button>("StartCaptureButton").IsEnabled =
                    available && allowed && combo.SelectedItem is DiagnosticCaptureSource;
                this.RequiredControl<Button>("ReplayButton").IsEnabled = available && allowed;
                this.RequiredControl<Button>("ClearCaptureButton").IsEnabled =
                    available && snapshot.Phase != DiagnosticCapturePhase.Empty;
                this.RequiredControl<Button>("StopCaptureButton").IsEnabled =
                    (snapshot.IsBusy || !m_operation.IsCompleted) && m_stop.IsCompleted;
                this.RequiredControl<Button>("ExportCaptureButton").IsEnabled =
                    available &&
                    snapshot.Phase == DiagnosticCapturePhase.Completed &&
                    this.RequiredControl<CheckBox>("ExportConsent").IsChecked == true;
                this.RequiredControl<TextBlock>("CaptureStatus").Text = snapshot.Status;
                this.RequiredControl<TextBlock>("CaptureCounts").Text = string.Create(CultureInfo.InvariantCulture,
                    $"{snapshot.Frames} frames; {snapshot.PayloadBytes} payload bytes; " +
                    $"{snapshot.FileBytes} PCAP bytes");
                this.RequiredControl<TextBlock>("CaptureError").Text = OperationError;
                if (m_last is null || m_last.Id != snapshot.Id || m_last.Phase != snapshot.Phase)
                {
                    this.RequiredControl<ListBox>("PacketList").ItemsSource = snapshot.Packets.ToList();
                }
                m_last = snapshot;
            }
            finally
            {
                m_refreshing = false;
            }
        }

        private readonly DiagnosticCaptureWorkspace m_captures;
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly DispatcherTimer m_timer;
        private CancellationTokenSource? m_action;
        private Task m_operation = Task.CompletedTask;
        private Task m_stop = Task.CompletedTask;
        private Task m_closeCancellation = Task.CompletedTask;
        private Task? m_disposal;
        private DiagnosticCaptureSnapshot? m_last;
        private DiagnosticCaptureSource[] m_sources = [];
        private bool m_closed;
        private bool m_refreshing;
        private static readonly FilePickerFileType s_pcapType = new("PCAP packet capture") { Patterns = ["*.pcap"] };
    }
}
