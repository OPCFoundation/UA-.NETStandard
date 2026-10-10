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
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Plugins.Alarms;

internal sealed record AlarmSource(string TargetId, string Name);

internal readonly record struct AlarmKey(NodeId ConditionId, NodeId BranchId);

internal enum AlarmConditionKind
{
    Condition,
    Acknowledgeable,
    Alarm,
    Dialog
}

/// <summary>
/// A bounded projection of one event. ConditionId is selected from the condition
/// object's NodeId attribute, never inferred from the event's SourceNode.
/// </summary>
internal sealed record AlarmCondition(
    AlarmKey Key,
    ByteString EventId,
    NodeId EventType,
    NodeId SourceNode,
    string SourceName,
    string ConditionName,
    string Message,
    ushort? Severity,
    DateTime? Time,
    DateTime? ReceiveTime,
    bool? Enabled,
    bool? Active,
    bool? Acknowledged,
    bool? Confirmed,
    bool Retain,
    StatusCode Quality,
    AlarmConditionKind Kind)
{
    public bool? Suppressed { get; init; }

    public bool? Latched { get; init; }

    public bool? DialogActive { get; init; }

    public double? MaxTimeShelved { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public ArrayOf<string> Responses { get; init; }
}

internal sealed record AlarmRow(AlarmCondition Condition, uint PartitionId, bool IsStale, long Revision)
{
    public string ConditionId => Condition.Key.ConditionId.ToString();

    public string BranchId => Condition.Key.BranchId.IsNull ? "(current)" : Condition.Key.BranchId.ToString();

    public string EventId => Condition.EventId.ToBase64();

    public string SourceNode => Condition.SourceNode.ToString();

    public string Source => string.IsNullOrEmpty(Condition.SourceName) ? SourceNode : Condition.SourceName;

    public string Name => string.IsNullOrEmpty(Condition.ConditionName) ? ConditionId : Condition.ConditionName;

    public string Severity => Condition.Severity?.ToString(CultureInfo.InvariantCulture) ?? "?";

    public string Enabled => Display(Condition.Enabled);

    public string Active => Display(Condition.Active);

    public string Acknowledged => Display(Condition.Acknowledged);

    public string Confirmed => Display(Condition.Confirmed);

    public string Retain => Display(Condition.Retain);

    public string Suppressed => Display(Condition.Suppressed);

    public string Latched => Display(Condition.Latched);

    public string Quality => Condition.Quality.ToString();

    public string Time => Condition.Time?.ToString("O", CultureInfo.InvariantCulture) ?? "unavailable";

    public string ReceiveTime => Condition.ReceiveTime?.ToString("O", CultureInfo.InvariantCulture) ?? "unavailable";

    public string Freshness => IsStale ? "Unreconciled" : "Observed";

    public string Message => Condition.Message;

    private static string Display(bool? value)
    {
        return value.HasValue ? value.Value ? "Yes" : "No" : "?";
    }
}

internal enum AlarmUpdateKind
{
    Condition,
    RefreshStart,
    RefreshEnd,
    RefreshRequired,
    Loss,
    Created,
    Recovered,
    Unavailable
}

internal sealed record AlarmUpdate(
    AlarmUpdateKind Kind,
    uint PartitionId = 0,
    AlarmCondition? Condition = null,
    string Detail = "");

internal enum AlarmRefreshState
{
    NotRequested,
    Requested,
    Receiving,
    Complete,
    Incomplete
}

internal sealed record AlarmHistoryEntry(DateTimeOffset Time, string Detail);

internal sealed record AlarmSnapshot(
    ArrayOf<AlarmRow> Conditions,
    ArrayOf<AlarmHistoryEntry> History,
    AlarmRefreshState RefreshState,
    string RefreshDetail,
    bool IsObserving,
    long DroppedUpdates,
    long EvictedConditions,
    long ReceivedEvents,
    long ObservationEpoch,
    long Revision);

internal enum AlarmOperationKind
{
    Acknowledge,
    Confirm,
    AddComment,
    Enable,
    Disable,
    TimedShelve,
    OneShotShelve,
    Unshelve,
    Reset,
    Suppress,
    Unsuppress,
    Respond
}

internal enum AlarmAvailability
{
    Unknown,
    Supported,
    Unsupported,
    Denied,
    Unavailable
}

/// <summary>
/// Read-only evidence for an operation. An unexposed condition can still accept
/// mandatory Part 9 methods; unknown is not evidence of either support or denial.
/// </summary>
internal sealed record AlarmOperation(
    AlarmOperationKind Kind,
    AlarmAvailability Availability,
    string Reason,
    NodeId ObjectId)
{
    public string Label => $"{Kind} — {Availability}";

    public bool CanInvoke => Availability == AlarmAvailability.Supported ||
        (Availability == AlarmAvailability.Unknown &&
            Kind is AlarmOperationKind.Acknowledge or AlarmOperationKind.Confirm or AlarmOperationKind.AddComment);
}

internal sealed record AlarmCommand(
    AlarmCondition Condition,
    AlarmOperationKind Operation,
    string Comment = "",
    double ShelvingMilliseconds = 60000,
    int ResponseIndex = 0);

internal enum AlarmCommandOutcome
{
    Accepted,
    Denied,
    Unsupported,
    Unavailable,
    Stale,
    Canceled,
    Failed
}

internal sealed record AlarmCommandResult(AlarmCommandOutcome Outcome, string Detail)
{
    public static AlarmCommandResult FromServiceResult(ServiceResultException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        StatusCode code = exception.StatusCode;
        AlarmCommandOutcome outcome = code == StatusCodes.BadUserAccessDenied ||
            code == StatusCodes.BadIdentityTokenRejected || code == StatusCodes.BadIdentityTokenInvalid ||
            code == StatusCodes.BadSecurityChecksFailed
            ? AlarmCommandOutcome.Denied
            : code == StatusCodes.BadNotSupported || code == StatusCodes.BadMethodInvalid ||
                code == StatusCodes.BadNotImplemented
                ? AlarmCommandOutcome.Unsupported
                : AlarmCommandOutcome.Failed;
        return new AlarmCommandResult(
            outcome, $"{outcome}: {exception.StatusCode}. {AlarmLimits.Text(exception.Message)}");
    }
}

internal sealed record AlarmStreamHealth(
    bool IsReady,
    string Detail,
    TimeSpan PublishingInterval,
    long DroppedUpdates,
    long MissingMessages,
    long RepublishRequests,
    ArrayOf<uint> PartitionIds);

/// <summary>
/// The stack/test seam. Opening is read-only apart from document-owned subscription
/// creation. There is exactly one reader per observation; mutations are explicit.
/// </summary>
internal interface IAlarmBackend
{
    ValueTask<IAlarmObservation> OpenAsync(
        AlarmSource source,
        TimeSpan publishingInterval,
        CancellationToken cancellationToken);
}

internal interface IAlarmObservation : IAsyncDisposable
{
    ChannelReader<AlarmUpdate> Updates { get; }

    AlarmStreamHealth Health { get; }

    ValueTask RefreshAsync(CancellationToken cancellationToken);

    ValueTask<ArrayOf<AlarmOperation>> InspectAsync(AlarmCondition condition, CancellationToken cancellationToken);

    ValueTask ExecuteAsync(AlarmCommand command, CancellationToken cancellationToken);
}

internal static class AlarmLimits
{
    public const int Conditions = 512;
    public const int History = 256;
    public const int PendingUpdates = 1024;
    public const int TextLength = 2048;
    public const int IdentifierLength = 4096;
    public const int EventIdLength = 4096;
    public const int ResponseCount = 32;
    public const int Partitions = 8;

    public static string Text(string? value)
    {
        return value is null ? string.Empty : value.Length <= TextLength ? value : value[..TextLength] + "…";
    }
}
