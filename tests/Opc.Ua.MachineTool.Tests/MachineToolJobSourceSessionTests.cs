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
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Tests;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using static Opc.Ua.MachineTool.Tests.MachineToolJobSourceTests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies the shared source through actual public machine builders, generated modern proxies and opc.tcp.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolJobSourceSessionTests
    {
        /// <summary>
        /// Buffered modern operations replay every canonical legacy transition and counter over a real session.
        /// </summary>
        [Test]
        public async Task GeneratedModernProxiesAndLegacyEventsShareOneAcceptedSourceOverTcpAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var monitoring = new MachineToolSnapshot
            {
                Operation = new MachineToolOperation { Mode = MachineOperationMode.Automatic, Sample = Acquired }
            };
            await using var source = new MachineToolJobSource(Options(Descriptor() with
            {
                RunsPlanned = 2,
                PartSetKey = "Batch",
                PartSetName = "Explicit parts",
                PartsPlannedPerRun = 2
            }), monitoring);
            await using var other = new MachineToolJobSource(Options());
            await using MachineToolRuntime<MachineToolState> runtime = await BuildAsync(
                fixture, "SharedJobs", source, true).ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> independent = await BuildAsync(
                fixture, "IndependentJobs", other, false).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "JobSourceClient").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                JobManagementTypeClient? management = await client.GetJobManagementAsync(runtime.NodeId)
                    .ConfigureAwait(false);
                Assert.That(management, Is.Not.Null);
                V2.ISA95JobOrderReceiverObjectTypeClient? receiver =
                    await management!.GetJobOrderControlAsync(telemetry).ConfigureAwait(false);
                V2.ISA95JobResponseProviderObjectTypeClient? responses =
                    await management.GetJobOrderResultsAsync(telemetry).ConfigureAwait(false);
                Assert.That(receiver, Is.Not.Null);
                Assert.That(responses, Is.Not.Null);
                Assert.That(await receiver!.StoreAsync(Order(), [new LocalizedText("approved")]).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                V2.ISA95JobOrderDataType updated = Order();
                updated.Priority = 6;
                Assert.That(await receiver.UpdateAsync(updated, []).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                Assert.That(await receiver.StartAsync("Job-1", []).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                Assert.That(await receiver.RevokeStartAsync("Job-1", []).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                Assert.That(await receiver.StartAsync("Job-1", []).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                await source.DrainAsync(runtime.RefreshAsync).ConfigureAwait(false);
                Assert.That(runtime.Production.Observation!.Production.Jobs[0].Phase,
                    Is.EqualTo(MachineToolProductionPhase.Initializing));
                Assert.That(runtime.State.Production!.ActiveProgram!.Name!.Value, Is.Empty);
                Assert.That(await other.GetJobOrdersV2Async().ConfigureAwait(false), Is.Empty);
                Assert.That(independent.Production.Observation!.Production.Jobs, Is.Empty);
                ProductionJobState job = FindJob(fixture, runtime);
                uint subscription = await SubscribeAsync(session, runtime.NodeId).ConfigureAwait(false);
                try
                {
                    Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.BeginExecution)
                        .ConfigureAwait(false));
                    Assert.That(await receiver.PauseAsync("Job-1", []).ConfigureAwait(false),
                        Is.EqualTo(Isa95JobReturnStatus.Success));
                    Assert.That(await receiver.ResumeAsync("Job-1", []).ConfigureAwait(false),
                        Is.EqualTo(Isa95JobReturnStatus.Success));
                    await source.RecordPartsAsync("Job-1", new MachineToolJobSourcePartCounts(2, 1, Acquired))
                        .ConfigureAwait(false);
                    await source.CompleteRunAsync("Job-1").ConfigureAwait(false);
                    await source.RecordPartsAsync("Job-1", new MachineToolJobSourcePartCounts(2, 2, Acquired))
                        .ConfigureAwait(false);
                    await source.CompleteRunAsync("Job-1").ConfigureAwait(false);
                    await source.DrainAsync(runtime.RefreshAsync).ConfigureAwait(false);
                    ArrayOf<uint> transitions = await ReadTransitionsAsync(session, job.State!.NodeId)
                        .ConfigureAwait(false);
                    Assert.That(transitions, Is.EqualTo(new uint[] { 0, 4, 5, 3, 1 }));
                    await AssertUInt32Async(session, job.RunsCompleted!.NodeId, 2).ConfigureAwait(false);
                    await AssertUInt32Async(session, job.PartsCompleted!.NodeId, 4).ConfigureAwait(false);
                    await AssertUInt32Async(session, job.PartsGood!.NodeId, 3).ConfigureAwait(false);
                    await AssertUInt32Async(session, job.State.CurrentState!.Number!.NodeId, 2).ConfigureAwait(false);
                    await AssertUInt32Async(session, job.State.LastTransition!.Number!.NodeId, 1)
                        .ConfigureAwait(false);
                    DataValue raw = await session.ReadValueAsync(
                        runtime.State.Monitoring!.MachineTool!.OperationMode!.NodeId).ConfigureAwait(false);
                    Assert.That(raw.StatusCode, Is.EqualTo(Acquired.StatusCode));
                    Assert.That(raw.SourceTimestamp, Is.EqualTo(Acquired.SourceTimestamp));
                    DataValue program = await session.ReadValueAsync(
                        runtime.State.Production.ActiveProgram.Name.NodeId)
                        .ConfigureAwait(false);
                    Assert.That(program.WrappedValue.TryGetValue(out string? name), Is.True);
                    Assert.That(name, Is.EqualTo("SUPPLIED.NC"));
                    Assert.That(program.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainSubstituteValue));
                    MachineToolJobSourceCapture captured = await source.CaptureAsync().ConfigureAwait(false);
                    Isa95JobResponseReceiptV2 accepted = await source.ReceiveJobResponseAsync(
                        new V2.ISA95JobResponseDataType
                        {
                            EncodingMask = (uint)V2.ISA95JobResponseDataTypeFields.JobResponseData,
                            JobResponseID = "ExternalResult",
                            JobOrderID = "Job-1",
                            JobState = captured.JobOrders[0].State,
                            JobResponseData =
                            [
                                new V2.ISA95ParameterDataType { ID = "ExplicitParts", Value = Variant.From(4u) }
                            ]
                        }).ConfigureAwait(false);
                    Assert.That(accepted.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
                    (V2.ISA95JobResponseDataType received, ulong status) =
                        await responses!.RequestJobResponseByJobOrderIDAsync("Job-1").ConfigureAwait(false);
                    Assert.That(status, Is.EqualTo(Isa95JobReturnStatus.Success));
                    Assert.That(received.JobResponseID, Is.EqualTo("ExternalResult"));
                    Assert.That(received.JobResponseData[0].Value.TryGetValue(out uint count), Is.True);
                    Assert.That(count, Is.EqualTo(4));
                    Assert.That(await receiver.ClearAsync("Job-1", []).ConfigureAwait(false),
                        Is.EqualTo(Isa95JobReturnStatus.Success));
                    await source.DrainAsync(runtime.RefreshAsync).ConfigureAwait(false);
                    Assert.That(fixture.Context.Manager.FindPredefinedNode(job.NodeId), Is.Null);
                }
                finally
                {
                    await session.DeleteSubscriptionsAsync(null, [subscription], CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await runtime.DisposeAsync().ConfigureAwait(false);
                Assert.That((await source.ReadAsync().ConfigureAwait(false)).Revision, Is.GreaterThan(0),
                    "Machine bindings borrow the application source; they do not dispose it.");
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Every termination path is accepted by the actual retained-production validator without invented resumes.
        /// </summary>
        [Test]
        public async Task WireStopAbortAndCancelReplayCanonicalTerminalObservationsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using var source = new MachineToolJobSource(Options());
            await using MachineToolRuntime<MachineToolState> runtime = await BuildAsync(
                fixture, "Terminations", source, false).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "JobSourceStops").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                JobManagementTypeClient management = (await client.GetJobManagementAsync(runtime.NodeId)
                    .ConfigureAwait(false))!;
                V2.ISA95JobOrderReceiverObjectTypeClient receiver =
                    (await management.GetJobOrderControlAsync(telemetry).ConfigureAwait(false))!;
                for (int path = 0; path < 3; path++)
                {
                    Assert.That(await receiver.StoreAndStartAsync(Order(), []).ConfigureAwait(false),
                        Is.EqualTo(Isa95JobReturnStatus.Success));
                    Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.BeginExecution)
                        .ConfigureAwait(false));
                    if (path == 1)
                    {
                        Assert.That(await receiver.PauseAsync("Job-1", []).ConfigureAwait(false),
                            Is.EqualTo(Isa95JobReturnStatus.Success));
                    }
                    ulong status = path == 2
                        ? await receiver.AbortAsync("Job-1", []).ConfigureAwait(false)
                        : await receiver.StopAsync("Job-1", []).ConfigureAwait(false);
                    Assert.That(status, Is.EqualTo(Isa95JobReturnStatus.Success));
                    await source.DrainAsync(runtime.RefreshAsync).ConfigureAwait(false);
                    ProductionJobState job = FindJob(fixture, runtime);
                    Assert.That(runtime.Production.Observation!.Production.Jobs[0].Phase,
                        Is.EqualTo(MachineToolProductionPhase.Aborted));
                    await AssertUInt32Async(session, job.RunsCompleted!.NodeId, 0).ConfigureAwait(false);
                    await AssertUInt32Async(session, job.State!.LastTransition!.Number!.NodeId, path == 1 ? 7u : 6u)
                        .ConfigureAwait(false);
                    Assert.That(await receiver.ClearAsync("Job-1", []).ConfigureAwait(false),
                        Is.EqualTo(Isa95JobReturnStatus.Success));
                    await source.DrainAsync(runtime.RefreshAsync).ConfigureAwait(false);
                }
                Assert.That(await receiver.StoreAsync(Order(), []).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                await source.DrainAsync(runtime.RefreshAsync).ConfigureAwait(false);
                ProductionJobState cancelled = FindJob(fixture, runtime);
                Assert.That(await receiver.CancelAsync("Job-1", []).ConfigureAwait(false),
                    Is.EqualTo(Isa95JobReturnStatus.Success));
                await source.DrainAsync(runtime.RefreshAsync).ConfigureAwait(false);
                Assert.That(fixture.Context.Manager.FindPredefinedNode(cancelled.NodeId), Is.Null);
                Assert.That(await receiver.StoreAsync(new V2.ISA95JobOrderDataType { JobOrderID = "Unknown" }, [])
                    .ConfigureAwait(false), Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
                Assert.That(await source.GetJobOrdersV2Async().ConfigureAwait(false), Is.Empty);
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Uses only the standard, parent-owned provider and two-provider job-management bindings.
        /// </summary>
        private static ValueTask<MachineToolRuntime<MachineToolState>> BuildAsync(
            MachineToolRuntimeFixture fixture, string name, MachineToolJobSource source, bool parts) =>
            fixture.Context.AddMachineTool(name)
                .WithProvider(source)
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan |
                        (parts ? MachineToolFeatures.ProductionParts : MachineToolFeatures.None)
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Reference source test");
                    identity.SerialNumber = name;
                    identity.ProductInstanceUri = "urn:job-source-tests:" + name;
                })
                .WithJobManagement(new MachineToolJobManagementOptions
                {
                    Receiver = source,
                    ResponseProvider = source,
                    Catalog = source
                }).BuildAsync();

        /// <summary>
        /// Resolves the actual generated job, not a test-only facade.
        /// </summary>
        private static ProductionJobState FindJob(
            MachineToolRuntimeFixture fixture, MachineToolRuntime<MachineToolState> runtime) =>
            (ProductionJobState)runtime.State.Production!.ProductionPlan!.FindChild(fixture.Context.Context,
                new QualifiedName("Job-1", fixture.Context.InstanceNamespaceIndex))!;

        /// <summary>
        /// Reads an actual wire value and preserves its substituted quality instead of requiring fabricated Good data.
        /// </summary>
        private static async ValueTask AssertUInt32Async(ISession session, NodeId nodeId, uint expected)
        {
            DataValue value = await session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out uint number), Is.True);
            Assert.That(number, Is.EqualTo(expected));
            Assert.That(value.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainSubstituteValue));
        }

        /// <summary>
        /// Subscribes to real root-routed transition events, with enough queue space to verify the entire history.
        /// </summary>
        private static async ValueTask<uint> SubscribeAsync(ISession session, NodeId machine)
        {
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 20, 1000, 100, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(Opc.Ua.BrowseNames.SourceNode)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = Opc.Ua.ObjectTypeIds.TransitionEventType,
                        BrowsePath =
                        [
                            new QualifiedName(Opc.Ua.BrowseNames.Transition),
                            new QualifiedName(Opc.Ua.BrowseNames.Number)
                        ],
                        AttributeId = Attributes.Value
                    }
                ],
                WhereClause = new ContentFilter()
            };
            CreateMonitoredItemsResponse monitored = await session.CreateMonitoredItemsAsync(
                null, subscription.SubscriptionId, TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = machine, AttributeId = Attributes.EventNotifier },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1, SamplingInterval = 0, QueueSize = 100, DiscardOldest = false,
                            Filter = new ExtensionObject(filter)
                        }
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(monitored.Results[0].StatusCode), Is.True);
            return subscription.SubscriptionId;
        }

        /// <summary>
        /// Reads five actual job transitions, excluding program and active-program events by source identity.
        /// </summary>
        private static async ValueTask<ArrayOf<uint>> ReadTransitionsAsync(ISession session, NodeId jobState)
        {
            var numbers = new List<uint>();
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];
            for (int attempt = 0; attempt < 10 && numbers.Count < 5; attempt++)
            {
                PublishResponse response = await session.PublishWithTimeoutAsync(acknowledgements, 1000)
                    .ConfigureAwait(false);
                acknowledgements =
                [
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = response.SubscriptionId,
                        SequenceNumber = response.NotificationMessage.SequenceNumber
                    }
                ];
                foreach (ExtensionObject data in response.NotificationMessage.NotificationData)
                {
                    if (data.TryGetValue(out EventNotificationList? events) && events != null)
                    {
                        foreach (EventFieldList entry in events.Events)
                        {
                            if (entry.EventFields[0].TryGetValue(out NodeId source) && source == jobState)
                            {
                                Assert.That(entry.EventFields[1].TryGetValue(out uint number), Is.True);
                                numbers.Add(number);
                            }
                        }
                    }
                }
            }
            return numbers.ToArrayOf();
        }
    }
}
