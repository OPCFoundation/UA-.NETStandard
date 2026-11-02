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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Verifies explicit pump history activation and shared Part 11 client/server routing.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("Historian")]
    [NonParallelizable]
    public sealed class PumpsHistoryTests
    {
        /// <summary>
        /// Creates a shared address-space owner for activation tests.
        /// </summary>
        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new PumpsServerFixture();
            await m_fixture.StartAsync();
        }

        /// <summary>
        /// Releases all activated history scopes with their owner.
        /// </summary>
        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync();
        }

        /// <summary>
        /// Keeps the pump and server-wide fallback unchanged until the application opts in.
        /// </summary>
        [Test]
        public async Task HistoryIsDisabledUntilExplicitlyActivatedAsync()
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            Assert.Multiple(() =>
            {
                Assert.That(variable.Historizing, Is.False);
                Assert.That(variable.AccessLevel & AccessLevels.HistoryRead, Is.Zero);
                Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
                Assert.That(Registry.Resolve(variable.NodeId), Is.Null);
            });
        }

        /// <summary>
        /// Publishes only selected data capabilities and makes their configuration nodes addressable.
        /// </summary>
        [Test]
        public async Task SelectedVariablesRetainProviderCapabilitiesWithoutDefaultFallbackAsync()
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState selected = Measurement(pump);
            var omitted = (BaseVariableState)pump.Measurements.Child(BrowseNames.DifferentialPressure)!;
            using var provider = new InMemoryHistorianProvider();
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.None with { ReadRawData = true };
            provider.Register(selected.NodeId, capabilities);

            IPumpBuilder returned = await m_fixture.Manager.Runtime.HistorizeAsync(
                pump, [selected], provider, autoCapture: false);

            Assert.That(returned, Is.SameAs(pump));
            Assert.That(Registry.Resolve(selected.NodeId), Is.SameAs(provider));
            Assert.That(Registry.Resolve(omitted.NodeId), Is.Null);
            Assert.That(omitted.Historizing, Is.False);
            Assert.That(selected.Historizing, Is.True);
            Assert.That(selected.AccessLevel & AccessLevels.HistoryRead, Is.Not.Zero);
            Assert.That(selected.UserAccessLevel & AccessLevels.HistoryRead, Is.Not.Zero);
            Assert.That(selected.AccessLevel & AccessLevels.HistoryWrite, Is.Zero);
            Assert.That(await provider.GetCapabilitiesAsync(selected.NodeId, default), Is.EqualTo(capabilities));
            AssertConfigurationRegistered(m_fixture.Manager, selected);
        }

        /// <summary>
        /// Rejects missing capability interfaces without substituting an in-memory provider.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnsupportedProvidersAreNotMaskedAsync(bool events)
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            var provider = new Mock<IHistorianProvider>(MockBehavior.Strict);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (events)
                {
                    await m_fixture.Manager.Runtime.HistorizeEventsAsync(pump, provider.Object);
                }
                else
                {
                    await m_fixture.Manager.Runtime.HistorizeAsync(pump, [variable], provider.Object);
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadHistoryOperationUnsupported));
            Assert.That(variable.Historizing, Is.False);
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
            Assert.That(Registry.Resolve(variable.NodeId), Is.Null);
            Assert.That(Registry.Resolve(pump.NodeId), Is.Null);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Requires provider-side configuration rather than implicitly registering a node.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnconfiguredProviderNodesAreRejectedAsync(bool events)
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            using var provider = new InMemoryHistorianProvider();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (events)
                {
                    await m_fixture.Manager.Runtime.HistorizeEventsAsync(pump, provider);
                }
                else
                {
                    await m_fixture.Manager.Runtime.HistorizeAsync(pump, [variable], provider);
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadHistoryOperationUnsupported));
            Assert.That(await provider.IsHistorizingAsync(events ? pump.NodeId : variable.NodeId, default), Is.False);
            Assert.That(variable.Historizing, Is.False);
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
        }

        /// <summary>
        /// Does not advertise history or enable archival insertion when the provider disallows it.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ProviderReadAndCaptureCapabilitiesAreRequiredAsync(bool events, bool readEnabled)
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            using var provider = new InMemoryHistorianProvider();
            provider.Register(
                events ? pump.NodeId : variable.NodeId,
                HistorianNodeCapabilities.None with
                {
                    ReadRawData = !events && readEnabled,
                    ReadEventHistory = events && readEnabled
                });
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (events)
                {
                    await m_fixture.Manager.Runtime.HistorizeEventsAsync(pump, provider);
                }
                else
                {
                    await m_fixture.Manager.Runtime.HistorizeAsync(pump, [variable], provider);
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadHistoryOperationUnsupported));
            Assert.That(variable.Historizing, Is.False);
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
        }

        /// <summary>
        /// Allows externally populated, read-only event history without granting HistoryWrite.
        /// </summary>
        [Test]
        public async Task ReadOnlyEventProviderCanBeActivatedWithoutCaptureAsync()
        {
            IPumpBuilder pump = await CreatePumpAsync();
            using var provider = new InMemoryHistorianProvider();
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.None with { ReadEventHistory = true };
            provider.Register(pump.NodeId, capabilities);

            IPumpBuilder returned = await m_fixture.Manager.Runtime.HistorizeEventsAsync(
                pump, provider, autoCapture: false);

            Assert.That(returned, Is.SameAs(pump));
            Assert.That(Registry.Resolve(pump.NodeId), Is.SameAs(provider));
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryRead, Is.Not.Zero);
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.SubscribeToEvents, Is.Not.Zero);
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryWrite, Is.Zero);
            Assert.That(await provider.GetCapabilitiesAsync(pump.NodeId, default), Is.EqualTo(capabilities));
            AssertConfigurationRegistered(m_fixture.Manager, pump.Pump);
        }

        /// <summary>
        /// Rejects repeated activation rather than attaching duplicate capture handlers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RepeatedActivationIsRejectedAsync(bool events)
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            using var provider = new InMemoryHistorianProvider();
            provider.Register(
                events ? pump.NodeId : variable.NodeId,
                events ? HistorianNodeCapabilities.EventReadWrite : HistorianNodeCapabilities.DataReadWrite);
            if (events)
            {
                await m_fixture.Manager.Runtime.HistorizeEventsAsync(pump, provider, autoCapture: false);
            }
            else
            {
                await m_fixture.Manager.Runtime.HistorizeAsync(pump, [variable], provider, autoCapture: false);
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (events)
                {
                    await m_fixture.Manager.Runtime.HistorizeEventsAsync(pump, provider, autoCapture: false);
                }
                else
                {
                    await m_fixture.Manager.Runtime.HistorizeAsync(pump, [variable], provider, autoCapture: false);
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Validates the complete selection before enabling any variable.
        /// </summary>
        [TestCase("Empty")]
        [TestCase("Null")]
        [TestCase("Duplicate")]
        [TestCase("OtherPump")]
        [TestCase("Unregistered")]
        public async Task InvalidSelectionsLeaveAllVariablesUnchangedAsync(string invalid)
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            IPumpBuilder other = await CreatePumpAsync();
            BaseVariableState otherVariable = Measurement(other);
            using var provider = new InMemoryHistorianProvider();
            provider.Register(variable.NodeId);
            ArrayOf<BaseVariableState> variables = invalid switch
            {
                "Empty" => [],
                "Null" => [variable, null!],
                "Duplicate" => [variable, variable],
                "OtherPump" => [variable, otherVariable],
                _ => [variable, new BaseDataVariableState(pump.Pump) { NodeId = new NodeId("unregistered", 1) }]
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_fixture.Manager.Runtime.HistorizeAsync(pump, variables, provider));

            Assert.That(variable.Historizing, Is.False);
            Assert.That(Registry.Resolve(variable.NodeId), Is.Null);
        }

        /// <summary>
        /// Propagates cancellation before changing either data or event metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationPreventsActivationAsync(bool events)
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            using var provider = new InMemoryHistorianProvider();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                if (events)
                {
                    await m_fixture.Manager.Runtime.HistorizeEventsAsync(
                        pump, provider, cancellationToken: cancellation.Token);
                }
                else
                {
                    await m_fixture.Manager.Runtime.HistorizeAsync(
                        pump, [variable], provider, cancellationToken: cancellation.Token);
                }
            });
            Assert.That(variable.Historizing, Is.False);
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
        }

        /// <summary>
        /// A stopped runtime cannot attach new history even while its address-space owner is still running.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RuntimeDisposalPreventsNewHistoryActivationAsync(bool events)
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await fixture.Manager.CreatePumpAsync(fixture.PumpName("StoppedRuntime"));
            BaseVariableState variable = Measurement(pump);
            using var provider = new InMemoryHistorianProvider();
            provider.Register(variable.NodeId);
            provider.Register(pump.NodeId, HistorianNodeCapabilities.EventReadWrite);
            await fixture.Manager.Runtime.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                if (events)
                {
                    await fixture.Manager.Runtime.HistorizeEventsAsync(pump, provider);
                }
                else
                {
                    await fixture.Manager.Runtime.HistorizeAsync(pump, [variable], provider);
                }
            });
            Assert.That(variable.Historizing, Is.False);
            Assert.That(pump.Pump.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
            var registry = ((IHistorianRegistryProvider)fixture.Server).HistorianRegistry;
            Assert.That(registry.Resolve(variable.NodeId), Is.Null);
            Assert.That(registry.Resolve(pump.NodeId), Is.Null);
        }

        /// <summary>
        /// A provider error after metadata installation starts leaves the node available for retry.
        /// </summary>
        [Test]
        public async Task FailedActivationRestoresMetadataAndCanBeRetriedAsync()
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            byte access = variable.AccessLevel;
            byte userAccess = variable.UserAccessLevel;
            var provider = new Mock<IHistorianProvider>(MockBehavior.Strict);
            provider.As<IHistorianDataProvider>();
            provider.Setup(value => value.IsHistorizingAsync(variable.NodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.None with { ReadRawData = true };
            provider.SetupSequence(value => value.GetCapabilitiesAsync(
                    variable.NodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(capabilities)
                .ThrowsAsync(new IOException("provider unavailable"));
            await Assert.ThrowsAsync<IOException>(async () =>
                await m_fixture.Manager.Runtime.HistorizeAsync(
                    pump, [variable], provider.Object, autoCapture: false));
            Assert.That(variable.Historizing, Is.False);
            Assert.That(variable.AccessLevel, Is.EqualTo(access));
            Assert.That(variable.UserAccessLevel, Is.EqualTo(userAccess));
            Assert.That(variable.FindChild(m_fixture.Manager.SystemContext,
                new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration)), Is.Null);
            Assert.That(Registry.Resolve(variable.NodeId), Is.Null);
            provider.Setup(value => value.GetCapabilitiesAsync(variable.NodeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(capabilities);
            await m_fixture.Manager.Runtime.HistorizeAsync(
                pump, [variable], provider.Object, autoCapture: false);
            Assert.That(variable.Historizing, Is.True);
            Assert.That(Registry.Resolve(variable.NodeId), Is.SameAs(provider.Object));
        }

        /// <summary>
        /// Application-owned routes cannot be replaced by a pump activation.
        /// </summary>
        [Test]
        public async Task ExistingDifferentProviderIsPreservedWhenActivationIsRejectedAsync()
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState variable = Measurement(pump);
            using var existing = new InMemoryHistorianProvider();
            using var attempted = new InMemoryHistorianProvider();
            existing.Register(variable.NodeId);
            attempted.Register(variable.NodeId);
            Registry.RegisterForNode(variable.NodeId, existing);
            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_fixture.Manager.Runtime.HistorizeAsync(pump, [variable], attempted));
                Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(Registry.Resolve(variable.NodeId), Is.SameAs(existing));
                Assert.That(variable.Historizing, Is.False);
                Assert.That(variable.AccessLevel & AccessLevels.HistoryRead, Is.Zero);
                Assert.That(variable.FindChild(m_fixture.Manager.SystemContext,
                    new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration)), Is.Null);
            }
            finally
            {
                Registry.UnregisterForNode(variable.NodeId);
            }
        }

        /// <summary>
        /// A later route conflict rolls back earlier nodes without replacing the application's existing route.
        /// </summary>
        [Test]
        public async Task LaterRouteConflictRollsBackEverySelectedVariableAsync()
        {
            IPumpBuilder pump = await CreatePumpAsync();
            BaseVariableState first = Measurement(pump);
            var second = (BaseVariableState)pump.Measurements.Child(BrowseNames.DifferentialPressure)!;
            byte firstAccess = first.AccessLevel;
            byte secondAccess = second.AccessLevel;
            using var existing = new InMemoryHistorianProvider();
            using var attempted = new InMemoryHistorianProvider();
            existing.Register(second.NodeId);
            attempted.Register(first.NodeId);
            attempted.Register(second.NodeId);
            Registry.RegisterForNode(second.NodeId, existing);
            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_fixture.Manager.Runtime.HistorizeAsync(
                        pump, [first, second], attempted, autoCapture: false));
                Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(Registry.Resolve(first.NodeId), Is.Null);
                Assert.That(Registry.Resolve(second.NodeId), Is.SameAs(existing));
                Assert.That(first.Historizing, Is.False);
                Assert.That(second.Historizing, Is.False);
                Assert.That(first.AccessLevel, Is.EqualTo(firstAccess));
                Assert.That(second.AccessLevel, Is.EqualTo(secondAccess));
                Assert.That(first.FindChild(m_fixture.Manager.SystemContext,
                    new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration)), Is.Null);
                Assert.That(second.FindChild(m_fixture.Manager.SystemContext,
                    new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration)), Is.Null);
            }
            finally
            {
                Registry.UnregisterForNode(second.NodeId);
            }
        }

        /// <summary>
        /// Owner disposal drains and detaches capture while preserving the provider and preexisting routes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OwnerDisposalDrainsCaptureAndReleasesOnlyOwnedRoutesAsync(bool existingRoute)
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await fixture.Manager.CreatePumpAsync(fixture.PumpName("Shutdown"));
            pump.Measurements.SetAnalog(BrowseNames.MassFlow, 0);
            BaseVariableState variable = Measurement(pump);
            var registry = ((IHistorianRegistryProvider)fixture.Server).HistorianRegistry;
            provider.Register(variable.NodeId);
            if (existingRoute)
            {
                registry.RegisterForNode(variable.NodeId, provider);
            }
            await fixture.Manager.Runtime.HistorizeAsync(
                pump, [variable], provider,
                captureOptions: new HistorianCaptureOptions { BatchWindow = TimeSpan.FromMinutes(1) });
            DateTimeUtc sourceTime = DateTimeUtc.Now;
            variable.WrappedValue = Variant.From(42d);
            variable.StatusCode = StatusCodes.UncertainDataSubNormal;
            variable.Timestamp = sourceTime;
            variable.ClearChangeMasks(fixture.Manager.SystemContext, false);
            await fixture.Manager.DisposeAsync();
            Assert.That(registry.Resolve(variable.NodeId), existingRoute ? Is.SameAs(provider) : Is.Null);
            Assert.That(await provider.IsHistorizingAsync(variable.NodeId, default), Is.True);
            variable.WrappedValue = Variant.From(43d);
            variable.ClearChangeMasks(fixture.Manager.SystemContext, false);
            using var requestContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
            var context = new HistorianOperationContext(
                fixture.Manager.SystemContext, requestContext, null, HistoryUpdateType.Insert);
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = variable.NodeId,
                    StartTime = DateTimeUtc.MinValue,
                    EndTime = DateTimeUtc.MaxValue,
                    IsForward = true
                }, default, CancellationToken.None);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Value.WrappedValue, Is.EqualTo(Variant.From(42d)));
            Assert.That(page.Values[0].Value.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainDataSubNormal));
            Assert.That(page.Values[0].Value.SourceTimestamp, Is.EqualTo(sourceTime));
        }

        /// <summary>
        /// Captures pump telemetry and events and reads raw, processed and event history over a real session.
        /// </summary>
        [Test]
        [Category("Integration")]
        public async Task SharedHistoryClientReadsCapturedRawProcessedAndEventHistoryAsync()
        {
            using var dataProvider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            using var eventProvider = new InMemoryHistorianProvider();
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true,
                UriScheme = Utils.UriSchemeOpcTcp
            };
            string pki = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "pump-history", Guid.NewGuid().ToString("N"));
            FileSystemAssetConfigurationStore? identity = null;
            try
            {
                StandardServer server = await fixture.StartAsync(pki);
                NodeManagerRegistration registration = await server.NodeManagerLifecycle.AddAsync(
                    new PumpsNodeManagerFactory(), callerContext: null);
                var manager = (PumpsNodeManager)registration.NodeManager;
                IPumpBuilder pump = await manager.CreatePumpAsync(
                    new QualifiedName("HistoricalPump", manager.InstanceNamespaceIndex));
                pump.WithNameplate(new PumpNameplate
                {
                    NodeId = NodeId.Null,
                    ProductInstanceUri = "urn:example:pump:history",
                    AssetId = "initial",
                    ComponentName = new LocalizedText("en", "Initial name"),
                    Location = "Skid 1"
                });
                identity = new FileSystemAssetConfigurationStore(Path.Combine(pki, "identity"));
                await manager.Runtime.WithRetainedIdentificationAsync(
                    pump, identity,
                    [
                        new RolePermissionType
                        {
                            RoleId = Opc.Ua.ObjectIds.WellKnownRole_Anonymous,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                        }
                    ]);
                BaseVariableState variable = Measurement(pump);
                dataProvider.Register(variable.NodeId);
                eventProvider.Register(
                    pump.NodeId,
                    HistorianNodeCapabilities.EventReadWrite with
                    {
                        EventTypes = [Opc.Ua.ObjectTypeIds.BaseEventType]
                    });
                await manager.Runtime.HistorizeAsync(pump, [variable], dataProvider);
                await manager.Runtime.HistorizeEventsAsync(pump, eventProvider);

                DateTime start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                SetSample(manager.SystemContext, variable, 10, start, StatusCodes.Good);
                SetSample(manager.SystemContext, variable, 14, start.AddSeconds(1), StatusCodes.Good);
                SetSample(
                    manager.SystemContext, variable, 20, start.AddSeconds(2), StatusCodes.UncertainSensorNotAccurate);
                IPumpGroupBuilder supervision = pump.Supervision(BrowseNames.SupervisionMechanics)
                    .SetDiscrete(BrowseNames.BearingFault, false);
                var input = (BaseVariableState)supervision.Child(BrowseNames.BearingFault)!;
                PumpAlarmBinding diagnostic = await manager.Runtime.AddDiagnosticAlarmAsync(
                    supervision, input, "BearingFailure", Opc.Ua.Di.DeviceHealthEnumeration.FAILURE);
                supervision.SetDiscrete(BrowseNames.BearingFault, true);
                input.Timestamp = start.AddSeconds(1);
                await diagnostic.EvaluateAsync();
                ByteString eventId = diagnostic.Alarm.EventId!.Value;
                await WaitForCaptureAsync(manager, variable, pump.Pump, dataProvider, eventProvider);

                using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
                await clientFixture.LoadClientConfigurationAsync(Path.Combine(pki, "client"));
                using Opc.Ua.Client.ISession session = await clientFixture.ConnectAsync(
                    new Uri($"opc.tcp://localhost:{fixture.Port}"), SecurityPolicies.None);
                var pumps = new PumpsClient(session, NUnitTelemetryContext.Create());
                await pumps.WriteAssetIdAsync(pump.NodeId, "retained-service-write");
                await pumps.WriteComponentNameAsync(pump.NodeId, new LocalizedText("de-DE", "Speisepumpe"));
                PumpNameplate? updatedIdentity = await pumps.ReadNameplateAsync(pump.NodeId);
                Assert.That(updatedIdentity!.AssetId, Is.EqualTo("retained-service-write"));
                Assert.That(updatedIdentity.ComponentName, Is.EqualTo(new LocalizedText("de-DE", "Speisepumpe")));
                var location = (BaseVariableState)pump.Identification.Child(Opc.Ua.Machinery.BrowseNames.Location)!;
                location.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = Opc.Ua.ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                    }
                ];
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await pumps.WriteLocationAsync(pump.NodeId, "unauthorized"));
                Assert.That(denied!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
                Assert.That(location.WrappedValue, Is.EqualTo(Variant.From("Skid 1")));
                HistoryClient history = pumps.Session.Historian();
                var raw = new List<DataValue>();
                await foreach (DataValue value in history.ReadRawAsync(
                    variable.NodeId, start, start.AddSeconds(3), maxValuesPerNode: 1))
                {
                    raw.Add(value);
                }
                Assert.That(raw, Has.Count.EqualTo(3), "shared client drains continuation points");
                Assert.That(raw[0].WrappedValue.TryGetValue(out double first), Is.True);
                Assert.That(first, Is.EqualTo(10));
                Assert.That(raw[1].WrappedValue.TryGetValue(out double second), Is.True);
                Assert.That(second, Is.EqualTo(14));
                Assert.That(raw[2].WrappedValue.TryGetValue(out double third), Is.True);
                Assert.That(third, Is.EqualTo(20));
                Assert.That(raw[2].StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainSensorNotAccurate));
                Assert.That(raw[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)start));
                Assert.That(raw[1].SourceTimestamp, Is.EqualTo((DateTimeUtc)start.AddSeconds(1)));
                Assert.That(raw[2].SourceTimestamp, Is.EqualTo((DateTimeUtc)start.AddSeconds(2)));

                var processed = new List<DataValue>();
                await foreach (DataValue value in history.ReadProcessedAsync(
                    variable.NodeId,
                    Opc.Ua.ObjectIds.AggregateFunction_Average,
                    start,
                    start.AddSeconds(2),
                    processingInterval: 2000))
                {
                    processed.Add(value);
                }
                Assert.That(processed, Has.Count.EqualTo(1));
                Assert.That(processed[0].WrappedValue.TryGetValue(out double average), Is.True);
                Assert.That(average, Is.EqualTo(12));
                Assert.That(StatusCode.IsGood(processed[0].StatusCode), Is.True);

                var filter = new EventFilter();
                filter.AddSelectClause(
                    Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.EventId, Attributes.Value);
                filter.AddSelectClause(
                    Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.SourceNode, Attributes.Value);
                filter.AddSelectClause(
                    Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.Time, Attributes.Value);
                filter.AddSelectClause(
                    Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.BrowseNames.Message, Attributes.Value);
                var events = new List<HistoryEventFieldList>();
                await foreach (HistoryEventFieldList value in history.ReadEventsAsync(
                    pump.NodeId, start, start.AddSeconds(3), filter))
                {
                    events.Add(value);
                }
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].EventFields[0].TryGetValue(out ByteString capturedId), Is.True);
                Assert.That(capturedId, Is.EqualTo(eventId));
                Assert.That(events[0].EventFields[1].TryGetValue(out NodeId source), Is.True);
                Assert.That(source, Is.EqualTo(input.NodeId));
                Assert.That(events[0].EventFields[2].TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.EqualTo((DateTimeUtc)start.AddSeconds(1)));
                Assert.That(events[0].EventFields[3].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo("Input is outside its configured normal state."));

                await using ManagedSession managed = await new ManagedSessionBuilder(
                    clientFixture.Config, NUnitTelemetryContext.Create())
                    .UseEndpoint(session.ConfiguredEndpoint)
                    .WithSessionName("Pumps condition refresh")
                    .ConnectAsync();
                using var refreshTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                bool refreshed = false;
                await foreach (EventNotification notification in managed.DefaultStreaming
                    .SubscribeEventsWithRefreshAsync(pump.NodeId, filter, ct: refreshTimeout.Token))
                {
                    if (notification.Fields[0].TryGetValue(out ByteString refreshedId) && refreshedId == eventId)
                    {
                        Assert.That(notification.Fields[1].TryGetValue(out NodeId refreshedSource), Is.True);
                        Assert.That(refreshedSource, Is.EqualTo(input.NodeId));
                        refreshed = true;
                        break;
                    }
                }
                Assert.That(refreshed, Is.True, "the pump notifier refreshes its retained nested condition");
                await managed.GetAlarmClient(NUnitTelemetryContext.Create()).AcknowledgeAsync(
                    diagnostic.Alarm.NodeId, eventId, new LocalizedText("Inspected"));
                Assert.That(diagnostic.Alarm.AckedState!.Id!.Value, Is.True);
                supervision.SetDiscrete(BrowseNames.BearingFault, false);
                await diagnostic.EvaluateAsync();
                Assert.That(diagnostic.Alarm.Retain!.Value, Is.False);
                await managed.DefaultStreaming.DisposeAsync();
                await session.CloseAsync();
            }
            finally
            {
                await fixture.StopAsync();
                identity?.Dispose();
                if (Directory.Exists(pki))
                {
                    Directory.Delete(pki, recursive: true);
                }
            }
        }

        /// <summary>
        /// Returns a new registered pump without sharing node bindings between tests.
        /// </summary>
        private ValueTask<IPumpBuilder> CreatePumpAsync()
        {
            return m_fixture.Manager.CreatePumpAsync(m_fixture.PumpName(
                "History" + (++m_nextPump).ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Materializes a real measurement declared by OPC 40223.
        /// </summary>
        private static BaseVariableState Measurement(IPumpBuilder pump)
        {
            return (BaseVariableState)pump.Measurements.Child(BrowseNames.MassFlow)!;
        }

        /// <summary>
        /// Checks that historical configuration is registered for service-level access.
        /// </summary>
        private static void AssertConfigurationRegistered(PumpsNodeManager manager, NodeState node)
        {
            NodeState? configuration = node.FindChild(
                manager.SystemContext, new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration));
            Assert.That(configuration, Is.Not.Null);
            Assert.That(manager.FindPredefinedNode(configuration!.NodeId), Is.SameAs(configuration));
        }

        /// <summary>
        /// Publishes a source value without changing its original quality or time.
        /// </summary>
        private static void SetSample(
            ISystemContext context,
            BaseVariableState variable,
            double value,
            DateTime time,
            StatusCode status)
        {
            variable.WrappedValue = Variant.From(value);
            variable.Timestamp = time;
            variable.StatusCode = status;
            variable.ClearChangeMasks(context, includeChildren: false);
        }

        /// <summary>
        /// Waits for the shared queues to archive their expected records before service reads.
        /// </summary>
        private static async Task WaitForCaptureAsync(
            PumpsNodeManager manager,
            BaseVariableState variable,
            PumpState pump,
            InMemoryHistorianProvider dataProvider,
            InMemoryHistorianProvider eventProvider)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
            var context = new HistorianOperationContext(
                manager.SystemContext, operation, variable, HistoryUpdateType.Insert);
            while (true)
            {
                HistorianPage<HistoricalDataValue> data = await dataProvider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = variable.NodeId,
                        StartTime = DateTimeUtc.MinValue,
                        EndTime = DateTimeUtc.MaxValue,
                        IsForward = true
                    },
                    default,
                    timeout.Token);
                HistorianPage<HistorianEventRecord> events = await eventProvider.ReadEventsAsync(
                    context,
                    new HistorianEventReadRequest
                    {
                        NodeId = pump.NodeId,
                        StartTime = DateTimeUtc.MinValue,
                        EndTime = DateTimeUtc.MaxValue,
                        IsForward = true,
                        Filter = new EventFilter()
                    },
                    default,
                    timeout.Token);
                if (data.Values.Count == 3 && events.Values.Count == 1)
                {
                    return;
                }
                await Task.Delay(10, timeout.Token);
            }
        }

        /// <summary>
        /// Gets the real shared historian registry.
        /// </summary>
        private IHistorianProviderRegistry Registry =>
            ((IHistorianRegistryProvider)m_fixture.Server).HistorianRegistry;

        /// <summary>The activation-test address space.</summary>
        private PumpsServerFixture m_fixture = null!;

        /// <summary>The next unique pump name.</summary>
        private int m_nextPump;
    }
}
