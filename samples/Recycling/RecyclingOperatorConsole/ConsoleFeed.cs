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
using System.Threading.Channels;
using Opc.Ua.Client;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// One DI-owned coordinator subscription, a bounded latest-state fanout, and run-scoped observations.
/// </summary>
internal sealed class ConsoleFeed(
    Func<CancellationToken, Task<ManagedSession>> connect,
    ConsoleOptions options,
    ITelemetryContext telemetry,
    TimeProvider? timeProvider = null) : BackgroundService, IOperatorPlantFeed
{
    /// <summary>
    /// Latest coherent snapshot, retained but marked unavailable during transport failure.
    /// </summary>
    public CommittedSnapshot? Snapshot => Volatile.Read(ref m_snapshot);

    /// <summary>
    /// Current browser projection.
    /// </summary>
    public ConsoleState State => Volatile.Read(ref m_state) ?? Empty();

    /// <summary>
    /// Current session wrapper; never exposes membership operations to HTTP.
    /// </summary>
    internal RecyclingCoordinatorClient? Client => Volatile.Read(ref m_client);

    /// <summary>
    /// Attaches one bounded consumer or explicitly refuses the configured limit.
    /// </summary>
    public Channel<ConsoleState>? Subscribe()
    {
        lock (m_gate)
        {
            if (m_consumers.Count >= 16)
            {
                return null;
            }
            var channel = Channel.CreateBounded<ConsoleState>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            m_consumers.Add(channel);
            channel.Writer.TryWrite(State);
            return channel;
        }
    }

    /// <summary>
    /// Releases a disconnected browser without affecting production membership.
    /// </summary>
    public void Unsubscribe(Channel<ConsoleState> channel)
    {
        lock (m_gate)
        {
            m_consumers.Remove(channel);
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Publishes workstation command receipts without altering authoritative plant state.
    /// </summary>
    public void SetCommands(CommandResult[] commands)
    {
        lock (m_gate)
        {
            m_commands = commands;
            Publish(State with { Commands = commands });
        }
    }

    /// <inheritdoc/>
    public ValueTask<RecyclingCommandOutcome> ExecuteAsync(
        RecyclingCommand command, CancellationToken cancellationToken) =>
        (Client ?? throw new IOException("Coordinator unavailable.")).ExecuteAsync(command, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ArrayOf<MaterialExecution>> ReadHistoryAsync(
        long after, int limit, CancellationToken cancellationToken) =>
        (Client ?? throw new IOException("Coordinator unavailable."))
            .ReadHistoryAsync(after, limit, cancellationToken);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ManagedSession session = await connect(stoppingToken).ConfigureAwait(false);
                var client = new RecyclingCoordinatorClient(session, telemetry);
                await using var clientScope = client.ConfigureAwait(false);
                Volatile.Write(ref m_client, client);
                ushort model = checked((ushort)session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri));
                DataValue line = await session.ReadValueAsync(new NodeId(
                    Model.Variables.Coordinator_LineId, model), stoppingToken).ConfigureAwait(false);
                if (!StatusCode.IsGood(line.StatusCode) ||
                    !line.WrappedValue.TryGetValue(out string lineId) || lineId != options.LineId)
                {
                    throw new InvalidDataException("Coordinator line identity does not match the configured line.");
                }
                CommittedSnapshot initial = await client.ReadSnapshotAsync(stoppingToken).ConfigureAwait(false);
                Accept(new CoordinatorNotification(initial, true, string.Empty));
                NodeId boundSession = session.SessionId;
                using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                IAsyncEnumerator<CoordinatorNotification> notifications = client.WatchAsync(watchStop.Token)
                    .GetAsyncEnumerator(watchStop.Token);
                await using var notificationScope = notifications.ConfigureAwait(false);
                while (!stoppingToken.IsCancellationRequested)
                {
                    Task<bool> next = notifications.MoveNextAsync().AsTask();
                    while (!next.IsCompleted)
                    {
                        await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(1), stoppingToken))
                            .ConfigureAwait(false);
                        if (stoppingToken.IsCancellationRequested)
                        {
                            try
                            {
                                await next.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (watchStop.IsCancellationRequested)
                            {
                            }
                            stoppingToken.ThrowIfCancellationRequested();
                        }
                        if (!session.Connected || session.SessionId != boundSession)
                        {
                            lock (m_gate)
                            {
                                m_gap = true;
                                Publish(State with
                                {
                                    Available = false,
                                    Connection = "Reconnecting",
                                    Detail = "Managed transport disconnected; source time is not a liveness clock.",
                                    Quality = "BadNoCommunication"
                                });
                            }
                            await watchStop.CancelAsync().ConfigureAwait(false);
                            try
                            {
                                await next.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (watchStop.IsCancellationRequested)
                            {
                            }
                            throw new IOException("Managed transport generation changed; rebuilding typed bindings.");
                        }
                    }
                    if (!await next.ConfigureAwait(false))
                    {
                        break;
                    }
                    if (session.SessionId != boundSession)
                    {
                        throw new IOException("Managed session was replaced; rebuilding typed namespace bindings.");
                    }
                    if (notifications.Current.StatusCode == StatusCodes.BadNodeIdUnknown ||
                        notifications.Current.StatusCode == StatusCodes.BadNodeIdInvalid)
                    {
                        throw new IOException("Coordinator binding changed; rediscovering typed namespace bindings.");
                    }
                    Accept(notifications.Current);
                }
                throw new IOException("Coordinator subscription ended.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ConsoleLog.ConnectionFailed(telemetry.CreateLogger<ConsoleFeed>(), exception);
                string status = exception is ServiceResultException service
                    ? service.StatusCode.ToString() : "BadNoCommunication";
                string detail = status.StartsWith("BadCertificate", StringComparison.Ordinal)
                    ? "Coordinator certificate rejected; validate trust, validity, and endpoint hostname."
                    : status is "BadUserAccessDenied" or "BadIdentityTokenRejected" or "BadIdentityTokenInvalid"
                        ? "Configured X.509 user identity was rejected or is not authorized."
                        : "Coordinator transport unavailable; last data retained. Check trust and identity.";
                lock (m_gate)
                {
                    m_gap = true;
                    Publish(State with
                    {
                        Available = false,
                        Connection = "Reconnecting",
                        Detail = detail,
                        Quality = status
                    });
                }
            }
            finally
            {
                Volatile.Write(ref m_client, null);
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies only monotonically committed, matching coordinator observations.
    /// </summary>
    internal void Accept(CoordinatorNotification notification)
    {
        lock (m_gate)
        {
            if (!notification.IsAvailable || !StatusCode.IsGood(notification.StatusCode))
            {
                m_gap = true;
                Publish(State with
                {
                    Available = false,
                    Connection = "Unavailable",
                    Detail = notification.Detail,
                    Quality = notification.StatusCode.ToString()
                });
                return;
            }
            CommittedSnapshot snapshot = notification.Snapshot;
            if (snapshot.RunId.Length == 0 || snapshot.ConfigurationId.Length == 0 ||
                snapshot.Stages.Count == 0)
            {
                throw new InvalidDataException("Coordinator returned an incomplete line snapshot.");
            }
            if (m_snapshot is { } previous && previous.RunId == snapshot.RunId &&
                previous.Revision > snapshot.Revision)
            {
                return;
            }
            if (m_snapshot is { } current && current.RunId == snapshot.RunId &&
                current.ConfigurationId != snapshot.ConfigurationId)
            {
                m_gap = true;
                Publish(State with
                {
                    Available = false,
                    Connection = "Unavailable",
                    Detail = "Configuration identity changed within one run; incompatible observation discarded.",
                    Quality = "BadConfigurationError"
                });
                return;
            }
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
            bool newRun = m_snapshot?.RunId != snapshot.RunId;
            if (newRun)
            {
                m_trends.Clear();
            }
            SummaryDto summary = Summarize(snapshot);
            if (newRun || m_snapshot?.Tick != snapshot.Tick)
            {
                bool gap = m_gap || (newRun && m_snapshot != null) ||
                    (!newRun && snapshot.Tick != m_snapshot!.Tick + 1);
                m_trends.Enqueue(new TrendDto(snapshot.RunId, Text(snapshot.Tick), Text(snapshot.LogicalSeconds),
                    now, gap, summary.WipMg!, summary.PackagedMg!,
                    summary.ElectricalJoules!, summary.ThermalJoules!));
                while (m_trends.Count > 360)
                {
                    m_trends.Dequeue();
                }
                m_gap = false;
            }
            Volatile.Write(ref m_snapshot, snapshot);
            Publish(new ConsoleState(
                "Connected", string.Empty, true, now, notification.SourceTimestampUtc,
                notification.StatusCode.ToString(),
                options.LineId, snapshot.RunId, snapshot.Scenario, snapshot.ConfigurationId,
                Text(snapshot.Revision), Text(snapshot.Tick), Text(snapshot.LogicalSeconds),
                snapshot.Lifecycle.ToString(), snapshot.BlockingReason, summary,
                Enum.GetValues<RecyclingProcess>().Select(process => ProjectProcess(snapshot, process)).ToArray(),
                snapshot.Holds.Span[..Math.Min(200, snapshot.Holds.Count)].ToArray().Select(static hold => new HoldDto(
                    Text(hold.Id), Text(hold.Tick), hold.Stage, hold.Reason, hold.Disposition.ToString(),
                    hold.DisposedBy, Text(hold.DispositionRevision))).ToArray(),
                m_trends.ToArray(), m_commands));
        }
    }

    /// <summary>
    /// Builds the process projection directly from the committed state.
    /// </summary>
    internal ProcessDto ProjectProcess(CommittedSnapshot snapshot, RecyclingProcess process)
    {
        ProcessParticipant? participant = snapshot.Participants.Span.ToArray()
            .FirstOrDefault(item => item.Process == process);
        StageSnapshot[] stages = snapshot.Stages.Span.ToArray().Where(stage => stage.Process == process).ToArray();
        bool initialized = participant?.Initialized == true &&
            participant.LeaseExpiresUtc > m_timeProvider.GetUtcNow().UtcDateTime && stages.Length > 0 &&
            stages.All(static stage => stage.State != EquipmentState.Unavailable);
        return new ProcessDto(process.ToString(), initialized,
            participant?.LeaseExpiresUtc, options.ProcessEndpoints.ContainsKey(process.ToString()),
            stages.Select(static stage => new StageDto(
                stage.Index, stage.Name, stage.State.ToString(), stage.Enabled, stage.JamAlarm,
                stage.StrandBreakAlarm, Text(stage.Inventory.Total), Text(stage.Transferred.Total),
                Text(stage.ElectricalJoules), Text(stage.ThermalJoules), Text(stage.Executions),
                stage.PhAvailable ? stage.Ph : null, stage.PhAvailable ? stage.PhSourceTimestampUtc : null,
                "Synthetic sample")).ToArray());
    }

    /// <summary>
    /// Converts exact mass without floating-point arithmetic.
    /// </summary>
    internal static MassDto Mass(MaterialMass mass) => new(
        Text(mass.NaturalPolymer), Text(mass.ColoredPolymer), Text(mass.DensePolymer), Text(mass.Labels),
        Text(mass.Dirt), Text(mass.Water), Text(mass.Dry), Text(mass.Total));

    /// <summary>
    /// Produces invariant transport integers.
    /// </summary>
    internal static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Calculates exact committed totals.
    /// </summary>
    private static SummaryDto Summarize(CommittedSnapshot snapshot)
    {
        long wip = 0;
        long packaged = 0;
        long electrical = 0;
        long thermal = 0;
        int holds = 0;
        foreach (StageSnapshot stage in snapshot.Stages)
        {
            wip = checked(wip + stage.Inventory.Total);
            electrical = checked(electrical + stage.ElectricalJoules);
            thermal = checked(thermal + stage.ThermalJoules);
        }
        foreach (MaterialPackage package in snapshot.Packages)
        {
            packaged = checked(packaged + package.Material.Mass.Total);
        }
        foreach (QualityHold hold in snapshot.Holds)
        {
            if (hold.Disposition == HoldDisposition.Held)
            {
                holds++;
            }
        }
        return new SummaryDto(Text(snapshot.SourceMass.Total), Text(wip), Text(packaged), Text(electrical),
            Text(thermal), snapshot.Packages.Count, holds);
    }

    /// <summary>
    /// Constructs an honestly unavailable initial state.
    /// </summary>
    private ConsoleState Empty() => new(
        "Connecting", "Waiting for an authenticated encrypted coordinator connection.", false, null, null,
        "BadWaitingForInitialData", options.LineId, string.Empty, string.Empty, string.Empty,
        "0", "0", "0", "Unavailable", string.Empty,
        new SummaryDto(null, null, null, null, null, null, null), [], [], [], []);

    /// <summary>
    /// Shares one immutable projection among bounded latest-state channels.
    /// </summary>
    private void Publish(ConsoleState state)
    {
        Volatile.Write(ref m_state, state);
        foreach (Channel<ConsoleState> consumer in m_consumers)
        {
            consumer.Writer.TryWrite(state);
        }
    }

    /// <summary>Protects bounded projection updates.</summary>
    private readonly System.Threading.Lock m_gate = new();
    /// <summary>At most sixteen latest-state channels.</summary>
    private readonly HashSet<Channel<ConsoleState>> m_consumers = [];
    /// <summary>At most 360 logical-time observations.</summary>
    private readonly Queue<TrendDto> m_trends = [];
    /// <summary>Last authoritative immutable coordinator image.</summary>
    private CommittedSnapshot? m_snapshot;
    /// <summary>One shared compact projection.</summary>
    private ConsoleState? m_state;
    /// <summary>Single owned current coordinator wrapper.</summary>
    private RecyclingCoordinatorClient? m_client;
    /// <summary>Bounded workstation receipts.</summary>
    private CommandResult[] m_commands = [];
    /// <summary>Whether continuity was interrupted since the last material tick.</summary>
    private bool m_gap;
    /// <summary>Injectable wall clock; material time remains coordinator-owned.</summary>
    private readonly TimeProvider m_timeProvider = timeProvider ?? TimeProvider.System;
}
