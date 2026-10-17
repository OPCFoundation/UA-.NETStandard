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

using System.Globalization;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Bounded workstation idempotency ledger. Only an explicit exact-envelope retry can reissue a mutation.
/// </summary>
internal sealed class ConsoleCommands(
    IOperatorPlantFeed feed, ConsoleOptions options, ITelemetryContext? telemetry = null)
{
    /// <summary>
    /// Looks up a workstation receipt; no persistence/recovery promise is made across host restarts.
    /// </summary>
    internal CommandResult? Find(string requestId)
    {
        lock (m_gate)
        {
            return m_entries.TryGetValue(requestId, out Entry? entry) ? entry.Result : null;
        }
    }

    /// <summary>
    /// Validates and submits one explicit command, retaining ambiguous outcomes for deliberate retry.
    /// </summary>
    internal async Task<(CommandResult? Result, int Status, string Error)> SubmitAsync(
        CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(request.RequestId, "D", out _) ||
            !long.TryParse(request.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture,
                out long revision) ||
            !long.TryParse(request.HoldId, NumberStyles.None, CultureInfo.InvariantCulture, out long holdId) ||
            !Enum.TryParse(request.Kind, false, out RecyclingCommandKind kind) ||
            !Enum.IsDefined(kind) || kind.ToString() != request.Kind ||
            kind is RecyclingCommandKind.Register or RecyclingCommandKind.Initialize or
                RecyclingCommandKind.Heartbeat or RecyclingCommandKind.Disconnect)
        {
            return (null, 400, "Invalid command envelope or forbidden membership operation.");
        }
        bool equipment = kind is RecyclingCommandKind.EnableProcess or RecyclingCommandKind.DisableProcess;
        bool dispositionCommand = kind == RecyclingCommandKind.DisposeHold;
        bool operatorCommand = equipment || dispositionCommand;
        if (options.Role == "Observer" || (!operatorCommand && options.Role != "SimulatorAdministrator"))
        {
            return (null, 403, "Configured control ceiling denies this command.");
        }
        RecyclingProcess process = default;
        HoldDisposition disposition = default;
        if ((equipment && (!Enum.TryParse(request.Process, false, out process) || !Enum.IsDefined(process) ||
                process.ToString() != request.Process)) ||
            (!equipment && request.Process.Length != 0) ||
            (dispositionCommand && (holdId <= 0 ||
                !Enum.TryParse(request.Disposition, false, out disposition) ||
                disposition is not (HoldDisposition.Released or HoldDisposition.Rejected) ||
                disposition.ToString() != request.Disposition)) ||
            (!dispositionCommand && (holdId != 0 || request.Disposition.Length != 0)))
        {
            return (null, 400, "Fields do not match the selected command.");
        }
        if (kind is RecyclingCommandKind.DisableProcess or RecyclingCommandKind.DisposeHold or
            RecyclingCommandKind.Stop or RecyclingCommandKind.Reset && !request.Confirmed)
        {
            return (null, 400, "Explicit destructive-action confirmation is required.");
        }
        CommandRequest envelope = request with { Retry = false };
        var command = new RecyclingCommand
        {
            RequestId = request.RequestId,
            LineId = options.LineId,
            RunId = request.RunId,
            ExpectedRevision = revision,
            Kind = kind,
            Process = process,
            HoldId = holdId,
            Disposition = disposition,
            Steps = kind == RecyclingCommandKind.Step ? 1 : 0
        };
        Entry entry;
        lock (m_gate)
        {
            if (m_entries.TryGetValue(request.RequestId, out Entry? existing))
            {
                if (existing.Envelope != envelope)
                {
                    return (existing.Result, 409, "Request ID already belongs to a different exact envelope.");
                }
                if (!request.Retry || existing.Result.State != "Unknown")
                {
                    return (existing.Result, 200, string.Empty);
                }
                entry = existing;
            }
            else
            {
                if (request.Retry)
                {
                    return (null, 409, "No original envelope exists after restart; retry cannot be recovered.");
                }
                CommittedSnapshot? snapshot = feed.Snapshot;
                if (!feed.State.Available || snapshot == null)
                {
                    return (null, 503, "No usable coordinator state.");
                }
                if (snapshot.RunId != request.RunId || revision > snapshot.Revision)
                {
                    return (null, 409, "Stale run or expected revision; inspect current state before a new action.");
                }
                if ((kind == RecyclingCommandKind.Reset && snapshot.Lifecycle != RunLifecycle.Stopped) ||
                    (kind == RecyclingCommandKind.Step && snapshot.Lifecycle != RunLifecycle.Paused))
                {
                    return (null, 409, "Lifecycle does not allow this command.");
                }
                if (m_entries.Values.Any(item => item.Result.State == "Pending"))
                {
                    return (null, 409, "Another command is pending; wait for its outcome.");
                }
                if (m_entries.Count >= 128)
                {
                    Entry? oldest = m_entries.Values.FirstOrDefault(item =>
                        item.Result.State is "Accepted" or "Rejected");
                    if (oldest == null)
                    {
                        return (null, 429, "Unresolved command ledger is full; no ambiguous receipt was discarded.");
                    }
                    m_entries.Remove(oldest.Envelope.RequestId);
                }
                entry = new Entry(envelope, command, Pending(request));
                m_entries.Add(request.RequestId, entry);
            }
            if (m_entries.Values.Any(item => item != entry && item.Result.State == "Pending"))
            {
                return (entry.Result, 409, "Another command is pending.");
            }
            entry.Result = Pending(request);
            Publish();
        }
        try
        {
            // Browser disconnect cannot turn an in-flight mutation into a silent automatic reissue.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            RecyclingCommandOutcome outcome = await feed.ExecuteAsync(entry.Command, timeout.Token)
                .ConfigureAwait(false);
            lock (m_gate)
            {
                entry.Result = new CommandResult(request.RequestId,
                    StatusCode.IsGood(new StatusCode(outcome.Status)) ? "Accepted" :
                        StatusCode.IsBad(new StatusCode(outcome.Status)) ? "Rejected" : "Unknown",
                    request.Kind, request.RunId, request.ExpectedRevision,
                    ConsoleFeed.Text(outcome.Revision), ConsoleFeed.Text(outcome.Tick),
                    new StatusCode(outcome.Status).ToString(), outcome.Message, DateTime.UtcNow);
                Publish();
                return (entry.Result, 200, string.Empty);
            }
        }
        catch (ServiceResultException exception) when (IsPreCommitRejection(exception.StatusCode))
        {
            ConsoleLog.CommandFailed(telemetry.CreateLogger<ConsoleCommands>(),
                exception.StatusCode.ToString(), exception);
            lock (m_gate)
            {
                entry.Result = entry.Result with
                {
                    State = "Rejected",
                    Status = exception.StatusCode.ToString(),
                    Message = "Coordinator explicitly rejected the command: " + exception.StatusCode,
                    UpdatedUtc = DateTime.UtcNow
                };
                Publish();
                return (entry.Result, 200, string.Empty);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string status = exception is ServiceResultException service
                ? service.StatusCode.ToString()
                : exception is OperationCanceledException or TimeoutException ? "BadTimeout" : "BadCommunicationError";
            ConsoleLog.CommandFailed(telemetry.CreateLogger<ConsoleCommands>(), status, exception);
            lock (m_gate)
            {
                entry.Result = entry.Result with
                {
                    State = "Unknown",
                    Status = status,
                    Message = "Outcome unknown. No automatic reissue. An explicit retry must use this exact envelope.",
                    UpdatedUtc = DateTime.UtcNow
                };
                Publish();
                return (entry.Result, 200, string.Empty);
            }

        }
    }

    /// <summary>
    /// Only known validation/authorization failures establish that a mutation was rejected before commit.
    /// </summary>
    private static bool IsPreCommitRejection(StatusCode status) =>
        status == StatusCodes.BadInvalidArgument || status == StatusCodes.BadInvalidState ||
        status == StatusCodes.BadOutOfRange || status == StatusCodes.BadUserAccessDenied ||
        status == StatusCodes.BadIdentityTokenRejected || status == StatusCodes.BadNotSupported ||
        status == StatusCodes.BadTooManyOperations;

    /// <summary>
    /// Creates a non-authoritative pending workstation receipt.
    /// </summary>
    private static CommandResult Pending(CommandRequest request) => new(
        request.RequestId, "Pending", request.Kind, request.RunId, request.ExpectedRevision,
        null, null, "Pending", "Awaiting authoritative coordinator outcome.", DateTime.UtcNow);

    /// <summary>
    /// Projects a bounded most-recent-first receipt list.
    /// </summary>
    private void Publish() => feed.SetCommands(
        m_entries.Values.Select(static item => item.Result)
            .OrderByDescending(static item => item.UpdatedUtc).ToArray());

    /// <summary>
    /// Retains the original normalized transport command and original browser envelope.
    /// </summary>
    private sealed class Entry(CommandRequest envelope, RecyclingCommand command, CommandResult result)
    {
        /// <summary>
        /// Original browser request without the retry switch.
        /// </summary>
        internal CommandRequest Envelope { get; } = envelope;

        /// <summary>
        /// Original exact coordinator request.
        /// </summary>
        internal RecyclingCommand Command { get; } = command;

        /// <summary>
        /// Latest honest outcome.
        /// </summary>
        internal CommandResult Result { get; set; } = result;
    }

    /// <summary>Serializes ledger changes across browser tabs.</summary>
    private readonly System.Threading.Lock m_gate = new();
    /// <summary>Bounded original envelopes and their honest outcomes.</summary>
    private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
}
