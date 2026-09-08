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
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Alarms;

namespace UaLens.Tests.Observe;

internal sealed class AlarmTestContext : IAsyncDisposable
{
    public AlarmTestContext()
    {
        Backend.Setup(backend => backend.OpenAsync(
                It.IsAny<AlarmSource>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((AlarmSource _, TimeSpan _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return new ValueTask<IAlarmObservation>(Observation);
            });
        Workspace = new AlarmWorkspace(Backend.Object, Host.Telemetry);
    }

    public ObserveTestHost Host { get; } = new();

    public FakeAlarmObservation Observation { get; } = new();

    public Mock<IAlarmBackend> Backend { get; } = new(MockBehavior.Strict);

    public AlarmWorkspace Workspace { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Workspace.StartAsync(
            new AlarmSource("i=2253", "Server"), TimeSpan.FromMilliseconds(250), 1, cancellationToken);
    }

    public async Task SendAsync(AlarmCondition condition)
    {
        long expected = Workspace.Snapshot().ReceivedEvents + 1;
        Observation.Publish(new AlarmUpdate(AlarmUpdateKind.Condition, 11, condition));
        await WaitUntilAsync(() => Workspace.Snapshot().ReceivedEvents >= expected).ConfigureAwait(false);
    }

    public static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate() && elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }
        Assert.That(predicate(), Is.True, "The bounded alarm consumer did not reach the expected state.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Workspace.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await Host.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class FakeAlarmObservation : IAlarmObservation
{
    public ChannelReader<AlarmUpdate> Updates => m_updates.Reader;

    public AlarmStreamHealth Health => new(
        IsReady, "Test event source", TimeSpan.FromMilliseconds(250), DroppedUpdates, 0, 0, [11]);

    public bool IsReady { get; set; } = true;

    public long DroppedUpdates { get; set; }

    public int RefreshCount => Volatile.Read(ref m_refreshCount);

    public int ExecuteCount => Volatile.Read(ref m_executeCount);

    public int DisposeCount => Volatile.Read(ref m_disposeCount);

    public AlarmCommand? LastCommand { get; private set; }

    public TaskCompletionSource<AlarmCommand> ExecuteStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Func<AlarmCommand, CancellationToken, ValueTask>? Execute { get; set; }

    public Func<CancellationToken, ValueTask>? Refresh { get; set; }

    public ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref m_refreshCount);
        return Refresh?.Invoke(cancellationToken) ?? ValueTask.CompletedTask;
    }

    public ValueTask<ArrayOf<AlarmOperation>> InspectAsync(
        AlarmCondition condition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ArrayOf<AlarmOperation>>(
        [
            new AlarmOperation(
                AlarmOperationKind.Acknowledge, AlarmAvailability.Supported, "Allowed", condition.Key.ConditionId)
        ]);
    }

    public ValueTask ExecuteAsync(AlarmCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref m_executeCount);
        LastCommand = command;
        ExecuteStarted.TrySetResult(command);
        return Execute?.Invoke(command, cancellationToken) ?? ValueTask.CompletedTask;
    }

    public void Publish(AlarmUpdate update)
    {
        Assert.That(m_updates.Writer.TryWrite(update), Is.True, "The test exceeded the production-sized queue.");
    }

    public void Complete()
    {
        m_updates.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref m_disposeCount);
        m_updates.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private readonly Channel<AlarmUpdate> m_updates = Channel.CreateBounded<AlarmUpdate>(
        new BoundedChannelOptions(AlarmLimits.PendingUpdates)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private int m_refreshCount;
    private int m_executeCount;
    private int m_disposeCount;
}

internal static class AlarmTestData
{
    public static DateTimeOffset Now => new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    public static AlarmCondition Condition(byte eventId = 1, uint branch = 0, uint condition = 500, bool retain = true)
    {
        return new AlarmCondition(
            new AlarmKey(new NodeId(condition, 2), branch == 0 ? NodeId.Null : new NodeId(branch, 2)),
            ByteString.From([eventId]),
            ObjectTypeIds.AlarmConditionType,
            new NodeId(100u, 2),
            "Boiler",
            "High temperature",
            "Temperature is high",
            700,
            Now.UtcDateTime.AddSeconds(eventId),
            Now.UtcDateTime.AddSeconds(eventId + 1),
            true,
            true,
            false,
            false,
            retain,
            StatusCodes.Good,
            AlarmConditionKind.Alarm)
        {
            Suppressed = false,
            Latched = true,
            MaxTimeShelved = 120000
        };
    }

    public static ArrayOf<Variant> Fields(AlarmEventProjection projection, NodeId eventType)
    {
        var fields = new Variant[projection.Filter.SelectClauses.Count];
        for (int i = 0; i < fields.Length; i++)
        {
            SimpleAttributeOperand clause = projection.Filter.SelectClauses[i];
            if (i == projection.ConditionIdIndex)
            {
                fields[i] = Variant.From(new NodeId(500u, 2));
                continue;
            }
            string? name = clause.BrowsePath[0].Name;
            fields[i] = name switch
            {
                BrowseNames.EventId => Variant.From(ByteString.From([1, 2, 3])),
                BrowseNames.EventType => Variant.From(eventType),
                BrowseNames.SourceNode => Variant.From(new NodeId(100u, 2)),
                BrowseNames.SourceName => Variant.From("Boiler"),
                BrowseNames.ConditionName => Variant.From("High temperature"),
                BrowseNames.Message => Variant.From(new LocalizedText("en", "Temperature is high")),
                BrowseNames.Severity => Variant.From((ushort)700),
                BrowseNames.Time or BrowseNames.ReceiveTime => Variant.From(Now.UtcDateTime),
                BrowseNames.BranchId => Variant.From(NodeId.Null),
                BrowseNames.Retain => Variant.From(true),
                BrowseNames.Quality => Variant.From(StatusCodes.Good),
                BrowseNames.EnabledState or BrowseNames.ActiveState => Variant.From(true),
                BrowseNames.AckedState or BrowseNames.ConfirmedState => Variant.From(false),
                _ => Variant.Null
            };
        }
        return fields;
    }

    public static ArrayOf<Variant> SetField(
        AlarmEventProjection projection,
        ArrayOf<Variant> fields,
        string name,
        Variant value)
    {
        Variant[] copy = fields.ToArray() ?? [];
        for (int i = 0; i < projection.ConditionIdIndex; i++)
        {
            if (projection.Filter.SelectClauses[i].BrowsePath[0].Name == name)
            {
                copy[i] = value;
            }
        }
        return copy;
    }
}
