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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CellRoboticsCompanionProviderTests
{
    [Test]
    public async Task DefaultProviderDiscoversTypedSystemsUsingRoboticsClientAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics);
        var provider = new RoboticsCompanionProvider();
        NodeId deviceSet = NodeId.Create(
            Opc.Ua.Di.Objects.DeviceSet, Opc.Ua.Di.Namespaces.OpcUaDi, session.NamespaceUris);
        var type = new NodeId(RoboticsModel.MotionDeviceSystemType, session.NamespaceIndex);
        session.Browses[deviceSet] =
        [
            session.Reference(s_system, "Robot cell", type),
            session.Reference(new NodeId("not-a-robot", 2), "Other device", Opc.Ua.ObjectTypeIds.BaseObjectType)
        ];

        ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(session.Context, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(s_system));
        Assert.That(targets[0].DisplayName, Is.EqualTo("Robot cell"));
        Assert.That(targets[0].TypeName, Is.EqualTo("MotionDeviceSystem"));
        Assert.That(provider.Descriptor.Maturity, Does.Contain("Published").And.Contain("separate draft"));
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public async Task InspectionForwardsBoundedTopologyAndPreservesAxisQualityAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics);
        var reader = new TestReader
        {
            Controllers = [s_controller],
            Devices = [s_device],
            Controller = new ControllerSnapshot
            {
                Identification = new RoboticsComponentIdentification
                {
                    NodeId = s_controller,
                    ComponentName = new LocalizedText("Controller A")
                },
                TaskControlIds = [new NodeId("task", 2)]
            },
            Device = new MotionDeviceSnapshot
            {
                Identification = new RoboticsComponentIdentification { NodeId = s_device },
                AxisIds = [s_axis],
                SpeedOverride = new DataValue(Variant.From(75d))
            },
            Axis = new AxisSnapshot
            {
                Identification = new RoboticsComponentIdentification { NodeId = s_axis },
                MotionProfile = AxisMotionProfileEnumeration.ROTARY,
                State = new AxisStateSnapshot
                {
                    ActualPosition = new DataValue(Variant.From(12.5d), StatusCodes.UncertainLastUsableValue),
                    ActualSpeed = new DataValue(Variant.From(2d)),
                    ActualAcceleration = new DataValue(Variant.From(0.5d))
                }
            },
            State = RoboticsOperationState.Ready
        };
        CompanionContext? forwarded = null;
        var provider = new RoboticsCompanionProvider(context =>
        {
            forwarded = context;
            return reader;
        });

        CompanionInspection inspection = await provider.InspectAsync(session.Context, s_target, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(forwarded, Is.SameAs(session.Context));
        Assert.That(reader.ReadNodes, Is.EqualTo(new[] { s_controller, s_device, s_axis }));
        Assert.That(CellProviderTestSession.Field(inspection.Values, "Controller 1 operation state")
            .TryGetValue(out string? state), Is.True);
        Assert.That(state, Is.EqualTo("Ready"));
        Assert.That(CellProviderTestSession.Field(inspection.Values, "Motion device 1 axis 1 position")
            .TryGetValue(out double position), Is.True);
        Assert.That(position, Is.EqualTo(12.5d));
        Assert.That(CellProviderTestSession.Field(inspection.Values, "Motion device 1 axis 1 position status")
            .TryGetValue(out StatusCode status), Is.True);
        Assert.That(status, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
        Assert.That(inspection.Operations, Has.Count.EqualTo(1));
        Assert.That(inspection.Operations[0].Safety, Is.EqualTo(CompanionOperationSafety.ReadOnly));
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public async Task SnapshotExecutionReadsAgainRatherThanReturningStaleInspectionAsync()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics);
        var reader = new TestReader();
        var provider = new RoboticsCompanionProvider(_ => reader);
        await provider.InspectAsync(session.Context, s_target, CancellationToken.None).ConfigureAwait(false);

        CompanionOperationResult result = await provider.ExecuteAsync(
            session.Context, s_target, "snapshot", null, CancellationToken.None).ConfigureAwait(false);

        Assert.That(reader.SystemReads, Is.EqualTo(2));
        Assert.That(result.Summary, Does.Contain("No motion"));
        Assert.That(CellProviderTestSession.Field(result.Values, "System").TryGetValue(out NodeId system), Is.True);
        Assert.That(system, Is.EqualTo(s_system));
    }

    [Test]
    public void DiscoveryStopsAndDisposesTheEnumeratorAtTheLimit()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics, maxTargets: 1);
        var reader = new TestReader
        {
            Entries = new CellProviderTestEntries<MotionDeviceSystemEntry>(
                [Entry(s_system), Entry(new NodeId("second", 2)), Entry(new NodeId("third", 2))])
        };
        var provider = new RoboticsCompanionProvider(_ => reader);

        Assert.That(
            async () => await provider.DiscoverAsync(session.Context, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Message.Contains("limit"));
        Assert.That(reader.Entries.Visited, Is.EqualTo(2));
        Assert.That(reader.Entries.Disposals, Is.EqualTo(1));
        Assert.That(reader.SystemReads, Is.Zero);
    }

    [Test]
    public void TopologyLimitRejectsASecondNodeBeforeReadingItsSnapshot()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics, maxTargets: 1);
        var reader = new TestReader { Devices = [s_device] };
        var provider = new RoboticsCompanionProvider(_ => reader);

        Assert.That(
            async () => await provider.InspectAsync(session.Context, s_target, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.ReadNodes, Is.Empty);
    }

    [Test]
    public void FieldLimitFailsInsteadOfReturningAnOversizedInspection()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics, maxFields: 1);
        var provider = new RoboticsCompanionProvider(_ => new TestReader());
        Assert.That(
            async () => await provider.InspectAsync(session.Context, s_target, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
    }

    [Test]
    public void CancellationBeforeDiscoveryDoesNotConstructAReader()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int created = 0;
        var provider = new RoboticsCompanionProvider(_ =>
        {
            created++;
            return new TestReader();
        });

        Assert.That(
            async () => await provider.DiscoverAsync(session.Context, cancellation.Token).ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(created, Is.Zero);
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public void CancellationDuringDiscoveryDisposesTheEnumerator()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics);
        using var cancellation = new CancellationTokenSource();
        var reader = new TestReader
        {
            Entries = new CellProviderTestEntries<MotionDeviceSystemEntry>([Entry(s_system)])
        };
        reader.Entries.OnMove = cancellation.Cancel;
        var provider = new RoboticsCompanionProvider(_ => reader);

        Assert.That(
            async () => await provider.DiscoverAsync(session.Context, cancellation.Token).ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(reader.Entries.Disposals, Is.EqualTo(1));
        Assert.That(reader.ReadNodes, Is.Empty);
    }

    [TestCase("start", null)]
    [TestCase("move", null)]
    [TestCase("snapshot", "arbitrary command")]
    public async Task UnsupportedOperationsCannotReachTheReader(string operation, string? input)
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics);
        int created = 0;
        var provider = new RoboticsCompanionProvider(_ =>
        {
            created++;
            return new TestReader();
        });
        await Assert.ThatAsync(
            () => provider.ExecuteAsync(session.Context, s_target, operation, input, CancellationToken.None).AsTask(),
            input is null
                ? Throws.TypeOf<ServiceResultException>()
                : Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
        Assert.That(created, Is.Zero);
    }

    [Test]
    public void ServiceErrorsAreNotReportedAsAnEmptySuccessfulCell()
    {
        var session = new CellProviderTestSession(Opc.Ua.Robotics.Namespaces.Robotics);
        var failure = new ServiceResultException(StatusCodes.BadUserAccessDenied);
        var reader = new TestReader { Failure = failure };
        var provider = new RoboticsCompanionProvider(_ => reader);
        Assert.That(
            async () => await provider.InspectAsync(session.Context, s_target, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.Exception.SameAs(failure));
    }

    private static MotionDeviceSystemEntry Entry(NodeId node)
    {
        return new MotionDeviceSystemEntry(
            node, new QualifiedName("System"), new LocalizedText("System"), new NodeId(1));
    }

    private sealed class TestReader : IRoboticsCompanionReader
    {
        public CellProviderTestEntries<MotionDeviceSystemEntry> Entries { get; set; } = new([]);

        public ArrayOf<NodeId> Controllers { get; init; } = [];

        public ArrayOf<NodeId> Devices { get; init; } = [];

        public ControllerSnapshot Controller { get; init; } = new();

        public MotionDeviceSnapshot Device { get; init; } = new();

        public AxisSnapshot Axis { get; init; } = new();

        public RoboticsOperationState? State { get; init; }

        public ServiceResultException? Failure { get; init; }

        public List<NodeId> ReadNodes { get; } = [];

        public int SystemReads { get; private set; }

        public IAsyncEnumerable<MotionDeviceSystemEntry> EnumerateSystemsAsync(CancellationToken cancellationToken)
        {
            return Entries;
        }

        public ValueTask<ArrayOf<NodeId>> GetControllersAsync(NodeId system, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemReads++;
            return Failure is null
                ? ValueTask.FromResult(Controllers)
                : ValueTask.FromException<ArrayOf<NodeId>>(Failure);
        }

        public ValueTask<ArrayOf<NodeId>> GetMotionDevicesAsync(NodeId system, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Devices);
        }

        public Task<ControllerSnapshot> ReadControllerAsync(NodeId controller, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadNodes.Add(controller);
            return Task.FromResult(Controller);
        }

        public ValueTask<RoboticsOperationState?> ReadControllerStateAsync(
            NodeId controller, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(State);
        }

        public Task<MotionDeviceSnapshot> ReadMotionDeviceAsync(NodeId device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadNodes.Add(device);
            return Task.FromResult(Device);
        }

        public Task<AxisSnapshot> ReadAxisAsync(NodeId axis, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadNodes.Add(axis);
            return Task.FromResult(Axis);
        }
    }

    private static readonly NodeId s_system = new("system", 2);
    private static readonly NodeId s_controller = new("controller", 2);
    private static readonly NodeId s_device = new("device", 2);
    private static readonly NodeId s_axis = new("axis", 2);
    private static readonly CompanionTarget s_target = new("robotics", s_system, "Cell", "MotionDeviceSystem");
}
