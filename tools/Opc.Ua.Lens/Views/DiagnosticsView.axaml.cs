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
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Diagnostics;

namespace UaLens.Views;

/// <summary>
/// In-window server-diagnostics panel — replaces the standalone
/// <c>DiagnosticsWindow</c> dialog with a dockable UserControl.  Polls
/// the same curated set of ServerStatus + ServerDiagnosticsSummary
/// Variables at 1 Hz while bound to a live <see cref="ManagedSession"/>.
/// Visibility is controlled by the parent window via <see cref="IsVisible"/>.
/// </summary>
internal sealed partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
        this.RequiredControl<ItemsControl>("RowsList").ItemsSource = Rows;
        this.RequiredControl<ItemsControl>("ClientRowsList").ItemsSource = ClientRows;
        this.RequiredControl<Button>("HideButton").Click += (_, _) => HideRequested?.Invoke();
        this.RequiredControl<Button>("ExportButton").Click += async (_, _) => await ExportAsync().ConfigureAwait(true);
        foreach ((NodeId _, string label) in s_targets)
        {
            Rows.Add(new DiagRow(label, "(loading…)"));
        }
        m_timer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            async (_, _) => await PollAsync().ConfigureAwait(true));
    }

    public ObservableCollection<DiagRow> Rows { get; } = new();

    public ObservableCollection<DiagnosticMetric> ClientRows { get; } = new();

    /// <summary>
    /// Raised when the user clicks the panel's close button.
    /// </summary>
    public event Action? HideRequested;

    /// <summary>
    /// Binds the diagnostic view's "Publishes" sub-tab to the shared
    /// <see cref="PublishLogObserver"/> owned by <c>MainViewModel</c>.
    /// Idempotent — safe to call repeatedly.
    /// </summary>
    public void BindPublishLog(PublishLogObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        m_publishLog = observer;
        this.RequiredControl<ListBox>("PublishList").ItemsSource = observer.Entries;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Bind a live session; pass null to detach and stop polling. Late read results
    /// from the previous generation cannot overwrite the current panel.
    /// </summary>
    public void Bind(ManagedSession? session)
    {
        m_generation++;
        m_session = session;
        RefreshClientRows();
        if (session is null)
        {
            m_timer.Stop();
            this.RequiredControl<TextBlock>("StatusLabel").Text = "(disconnected)";
            for (int i = 0; i < Rows.Count; i++)
            {
                Rows[i] = new DiagRow(Rows[i].Name, "—");
            }
            return;
        }
        m_timer.Start();
        _ = PollAsync();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        m_timer.Stop();
        m_generation++;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (m_session is not null)
        {
            m_timer.Start();
        }
    }

    private async Task PollAsync()
    {
        ManagedSession? session = m_session;
        if (session is null || m_polling)
        {
            return;
        }
        m_polling = true;
        long generation = m_generation;
        try
        {
            RefreshClientRows();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var values = new List<DataValue>(s_targets.Length);
            uint limit = session.OperationLimits.MaxNodesPerRead;
            int batchSize = limit == 0 ? s_targets.Length : (int)Math.Min(limit, (uint)s_targets.Length);
            for (int offset = 0; offset < s_targets.Length; offset += batchSize)
            {
                int count = Math.Min(batchSize, s_targets.Length - offset);
                var ids = new ReadValueId[count];
                for (int i = 0; i < count; i++)
                {
                    ids[i] = new ReadValueId
                    {
                        NodeId = s_targets[offset + i].Id,
                        AttributeId = Attributes.Value
                    };
                }
                ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Server,
                    new ArrayOf<ReadValueId>(ids), cancellation.Token).ConfigureAwait(true);
                if (StatusCode.IsBad(response.ResponseHeader.ServiceResult))
                {
                    throw new ServiceResultException(response.ResponseHeader.ServiceResult);
                }
                for (int i = 0; i < count; i++)
                {
                    values.Add(i < response.Results.Count
                        ? response.Results[i]
                        : new DataValue(Variant.Null, StatusCodes.BadNoData));
                }
            }
            if (generation != m_generation || !ReferenceEquals(session, m_session))
            {
                return;
            }
            for (int i = 0; i < s_targets.Length; i++)
            {
                DataValue dv = values[i];
                string text = CorrelatedDiagnostics.Value(dv);
                if (s_targets[i].Label.StartsWith("Limits.", StringComparison.Ordinal) &&
                    StatusCode.IsGood(dv.StatusCode) && dv.WrappedValue.TryGetValue(out uint advertised) &&
                    advertised == 0)
                {
                    text = "Unspecified (0); no stated cap.";
                }
                Rows[i] = new DiagRow(s_targets[i].Label, text);
            }
            this.RequiredControl<TextBlock>("StatusLabel").Text =
                "Last poll: " + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                $"  ({values.Count} attributes; bounded to effective read limit)";
        }
        catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException or
            InvalidOperationException or TimeoutException or NotSupportedException or IOException)
        {
            if (generation == m_generation)
            {
                string failure = CorrelatedDiagnostics.Failure(exception);
                this.RequiredControl<TextBlock>("StatusLabel").Text = "Poll unavailable: " + failure;
                for (int i = 0; i < Rows.Count; i++)
                {
                    Rows[i] = new DiagRow(Rows[i].Name, "Unavailable: " + failure);
                }
            }
        }
        finally
        {
            m_polling = false;
        }
    }

    private void RefreshClientRows()
    {
        ClientRows.Clear();
        foreach (DiagnosticMetric row in CorrelatedDiagnostics.Capture(m_session, m_publishLog))
        {
            ClientRows.Add(row);
        }
    }

    private async Task ExportAsync()
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { CanSave: true } storage)
            {
                return;
            }
            ByteString evidence = DiagnosticEvidenceExport.Create(m_session, m_publishLog);
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export bounded, redacted diagnostic metadata",
                SuggestedFileName = "ualens-diagnostics.json",
                DefaultExtension = "json"
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            using (file)
            {
                Stream stream = await file.OpenWriteAsync().ConfigureAwait(true);
                await using (stream.ConfigureAwait(true))
                {
                    stream.SetLength(0);
                    await stream.WriteAsync(evidence.Memory, CancellationToken.None).ConfigureAwait(true);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or InvalidOperationException or OperationCanceledException or
            ServiceResultException or JsonException)
        {
            this.RequiredControl<TextBlock>("StatusLabel").Text =
                "Export failed: " + CorrelatedDiagnostics.Failure(exception);
        }
    }

    private static readonly (NodeId Id, string Label)[] s_targets =
    {
        (VariableIds.Server_ServerStatus_StartTime, "ServerStatus.StartTime"),
        (VariableIds.Server_ServerStatus_CurrentTime, "ServerStatus.CurrentTime"),
        (VariableIds.Server_ServerStatus_State, "ServerStatus.State"),
        (VariableIds.Server_ServerStatus_BuildInfo_ProductName, "BuildInfo.ProductName"),
        (VariableIds.Server_ServerStatus_BuildInfo_ProductUri, "BuildInfo.ProductUri"),
        (VariableIds.Server_ServerStatus_BuildInfo_ManufacturerName, "BuildInfo.ManufacturerName"),
        (VariableIds.Server_ServerStatus_BuildInfo_SoftwareVersion, "BuildInfo.SoftwareVersion"),
        (VariableIds.Server_ServerStatus_BuildInfo_BuildNumber, "BuildInfo.BuildNumber"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSessionCount,
            "Diag.CurrentSessionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount,
            "Diag.CumulatedSessionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_RejectedSessionCount,
            "Diag.RejectedSessionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_RejectedRequestsCount,
            "Diag.RejectedRequestsCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSubscriptionCount,
            "Diag.CurrentSubscriptionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSubscriptionCount,
            "Diag.CumulatedSubscriptionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_PublishingIntervalCount,
            "Diag.PublishingIntervalCount"),
        (VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead, "Limits.MaxNodesPerRead"),
        (VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite, "Limits.MaxNodesPerWrite"),
        (VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse, "Limits.MaxNodesPerBrowse"),
        (VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall, "Limits.MaxNodesPerMethodCall"),
        (VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall,
            "Limits.MaxMonitoredItemsPerCall")
    };

    private readonly DispatcherTimer m_timer;
    private ManagedSession? m_session;
    private PublishLogObserver? m_publishLog;
    private long m_generation;
    private bool m_polling;
}

/// <summary>
/// One row in the live diagnostics table.
/// </summary>
internal sealed record DiagRow(string Name, string Value);
