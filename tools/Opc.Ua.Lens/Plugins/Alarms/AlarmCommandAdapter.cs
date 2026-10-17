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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client.Alarms;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Bounded, selection-scoped method evidence and typed Part 9 commands. Metadata
/// is read afresh before each command rather than cached across reconnects.
/// </summary>
internal sealed class AlarmCommandAdapter
{
    public AlarmCommandAdapter(
        ISessionClient session,
        NamespaceTable namespaceUris,
        ITelemetryContext telemetry,
        IAlarmOperations? operations = null,
        IDialogConditionOperations? dialogs = null)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_namespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
        ArgumentNullException.ThrowIfNull(telemetry);
        var fallback = new AlarmClient(session, telemetry);
        m_operations = operations ?? fallback;
        m_dialogs = dialogs ?? fallback;
    }

    public async ValueTask<ArrayOf<AlarmOperation>> InspectAsync(
        AlarmCondition condition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        cancellationToken.ThrowIfCancellationRequested();
        var paths = new List<BrowsePath>(s_operations.Count + 1);
        foreach (AlarmOperationKind kind in s_operations)
        {
            paths.Add(MakePath(condition.Key.ConditionId, kind));
        }
        paths.Add(new BrowsePath
        {
            StartingNode = condition.Key.ConditionId,
            RelativePath = new RelativePath { Elements = [Element(BrowseNames.ShelvingState)] }
        });
        TranslateBrowsePathsToNodeIdsResponse translated = await m_session.TranslateBrowsePathsToNodeIdsAsync(
            null, [.. paths], cancellationToken).ConfigureAwait(false);
        if (StatusCode.IsBad(translated.ResponseHeader.ServiceResult))
        {
            throw new ServiceResultException(translated.ResponseHeader.ServiceResult);
        }
        if (translated.Results.Count != paths.Count)
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "The alarm method lookup was incomplete.");
        }
        NodeId shelvingId = Resolve(translated.Results[^1]);
        var methods = new NodeId[s_operations.Count];
        var reads = new List<ReadValueId>(s_operations.Count * 2);
        for (int i = 0; i < s_operations.Count; i++)
        {
            methods[i] = Resolve(translated.Results[i]);
            reads.Add(new ReadValueId { NodeId = methods[i], AttributeId = Attributes.Executable });
            reads.Add(new ReadValueId { NodeId = methods[i], AttributeId = Attributes.UserExecutable });
        }
        ReadResponse response = await m_session.ReadAsync(
            null, 0, TimestampsToReturn.Neither, [.. reads], cancellationToken).ConfigureAwait(false);
        if (StatusCode.IsBad(response.ResponseHeader.ServiceResult))
        {
            throw new ServiceResultException(response.ResponseHeader.ServiceResult);
        }
        if (response.Results.Count != reads.Count)
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError,
                "The alarm method permission read was incomplete.");
        }
        var result = new List<AlarmOperation>(s_operations.Count);
        for (int i = 0; i < s_operations.Count; i++)
        {
            AlarmOperationKind kind = s_operations[i];
            NodeId objectId = IsShelving(kind) ? shelvingId : condition.Key.ConditionId;
            string? unavailable = Ineligible(condition, kind);
            AlarmAvailability availability;
            string reason;
            if (unavailable is not null)
            {
                availability = AlarmAvailability.Unsupported;
                reason = unavailable;
            }
            else if (translated.Results[i].StatusCode == StatusCodes.BadUserAccessDenied)
            {
                availability = AlarmAvailability.Denied;
                reason = "The server denied method discovery for this identity.";
            }
            else if (StatusCode.IsBad(translated.Results[i].StatusCode) &&
                translated.Results[i].StatusCode != StatusCodes.BadNoMatch &&
                translated.Results[i].StatusCode != StatusCodes.BadNodeIdUnknown &&
                translated.Results[i].StatusCode != StatusCodes.BadNodeIdInvalid &&
                translated.Results[i].StatusCode != StatusCodes.BadBrowseNameInvalid)
            {
                availability = AlarmAvailability.Unavailable;
                reason = $"Method discovery failed: {translated.Results[i].StatusCode}.";
            }
            else if (methods[i].IsNull || objectId.IsNull)
            {
                availability = IsBasic(kind) ? AlarmAvailability.Unknown : AlarmAvailability.Unsupported;
                reason = IsBasic(kind)
                    ? "The condition/method is not exposed. An explicit Part 9 call may still be supported."
                    : "No local instance method (and required state-machine object) was resolved.";
            }
            else
            {
                (availability, reason) = ReadAvailability(response.Results[i * 2], response.Results[(i * 2) + 1]);
            }
            result.Add(new AlarmOperation(kind, availability, reason, objectId));
        }
        return [.. result];
    }

    public async ValueTask ExecuteAsync(AlarmCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);
        ArrayOf<AlarmOperation> operations = await InspectAsync(command.Condition, cancellationToken)
            .ConfigureAwait(false);
        AlarmOperation? selected = null;
        foreach (AlarmOperation operation in operations)
        {
            if (operation.Kind == command.Operation)
            {
                selected = operation;
                break;
            }
        }
        if (selected is null || !selected.CanInvoke)
        {
            StatusCode code = selected?.Availability == AlarmAvailability.Denied
                ? StatusCodes.BadUserAccessDenied
                : selected?.Availability == AlarmAvailability.Unavailable
                    ? StatusCodes.BadInvalidState
                    : StatusCodes.BadNotSupported;
            throw new ServiceResultException(code, selected?.Reason ?? "No supported alarm operation was selected.");
        }
        NodeId conditionId = command.Condition.Key.ConditionId;
        ByteString eventId = command.Condition.EventId;
        var comment = new LocalizedText(command.Comment);
        switch (command.Operation)
        {
            case AlarmOperationKind.Acknowledge:
                await m_operations.AcknowledgeAsync(conditionId, eventId, comment, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AlarmOperationKind.Confirm:
                await m_operations.ConfirmAsync(conditionId, eventId, comment, cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.AddComment:
                await m_operations.AddCommentAsync(conditionId, eventId, comment, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AlarmOperationKind.Enable:
                await m_operations.EnableAsync(conditionId, cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.Disable:
                await m_operations.DisableAsync(conditionId, cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.TimedShelve:
                // Shelving methods belong to the ShelvingState object, not the condition.
                await m_operations.TimedShelveAsync(selected.ObjectId, command.ShelvingMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AlarmOperationKind.OneShotShelve:
                await m_operations.OneShotShelveAsync(selected.ObjectId, cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.Unshelve:
                await m_operations.UnshelveAsync(selected.ObjectId, cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.Reset:
                await m_operations.ResetAsync(conditionId, ct: cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.Suppress:
                await m_operations.SuppressAsync(conditionId, ct: cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.Unsuppress:
                await m_operations.UnsuppressAsync(conditionId, ct: cancellationToken).ConfigureAwait(false);
                break;
            case AlarmOperationKind.Respond:
                await m_dialogs.RespondAsync(conditionId, command.ResponseIndex, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    internal static void Validate(AlarmCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Condition);
        if (command.Condition.Key.ConditionId.IsNull || command.Condition.EventId.IsNull ||
            command.Condition.EventId.Length is 0 or > AlarmLimits.EventIdLength)
        {
            throw new ArgumentException("A real ConditionId and its latest EventId are required.", nameof(command));
        }
        if (command.Comment is null || command.Comment.Length > AlarmLimits.TextLength ||
            (command.Operation == AlarmOperationKind.AddComment && string.IsNullOrWhiteSpace(command.Comment)))
        {
            throw new ArgumentException("Enter a comment of at most 2048 characters.", nameof(command));
        }
        if (command.Operation == AlarmOperationKind.TimedShelve &&
            (!double.IsFinite(command.ShelvingMilliseconds) || command.ShelvingMilliseconds is <= 0 or > 86400000 ||
                (command.Condition.MaxTimeShelved is > 0 &&
                    command.ShelvingMilliseconds > command.Condition.MaxTimeShelved)))
        {
            throw new ArgumentException(
                "The shelving duration exceeds the server or document limit (one day).", nameof(command));
        }
        if (command.Operation == AlarmOperationKind.Respond &&
            (command.ResponseIndex < 0 || command.ResponseIndex >= command.Condition.Responses.Count))
        {
            throw new ArgumentException("Select one of this dialog's response options.", nameof(command));
        }
    }

    private NodeId Resolve(BrowsePathResult result)
    {
        if (StatusCode.IsBad(result.StatusCode) || result.Targets.Count != 1 ||
            result.Targets[0].RemainingPathIndex != uint.MaxValue || result.Targets[0].TargetId.ServerIndex != 0)
        {
            return NodeId.Null;
        }
        return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, m_namespaceUris);
    }

    private static (AlarmAvailability Availability, string Reason) ReadAvailability(
        in DataValue executable,
        in DataValue userExecutable)
    {
        if (executable.StatusCode == StatusCodes.BadUserAccessDenied ||
            userExecutable.StatusCode == StatusCodes.BadUserAccessDenied ||
            (StatusCode.IsGood(userExecutable.StatusCode) &&
                userExecutable.WrappedValue.TryGetValue(out bool userCanExecute) && !userCanExecute))
        {
            return (AlarmAvailability.Denied, "UserExecutable is false, or its read was denied for this identity.");
        }
        if (StatusCode.IsGood(executable.StatusCode) &&
            executable.WrappedValue.TryGetValue(out bool canExecute) && !canExecute)
        {
            return (AlarmAvailability.Unavailable, "The method is not executable in the current server state.");
        }
        return StatusCode.IsGood(executable.StatusCode) && StatusCode.IsGood(userExecutable.StatusCode) &&
            executable.WrappedValue.TryGetValue(out bool enabled) && enabled &&
            userExecutable.WrappedValue.TryGetValue(out bool authorized) && authorized
            ? (AlarmAvailability.Supported, "The instance method is present, Executable and UserExecutable.")
            : (AlarmAvailability.Unknown, "Method executable/permission evidence is unavailable.");
    }

    private static string? Ineligible(AlarmCondition condition, AlarmOperationKind kind)
    {
        if (kind == AlarmOperationKind.Acknowledge &&
            condition.Kind is not (AlarmConditionKind.Alarm or AlarmConditionKind.Acknowledgeable))
        {
            return "This is not an acknowledgeable condition.";
        }
        if (kind == AlarmOperationKind.Confirm && !condition.Confirmed.HasValue)
        {
            return "This condition did not expose ConfirmedState.";
        }
        if (kind == AlarmOperationKind.Respond &&
            (condition.Kind != AlarmConditionKind.Dialog || condition.Responses.Count == 0))
        {
            return "No bounded dialog response options were reported.";
        }
        if ((kind is AlarmOperationKind.TimedShelve or AlarmOperationKind.OneShotShelve or AlarmOperationKind.Unshelve or
            AlarmOperationKind.Reset or AlarmOperationKind.Suppress or AlarmOperationKind.Unsuppress) &&
            condition.Kind != AlarmConditionKind.Alarm)
        {
            return "This operation requires an alarm condition.";
        }
        if (!IsBasic(kind) && !condition.Key.BranchId.IsNull)
        {
            return "Select the current branch for operations affecting the entire condition.";
        }
        return null;
    }

    private static bool IsBasic(AlarmOperationKind kind)
    {
        return kind is AlarmOperationKind.Acknowledge or AlarmOperationKind.Confirm or AlarmOperationKind.AddComment;
    }

    private static bool IsShelving(AlarmOperationKind kind)
    {
        return kind is AlarmOperationKind.TimedShelve or
            AlarmOperationKind.OneShotShelve or AlarmOperationKind.Unshelve;
    }

    private static BrowsePath MakePath(NodeId conditionId, AlarmOperationKind kind)
    {
        string name = kind.ToString();
        return new BrowsePath
        {
            StartingNode = conditionId,
            RelativePath = new RelativePath
            {
                Elements = IsShelving(kind)
                    ? [Element(BrowseNames.ShelvingState), Element(name)]
                    : [Element(name)]
            }
        };
    }

    private static RelativePathElement Element(string name)
    {
        return new RelativePathElement
        {
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            IncludeSubtypes = true,
            TargetName = QualifiedName.From(name)
        };
    }

    private static readonly ArrayOf<AlarmOperationKind> s_operations =
    [
        AlarmOperationKind.Acknowledge,
        AlarmOperationKind.Confirm,
        AlarmOperationKind.AddComment,
        AlarmOperationKind.Enable,
        AlarmOperationKind.Disable,
        AlarmOperationKind.TimedShelve,
        AlarmOperationKind.OneShotShelve,
        AlarmOperationKind.Unshelve,
        AlarmOperationKind.Reset,
        AlarmOperationKind.Suppress,
        AlarmOperationKind.Unsuppress,
        AlarmOperationKind.Respond
    ];

    private readonly ISessionClient m_session;
    private readonly NamespaceTable m_namespaceUris;
    private readonly IAlarmOperations m_operations;
    private readonly IDialogConditionOperations m_dialogs;
}
