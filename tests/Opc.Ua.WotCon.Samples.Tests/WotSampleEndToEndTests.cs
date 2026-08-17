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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Client;
using AggregationClient;

namespace Opc.Ua.WotCon.Samples.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [Category("Samples")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class WotSampleEndToEndTests
    {
        private const string kPumpNamespaceUri =
            "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";

        private const string kSourceANamespaceUri =
            "urn:opcfoundation.org:UA:WotAggregation:SourceA";

        private const string kWotConNamespaceUri = "http://opcfoundation.org/UA/WoT-Con/";
        private const string kPumpsNamespaceUri = "http://opcfoundation.org/UA/Pumps/";
        private const string kDiNamespaceUri = "http://opcfoundation.org/UA/DI/";

        [Test]
        public async Task RealSamplesAggregateSubscribeAndReplaceGenerationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);

            AggregationClientResult result = await AggregationClientRunner
                .RunAsync(environment.ClientOptions, timeout.Token)
                .ConfigureAwait(false);

            Assert.That(result.LoadResult.Uploaded, Has.Count.EqualTo(s_expectedResourceIds.Length));
            var uploadedResourceIds = new List<string>();
            foreach (WotRegistryDocumentLoadOutcome upload in result.LoadResult.Uploaded)
            {
                uploadedResourceIds.Add(upload.Document.ResourceId);
            }
            Assert.That(uploadedResourceIds, Is.EqualTo(s_expectedResourceIds));
            WotRegistryRefreshResult firstRefresh = result.LoadResult.Refresh ??
                throw new InvalidOperationException("The real loader did not run Refresh.");
            Assert.That(firstRefresh.HasFailures, Is.False, FormatRefresh(firstRefresh));
            bool pumpActive = false;
            foreach (WoTResourceLoadResultDataType load in firstRefresh.Results)
            {
                pumpActive |=
                    load.ResourceId == "sample-pump" &&
                    load.LoadState == WoTLoadStateEnum.Active;
            }
            Assert.That(pumpActive, Is.True);
            Assert.That(result.Values, Has.Count.EqualTo(10));
            AssertResultDouble(
                result,
                "DifferentialPressure",
                environment.SourceAValues.DifferentialPressure);
            AssertResultDouble(result, "FluidTemperature", environment.SourceAValues.FluidTemperature);
            AssertResultDouble(result, "MassFlow", environment.SourceAValues.MassFlow);
            AssertResultDouble(result, "Level", environment.SourceAValues.Level);
            AssertResultBoolean(result, "Cavitation", environment.SourceAValues.Cavitation);
            AssertResultDouble(
                result,
                "BearingTemperature",
                environment.SourceBValues.BearingTemperature);
            AssertResultDouble(result, "PumpPowerInput", environment.SourceBValues.PumpPowerInput);
            AssertResultDouble(result, "PumpEfficiency", environment.SourceBValues.PumpEfficiency);
            AssertResultUInt32(result, "NumberOfStarts", environment.SourceBValues.NumberOfStarts);
            AssertResultBoolean(result, "MotorOverheat", environment.SourceBValues.MotorOverheat);

            WotClientConnection connection = await environment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable connectionLifetime = connection.ConfigureAwait(false);
            ManagedSession session = connection.Session;
            int pumpNamespaceIndex = session.NamespaceUris.GetIndex(kPumpNamespaceUri);
            Assert.That(pumpNamespaceIndex, Is.GreaterThan(0), "The Pump namespace must exist.");
            ushort pumpNs = checked((ushort)pumpNamespaceIndex);
            var pumpNodeId = new NodeId("Pump1", pumpNs);
            var operationalNodeId = new NodeId("Pump1.Operational", pumpNs);
            var measurementsNodeId = new NodeId("Pump1.Operational.Measurements", pumpNs);
            var eventsNodeId = new NodeId("Pump1.Events", pumpNs);
            var processFluidNodeId = new NodeId("Pump1.Events.SupervisionProcessFluid", pumpNs);
            var pumpOperationNodeId = new NodeId("Pump1.Events.SupervisionPumpOperation", pumpNs);
            var differentialPressureNodeId = new NodeId(
                "Pump1.Operational.Measurements.DifferentialPressure",
                pumpNs);

            await AssertPumpHierarchyAsync(
                session,
                pumpNodeId,
                operationalNodeId,
                measurementsNodeId,
                eventsNodeId,
                processFluidNodeId,
                pumpOperationNodeId,
                timeout.Token).ConfigureAwait(false);

            WotClientConnection subscriptionConnection = await environment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable subscriptionConnectionLifetime =
                subscriptionConnection.ConfigureAwait(false);
            ManagedSession subscriptionSession = subscriptionConnection.Session;
            CreateSubscriptionResponse createSubscription =
                await subscriptionSession.CreateSubscriptionAsync(
                    null,
                    100,
                    1000,
                    10,
                    0,
                    true,
                    0,
                    timeout.Token).ConfigureAwait(false);
            uint subscriptionId = createSubscription.SubscriptionId;
            var request = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = differentialPressureNodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 50,
                    QueueSize = 10,
                    DiscardOldest = true
                }
            };
            CreateMonitoredItemsResponse createItem =
                await subscriptionSession.CreateMonitoredItemsAsync(
                    null,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { request }.ToArrayOf(),
                    timeout.Token).ConfigureAwait(false);
            Assert.That(createItem.Results, Has.Count.EqualTo(1));
            Assert.That(createItem.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            uint monitoredItemId = createItem.Results[0].MonitoredItemId;
            (DataValue initial, ArrayOf<SubscriptionAcknowledgement> acknowledgements) =
                await PublishDataChangeAsync(
                    subscriptionSession,
                    [],
                    TimeSpan.FromSeconds(15),
                    timeout.Token).ConfigureAwait(false);
            Assert.That(initial.StatusCode, Is.EqualTo(StatusCodes.Good));
            AssertDataValue(initial, environment.SourceAValues.DifferentialPressure);
            using var retiredPublishLifetime =
                CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            // CA2025 cannot see that the finally block cancels and awaits this task before
            // the subscription connection is disposed. TODO: Remove when flow analysis supports it.
#pragma warning disable CA2025
            Task<(DataValue Value, ArrayOf<SubscriptionAcknowledgement> Acknowledgements)>
                retiredGenerationPublish = PublishDataChangeAsync(
                    subscriptionSession,
                    acknowledgements,
                    TimeSpan.FromMinutes(4),
                    retiredPublishLifetime.Token);
#pragma warning restore CA2025

            try
            {
                string changedPump = CreateChangedPumpDocument(environment);
                (WotRegistryGroupClient group, _) = await connection.Registry
                    .GetOrCreateThingDescriptionGroupAsync(timeout.Token)
                    .ConfigureAwait(false);
                (WotRegistryResourceClient pumpResource, _, _) = await group
                    .GetOrCreateResourceAsync("sample-pump", string.Empty, timeout.Token)
                    .ConfigureAwait(false);
                await pumpResource.UploadNewVersionAsync(
                    ByteString.From(Encoding.UTF8.GetBytes(changedPump)),
                    ct: timeout.Token).ConfigureAwait(false);

                WotRegistryRefreshResult secondRefresh = await connection.Registry
                    .RefreshAllAsync(
                        requestId: "sample-pump-replacement",
                        ct: timeout.Token).ConfigureAwait(false);
                if (secondRefresh.HasFailures)
                {
                    Assert.Fail(FormatRefresh(secondRefresh));
                }
                Assert.That(secondRefresh.NewGeneration, Is.GreaterThan(firstRefresh.NewGeneration));

                DataValue replacement = await ReadValueAsync(
                    session,
                    differentialPressureNodeId,
                    timeout.Token).ConfigureAwait(false);
                Assert.That(replacement.StatusCode, Is.EqualTo(StatusCodes.Good));
                AssertDataValue(replacement, environment.SourceBValues.BearingTemperature);

                ArrayOf<uint> monitoredItemIds = [monitoredItemId];
                SetMonitoringModeResponse disabled = await subscriptionSession
                    .SetMonitoringModeAsync(
                        null,
                        subscriptionId,
                        MonitoringMode.Disabled,
                        monitoredItemIds,
                        timeout.Token).ConfigureAwait(false);
                Assert.That(disabled.Results[0], Is.EqualTo(StatusCodes.Good));
                SetMonitoringModeResponse reporting = await subscriptionSession
                    .SetMonitoringModeAsync(
                        null,
                        subscriptionId,
                        MonitoringMode.Reporting,
                        monitoredItemIds,
                        timeout.Token).ConfigureAwait(false);
                Assert.That(reporting.Results[0], Is.EqualTo(StatusCodes.Good));
                (DataValue retiredGenerationNotification, _) =
                    await retiredGenerationPublish.ConfigureAwait(false);
                Assert.That(retiredGenerationNotification.StatusCode, Is.EqualTo(StatusCodes.Good));
                AssertDataValue(
                    retiredGenerationNotification,
                    environment.SourceAValues.DifferentialPressure);
            }
            finally
            {
                retiredPublishLifetime.Cancel();
                try
                {
                    _ = await retiredGenerationPublish.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (retiredPublishLifetime.IsCancellationRequested)
                {
                }
                catch (ServiceResultException ex)
                    when (retiredPublishLifetime.IsCancellationRequested &&
                        ex.StatusCode == StatusCodes.BadRequestInterrupted)
                {
                }
                DeleteSubscriptionsResponse deleted = await subscriptionSession
                    .DeleteSubscriptionsAsync(
                        null,
                        new uint[] { subscriptionId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                Assert.That(
                    deleted.Results[0],
                    Is.EqualTo(StatusCodes.Good).Or.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            }

            DataValue afterDrain = await ReadValueAsync(
                session,
                differentialPressureNodeId,
                timeout.Token).ConfigureAwait(false);
            Assert.That(afterDrain.StatusCode, Is.EqualTo(StatusCodes.Good));
            AssertDataValue(afterDrain, environment.SourceBValues.BearingTemperature);
        }

        [Test]
        public async Task CurrentPumpAlarmDocumentsDoNotWireAggregationAlarmEventRoundTripAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);

            OpcUaClientConnection source = await environment
                .ConnectSourceAAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable sourceLifetime = source.ConfigureAwait(false);

            ushort sourceNs = ResolveNamespace(source.Session, kSourceANamespaceUri);
            var upstreamSignal = new NodeId(
                "Pump1.Events.SupervisionProcessFluid.Cavitation",
                sourceNs);
            var upstreamAlarm = new NodeId(
                "Pump1.Events.SupervisionProcessFluid.Cavitation.Alarm",
                sourceNs);
            await WriteBooleanAsync(source.Session, upstreamSignal, value: false, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(await ReadTwoStateAsync(
                    source.Session,
                    upstreamAlarm,
                    "ActiveState",
                    timeout.Token).ConfigureAwait(false),
                Is.False,
                "The upstream signal must start inactive so the trip is observable.");

            AggregationClientResult result = await AggregationClientRunner
                .RunAsync(environment.ClientOptions, timeout.Token)
                .ConfigureAwait(false);
            WotRegistryRefreshResult refresh = result.LoadResult.Refresh ??
                throw new InvalidOperationException("The real loader did not run Refresh.");
            Assert.That(refresh.HasFailures, Is.False, FormatRefresh(refresh));

            WotClientConnection connection = await environment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable connectionLifetime = connection.ConfigureAwait(false);

            ushort pumpNs = ResolveNamespace(connection.Session, kPumpNamespaceUri);
            ushort wotConNs = ResolveNamespace(connection.Session, kWotConNamespaceUri);
            var pumpNodeId = new NodeId("Pump1", pumpNs);
            var pump1AssetViewNodeId = new NodeId(
                "WoTRegistry/groups/thingdescriptions/resources/pump1-asset/View",
                wotConNs);
            NodeId supervisionGroupNodeId = await FindOrganizedChildAsync(
                connection.Session,
                pump1AssetViewNodeId,
                "Supervision",
                timeout.Token).ConfigureAwait(false);
            await WriteBooleanAsync(source.Session, upstreamSignal, value: true, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(await ReadTwoStateAsync(
                    source.Session,
                    upstreamAlarm,
                    "ActiveState",
                    timeout.Token).ConfigureAwait(false),
                Is.True,
                "Tripping the upstream boolean must raise the upstream alarm.");
            Assert.That(await ReadTwoStateAsync(
                    source.Session,
                    upstreamAlarm,
                    "AckedState",
                    timeout.Token).ConfigureAwait(false),
                Is.False,
                "The upstream alarm must require acknowledgement after the trip.");

            await AssertDoesNotOrganizeChildAsync(
                connection.Session,
                supervisionGroupNodeId,
                "CavitationAlarm",
                timeout.Token).ConfigureAwait(false);
            await AssertDoesNotGenerateEventTypeAsync(
                connection.Session,
                pumpNodeId,
                "pump1CavitationAlarm",
                timeout.Token).ConfigureAwait(false);
            AssertPumpActionsDoNotDeclareConditionRoundTrip(environment.DocumentsDirectory);
        }

        [Test]
        public async Task InvalidDocumentFailsThroughRealLoaderAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);
            string documents = environment.CreateDocumentsCopy();
            await File.WriteAllTextAsync(
                Path.Combine(documents, "invalid.td.json"),
                "{not-json",
                timeout.Token).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(documents, "documents.json"),
                                     /*lang=json,strict*/
                                     """
                [{
                  "dependsOn": [],
                  "documentKind": "ThingDescription",
                  "groupId": "thingdescriptions",
                  "path": "invalid.td.json",
                  "resourceId": "invalid"
                }]
                """,
                timeout.Token).ConfigureAwait(false);

            Exception failure = await CaptureFailureAsync(
                () => AggregationClientRunner.RunAsync(
                    environment.CreateClientOptions(documents),
                    timeout.Token)).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<ServiceResultException>());
        }

        [Test]
        public async Task MissingManifestDependencyFailsBeforeUploadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);
            string documents = environment.CreateDocumentsCopy();
            await File.WriteAllTextAsync(
                Path.Combine(documents, "documents.json"),
                                     /*lang=json,strict*/
                                     """
                [{
                  "dependsOn": ["not-present"],
                  "documentKind": "ThingModel",
                  "groupId": "thingmodels",
                  "path": "Opc.Ua.Di.tm.json",
                  "resourceId": "opc-ua-di"
                }]
                """,
                timeout.Token).ConfigureAwait(false);

            Exception failure = await CaptureFailureAsync(
                () => AggregationClientRunner.RunAsync(
                    environment.CreateClientOptions(documents),
                    timeout.Token)).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<InvalidDataException>());
            Assert.That(failure.Message, Does.Contain("missing or cyclic dependency"));
        }

        [Test]
        public async Task BadTargetMappingFailsRefreshAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);
            string documents = environment.CreateDocumentsCopy();
            string pumpPath = Path.Combine(documents, "SamplePump.td.json");
            string pump = await File.ReadAllTextAsync(pumpPath, timeout.Token).ConfigureAwait(false);
            pump = pump.Replace(
                "nsu=urn:opcfoundation.org:UA:WotAggregation:PumpInstance;" +
                "s=Pump1.Operational.Measurements.DifferentialPressure",
                "nsu=urn:opcfoundation.org:UA:WotAggregation:PumpInstance;" +
                "s=Pump1.Missing.DifferentialPressure",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(pumpPath, pump, timeout.Token).ConfigureAwait(false);

            Exception failure = await CaptureFailureAsync(
                () => AggregationClientRunner.RunAsync(
                    environment.CreateClientOptions(documents),
                    timeout.Token)).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<ServiceResultException>());
        }

        [Test]
        public async Task UnavailableUpstreamEndpointFailsMappedReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);
            string unavailableEndpoint =
                $"opc.tcp://127.0.0.1:{TestPorts.GetFreePort()}/UnavailableSource";

            Exception failure = await CaptureFailureAsync(
                () => AggregationClientRunner.RunAsync(
                    environment.CreateClientOptions(
                        environment.DocumentsDirectory,
                        sourceAEndpoint: unavailableEndpoint),
                    timeout.Token)).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<ServiceResultException>());
        }

        private static async Task AssertPumpHierarchyAsync(
            ISession session,
            NodeId pumpNodeId,
            NodeId operationalNodeId,
            NodeId measurementsNodeId,
            NodeId eventsNodeId,
            NodeId processFluidNodeId,
            NodeId pumpOperationNodeId,
            CancellationToken cancellationToken)
        {
            ushort pumpsNs = ResolveNamespace(session, kPumpsNamespaceUri);
            ushort diNs = ResolveNamespace(session, kDiNamespaceUri);
            await AssertTypeDefinitionAsync(
                session,
                pumpNodeId,
                new NodeId(1052u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                new NodeId("Pump1.Identification", pumpNodeId.NamespaceIndex),
                new NodeId(1005u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                operationalNodeId,
                new NodeId(1053u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                measurementsNodeId,
                new NodeId(1054u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                eventsNodeId,
                new NodeId(1019u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                processFluidNodeId,
                new NodeId(1015u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                pumpOperationNodeId,
                new NodeId(1016u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                new NodeId(
                    "Pump1.Events.SupervisionProcessFluid.Cavitation",
                    pumpNodeId.NamespaceIndex),
                new NodeId(2373u),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                new NodeId(
                    "Pump1.Events.SupervisionPumpOperation.MotorOverheat",
                    pumpNodeId.NamespaceIndex),
                new NodeId(2373u),
                cancellationToken).ConfigureAwait(false);

            await AssertChildrenAsync(
                session,
                pumpNodeId,
                cancellationToken,
                "Identification",
                "Operational",
                "Events",
                "Maintenance").ConfigureAwait(false);
            await AssertChildrenAsync(
                session,
                operationalNodeId,
                cancellationToken,
                "Measurements").ConfigureAwait(false);
            await AssertChildrenAsync(
                session,
                measurementsNodeId,
                cancellationToken,
                "DifferentialPressure",
                "FluidTemperature",
                "BearingTemperature",
                "PumpPowerInput",
                "MassFlow",
                "PumpEfficiency",
                "Level",
                "NumberOfStarts").ConfigureAwait(false);
            await AssertChildrenAsync(
                session,
                eventsNodeId,
                cancellationToken,
                "SupervisionProcessFluid",
                "SupervisionPumpOperation").ConfigureAwait(false);
            await AssertChildrenAsync(
                session,
                processFluidNodeId,
                cancellationToken,
                "Cavitation").ConfigureAwait(false);
            await AssertChildrenAsync(
                session,
                pumpOperationNodeId,
                cancellationToken,
                "MotorOverheat").ConfigureAwait(false);
        }

        private static async Task AssertTypeDefinitionAsync(
            ISession session,
            NodeId nodeId,
            NodeId expectedTypeDefinition,
            CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                nodeId,
                maxResultsToReturn: 1,
                BrowseDirection.Forward,
                Ua.ReferenceTypeIds.HasTypeDefinition,
                includeSubtypes: false,
                (uint)NodeClass.ObjectType | (uint)NodeClass.VariableType,
                cancellationToken).ConfigureAwait(false);
            Assert.That(references, Has.Count.EqualTo(1), $"{nodeId} must have a TypeDefinition.");
            var actual = ExpandedNodeId.ToNodeId(references[0].NodeId, session.NamespaceUris);
            Assert.That(actual, Is.EqualTo(expectedTypeDefinition), $"Unexpected TypeDefinition for {nodeId}.");
        }

        private static async Task AssertChildrenAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken cancellationToken,
            params string[] expectedNames)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                nodeId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                Ua.ReferenceTypeIds.HierarchicalReferences,
                includeSubtypes: true,
                (uint)NodeClass.Object | (uint)NodeClass.Variable,
                cancellationToken).ConfigureAwait(false);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReferenceDescription reference in references)
            {
                if (reference.BrowseName.Name is string name)
                {
                    names.Add(name);
                }
            }
            foreach (string expectedName in expectedNames)
            {
                Assert.That(names, Does.Contain(expectedName), $"{nodeId} is missing {expectedName}.");
            }
        }

        private static async Task<DataValue> ReadValueAsync(
            ManagedSession session,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                }
            ];
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                cancellationToken).ConfigureAwait(false);
            return response.Results[0];
        }

        private static async Task WriteBooleanAsync(
            ManagedSession session,
            NodeId nodeId,
            bool value,
            CancellationToken cancellationToken)
        {
            var write = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(Variant.From(value))
            };
            WriteResponse response = await session
                .WriteAsync(null, [write], cancellationToken)
                .ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True,
                $"Writing {nodeId} must succeed; got {response.Results[0]}.");
        }

        private static async Task<bool> ReadTwoStateAsync(
            ManagedSession session,
            NodeId alarm,
            string stateBrowseName,
            CancellationToken cancellationToken)
        {
            NodeId stateId = await TranslateAsync(
                    session,
                    alarm,
                    stateBrowseName + "/Id",
                    cancellationToken)
                .ConfigureAwait(false);
            DataValue value = await ReadValueAsync(session, stateId, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True,
                $"Reading {stateId} must succeed; got {value.StatusCode}.");
            Assert.That(value.WrappedValue.TryGetValue(out bool result), Is.True,
                $"{stateId} must carry a Boolean.");
            return result;
        }

        private static async Task<NodeId> TranslateAsync(
            ManagedSession session,
            NodeId start,
            string relativePath,
            CancellationToken cancellationToken)
        {
            var browsePath = new BrowsePath
            {
                StartingNode = start,
                RelativePath = RelativePath.Parse(relativePath, session.TypeTree)
            };
            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, [browsePath], cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<BrowsePathResult> results = response.Results;
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(results[0].StatusCode), Is.True,
                $"'{relativePath}' must resolve from {start}; got {results[0].StatusCode}.");
            Assert.That(results[0].Targets, Has.Count.GreaterThan(0));
            return ExpandedNodeId.ToNodeId(results[0].Targets[0].TargetId, session.NamespaceUris);
        }

        private static async Task<NodeId> FindOrganizedChildAsync(
            ManagedSession session,
            NodeId parentNodeId,
            string browseName,
            CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                parentNodeId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                Ua.ReferenceTypeIds.Organizes,
                includeSubtypes: false,
                nodeClassMask: 0,
                cancellationToken).ConfigureAwait(false);

            foreach (ReferenceDescription reference in references)
            {
                if (string.Equals(reference.BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                }
            }

            var names = new List<string>();
            foreach (ReferenceDescription reference in references)
            {
                names.Add(reference.BrowseName.Name ?? string.Empty);
            }
            throw new AssertionException(
                $"{parentNodeId} does not organize '{browseName}'. Found: {string.Join(", ", names)}.");
        }

        private static async Task AssertDoesNotOrganizeChildAsync(
            ManagedSession session,
            NodeId parentNodeId,
            string browseName,
            CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                parentNodeId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                Ua.ReferenceTypeIds.Organizes,
                includeSubtypes: false,
                nodeClassMask: 0,
                cancellationToken).ConfigureAwait(false);

            foreach (ReferenceDescription reference in references)
            {
                Assert.That(reference.BrowseName.Name, Is.Not.EqualTo(browseName),
                    "The current projection view does not organize selected event affordances yet.");
            }
        }

        private static async Task AssertDoesNotGenerateEventTypeAsync(
            ManagedSession session,
            NodeId notifierNodeId,
            string browseName,
            CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                notifierNodeId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                Ua.ReferenceTypeIds.GeneratesEvent,
                includeSubtypes: false,
                nodeClassMask: (uint)NodeClass.ObjectType,
                cancellationToken).ConfigureAwait(false);

            foreach (ReferenceDescription reference in references)
            {
                Assert.That(reference.BrowseName.Name, Is.Not.EqualTo(browseName),
                    "The current materialized Pump1 object does not generate TD event affordances yet.");
            }
        }

        private static void AssertPumpActionsDoNotDeclareConditionRoundTrip(
            string documentsDirectory)
        {
            string pumpPath = Path.Combine(documentsDirectory, "SamplePump.td.json");
            JsonObject root = JsonNode.Parse(File.ReadAllText(pumpPath))?.AsObject() ??
                throw new InvalidDataException("SamplePump.td.json is empty.");
            JsonObject actions = root["actions"]?.AsObject() ??
                throw new InvalidDataException("SamplePump.td.json has no actions.");

            foreach (KeyValuePair<string, JsonNode?> action in actions)
            {
                JsonObject actionObject = action.Value?.AsObject() ??
                    throw new InvalidDataException($"Action '{action.Key}' is not an object.");
                Assert.Multiple(() =>
                {
                    Assert.That(actionObject.ContainsKey("uav:conditionAction"), Is.False,
                        $"{action.Key} is a pump method, not a condition method.");
                    Assert.That(actionObject.ContainsKey("uav:actsOn"), Is.False,
                        $"{action.Key} does not identify an upstream condition instance.");
                });
            }
        }

        private static async Task<(
            DataValue Value,
            ArrayOf<SubscriptionAcknowledgement> Acknowledgements)> PublishDataChangeAsync(
                ManagedSession session,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                TimeSpan timeout,
                CancellationToken cancellationToken)
        {
            using var publishTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            publishTimeout.CancelAfter(timeout);
            try
            {
                while (true)
                {
                    PublishResponse response = await session.PublishAsync(
                        null,
                        acknowledgements,
                        publishTimeout.Token).ConfigureAwait(false);
                    uint sequenceNumber = response.NotificationMessage.SequenceNumber;
                    if (sequenceNumber != 0)
                    {
                        acknowledgements =
                        [
                            new SubscriptionAcknowledgement
                            {
                                SubscriptionId = response.SubscriptionId,
                                SequenceNumber = sequenceNumber
                            }
                        ];
                    }
                    foreach (ExtensionObject notificationData
                        in response.NotificationMessage.NotificationData)
                    {
                        if (ExtensionObject.ToEncodeable(notificationData) is not DataChangeNotification dataChange)
                        {
                            continue;
                        }
                        foreach (MonitoredItemNotification notification in dataChange.MonitoredItems)
                        {
                            if (notification.ClientHandle == 1)
                            {
                                if (notification.Value.StatusCode ==
                                    StatusCodes.BadWaitingForInitialData)
                                {
                                    continue;
                                }
                                return (notification.Value, acknowledgements);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AssertionException("Timed out waiting for a mapped value notification.");
            }
        }

        private static void AssertResultDouble(
            AggregationClientResult result,
            string name,
            double expected)
        {
            WotPumpValueResult value = FindResultValue(result, name);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), name);
            Assert.That(value.Value.TryGetValue(out double actual), Is.True, name);
            Assert.That(actual, Is.EqualTo(expected), name);
        }

        private static void AssertResultBoolean(
            AggregationClientResult result,
            string name,
            bool expected)
        {
            WotPumpValueResult value = FindResultValue(result, name);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), name);
            Assert.That(value.Value.TryGetValue(out bool actual), Is.True, name);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static void AssertResultUInt32(
            AggregationClientResult result,
            string name,
            uint expected)
        {
            WotPumpValueResult value = FindResultValue(result, name);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), name);
            Assert.That(value.Value.TryGetValue(out uint actual), Is.True, name);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static WotPumpValueResult FindResultValue(
            AggregationClientResult result,
            string name)
        {
            foreach (WotPumpValueResult candidate in result.Values)
            {
                if (candidate.Name == name)
                {
                    return candidate;
                }
            }
            throw new AssertionException($"The loader did not return '{name}'.");
        }

        private static void AssertDataValue(DataValue value, double expected)
        {
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static string CreateChangedPumpDocument(WotSampleEnvironment environment)
        {
            string path = Path.Combine(environment.DocumentsDirectory, "SamplePump.td.json");
            JsonObject root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
                throw new InvalidDataException("SamplePump.td.json is empty.");
            JsonObject properties = root["properties"]?.AsObject() ??
                throw new InvalidDataException("The Pump properties are missing.");
            JsonObject differentialPressure = properties["DifferentialPressure"]?.AsObject() ??
                throw new InvalidDataException("DifferentialPressure is missing.");
            JsonArray forms = differentialPressure["forms"]?.AsArray() ??
                throw new InvalidDataException("DifferentialPressure forms are missing.");
            JsonObject form = forms[0]?.AsObject() ??
                throw new InvalidDataException("DifferentialPressure form is missing.");
            form["href"] = environment.ClientOptions.SourceBEndpoint;
            form["uav:id"] =
                "nsu=urn:opcfoundation.org:UA:WotAggregation:SourceB;" +
                "s=Pump1.Operational.Measurements.BearingTemperature";

            string content = root.ToJsonString();
            return content
                .Replace(
                    "${SOURCE_A_ENDPOINT}",
                    environment.ClientOptions.SourceAEndpoint,
                    StringComparison.Ordinal)
                .Replace(
                    "${SOURCE_B_ENDPOINT}",
                    environment.ClientOptions.SourceBEndpoint,
                    StringComparison.Ordinal);
        }

        private static async Task<Exception> CaptureFailureAsync(
            Func<Task<AggregationClientResult>> action)
        {
            try
            {
                _ = await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ex;
            }
            throw new AssertionException("The workflow unexpectedly succeeded.");
        }

        private static ushort ResolveNamespace(ISession session, string namespaceUri)
        {
            int namespaceIndex = session.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThan(0), $"Missing namespace {namespaceUri}.");
            return checked((ushort)namespaceIndex);
        }

        private static string FormatRefresh(WotRegistryRefreshResult refresh)
        {
            var details = new List<string>
            {
                $"Summary: {refresh.Summary.Outcome}; total={refresh.Summary.Total}; " +
                $"succeeded={refresh.Summary.Succeeded}; failed={refresh.Summary.Failed}; " +
                $"generation={refresh.NewGeneration}"
            };
            foreach (WoTResourceLoadResultDataType result in refresh.Results)
            {
                details.Add(
                    $"{result.ResourceId}: {result.Phase}/{result.Outcome}: {result.Message}");
            }
            return string.Join("; ", details);
        }

        private static readonly string[] s_expectedResourceIds =
        [
            "opc-ua-di",
            "opc-ua-machinery",
            "opc-ua-pumps",
            "sample-pump",
            "pump1-members",
            "pump1-processdata",
            "pump1-conditiondata",
            "pump1-supervision",
            "pump1-management",
            "pump1-asset",
            "pump2-members",
            "pump2-processdata",
            "pump2-conditiondata",
            "pump2-supervision",
            "pump2-management",
            "pump2-asset"
        ];
    }
}
