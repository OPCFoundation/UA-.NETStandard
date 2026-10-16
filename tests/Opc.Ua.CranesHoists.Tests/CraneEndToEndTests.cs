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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Exercises native crane monitoring and configuration over real opc.tcp sessions on both stock owners.
    /// </summary>
    [TestFixture(false)]
    [TestFixture(true)]
    [NonParallelizable]
    public sealed class CraneEndToEndTests
    {
        /// <summary>
        /// Chooses the existing namespace owner.
        /// </summary>
        public CraneEndToEndTests(bool roboticsOwner)
        {
            m_roboticsOwner = roboticsOwner;
        }

        /// <summary>
        /// Starts an explicitly configured full crane and two independent real client sessions.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_server = new CraneServerFixture(m_roboticsOwner, reverseNamespaces: true);
            await m_server.StartAsync().ConfigureAwait(false);
            m_provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples("InteropCrane", true), true);
            var authorizer = new Mock<ICraneWriteAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(value => value.AuthorizeAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CraneLimitRequest>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext caller, CraneLimitRequest _, CancellationToken _) =>
                    new ValueTask<bool>(caller is SessionSystemContext context &&
                        context.SessionId == m_authorizedSession));
            var limits = new Mock<ICraneLimitProvider>(MockBehavior.Strict);
            limits.Setup(value => value.RequestAsync(It.IsAny<CraneLimitRequest>(), It.IsAny<CancellationToken>()))
                .Returns((CraneLimitRequest request, CancellationToken cancellationToken) => m_refuseRequests
                    ? new ValueTask<StatusCode>(StatusCodes.BadOutOfService)
                    : m_provider.RequestAsync(request, cancellationToken));
            var context = new CranesHoistsBuildContext(m_server.RoboticsContext(), m_server.MachineryContext());
            m_crane = await CraneRuntimeTestData.Define(context, "InteropCrane", m_provider, true,
                limits.Object, authorizer.Object).BuildAsync().ConfigureAwait(false);
            m_first = await CraneClientConnection.ConnectAsync(m_server, managed: true).ConfigureAwait(false);
            m_second = await CraneClientConnection.ConnectAsync(m_server).ConfigureAwait(false);
            m_authorizedSession = m_first.Session.SessionId;
            m_client = new CranesHoistsClient(m_first.Session, NUnitTelemetryContext.Create(),
                new CranesHoistsClientOptions { BrowsePageSize = 1 });
        }

        /// <summary>
        /// Restores the acquired data after each independently asserted scenario.
        /// </summary>
        [TearDown]
        public async Task RestoreAsync()
        {
            m_refuseRequests = false;
            await m_provider.SetSamplesAsync(CraneRuntimeTestData.Samples("InteropCrane", true)).ConfigureAwait(false);
            await m_crane.RefreshAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Closes sessions before removing the graph and stopping its existing owner.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_second != null)
            {
                await m_second.DisposeAsync().ConfigureAwait(false);
            }
            if (m_first != null)
            {
                await m_first.DisposeAsync().ConfigureAwait(false);
            }
            if (m_crane != null)
            {
                await m_crane.RemoveAsync().ConfigureAwait(false);
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server != null)
            {
                await m_server.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Every crane CU is exercised through real discovery, values, metadata and relationships.
        /// </summary>
        [Test]
        public async Task AllCraneUnitsHaveRealSessionBehaviorWithReorderedNamespaces()
        {
            ArrayOf<CraneEntry> discovered = await m_client.DiscoverAsync().ConfigureAwait(false);
            Assert.That(discovered, Has.Count.EqualTo(1));
            Assert.That(discovered[0].NodeId, Is.EqualTo(m_crane.NodeId));
            NodeId machines = NodeId.Create(Machinery.Objects.Machines, Machinery.Namespaces.Machinery,
                m_first.Session.NamespaceUris);
            ArrayOf<CraneEntry> organized = await m_client.DiscoverAsync(machines).ConfigureAwait(false);
            Assert.That(organized[0].NodeId, Is.EqualTo(m_crane.NodeId));
            CraneSystemSnapshot snapshot = await m_client.ReadSystemAsync(m_crane.NodeId).ConfigureAwait(false);
            Assert.That(snapshot.Elements, Has.Count.EqualTo(5));
            Assert.That(snapshot.System.Entry.Kind, Is.EqualTo(CraneElementKind.System));
            Assert.That(snapshot.System.Identification, Is.Not.Null);
            Assert.That(snapshot.System.Identification!.SerialNumber.WrappedValue.GetString(),
                Is.EqualTo("InteropCrane"));
            Assert.That(Signal(snapshot.System, CraneSignal.SystemOn).Value.WrappedValue.GetBoolean(), Is.True);
            Assert.That(Signal(snapshot.System, CraneSignal.OperationalMode).Value.WrappedValue.GetInt32(),
                Is.EqualTo((int)CraneOperationalModeEnumeration.MANUAL));
            Assert.That(Signal(snapshot.System, CraneSignal.ExternalControlRequest).Value.WrappedValue.GetInt32(),
                Is.EqualTo((int)ExternalControlRequestEnumeration.REQUESTED_AND_CONTROL_ACTIVE));
            CraneElementSnapshot device = Element(snapshot, "Hoist");
            Assert.That(device.Entry.TypeDefinition, Is.EqualTo(TypeId(ObjectTypes.CraneMotionDeviceType)));
            Assert.That(device.Identification!.SerialNumber.WrappedValue.GetString(), Is.EqualTo("InteropCrane-hoist"));
            Assert.That(Signal(device, CraneSignal.MotionDeviceCategory).Value.WrappedValue.GetInt32(),
                Is.EqualTo((int)CraneMotionDeviceCategoryEnum.HOIST));
            Assert.That(Signal(device, CraneSignal.DesignedWorkingPeriod).Value.WrappedValue.GetDouble(),
                Is.EqualTo(92.5));
            CraneElementSnapshot axis = Element(snapshot, "Lift");
            Assert.That(Signal(axis, CraneSignal.ActualPositionCapability).Value.WrappedValue.GetBoolean(), Is.True);
            Assert.That(Signal(axis, CraneSignal.ActualPosition).Value.WrappedValue.GetDouble(), Is.EqualTo(12.75));
            Assert.That(Signal(axis, CraneSignal.LoadMass).Value.WrappedValue.GetDouble(), Is.EqualTo(153));
            Assert.That(Signal(axis, CraneSignal.RatedSpeed).Value.WrappedValue.GetDouble(), Is.EqualTo(2.5));
            Assert.That(axis.AxisFunctionValues, Has.Count.EqualTo(9));
            for (int i = 0; i < 9; i++)
            {
                Assert.That(axis.AxisFunctionValues[i].Value, Is.EqualTo(i));
            }
            Assert.That(Signal(Element(snapshot, "LiftMotor"), CraneSignal.BrakeReleased)
                .Value.WrappedValue.GetBoolean(),
                Is.True);
            Assert.That(Signal(Element(snapshot, "LiftMotor"), CraneSignal.Overheated).Value.WrappedValue.GetBoolean(),
                Is.False);
            Assert.That(Signal(Element(snapshot, "BackupBrake"), CraneSignal.BrakeReleased)
                .Value.WrappedValue.GetBoolean(),
                Is.False);
            CraneElementSnapshot protection = Element(snapshot, "Overload");
            Assert.That(Signal(protection, CraneSignal.ProtectiveFunctionMode).Value.WrappedValue.GetInt32(),
                Is.EqualTo((int)ProtectiveFunctionEnumeration.FORCE_LIMITER));
            Assert.That(Signal(protection, CraneSignal.Enabled).Value.WrappedValue.GetBoolean(), Is.True);
            Assert.That(Signal(protection, CraneSignal.Active).Value.WrappedValue.GetBoolean(), Is.False);
            Assert.That(Signal(protection, CraneSignal.DirMinusStop).Value.WrappedValue.GetBoolean(), Is.True);
            Assert.That(Signal(protection, CraneSignal.DirPlusSlowdown).Value.WrappedValue.GetBoolean(), Is.False);
            Assert.That(protection.Relationships, Has.Count.EqualTo(1));
            Assert.That(protection.Relationships[0].ReferenceType, Is.EqualTo(NodeId.Create(
                ReferenceTypes.ProtectsAxis, CranesHoistsModel.NamespaceUri, m_first.Session.NamespaceUris)));
            Assert.That(protection.Relationships[0].Target, Is.EqualTo(axis.Entry.NodeId));
            Assert.That(Signal(axis, CraneSignal.ActualPosition).Value.SourceTimestamp,
                Is.EqualTo(CraneRuntimeTestData.SourceTime));
        }

        /// <summary>
        /// All standard operating modes and external-control values roundtrip
        /// through provider publication and UA reads.
        /// </summary>
        [Test]
        public async Task EveryModeAndControlEnumerationValueRoundtripsOverTcp()
        {
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples("InteropCrane", true);
            for (int mode = 0; mode <= 5; mode++)
            {
                for (int external = 0; external <= 3; external++)
                {
                    ArrayOf<CraneSample> next = CraneRuntimeTestData.Replace(samples,
                        CraneRuntimeTestData.Sample("InteropCrane", CraneSignal.OperationalMode, Variant.From(mode)));
                    next = CraneRuntimeTestData.Replace(next,
                        CraneRuntimeTestData.Sample("InteropCrane",
                            CraneSignal.ExternalControlRequest, Variant.From(external)));
                    await m_provider.SetSamplesAsync(next).ConfigureAwait(false);
                    await m_crane.RefreshAsync().ConfigureAwait(false);
                    CraneElementSnapshot root = await m_client.ReadElementAsync(m_crane.NodeId).ConfigureAwait(false);
                    Assert.That(Signal(root, CraneSignal.OperationalMode).Value.WrappedValue.GetInt32(),
                        Is.EqualTo(mode));
                    Assert.That(Signal(root, CraneSignal.ExternalControlRequest).Value.WrappedValue.GetInt32(),
                        Is.EqualTo(external));
                }
            }
        }

        /// <summary>
        /// Authorized standard writes return acceptance without pretending the requested limit is active.
        /// </summary>
        [TestCase(0.0)]
        [TestCase(100.0)]
        [TestCase(48.75)]
        public async Task LimitWritesUseRealAuthorizationAndPreserveReportedActiveLimits(double requested)
        {
            NodeId axis = m_crane.GetElement("Lift").NodeId;
            int before = m_provider.AcceptedRequests.Count;
            Assert.That(await m_client.ConfigureLimitAsync(
                axis, CraneSignal.SpeedLimitDirPlusSetpoint, Variant.From(requested)).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            CraneElementSnapshot actual = await m_client.ReadElementAsync(axis).ConfigureAwait(false);
            Assert.That(Signal(actual, CraneSignal.SpeedLimitDirPlusSetpoint).Value.WrappedValue.GetDouble(),
                Is.EqualTo(requested));
            Assert.That(Signal(actual, CraneSignal.SpeedLimitDirPlus).Value.WrappedValue.GetDouble(), Is.EqualTo(31.5));
            var other = new CranesHoistsClient(m_second.Session, NUnitTelemetryContext.Create());
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await other.ConfigureLimitAsync(axis, CraneSignal.SpeedLimitDirPlusSetpoint, Variant.From(25.0))
                    .ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(m_provider.AcceptedRequests, Has.Count.EqualTo(before + 1));
            actual = await m_client.ReadElementAsync(axis).ConfigureAwait(false);
            Assert.That(Signal(actual, CraneSignal.SpeedLimitDirPlusSetpoint).Value.WrappedValue.GetDouble(),
                Is.EqualTo(requested));
        }

        /// <summary>
        /// An equipment provider's refusal is a real operation error, not a successful no-op.
        /// </summary>
        [Test]
        public async Task ProviderRefusalRetainsRequestAndReportedStateOverTcp()
        {
            NodeId axis = m_crane.GetElement("Lift").NodeId;
            CraneElementSnapshot before = await m_client.ReadElementAsync(axis).ConfigureAwait(false);
            int requests = m_provider.AcceptedRequests.Count;
            m_refuseRequests = true;
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.ConfigureLimitAsync(axis, CraneSignal.SpeedLimitDirPlusSetpoint, Variant.From(12.0))
                    .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadOutOfService));
            CraneElementSnapshot after = await m_client.ReadElementAsync(axis).ConfigureAwait(false);
            Assert.That(Signal(after, CraneSignal.SpeedLimitDirPlusSetpoint).Value.WrappedValue,
                Is.EqualTo(Signal(before, CraneSignal.SpeedLimitDirPlusSetpoint).Value.WrappedValue));
            Assert.That(Signal(after, CraneSignal.SpeedLimitDirPlus).Value.WrappedValue,
                Is.EqualTo(Signal(before, CraneSignal.SpeedLimitDirPlus).Value.WrappedValue));
            Assert.That(m_provider.AcceptedRequests, Has.Count.EqualTo(requests));
        }

        /// <summary>
        /// Adjacent out-of-range and non-finite requests fail over the real wire.
        /// </summary>
        [TestCase(-double.Epsilon)]
        [TestCase(100.00000000000001)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidLimitsReturnExplicitWireErrors(double requested)
        {
            int before = m_provider.AcceptedRequests.Count;
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.ConfigureLimitAsync(m_crane.GetElement("Lift").NodeId,
                    CraneSignal.SpeedLimitDirMinusSetpoint, Variant.From(requested)).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(m_provider.AcceptedRequests, Has.Count.EqualTo(before));
        }

        /// <summary>
        /// The typed facade never exposes an arbitrary actuation write, and the wire rejects wrong scalar types.
        /// </summary>
        [Test]
        public void ReadonlyAndWrongTypeRequestsCannotReachTheProvider()
        {
            int before = m_provider.AcceptedRequests.Count;
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await m_client.ConfigureLimitAsync(m_crane.NodeId, CraneSignal.SystemOn, Variant.From(false))
                    .ConfigureAwait(false));
            ServiceResultException wrongType = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.ConfigureLimitAsync(m_crane.GetElement("Lift").NodeId,
                    CraneSignal.SpeedLimitDirMinusSetpoint, Variant.From("not a Double")).ConfigureAwait(false))!;
            Assert.That(wrongType.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(m_provider.AcceptedRequests, Has.Count.EqualTo(before));
        }

        /// <summary>
        /// Standard Write cannot alter provider-reported limits or protective state, even in an authorized session.
        /// </summary>
        [Test]
        public async Task ActiveLimitsAndProtectionStateAreReadonlyOverTcp()
        {
            WriteResponse response = await m_first.Session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = m_crane.GetSignal(new("Overload", CraneSignal.Enabled)).NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(false))
                },
                new WriteValue
                {
                    NodeId = m_crane.GetSignal(new("Overload", CraneSignal.Active)).NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(true))
                },
                new WriteValue
                {
                    NodeId = m_crane.GetSignal(new("Lift", CraneSignal.SpeedLimitDirPlus)).NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(100.0))
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(3));
            foreach (StatusCode status in response.Results)
            {
                Assert.That(status, Is.EqualTo(StatusCodes.BadNotWritable));
            }
            Assert.That(m_crane.ReadSignal(new("Overload", CraneSignal.Enabled)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(m_crane.ReadSignal(new("Overload", CraneSignal.Active)).WrappedValue.GetBoolean(), Is.False);
            Assert.That(m_crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirPlus)).WrappedValue.GetDouble(),
                Is.EqualTo(31.5));
        }

        /// <summary>
        /// Data observation preserves original quality/time and removes only its own subscription.
        /// </summary>
        [Test]
        public async Task BoundedDataObservationPreservesQualityAndUnrelatedSubscriptions()
        {
            Assert.That(m_first.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            await using ISubscription unrelated = manager!.Add(new Mock<ISubscriptionNotificationHandler>().Object,
                new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions()));
            int before = manager.Count;
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Replace(
                CraneRuntimeTestData.Samples("InteropCrane", true),
                CraneRuntimeTestData.Sample(
                    "Lift", CraneSignal.ActualPosition, Variant.From(12.75), StatusCodes.Uncertain));
            await m_provider.SetSamplesAsync(samples).ConfigureAwait(false);
            await m_crane.RefreshAsync().ConfigureAwait(false);
            NodeId valueId = m_crane.GetSignal(new("Lift", CraneSignal.ActualPosition)).NodeId;
            CraneObservation observation = await m_client.ObserveValuesAsync([valueId],
                new CraneObservationOptions { MaximumNotifications = 1, Window = TimeSpan.FromSeconds(10) })
                .ConfigureAwait(false);
            Assert.That(observation.Values, Has.Count.EqualTo(1));
            Assert.That(observation.Values[0].NodeId, Is.EqualTo(valueId));
            Assert.That(observation.Values[0].Value.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(observation.Values[0].Value.SourceTimestamp, Is.EqualTo(CraneRuntimeTestData.SourceTime));
            Assert.That(observation.LimitReached, Is.True);
            Assert.That(manager.Count, Is.EqualTo(before));
            Assert.That(manager.Items, Does.Contain(unrelated));
            Assert.That(m_first.Session.Connected, Is.True);
        }

        /// <summary>
        /// Canceling a bounded observation drains its owned resources while leaving the session connected.
        /// </summary>
        [Test]
        public async Task ObservationCancellationReleasesItsSubscription()
        {
            Assert.That(m_first.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            int before = manager!.Count;
            using var cancellation = new CancellationTokenSource();
            Task<CraneObservation> operation = m_client.ObserveEventsAsync(m_crane.EventNotifierId,
                new CraneObservationOptions { Window = TimeSpan.FromSeconds(30) }, cancellation.Token).AsTask();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!manager.Items.Any(subscription => subscription.MonitoredItems.Items.Any(item => item.Created)))
            {
                await Task.Delay(20, deadline.Token).ConfigureAwait(false);
            }
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await operation.ConfigureAwait(false));
            Assert.That(manager.Count, Is.EqualTo(before));
            Assert.That(m_first.Session.Connected, Is.True);
        }

        /// <summary>
        /// Failed monitored-item creation is an error, not an empty successful observation.
        /// </summary>
        [Test]
        public void ObservationOfMissingNodeReturnsError()
        {
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.ObserveValuesAsync([new NodeId("Missing", m_crane.NodeId.NamespaceIndex)],
                    new CraneObservationOptions { Window = TimeSpan.FromSeconds(10) }).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// A default instance does not accidentally browse into optional type-declaration values.
        /// </summary>
        [Test]
        public async Task MinimalInstancePreservesOptionalAbsenceOverTcp()
        {
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples("InteropMinimal"));
            var context = new CranesHoistsBuildContext(m_server.RoboticsContext(), m_server.MachineryContext());
            CraneSystemHandle minimal = await CraneRuntimeTestData.Define(context, "InteropMinimal", provider)
                .BuildAsync().ConfigureAwait(false);
            try
            {
                CraneSystemSnapshot snapshot = await m_client.ReadSystemAsync(minimal.NodeId).ConfigureAwait(false);
                Assert.That(snapshot.Elements, Has.Count.EqualTo(3));
                Assert.That(Signal(snapshot.System, CraneSignal.ExternalControlRequest).NodeId.IsNull, Is.True);
                Assert.That(Signal(Element(snapshot, "Hoist"), CraneSignal.DesignedWorkingPeriod).NodeId.IsNull,
                    Is.True);
                CraneElementSnapshot axis = Element(snapshot, "Lift");
                Assert.That(Signal(axis, CraneSignal.ActualPosition).Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(Signal(axis, CraneSignal.AxisFunction).NodeId.IsNull, Is.True);
                Assert.That(Signal(axis, CraneSignal.SpeedLimitDirPlusSetpoint).NodeId.IsNull, Is.True);
                ServiceResultException unsupported = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_client.ConfigureLimitAsync(axis.Entry.NodeId, CraneSignal.SpeedLimitDirPlusSetpoint,
                        Variant.From(50.0)).ConfigureAwait(false))!;
                Assert.That(unsupported.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }
            finally
            {
                await minimal.RemoveAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Physical component references are discovered over TCP without duplicating the devices in the inventory.
        /// </summary>
        [Test]
        public async Task PhysicalCompositionIsReportedWithoutDuplicateDevicesOverTcp()
        {
            const string name = "InteropPhysical";
            var samples = new System.Collections.Generic.List<CraneSample>(
                CraneRuntimeTestData.Samples(name).ToArray()!)
            {
                CraneRuntimeTestData.Sample("Trolley", CraneSignal.SpeedOverride, Variant.From(100.0)),
                CraneRuntimeTestData.Sample("TrolleyMotor", CraneSignal.MotorTemperature, Variant.From(31.0))
            };
            await using var provider = new SimulatedCraneProvider(samples.ToArrayOf());
            var context = new CranesHoistsBuildContext(m_server.RoboticsContext(), m_server.MachineryContext());
            CraneSystemBuilder builder = CraneRuntimeTestData.Define(context, name, provider);
            builder.AddDevice("Trolley", CraneMotionDeviceCategoryEnum.TROLLEY_TRAVERSE, device =>
                device.WithIdentification(data => CraneRuntimeTestData.Identify(data, "Trolley"))
                    .WithPhysicalComponent("Hoist")
                    .AddAxis("TrolleyAxis", axis => axis.AddMotor("TrolleyMotor")));
            CraneSystemHandle system = await builder.BuildAsync().ConfigureAwait(false);
            try
            {
                CraneSystemSnapshot snapshot = await m_client.ReadSystemAsync(system.NodeId).ConfigureAwait(false);
                Assert.That(snapshot.Elements, Has.Count.EqualTo(6));
                CraneElementSnapshot trolley = Element(snapshot, "Trolley");
                Assert.That(trolley.Relationships, Has.Count.EqualTo(1));
                Assert.That(trolley.Relationships[0].ReferenceType,
                    Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasPhysicalComponent));
                Assert.That(trolley.Relationships[0].Target, Is.EqualTo(Element(snapshot, "Hoist").Entry.NodeId));
            }
            finally
            {
                await system.RemoveAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Optional load geometry keeps the inherited Robotics structures and per-acquisition quality over TCP.
        /// </summary>
        [Test]
        public async Task OptionalLoadGeometryUsesTheNativeRoboticsStructures()
        {
            const string name = "InteropGeometry";
            var inertia = new ThreeDVector { X = 1, Y = 2, Z = 3 };
            var center = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 4, Y = 5, Z = 6 },
                Orientation = new ThreeDOrientation { A = 10, B = 20, C = 30 }
            };
            var samples = new System.Collections.Generic.List<CraneSample>(
                CraneRuntimeTestData.Samples(name, true).ToArray()!)
            {
                CraneRuntimeTestData.Sample("Lift", CraneSignal.LoadInertia, Variant.FromStructure(inertia)),
                CraneRuntimeTestData.Sample("Lift", CraneSignal.LoadCenterOfMass, Variant.FromStructure(center))
            };
            await using var provider = new SimulatedCraneProvider(samples.ToArrayOf(), true);
            var context = new CranesHoistsBuildContext(m_server.RoboticsContext(), m_server.MachineryContext());
            CraneSystemHandle system = await CraneRuntimeTestData.Define(context, name, provider, true,
                configureLoad: static (load, systemContext) =>
                    load.AddCenterOfMass(systemContext).AddInertia(systemContext)).BuildAsync().ConfigureAwait(false);
            try
            {
                CraneElementSnapshot snapshot = await m_client.ReadElementAsync(system.GetElement("Lift").NodeId)
                    .ConfigureAwait(false);
                DataValue inertiaValue = Signal(snapshot, CraneSignal.LoadInertia).Value;
                DataValue centerValue = Signal(snapshot, CraneSignal.LoadCenterOfMass).Value;
                Assert.That(inertiaValue.WrappedValue.TryGetValue<ThreeDVector>(out ThreeDVector? readInertia,
                    m_first.Session.MessageContext), Is.True);
                Assert.That(centerValue.WrappedValue.TryGetValue<ThreeDFrame>(out ThreeDFrame? readCenter,
                    m_first.Session.MessageContext), Is.True);
                Assert.That(readInertia!.X, Is.EqualTo(1));
                Assert.That(readInertia.Y, Is.EqualTo(2));
                Assert.That(readInertia.Z, Is.EqualTo(3));
                Assert.That(readCenter!.CartesianCoordinates.X, Is.EqualTo(4));
                Assert.That(readCenter.CartesianCoordinates.Y, Is.EqualTo(5));
                Assert.That(readCenter.CartesianCoordinates.Z, Is.EqualTo(6));
                Assert.That(readCenter.Orientation.A, Is.EqualTo(10));
                Assert.That(centerValue.SourceTimestamp, Is.EqualTo(CraneRuntimeTestData.SourceTime));
                Assert.That(inertiaValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                var load = ((CraneAxisState)system.GetElement("Lift")).ActualLoad!;
                ReadResponse coordinates = await m_first.Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [
                    new ReadValueId { NodeId = load.Inertia!.X!.NodeId, AttributeId = Attributes.Value },
                    new ReadValueId
                    {
                        NodeId = load.CenterOfMass!.CartesianCoordinates!.X!.NodeId,
                        AttributeId = Attributes.Value
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(coordinates.Results, Has.Count.EqualTo(2));
                Assert.That(coordinates.Results[0].WrappedValue.GetDouble(), Is.EqualTo(1));
                Assert.That(coordinates.Results[1].WrappedValue.GetDouble(), Is.EqualTo(4));
                ArrayOf<CraneSample> changed = CraneRuntimeTestData.Replace(samples.ToArrayOf(),
                    CraneRuntimeTestData.Sample(
                        "Lift", CraneSignal.LoadInertia, Variant.Null, StatusCodes.BadSensorFailure));
                changed = CraneRuntimeTestData.Replace(changed, CraneRuntimeTestData.Sample(
                    "Lift", CraneSignal.LoadCenterOfMass, Variant.FromStructure(center), StatusCodes.Uncertain));
                await provider.SetSamplesAsync(changed).ConfigureAwait(false);
                await system.RefreshAsync().ConfigureAwait(false);
                coordinates = await m_first.Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [
                    new ReadValueId { NodeId = load.Inertia.X!.NodeId, AttributeId = Attributes.Value },
                    new ReadValueId
                    {
                        NodeId = load.CenterOfMass.CartesianCoordinates!.X!.NodeId,
                        AttributeId = Attributes.Value
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(coordinates.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadSensorFailure));
                Assert.That(coordinates.Results[0].WrappedValue.IsNull, Is.True);
                Assert.That(coordinates.Results[1].StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                Assert.That(coordinates.Results[1].SourceTimestamp, Is.EqualTo(CraneRuntimeTestData.SourceTime));
                Assert.That(coordinates.Results[1].WrappedValue.GetDouble(), Is.EqualTo(4));
            }
            finally
            {
                await system.RemoveAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Vendor subtypes retain their actual type identities while remaining discoverable through the standard base.
        /// </summary>
        [Test]
        public async Task VendorSystemSubtypeIsDiscoveredWithoutReplacingItsTypeIdentity()
        {
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId("VendorCraneSystemType", m_crane.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName("VendorCraneSystemType", m_crane.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("Vendor crane system"),
                SuperTypeId = TypeId(ObjectTypes.CraneMotionDeviceSystemType),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            await m_server.Manager.AddPredefinedNodeAsync(type).ConfigureAwait(false);
            NodeId original = m_crane.State.TypeDefinitionId;
            m_crane.State.TypeDefinitionId = type.NodeId;
            m_first.Session.NodeCache.InvalidateNode(m_crane.NodeId);
            try
            {
                ArrayOf<CraneEntry> discovered = await m_client.DiscoverAsync().ConfigureAwait(false);
                Assert.That(discovered[0].TypeDefinition, Is.EqualTo(type.NodeId));
                CraneElementSnapshot snapshot = await m_client.ReadElementAsync(m_crane.NodeId).ConfigureAwait(false);
                Assert.That(snapshot.Entry.TypeDefinition, Is.EqualTo(type.NodeId));
                Assert.That(snapshot.Entry.Kind, Is.EqualTo(CraneElementKind.System));
                Assert.That(Signal(snapshot, CraneSignal.SystemOn).Value.WrappedValue.GetBoolean(), Is.True);
            }
            finally
            {
                m_crane.State.TypeDefinitionId = original;
                m_first.Session.NodeCache.InvalidateNode(m_crane.NodeId);
            }
        }

        /// <summary>
        /// Every published category and protective-function enum value is read through its real standard variable.
        /// </summary>
        [Test]
        public async Task CategoryAndProtectionEnumValuesRoundtripOverTcp()
        {
            var device = (CraneMotionDeviceState)m_crane.GetElement("Hoist");
            var protection = (ProtectiveFunctionState)m_crane.GetElement("Overload");
            CraneMotionDeviceCategoryEnum category = device.CraneMotionDeviceCategory!.Value;
            ProtectiveFunctionEnumeration mode = protection.ProtectiveFunctionMode!.Value;
            try
            {
                for (int value = 0; value <= 7; value++)
                {
                    device.CraneMotionDeviceCategory.Value = (CraneMotionDeviceCategoryEnum)value;
                    CraneElementSnapshot snapshot = await m_client.ReadElementAsync(device.NodeId)
                        .ConfigureAwait(false);
                    Assert.That(Signal(snapshot, CraneSignal.MotionDeviceCategory).Value.WrappedValue.GetInt32(),
                        Is.EqualTo(value));
                }
                for (int value = 0; value <= 4; value++)
                {
                    protection.ProtectiveFunctionMode.Value = (ProtectiveFunctionEnumeration)value;
                    CraneElementSnapshot snapshot = await m_client.ReadElementAsync(protection.NodeId)
                        .ConfigureAwait(false);
                    Assert.That(Signal(snapshot, CraneSignal.ProtectiveFunctionMode).Value.WrappedValue.GetInt32(),
                        Is.EqualTo(value));
                }
            }
            finally
            {
                device.CraneMotionDeviceCategory.Value = category;
                protection.ProtectiveFunctionMode.Value = mode;
            }
        }

        /// <summary>
        /// The selected shared event publisher reaches a real, bounded client subscription with unchanged identity.
        /// </summary>
        [Test]
        public async Task StandardEventPublicationReachesTheOwnedObservation()
        {
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId("CraneDiagnosticEventType", m_crane.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName("CraneDiagnosticEventType", m_crane.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("Test crane diagnostic"),
                SuperTypeId = Opc.Ua.ObjectTypeIds.BaseEventType,
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            await m_server.Manager.AddPredefinedNodeAsync(type).ConfigureAwait(false);
            Assert.That(m_first.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            int before = manager!.Count;
            Task<CraneObservation> operation = m_client.ObserveEventsAsync(m_crane.EventNotifierId,
                new CraneObservationOptions { MaximumNotifications = 1, Window = TimeSpan.FromSeconds(10) }).AsTask();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!manager.Items.Any(subscription => subscription.MonitoredItems.Items.Any(item => item.Created)))
            {
                await Task.Delay(20, deadline.Token).ConfigureAwait(false);
            }
            BaseEventState notification = m_server.Manager.SystemContext.CreateInstanceOfBaseEventType();
            notification.TypeDefinitionId = type.NodeId;
            notification.EventType!.Value = type.NodeId;
            ByteString eventId = ByteString.From(new byte[] { 7, 8, 9 });
            notification.EventId!.Value = eventId;
            notification.SourceNode!.Value = m_crane.NodeId;
            notification.SourceName!.Value = "InteropCrane";
            notification.Time!.Value = CraneRuntimeTestData.SourceTime;
            notification.ReceiveTime!.Value = DateTimeUtc.Now;
            notification.Message!.Value = new LocalizedText("Explicitly simulated diagnostic");
            notification.Severity!.Value = 200;
            await m_crane.PublishEventAsync(notification).ConfigureAwait(false);
            CraneObservation observed = await operation.ConfigureAwait(false);
            Assert.That(observed.Events, Has.Count.EqualTo(1));
            Assert.That(observed.Events[0].EventType, Is.EqualTo(type.NodeId));
            Assert.That(observed.Events[0].EventId, Is.EqualTo(eventId));
            Assert.That(manager.Count, Is.EqualTo(before));
        }

        /// <summary>
        /// The injectable factory and session extension borrow the same real managed session.
        /// </summary>
        [Test]
        public async Task ClientFactoryUsesTheExistingManagedSessionWithoutTakingOwnership()
        {
            var managed = (Opc.Ua.Client.ManagedSession)m_first.Session;
            var factory = new CranesHoistsClientFactory(_ => Task.FromResult(managed), NUnitTelemetryContext.Create());
            CranesHoistsClient created = await factory.CreateAsync().ConfigureAwait(false);
            Assert.That(created.Session, Is.SameAs(managed));
            Assert.That((await created.DiscoverAsync().ConfigureAwait(false))[0].NodeId, Is.EqualTo(m_crane.NodeId));
            CranesHoistsClient extension = managed.CranesHoists(NUnitTelemetryContext.Create());
            Assert.That(extension.Session, Is.SameAs(managed));
            Assert.That(managed.Connected, Is.True);
        }

        /// <summary>
        /// Resolves an actual snapshot element rather than relying on discovery order.
        /// </summary>
        private static CraneElementSnapshot Element(CraneSystemSnapshot snapshot, string name)
        {
            return snapshot.Elements.ToArray()!.Single(element => element.Entry.BrowseName.Name == name);
        }

        /// <summary>
        /// Resolves one typed standard signal from a snapshot.
        /// </summary>
        private static CraneSignalValue Signal(CraneElementSnapshot snapshot, CraneSignal signal)
        {
            return snapshot.Signals.ToArray()!.Single(value => value.Signal == signal);
        }

        /// <summary>
        /// Resolves canonical type identities through the reordered session namespace table.
        /// </summary>
        private NodeId TypeId(uint identifier)
        {
            return NodeId.Create(identifier, CranesHoistsModel.NamespaceUri, m_first.Session.NamespaceUris);
        }

        private readonly bool m_roboticsOwner;
        private CraneServerFixture m_server = null!;
        private SimulatedCraneProvider m_provider = null!;
        private CraneSystemHandle m_crane = null!;
        private CraneClientConnection m_first = null!;
        private CraneClientConnection m_second = null!;
        private CranesHoistsClient m_client = null!;
        private NodeId m_authorizedSession;
        private bool m_refuseRequests;
    }
}
