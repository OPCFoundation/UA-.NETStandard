/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.Historian;

/// <summary>Operating mode for the Historian tab — selects which OPC UA
/// HistoryRead variant is dispatched on <see cref="HistorianPlugin.ReadCommand"/>.</summary>
internal enum HistorianReadMode
{
    Raw,
    Processed,
    AtTime
}

/// <summary>Time-range quick-pick units backing the Last-N quick range.</summary>
internal enum HistorianTimeUnit
{
    Minutes,
    Hours,
    Days
}

/// <summary>One row in the aggregate-type ComboBox.</summary>
internal sealed class AggregateOption
{
    public required string DisplayName { get; init; }
    public required NodeId NodeId { get; init; }
    public override string ToString() => DisplayName;
}

/// <summary>
/// One row in the At Time list.  Three visual states driven by
/// <see cref="IsAddButton"/> and <see cref="IsEditing"/>:
/// <list type="bullet">
/// <item><c>IsAddButton=true</c> — trailing sentinel; renders as a `+` button.</item>
/// <item><c>IsAddButton=false, IsEditing=true</c> — editable picker + ✓ confirm.</item>
/// <item><c>IsAddButton=false, IsEditing=false</c> — read-only label + ✗ remove.</item>
/// </list>
/// </summary>
internal sealed partial class AtTimeRow : ObservableObject
{
    [ObservableProperty]
    private DateTime m_timestamp = DateTime.UtcNow;

    /// <summary>True while this row is being edited (picker shown).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditor))]
    [NotifyPropertyChangedFor(nameof(IsLabel))]
    private bool m_isEditing = true;

    /// <summary>True when this row is the trailing sentinel that renders as `+`.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditor))]
    [NotifyPropertyChangedFor(nameof(IsLabel))]
    private bool m_isAddButton;

    public bool IsEditor => !IsAddButton && IsEditing;
    public bool IsLabel => !IsAddButton && !IsEditing;

    public required System.Windows.Input.ICommand RemoveCommand { get; init; }
    public required System.Windows.Input.ICommand ConfirmCommand { get; init; }
}

/// <summary>
/// View model for a single Historian tab.  Owns:
/// <list type="bullet">
///   <item>Target <see cref="NodeId"/> to read history for.</item>
///   <item>Selected <see cref="HistorianReadMode"/> + mode-specific config.</item>
///   <item>Time range — either Last-N or custom Start/End ISO-8601 strings.</item>
///   <item>The materialised <see cref="Rows"/> collection consumed by the
///   view's table + ScottPlot line chart.</item>
/// </list>
/// All OPC UA traffic is dispatched through <see cref="HistoryReader"/>
/// and <see cref="HistoryUpdater"/> against the live session — the
/// session reference is re-fetched on every call because the underlying
/// <see cref="ConnectionService"/> may reconnect under us.
/// </summary>
internal sealed partial class HistorianPlugin : ObservableObject, IPlugin
{
    private static int s_nextNumber;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private HistorianView? m_view;
    private CancellationTokenSource? m_readCts;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "● Idle — pick a Variable and click Read.";

    // ---- Target ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
    [NotifyPropertyChangedFor(nameof(TargetDescription))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    private NodeId targetNodeId = NodeId.Null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetDescription))]
    private string m_targetDisplayName = string.Empty;

    /// <summary>True when a non-null target NodeId has been picked.</summary>
    public bool HasTarget => !TargetNodeId.IsNull;

    public string TargetDescription
    {
        get
        {
            if (TargetNodeId.IsNull)
            {
                return "(no target — Pick Variable… or select one in the address-space tree)";
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0}  •  {1}  ·  Range: {2}",
                TargetDisplayName, TargetNodeId, FormatRange());
        }
    }

    // ---- Read mode + per-mode config ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRawMode))]
    [NotifyPropertyChangedFor(nameof(IsProcessedMode))]
    [NotifyPropertyChangedFor(nameof(IsAtTimeMode))]
    [NotifyPropertyChangedFor(nameof(SelectedReadModeIndex))]
    private HistorianReadMode m_readMode = HistorianReadMode.Raw;

    public bool IsRawMode => ReadMode == HistorianReadMode.Raw;
    public bool IsProcessedMode => ReadMode == HistorianReadMode.Processed;
    public bool IsAtTimeMode => ReadMode == HistorianReadMode.AtTime;

    /// <summary>
    /// Int adapter for the TabControl's SelectedIndex binding.  Keeps the
    /// XAML free of value converters (which AOT compiles awkwardly).
    /// </summary>
    public int SelectedReadModeIndex
    {
        get => (int)ReadMode;
        set
        {
            if (Enum.IsDefined(typeof(HistorianReadMode), value))
            {
                ReadMode = (HistorianReadMode)value;
            }
        }
    }

    // Raw options.

    [ObservableProperty]
    private bool m_returnBounds;

    [ObservableProperty]
    private bool m_isReadModified;

    [ObservableProperty]
    private uint m_numValuesPerNode = 1000;

    // Processed options.

    public IReadOnlyList<AggregateOption> AggregateOptions { get; } =
        BuildAggregateOptions();

    [ObservableProperty]
    private AggregateOption m_selectedAggregate;

    [ObservableProperty]
    private double m_processingIntervalMs = 1000;

    // AtTime timestamps.

    public ObservableCollection<AtTimeRow> AtTimes { get; } = new();

    // ---- Time range ----
    //
    // Custom range is the single source of truth.  "Last N units" is
    // a quick-fill action driven by SetLastRangeDialog which writes
    // Start = UtcNow - span; End = UtcNow.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetDescription))]
    private DateTime m_customStart = DateTime.UtcNow.AddHours(-1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetDescription))]
    private DateTime m_customEnd = DateTime.UtcNow;

    // ---- Results ----

    public ObservableCollection<HistoryRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private HistoryRow? m_selectedRow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelReadCommand))]
    private bool m_isReading;

    public HistorianPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n = Interlocked.Increment(ref s_nextNumber);
        m_title = string.Create(CultureInfo.InvariantCulture, $"Historian {n}");
        m_selectedAggregate = AggregateOptions[0];
        // Seed with the trailing "+" sentinel only.  Users add rows by
        // clicking the sentinel; each added row starts in editing mode.
        AtTimes.Add(NewSentinelRow());
        AtTimes.CollectionChanged += OnAtTimesChanged;
        // Seed the target from the current address-space selection if any.
        if (m_host.Main.SelectedNode is { } sel && sel.NodeClass == NodeClass.Variable)
        {
            TargetNodeId = sel.NodeId;
            TargetDisplayName = sel.Text;
        }
    }

    private void OnAtTimesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ReadCommand.NotifyCanExecuteChanged();
    }

    private static List<AggregateOption> BuildAggregateOptions()
    {
        return new List<AggregateOption>
        {
            new() { DisplayName = "Average",  NodeId = new NodeId(Objects.AggregateFunction_Average)  },
            new() { DisplayName = "Minimum",  NodeId = new NodeId(Objects.AggregateFunction_Minimum)  },
            new() { DisplayName = "Maximum",  NodeId = new NodeId(Objects.AggregateFunction_Maximum)  },
            new() { DisplayName = "Count",    NodeId = new NodeId(Objects.AggregateFunction_Count)    },
            new() { DisplayName = "Total",    NodeId = new NodeId(Objects.AggregateFunction_Total)    },
            new() { DisplayName = "Start",    NodeId = new NodeId(Objects.AggregateFunction_Start)    },
            new() { DisplayName = "End",      NodeId = new NodeId(Objects.AggregateFunction_End)      },
            new() { DisplayName = "Delta",    NodeId = new NodeId(Objects.AggregateFunction_Delta)    }
        };
    }

    // ---- IPlugin ----

    public PluginKind Kind => PluginKind.Historian;

    Control? IPlugin.View => m_view ??= new HistorianView { DataContext = this };

    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        return new[]
        {
            CreateMenuItem("_Read",                ReadCommand),
            CreateMenuItem("_Pick Variable…",      PickVariableCommand),
            CreateMenuItem("_Range…",              PickRangeCommand),
            CreateMenuItem("_Edit Selected…",      EditSelectedCommand),
            CreateMenuItem("_Delete Selected",     DeleteSelectedCommand),
            CreateMenuItem("Export _CSV…",         ExportCsvCommand)
        };
    }

    private static MenuItem CreateMenuItem(string header, System.Windows.Input.ICommand cmd)
        => new() { Header = header, Command = cmd };

    public void OnActivated() { }
    public void OnDeactivated() { }

    public ValueTask DisposeAsync()
    {
        try
        {
            m_readCts?.Cancel();
            m_readCts?.Dispose();
            m_readCts = null;
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Historian tab {Title} dispose failed.", Title);
        }
        return ValueTask.CompletedTask;
    }

    // ---- Commands ----

    [RelayCommand]
    private async Task PickVariableAsync()
    {
        if (m_host.Connection.Session is not { } session)
        {
            Status = "● Connect to a server first.";
            return;
        }

        // Smart default: when the address-space tree is visible AND a
        // suitable Variable is already selected (Historizing=true),
        // use it directly instead of opening the picker.
        if (m_host.Main.IsAddressSpaceVisible
            && m_host.Main.SelectedNode is { NodeClass: NodeClass.Variable } sel
            && await IsHistorizingAsync(session, sel.NodeId, CancellationToken.None).ConfigureAwait(true))
        {
            TargetNodeId = sel.NodeId;
            TargetDisplayName = sel.Text;
            Status = $"● Target set: {sel.Text}";
            return;
        }

        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            Status = "● No owner window.";
            return;
        }

        var picker = new BrowsePickerDialog(new BrowsePickerDialog.Options(
            Session: session,
            Root: ObjectIds.ObjectsFolder,
            Title: "Pick historising variable",
            AcceptedClasses: NodeClass.Variable,
            AcceptPredicate: async (id, cls) =>
            {
                if (cls != NodeClass.Variable)
                {
                    return false;
                }
                return await IsHistorizingAsync(session, id, CancellationToken.None).ConfigureAwait(true);
            },
            Header: "Browse the address space and pick a Variable whose Historizing attribute is true."));
        NodeId? picked = await picker.ShowDialog<NodeId?>(owner).ConfigureAwait(true);
        if (!picked.HasValue || picked.Value.IsNull)
        {
            return;
        }
        TargetNodeId = picked.Value;
        TargetDisplayName = picker.PickedDisplay;
        Status = $"● Target set: {picker.PickedDisplay}";
    }

    /// <summary>
    /// Reads the <see cref="Attributes.Historizing"/> attribute and
    /// returns true when the attribute reads back as true.  Variables
    /// whose Historizing attribute is unsupported (BadAttributeIdInvalid)
    /// are filtered out — this is the strict reading of "historising"
    /// the user requested.
    /// </summary>
    private static async Task<bool> IsHistorizingAsync(
        ManagedSession session, NodeId nodeId, CancellationToken ct)
    {
        try
        {
            ArrayOf<ReadValueId> ids = new ReadValueId[]
            {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Historizing }
            };
            ReadResponse resp = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, ids, ct).ConfigureAwait(false);
            if (resp.Results.Count == 0)
            {
                return false;
            }
            if (StatusCode.IsBad(resp.Results[0].StatusCode))
            {
                return false;
            }
            return resp.Results[0].WrappedValue.TryGetValue(out bool historizing) && historizing;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private void AddTimestamp()
    {
        InsertEditableBeforeSentinel();
    }

    [RelayCommand]
    private async Task PickRangeAsync()
    {
        Window? owner = GetOwnerWindow();
        var dlg = new RangeDialog(CustomStart, CustomEnd);
        (DateTime Start, DateTime End)? result;
        if (owner is not null)
        {
            result = await dlg.ShowDialog<(DateTime, DateTime)?>(owner).ConfigureAwait(true);
        }
        else
        {
            return;
        }
        if (!result.HasValue)
        {
            return;
        }
        CustomStart = result.Value.Start;
        CustomEnd = result.Value.End;
    }

    private void RemoveTimestamp(AtTimeRow? row)
    {
        if (row is null || row.IsAddButton)
        {
            return;
        }

        AtTimes.Remove(row);
    }

    private void ConfirmTimestamp(AtTimeRow? row)
    {
        if (row is null || row.IsAddButton)
        {
            return;
        }
        row.IsEditing = false;
    }

    private void InsertEditableBeforeSentinel()
    {
        int sentinelIndex = -1;
        for (int i = 0; i < AtTimes.Count; i++)
        {
            if (AtTimes[i].IsAddButton)
            {
                sentinelIndex = i;
                break;
            }
        }
        AtTimeRow row = NewEditableRow();
        if (sentinelIndex < 0)
        {
            AtTimes.Add(row);
            AtTimes.Add(NewSentinelRow());
        }
        else
        {
            AtTimes.Insert(sentinelIndex, row);
        }
    }

    private AtTimeRow NewEditableRow()
    {
        AtTimeRow? capture = null;
        var row = new AtTimeRow
        {
            RemoveCommand = new RelayCommand(() => RemoveTimestamp(capture)),
            ConfirmCommand = new RelayCommand(() => ConfirmTimestamp(capture)),
            IsEditing = true
        };
        capture = row;
        return row;
    }

    private AtTimeRow NewSentinelRow()
    {
        // Sentinel reuses RemoveCommand to add a fresh editable row
        // before itself; the DataTemplate binds the "+" button to it.
        // ConfirmCommand is required by the record but unused.
        var sentinel = new AtTimeRow
        {
            IsAddButton = true,
            IsEditing = false,
            RemoveCommand = new RelayCommand(InsertEditableBeforeSentinel),
            ConfirmCommand = new RelayCommand(() => { })
        };
        return sentinel;
    }

    private bool CanRead() =>
        !IsReading
        && HasTarget
        && m_host.Main.Connection.Session is not null;

    [RelayCommand(CanExecute = nameof(CanRead))]
    private async Task ReadAsync()
    {
        if (m_host.Main.Connection.Session is not { } session)
        {
            Status = "● Not connected — connect first.";
            return;
        }
        if (TargetNodeId.IsNull)
        {
            Status = "● No target — Pick Variable… first.";
            return;
        }

        m_readCts?.Cancel();
        m_readCts?.Dispose();
        m_readCts = new CancellationTokenSource();
        CancellationToken ct = m_readCts.Token;

        var reader = new HistoryReader(session);
        IsReading = true;
        Status = "● Reading…";
        Dispatcher.UIThread.Post(() => Rows.Clear());

        try
        {
            List<HistoryRow> rows;
            switch (ReadMode)
            {
                case HistorianReadMode.Raw:
                {
                    (DateTime start, DateTime end) = ResolveRange();
                    rows = await reader.ReadRawAsync(
                        TargetNodeId, start, end,
                        ReturnBounds, IsReadModified, NumValuesPerNode, ct).ConfigureAwait(true);
                    Status = string.Format(CultureInfo.InvariantCulture,
                        "● {0} rows · raw mode · {1}",
                        rows.Count, FormatRange());
                    break;
                }
                case HistorianReadMode.Processed:
                {
                    (DateTime start, DateTime end) = ResolveRange();
                    rows = await reader.ReadProcessedAsync(
                        TargetNodeId,
                        SelectedAggregate.NodeId,
                        start, end,
                        ProcessingIntervalMs, ct).ConfigureAwait(true);
                    Status = string.Format(CultureInfo.InvariantCulture,
                        "● {0} rows · processed ({1}, {2}ms) · {3}",
                        rows.Count, SelectedAggregate.DisplayName, ProcessingIntervalMs, FormatRange());
                    break;
                }
                case HistorianReadMode.AtTime:
                {
                    var times = new List<DateTime>(AtTimes.Count);
                    foreach (AtTimeRow row in AtTimes)
                    {
                        // Skip the trailing "+" sentinel and any still-editing rows.
                        if (row.IsAddButton || row.IsEditing)
                        {
                            continue;
                        }
                        DateTime ts = row.Timestamp.Kind == DateTimeKind.Utc
                            ? row.Timestamp
                            : row.Timestamp.ToUniversalTime();
                        times.Add(ts);
                    }
                    if (times.Count == 0)
                    {
                        Status = "● Add at least one confirmed UTC timestamp.";
                        IsReading = false;
                        return;
                    }
                    rows = await reader.ReadAtTimeAsync(TargetNodeId, times, ct).ConfigureAwait(true);
                    Status = string.Format(CultureInfo.InvariantCulture,
                        "● {0} rows · at-time ({1} requested)", rows.Count, times.Count);
                    break;
                }
                default:
                    rows = new List<HistoryRow>();
                    break;
            }

            Dispatcher.UIThread.Post(() =>
            {
                foreach (HistoryRow r in rows)
                {
                    Rows.Add(r);
                }
            });
        }
        catch (OperationCanceledException)
        {
            Status = "● Read cancelled.";
        }
        catch (ServiceResultException ex)
        {
            Status = $"● Read failed: {ex.StatusCode} — {ex.Message}";
            m_log.LogWarning(ex, "Historian tab {Title} HistoryRead failed.", Title);
        }
        catch (Exception ex)
        {
            Status = $"● Read failed: {ex.Message}";
            m_log.LogWarning(ex, "Historian tab {Title} HistoryRead failed.", Title);
        }
        finally
        {
            IsReading = false;
        }
    }

    private bool CanCancelRead() => IsReading;

    [RelayCommand(CanExecute = nameof(CanCancelRead))]
    private void CancelRead()
    {
        m_readCts?.Cancel();
    }

    private bool CanModifyRow() => SelectedRow is not null && HasTarget;

    [RelayCommand(CanExecute = nameof(CanModifyRow))]
    private async Task EditSelectedAsync()
    {
        if (SelectedRow is not { } row || TargetNodeId.IsNull)
        {
            return;
        }

        if (m_host.Main.Connection.Session is not { } session)
        {
            Status = "● Not connected — connect first.";
            return;
        }

        Window? owner = GetOwnerWindow();
        var dialog = new EditHistoryRowDialog(TargetNodeId, row);
        EditHistoryRowResult? result = owner is null
            ? await dialog.ShowDialog<EditHistoryRowResult?>(new Window()).ConfigureAwait(true)
            : await dialog.ShowDialog<EditHistoryRowResult?>(owner).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        try
        {
            var updater = new HistoryUpdater(session);
            await updater.UpdateAsync(
                result.NodeId, result.Action, result.Timestamp,
                result.Value, result.Status, CancellationToken.None).ConfigureAwait(true);
            Status = $"● HistoryUpdate ({result.Action}) applied at {row.DisplayTimestamp}.";
        }
        catch (ServiceResultException ex)
        {
            Status = FriendlyError("HistoryUpdate", ex);
            m_log.LogWarning(ex, "Historian tab {Title} HistoryUpdate failed.", Title);
        }
        catch (Exception ex)
        {
            Status = $"● HistoryUpdate failed: {ex.Message}";
            m_log.LogWarning(ex, "Historian tab {Title} HistoryUpdate failed.", Title);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifyRow))]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedRow is not { } row || TargetNodeId.IsNull)
        {
            return;
        }

        if (m_host.Main.Connection.Session is not { } session)
        {
            Status = "● Not connected — connect first.";
            return;
        }
        try
        {
            var updater = new HistoryUpdater(session);
            await updater.DeleteAsync(TargetNodeId, row.SourceTimestamp, CancellationToken.None)
                .ConfigureAwait(true);
            Dispatcher.UIThread.Post(() => Rows.Remove(row));
            Status = $"● Deleted history row at {row.DisplayTimestamp}.";
        }
        catch (ServiceResultException ex)
        {
            Status = FriendlyError("HistoryDelete", ex);
            m_log.LogWarning(ex, "Historian tab {Title} HistoryDelete failed.", Title);
        }
        catch (Exception ex)
        {
            Status = $"● HistoryDelete failed: {ex.Message}";
            m_log.LogWarning(ex, "Historian tab {Title} HistoryDelete failed.", Title);
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }

        IStorageProvider? storage = owner.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var opts = new FilePickerSaveOptions
        {
            Title = "Export history rows",
            SuggestedFileName = string.Format(CultureInfo.InvariantCulture,
                "ualens-history-{0:yyyyMMdd-HHmmss}.csv", DateTime.UtcNow),
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } }
            }
        };
        IStorageFile? file = await storage.SaveFilePickerAsync(opts).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        try
        {
            Stream s = await file.OpenWriteAsync().ConfigureAwait(true);
            await using (s.ConfigureAwait(false))
            {
                var w = new StreamWriter(s, new UTF8Encoding(false));
                await using (w.ConfigureAwait(false))
                {
                    await w.WriteLineAsync("source_timestamp_utc,server_timestamp_utc,value,status").ConfigureAwait(true);
                    foreach (HistoryRow row in Rows)
                    {
                        await w.WriteLineAsync(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3}",
                            row.SourceTimestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                            row.ServerTimestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                            CsvEscape(row.DisplayValue),
                            row.DisplayStatus)).ConfigureAwait(true);
                    }
                }
            }
            Status = $"● Exported {Rows.Count} rows to {file.Name}.";
        }
        catch (Exception ex)
        {
            Status = $"● Export failed: {ex.Message}";
            m_log.LogWarning(ex, "Historian tab {Title} CSV export failed.", Title);
        }
    }

    // ---- Helpers ----

    private (DateTime Start, DateTime End) ResolveRange()
    {
        DateTime start = CustomStart.Kind == DateTimeKind.Utc ? CustomStart : CustomStart.ToUniversalTime();
        DateTime end = CustomEnd.Kind == DateTimeKind.Utc ? CustomEnd : CustomEnd.ToUniversalTime();
        return (start, end);
    }

    private string FormatRange()
    {
        (DateTime start, DateTime end) = ResolveRange();
        return string.Format(CultureInfo.InvariantCulture,
            "{0:yyyy-MM-ddTHH:mm:ssZ} → {1:yyyy-MM-ddTHH:mm:ssZ}",
            start, end);
    }

    private static readonly System.Buffers.SearchValues<char> s_csvQuoteChars =
        System.Buffers.SearchValues.Create(",\"\n\r");

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        bool quote = s.AsSpan().IndexOfAny(s_csvQuoteChars) >= 0;
        if (!quote)
        {
            return s;
        }

        return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string FriendlyError(string op, ServiceResultException ex)
    {
        // StatusCodes.* constants are static readonly StatusCode (not const),
        // so they can't be used as switch case labels.  Use plain if/else
        // and compare on the raw uint code instead.
        uint code = ex.StatusCode.Code;
        if (code == StatusCodes.BadHistoryOperationUnsupported.Code)
        {
            return $"● {op} unsupported by server (BadHistoryOperationUnsupported).";
        }
        if (code == StatusCodes.BadNotSupported.Code)
        {
            return $"● {op} not supported (BadNotSupported) — server may not enable HistoryWrite on this node.";
        }
        if (code == StatusCodes.BadUserAccessDenied.Code)
        {
            return $"● {op} denied (BadUserAccessDenied) — connect with a user that has HistoryWrite rights.";
        }
        return $"● {op} failed: {ex.StatusCode} — {ex.Message}";
    }

    private static Window? GetOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desk)
        {
            return desk.MainWindow;
        }
        return null;
    }
}
