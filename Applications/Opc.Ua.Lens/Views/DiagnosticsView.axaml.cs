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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
    private readonly DispatcherTimer m_timer;
    private ManagedSession? m_session;
    public ObservableCollection<DiagRow> Rows { get; } = new();

    /// <summary>Raised when the user clicks the panel's × close button.</summary>
    public event Action? HideRequested;

    private static readonly (NodeId Id, string Label)[] s_targets = new (NodeId, string)[]
    {
        (VariableIds.Server_ServerStatus_StartTime, "ServerStatus.StartTime"),
        (VariableIds.Server_ServerStatus_CurrentTime, "ServerStatus.CurrentTime"),
        (VariableIds.Server_ServerStatus_State, "ServerStatus.State"),
        (VariableIds.Server_ServerStatus_BuildInfo_ProductName, "BuildInfo.ProductName"),
        (VariableIds.Server_ServerStatus_BuildInfo_ProductUri, "BuildInfo.ProductUri"),
        (VariableIds.Server_ServerStatus_BuildInfo_ManufacturerName, "BuildInfo.ManufacturerName"),
        (VariableIds.Server_ServerStatus_BuildInfo_SoftwareVersion, "BuildInfo.SoftwareVersion"),
        (VariableIds.Server_ServerStatus_BuildInfo_BuildNumber, "BuildInfo.BuildNumber"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSessionCount, "Diag.CurrentSessionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount, "Diag.CumulatedSessionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_RejectedSessionCount, "Diag.RejectedSessionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_RejectedRequestsCount, "Diag.RejectedRequestsCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSubscriptionCount, "Diag.CurrentSubscriptionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSubscriptionCount, "Diag.CumulatedSubscriptionCount"),
        (VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_PublishingIntervalCount, "Diag.PublishingIntervalCount"),
    };

    public DiagnosticsView()
    {
        InitializeComponent();
        this.RequiredControl<ItemsControl>("RowsList").ItemsSource = Rows;
        this.RequiredControl<Button>("HideButton").Click += (_, _) => HideRequested?.Invoke();
        foreach ((NodeId _, string label) in s_targets)
        {
            Rows.Add(new DiagRow(label, "(loading…)"));
        }
        m_timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await PollAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// Binds the diagnostic view's "Publishes" sub-tab to the shared
    /// <see cref="PublishLogObserver"/> owned by <c>MainViewModel</c>.
    /// Idempotent — safe to call repeatedly.
    /// </summary>
    public void BindPublishLog(PublishLogObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        this.RequiredControl<ListBox>("PublishList").ItemsSource = observer.Entries;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Bind a live session; pass <c>null</c> to detach and stop polling.</summary>
    public void Bind(ManagedSession? session)
    {
        m_session = session;
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
    }

    private async Task PollAsync()
    {
        if (m_session is null)
        {
            return;
        }

        try
        {
            var ids = new List<ReadValueId>(s_targets.Length);
            foreach ((NodeId id, _) in s_targets)
            {
                ids.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Value });
            }
            ReadResponse resp = await m_session.ReadAsync(null, 0, TimestampsToReturn.Server,
                new ArrayOf<ReadValueId>(ids.ToArray()), CancellationToken.None).ConfigureAwait(true);
            for (int i = 0; i < s_targets.Length && i < resp.Results.Count; i++)
            {
                DataValue dv = resp.Results[i];
                string text = StatusCode.IsBad(dv.StatusCode)
                    ? $"(bad: {dv.StatusCode})"
                    : Format(dv.WrappedValue);
                Rows[i] = new DiagRow(s_targets[i].Label, text);
            }
            this.RequiredControl<TextBlock>("StatusLabel").Text =
                $"Last poll: {DateTime.Now:HH:mm:ss.fff}  ({resp.Results.Count} attrs)";
        }
        catch (Exception ex)
        {
            this.RequiredControl<TextBlock>("StatusLabel").Text = "Poll failed: " + ex.Message;
        }
    }

    private static string Format(Variant v)
    {
        if (v.IsNull)
        {
            return "(null)";
        }

        object? boxed = v.AsBoxedObject();
        return boxed switch
        {
            null => "(null)",
            string s => s,
            LocalizedText l => l.Text ?? string.Empty,
            QualifiedName q => q.ToString() ?? string.Empty,
            DateTime dt => dt.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => boxed.ToString() ?? string.Empty
        };
    }
}

/// <summary>One row in the live diagnostics table.</summary>
internal sealed record DiagRow(string Name, string Value);

