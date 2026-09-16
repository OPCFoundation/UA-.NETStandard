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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AggregationClient;
using FlatTagServer;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Exercises real WoT sample aggregation, secured management, live generation replacement, and loader failures.
    /// </summary>
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

        /// <summary>
        /// Verifies that the upstream session factory selects the configured message-security mode and policy.
        /// </summary>
        [TestCase(true, MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)]
        [TestCase(false, MessageSecurityMode.None, SecurityPolicies.None)]
        public async Task UpstreamFactorySelectsConfiguredEndpointAsync(
            bool secure, MessageSecurityMode expectedMode, string expectedPolicy)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await using WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token, secure: secure).ConfigureAwait(false);
            OpcUaWotBindingOptions binding = environment.AggregationHost.Services
                .GetRequiredService<OpcUaWotBindingOptions>();
            ISession session = await binding.SessionFactory!(
                environment.ClientOptions.SourceAEndpoint, timeout.Token).ConfigureAwait(false);
            Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(expectedMode));
            Assert.That(session.ConfiguredEndpoint.Description.SecurityPolicyUri,
                Is.EqualTo(expectedPolicy));
        }

        /// <summary>
        /// Verifies that an unsupported upstream security policy is rejected instead of downgraded or retried
        /// indefinitely.
        /// </summary>
        [Test]
        public async Task UnsupportedUpstreamPolicyFailsWithoutDowngradeOrReconnectLoopAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await using WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token, secure: true, aggregationSecurityNone: true).ConfigureAwait(false);
            OpcUaWotBindingOptions binding = environment.AggregationHost.Services
                .GetRequiredService<OpcUaWotBindingOptions>();
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await binding.SessionFactory!(
                    environment.ClientOptions.SourceAEndpoint, timeout.Token).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        /// <summary>
        /// Verifies that encrypted registry management accepts an administrator but denies anonymous management
        /// requests.
        /// </summary>
        [Test]
        public async Task EncryptedRegistryManagementAuthenticatesAdministratorAndRejectsAnonymousAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await using WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token, secure: true).ConfigureAwait(false);
            await using (WotClientConnection administrator = await environment.ConnectAsync(timeout.Token)
                .ConfigureAwait(false))
            {
                WotRegistryRefreshResult refresh = await administrator.Registry
                    .RefreshAllAsync(ct: timeout.Token).ConfigureAwait(false);
                Assert.That(refresh.HasFailures, Is.False);
                Assert.That(administrator.Session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            }
            AggregationClientOptions anonymousOptions = environment.CreateClientOptions(environment.DocumentsDirectory);
            anonymousOptions.IdentityProvider = null;
            await using WotClientConnection anonymous = await WotClientConnection
                .CreateAsync(anonymousOptions, timeout.Token).ConfigureAwait(false);
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await anonymous.Registry.RefreshAllAsync(ct: timeout.Token).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            await using OpcUaClientConnection source = await environment.ConnectSourceAAsync(timeout.Token)
                .ConfigureAwait(false);
            Assert.That(source.Session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(source.Session.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        /// <summary>
        /// Verifies that a valid username identity without the SecurityAdmin role cannot refresh the registry.
        /// </summary>
        [Test]
        public async Task AuthenticatedUserWithoutSecurityAdminCannotManageRegistryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await using WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token, secure: true, grantSecurityAdmin: false).ConfigureAwait(false);
            await using WotClientConnection connection = await environment.ConnectAsync(timeout.Token)
                .ConfigureAwait(false);
            Assert.That(connection.Session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await connection.Registry.RefreshAllAsync(ct: timeout.Token).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Verifies that mutually trusted peers aggregate both pumps successfully over encrypted connections.
        /// </summary>
        [Test]
        public async Task ProvisionedPeersAggregateUsingEncryptedUpstreamConnectionsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await using WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token, secure: true).ConfigureAwait(false);
            AggregationClientResult result = await AggregationClientRunner
                .RunAsync(environment.ClientOptions, timeout.Token).ConfigureAwait(false);
            Assert.That(result.Pumps, Has.Count.EqualTo(2));
            Assert.That(result.LoadResult.Refresh!.HasFailures, Is.False);
            foreach (WotPumpResult pump in result.Pumps)
            {
                Assert.That(pump.Values, Has.Count.EqualTo(15), pump.Name);
                Assert.That(
                    pump.Values.ToList().All(value => value.StatusCode == StatusCodes.Good),
                    Is.True,
                    pump.Name);
            }
            await using WotClientConnection connection = await environment.ConnectAsync(timeout.Token)
                .ConfigureAwait(false);
            Assert.That(connection.Session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(connection.Session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
        }

        /// <summary>
        /// Verifies pump projection and subscriptions across a mapping replacement, retaining the retired
        /// subscription's data.
        /// </summary>
        [Test]
        public async Task RealSamplesAggregateSubscribeAndReplaceGenerationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);

            AggregationClientResult result = await AggregationClientRunner
                .RunAsync(environment.ClientOptions, timeout.Token).ConfigureAwait(false);

            using JsonDocument manifest = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(environment.DocumentsDirectory, "documents.json")));
            string[] expectedResourceIds = manifest.RootElement.EnumerateArray()
                .Select(entry => entry.GetProperty("resourceId").GetString()!).ToArray();
            Assert.That(result.LoadResult.Uploaded, Has.Count.EqualTo(expectedResourceIds.Length));
            var uploadedResourceIds = new List<string>();
            foreach (WotRegistryDocumentLoadOutcome upload in result.LoadResult.Uploaded)
            {
                uploadedResourceIds.Add(upload.Document.ResourceId);
            }
            Assert.That(uploadedResourceIds, Is.EquivalentTo(expectedResourceIds));
            Assert.That(uploadedResourceIds, Does.Contain("sample-pump").And.Contain("pump2-asset"));
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
            Assert.That(result.Pumps, Has.Count.EqualTo(2));
            Assert.That(result.Pumps[0].Name, Is.EqualTo("Pump1"));
            Assert.That(result.Pumps[1].Name, Is.EqualTo("Pump2"));
            Assert.That(result.Values, Has.Count.EqualTo(15));
            for (int i = 0; i < result.Pumps.Count; i++)
            {
                WotPumpResult pump = result.Pumps[i];
                FlatTagValues sourceA = i == 0 ? environment.SourceAValues : environment.SourceAPump2Values;
                FlatTagValues sourceB = i == 0 ? environment.SourceBValues : environment.SourceBPump2Values;
                List<WotPumpValueResult> readings = pump.Values.ToList();
                List<WotPumpBrowseNode> browsed = pump.BrowsedNodes.ToList();
                Assert.That(readings, Has.Count.EqualTo(15), pump.Name);
                Assert.That(readings.All(value => value.StatusCode == StatusCodes.Good), Is.True, pump.Name);
                WotPumpValueResult manufacturer = readings.Single(value => value.Name == "Manufacturer");
                Assert.That(manufacturer.Value.TryGetValue(out LocalizedText name), Is.True);
                Assert.That(name.Text, Is.EqualTo(sourceA.Manufacturer), pump.Name);
                WotPumpValueResult serial = readings.Single(value => value.Name == "SerialNumber");
                Assert.That(serial.Value.TryGetValue(out string? serialNumber), Is.True);
                Assert.That(serialNumber, Is.EqualTo(sourceA.SerialNumber), pump.Name);
                WotPumpValueResult identity = readings.Single(value => value.Name == "ProductInstanceUri");
                Assert.That(identity.Value.TryGetValue(out string? productInstanceUri), Is.True);
                Assert.That(productInstanceUri, Is.EqualTo(sourceA.ProductInstanceUri), pump.Name);
                Assert.That(browsed.Count(node => node.BrowseName.Name == "EngineeringUnits"), Is.EqualTo(7), pump.Name);
                Assert.That(browsed.Count(node => node.BrowseName.Name == "EURange"), Is.EqualTo(7), pump.Name);
                Assert.That(browsed.Any(node => node.NodeClass == NodeClass.Method), Is.True, pump.Name);
                AssertResultDouble(pump, "DifferentialPressure", sourceA.DifferentialPressure);
                AssertResultDouble(pump, "FluidTemperature", sourceA.FluidTemperature);
                AssertResultDouble(pump, "MassFlow", sourceA.MassFlow);
                AssertResultDouble(pump, "Level", sourceA.Level);
                AssertResultBoolean(pump, "Cavitation", sourceA.Cavitation);
                AssertResultDouble(pump, "BearingTemperature", sourceB.BearingTemperature);
                AssertResultDouble(pump, "PumpPowerInput", sourceB.PumpPowerInput);
                AssertResultDouble(pump, "PumpEfficiency", sourceB.PumpEfficiency);
                AssertResultUInt32(pump, "NumberOfStarts", sourceB.NumberOfStarts);
                AssertResultBoolean(pump, "MotorOverheat", sourceB.MotorOverheat);
                AssertResultBoolean(pump, "SourceARunning", true);
                AssertResultBoolean(pump, "SourceBRunning", true);
            }

            WotClientConnection connection = await environment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable connectionLifetime = connection.ConfigureAwait(false);
            ManagedSession session = connection.Session;
            int pumpNamespaceIndex = session.NamespaceUris.GetIndex(kPumpNamespaceUri);
            Assert.That(pumpNamespaceIndex, Is.GreaterThan(0), "The Pump namespace must exist.");
            ushort pumpNs = checked((ushort)pumpNamespaceIndex);
            var differentialPressureNodeId = new NodeId(
                "Pump1.Operational.Measurements.DifferentialPressure",
                pumpNs);

            foreach (string pumpName in s_pumpNames)
            {
                await AssertPumpHierarchyAsync(session, pumpName, timeout.Token).ConfigureAwait(false);
            }

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
                (string resourceId, string changedPump) = await CreateChangedPumpDocumentAsync(
                    environment, timeout.Token).ConfigureAwait(false);
                (WotRegistryGroupClient group, _) = await connection.Registry
                    .GetOrCreateThingDescriptionGroupAsync(timeout.Token)
                    .ConfigureAwait(false);
                (WotRegistryResourceClient pumpResource, _, _) = await group
                    .GetOrCreateResourceAsync(resourceId, string.Empty, timeout.Token)
                    .ConfigureAwait(false);
                WotRegistryUploadResult upload = await pumpResource
                    .UploadNewVersionAndGetResultAsync(
                        ByteString.From(Encoding.UTF8.GetBytes(changedPump)),
                        ct: timeout.Token)
                    .ConfigureAwait(false);
                await pumpResource
                    .SetDefaultVersionAsync(
                        upload.VersionId,
                        expectedEpoch: 0,
                        timeout.Token)
                    .ConfigureAwait(false);

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

        /// <summary>
        /// Verifies that projected pump groups and management actions preserve source ownership and complete alarm
        /// attention.
        /// </summary>
        [Test]
        public async Task RealSamplesRouteManagementAndConditionActionsToEachSourceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);

            environment.ClientOptions.ExerciseControls = true;
            AggregationClientResult result = await AggregationClientRunner
                .RunAsync(environment.ClientOptions, timeout.Token)
                .ConfigureAwait(false);
            WotRegistryRefreshResult refresh = result.LoadResult.Refresh ??
                throw new InvalidOperationException("The real loader did not run Refresh.");
            Assert.That(refresh.HasFailures, Is.False, FormatRefresh(refresh));
            Assert.That(refresh.Results.ToArray()!.All(item => item.Outcome != WoTOutcomeEnum.Warning),
                Is.True, FormatRefresh(refresh));
            Assert.That(result.Controls, Has.Count.EqualTo(4));
            Assert.That(
                result.Controls.ToArray()!.Select(item => item.PumpName + "/" + item.SourceName),
                Is.EquivalentTo(s_controlPairs));

            OpcUaClientConnection sourceA = await environment.ConnectSourceAAsync(timeout.Token).ConfigureAwait(false);
            await using var sourceALifetime = sourceA.ConfigureAwait(false);
            OpcUaClientConnection sourceB = await environment.ConnectSourceBAsync(timeout.Token).ConfigureAwait(false);
            await using var sourceBLifetime = sourceB.ConfigureAwait(false);
            foreach (string pumpName in s_pumpNames)
            {
                await AssertCompletedSourceConditionAsync(
                    sourceA.Session, pumpName, kSourceANamespaceUri,
                    "Events.SupervisionProcessFluid.Cavitation", timeout.Token).ConfigureAwait(false);
                await AssertCompletedSourceConditionAsync(
                    sourceB.Session, pumpName, "urn:opcfoundation.org:UA:WotAggregation:SourceB",
                    "Events.SupervisionPumpOperation.MotorOverheat", timeout.Token).ConfigureAwait(false);
            }

            WotClientConnection connection = await environment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable connectionLifetime = connection.ConfigureAwait(false);

            ushort pumpNs = ResolveNamespace(connection.Session, kPumpNamespaceUri);
            ushort wotConNs = ResolveNamespace(connection.Session, kWotConNamespaceUri);
            for (int i = 0; i < result.Pumps.Count; i++)
            {
                string pumpName = result.Pumps[i].Name;
                var assetView = new NodeId(
                    "WoTRegistry/groups/thingdescriptions/resources/" +
                    pumpName.ToLowerInvariant() + "-asset/View",
                    wotConNs);
                NodeId processGroup = await FindOrganizedChildAsync(
                    connection.Session, assetView, "ProcessData", timeout.Token).ConfigureAwait(false);
                await AssertOrganizedMeasurementsAsync(
                    connection.Session, processGroup, pumpName, pumpNs, s_processMeasurements, timeout.Token)
                    .ConfigureAwait(false);
                NodeId conditionGroup = await FindOrganizedChildAsync(
                    connection.Session, assetView, "ConditionData", timeout.Token).ConfigureAwait(false);
                await AssertOrganizedMeasurementsAsync(
                    connection.Session, conditionGroup, pumpName, pumpNs, s_conditionMeasurements, timeout.Token)
                    .ConfigureAwait(false);
                NodeId eventGroup = await FindOrganizedChildAsync(
                    connection.Session, assetView, "Supervision", timeout.Token).ConfigureAwait(false);
                (_, _, ArrayOf<ReferenceDescription> eventTypes) = await connection.Session.BrowseAsync(
                    null, null, eventGroup, 0, BrowseDirection.Forward, Ua.ReferenceTypeIds.Organizes,
                    false, 0, timeout.Token).ConfigureAwait(false);
                Assert.That(eventTypes, Has.Count.EqualTo(4), pumpName);
                Assert.That(eventTypes.ToList().Count(item => item.NodeClass == NodeClass.ObjectType), Is.EqualTo(2));
                Assert.That(eventTypes.ToList().Count(item => item.NodeClass == NodeClass.Variable), Is.EqualTo(2));
                AssertOrganizedIds(
                    connection.Session, eventTypes, pumpName, pumpNs, s_supervisionMembers);
                NodeId managementGroup = await FindOrganizedChildAsync(
                    connection.Session, assetView, "Management", timeout.Token).ConfigureAwait(false);
                (_, _, ArrayOf<ReferenceDescription> methods) = await connection.Session.BrowseAsync(
                    null, null, managementGroup, 0, BrowseDirection.Forward, Ua.ReferenceTypeIds.Organizes,
                    false, 0, timeout.Token).ConfigureAwait(false);
                Assert.That(methods, Has.Count.EqualTo(14), pumpName);
                Assert.That(methods.ToList().Count(item => item.NodeClass == NodeClass.Method), Is.EqualTo(10));
                Assert.That(methods.ToList().Count(item => item.NodeClass == NodeClass.ObjectType), Is.EqualTo(2));
                Assert.That(methods.ToList().Count(item => item.NodeClass == NodeClass.Variable), Is.EqualTo(2));
                AssertOrganizedIds(
                    connection.Session, methods, pumpName, pumpNs, s_managementMembers);
            }
            await AssertSourceOwnershipAsync(
                connection.Session, sourceA.Session, sourceB.Session, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that invalid Thing Description JSON is rejected through the real document-upload path.
        /// </summary>
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

        /// <summary>
        /// Verifies that a missing manifest dependency is reported before document upload.
        /// </summary>
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

        /// <summary>
        /// Verifies that mapping a measurement to a nonexistent target node causes the real refresh to fail.
        /// </summary>
        [Test]
        public async Task BadTargetMappingFailsRefreshAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            WotSampleEnvironment environment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable environmentLifetime = environment.ConfigureAwait(false);
            string documents = environment.CreateDocumentsCopy();
            (string path, _, JsonObject root, JsonObject property) = await FindMeasurementDocumentAsync(
                documents, timeout.Token).ConfigureAwait(false);
            property["uav:mapToNodeId"] =
                "nsu=urn:opcfoundation.org:UA:WotAggregation:PumpInstance;" +
                "s=Pump1.Missing.DifferentialPressure";
            await File.WriteAllTextAsync(path, root.ToJsonString(), timeout.Token).ConfigureAwait(false);

            Exception failure = await CaptureFailureAsync(
                () => AggregationClientRunner.RunAsync(
                    environment.CreateClientOptions(documents),
                    timeout.Token)).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Verifies that a mapped read reports failure when its upstream source endpoint is unavailable.
        /// </summary>
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
            string pumpName,
            CancellationToken cancellationToken)
        {
            ushort pumpsNs = ResolveNamespace(session, kPumpsNamespaceUri);
            ushort pumpNs = ResolveNamespace(session, kPumpNamespaceUri);
            var pumpNodeId = new NodeId(pumpName, pumpNs);
            var identificationNodeId = new NodeId(pumpName + ".Identification", pumpNs);
            var operationalNodeId = new NodeId(pumpName + ".Operational", pumpNs);
            var measurementsNodeId = new NodeId(pumpName + ".Operational.Measurements", pumpNs);
            var eventsNodeId = new NodeId(pumpName + ".Events", pumpNs);
            var processFluidNodeId = new NodeId(pumpName + ".Events.SupervisionProcessFluid", pumpNs);
            var pumpOperationNodeId = new NodeId(pumpName + ".Events.SupervisionPumpOperation", pumpNs);
            await AssertTypeDefinitionAsync(
                session,
                pumpNodeId,
                new NodeId(1052u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                identificationNodeId,
                new NodeId(1005u, pumpsNs),
                cancellationToken).ConfigureAwait(false);
            (_, _, ArrayOf<ReferenceDescription> identificationProperties) = await session.BrowseAsync(
                null, null, identificationNodeId,
                0, BrowseDirection.Forward, Ua.ReferenceTypeIds.HasProperty, false,
                (uint)NodeClass.Variable, cancellationToken).ConfigureAwait(false);
            Assert.That(
                identificationProperties.ToList().Select(property => property.BrowseName.Name),
                Is.EquivalentTo(s_identityProperties));
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
                    pumpName + ".Events.SupervisionProcessFluid.Cavitation",
                    pumpNodeId.NamespaceIndex),
                new NodeId(2373u),
                cancellationToken).ConfigureAwait(false);
            await AssertTypeDefinitionAsync(
                session,
                new NodeId(
                    pumpName + ".Events.SupervisionPumpOperation.MotorOverheat",
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

        private static async Task AssertOrganizedMeasurementsAsync(
            ManagedSession session,
            NodeId parentNodeId,
            string pumpName,
            ushort namespaceIndex,
            string[] names,
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

            Assert.That(
                references.ToArray()!
                    .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris)),
                Is.EquivalentTo(names.Select(name =>
                    new NodeId($"{pumpName}.Operational.Measurements.{name}", namespaceIndex))));
        }

        private static void AssertOrganizedIds(
            ManagedSession session,
            ArrayOf<ReferenceDescription> references,
            string pumpName,
            ushort namespaceIndex,
            string[] names)
        {
            Assert.That(
                references.ToList()
                    .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris)),
                Is.EquivalentTo(names.Select(name => new NodeId($"{pumpName}.{name}", namespaceIndex))),
                pumpName);
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
            WotPumpResult result,
            string name,
            double expected)
        {
            WotPumpValueResult value = FindResultValue(result, name);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), name);
            Assert.That(value.Value.TryGetValue(out double actual), Is.True, name);
            Assert.That(actual, Is.EqualTo(expected), name);
        }

        private static void AssertResultBoolean(
            WotPumpResult result,
            string name,
            bool expected)
        {
            WotPumpValueResult value = FindResultValue(result, name);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), name);
            Assert.That(value.Value.TryGetValue(out bool actual), Is.True, name);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static void AssertResultUInt32(
            WotPumpResult result,
            string name,
            uint expected)
        {
            WotPumpValueResult value = FindResultValue(result, name);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), name);
            Assert.That(value.Value.TryGetValue(out uint actual), Is.True, name);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static WotPumpValueResult FindResultValue(
            WotPumpResult result,
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

        private static async Task<(string ResourceId, string Json)> CreateChangedPumpDocumentAsync(
            WotSampleEnvironment environment, CancellationToken cancellationToken)
        {
            (_, string resourceId, JsonObject root, JsonObject differentialPressure) =
                await FindMeasurementDocumentAsync(environment.DocumentsDirectory, cancellationToken)
                    .ConfigureAwait(false);
            JsonArray forms = differentialPressure["forms"]?.AsArray() ??
                throw new InvalidDataException("DifferentialPressure forms are missing.");
            JsonObject form = forms[0]?.AsObject() ??
                throw new InvalidDataException("DifferentialPressure form is missing.");
            form["href"] = environment.ClientOptions.SourceBEndpoint;
            form["uav:id"] =
                "nsu=urn:opcfoundation.org:UA:WotAggregation:SourceB;" +
                "s=Pump1.Operational.Measurements.BearingTemperature";

            string content = root.ToJsonString();
            return (resourceId, content
                .Replace(
                    "${SOURCE_A_ENDPOINT}",
                    environment.ClientOptions.SourceAEndpoint,
                    StringComparison.Ordinal)
                .Replace(
                    "${SOURCE_B_ENDPOINT}",
                    environment.ClientOptions.SourceBEndpoint,
                    StringComparison.Ordinal));
        }

        private static async Task<(string Path, string ResourceId, JsonObject Root, JsonObject Property)>
            FindMeasurementDocumentAsync(string directory, CancellationToken cancellationToken)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(directory, "documents.json"), cancellationToken).ConfigureAwait(false));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                if (entry.GetProperty("documentKind").GetString() != "ThingDescription")
                {
                    continue;
                }
                string path = Path.Combine(directory, entry.GetProperty("path").GetString()!);
                JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)
                    .ConfigureAwait(false))!.AsObject();
                if (root["properties"] is not JsonObject properties)
                {
                    continue;
                }
                foreach (KeyValuePair<string, JsonNode?> member in properties)
                {
                    if (member.Value is JsonObject property &&
                        property["uav:mapToNodeId"]?.GetValue<string>() ==
                            "nsu=" + kPumpNamespaceUri + ";s=Pump1.Operational.Measurements.DifferentialPressure")
                    {
                        return (path, entry.GetProperty("resourceId").GetString()!, root, property);
                    }
                }
            }
            throw new AssertionException("The manifest has no document binding Pump1 DifferentialPressure.");
        }

        private static async Task AssertCompletedSourceConditionAsync(
            ManagedSession session, string pump, string namespaceUri, string signalPath,
            CancellationToken cancellationToken)
        {
            ushort ns = ResolveNamespace(session, namespaceUri);
            var alarm = new NodeId(pump + "." + signalPath + ".Alarm", ns);
            Assert.That(await ReadTwoStateAsync(session, alarm, "ActiveState", cancellationToken)
                .ConfigureAwait(false), Is.False);
            Assert.That(await ReadTwoStateAsync(session, alarm, "AckedState", cancellationToken)
                .ConfigureAwait(false), Is.True);
            Assert.That(await ReadTwoStateAsync(session, alarm, "ConfirmedState", cancellationToken)
                .ConfigureAwait(false), Is.True);
            NodeId retain = await TranslateAsync(session, alarm, "Retain", cancellationToken).ConfigureAwait(false);
            DataValue value = await ReadValueAsync(session, retain, cancellationToken).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out bool retained), Is.True);
            Assert.That(retained, Is.False);
        }

        private static async Task AssertSourceOwnershipAsync(
            ManagedSession aggregate,
            ManagedSession sourceA,
            ManagedSession sourceB,
            CancellationToken cancellationToken)
        {
            ushort pumpNs = ResolveNamespace(aggregate, kPumpNamespaceUri);
            ushort sourceANs = ResolveNamespace(sourceA, kSourceANamespaceUri);
            ushort sourceBNs = ResolveNamespace(
                sourceB, "urn:opcfoundation.org:UA:WotAggregation:SourceB");
            ManagedSession[] sources = [sourceA, sourceA, sourceB, sourceB];
            NodeId[] runningNodes =
            [
                new("Pump1.Running", sourceANs), new("Pump2.Running", sourceANs),
                new("Pump1.Running", sourceBNs), new("Pump2.Running", sourceBNs)
            ];
            bool[] expected = [true, true, true, true];

            _ = await aggregate.CallAsync(
                new NodeId("Pump1", pumpNs), new NodeId("Pump1.SourceAStop", pumpNs), cancellationToken)
                .ConfigureAwait(false);
            expected[0] = false;
            await AssertSourceFlagsAsync(sources, runningNodes, expected, cancellationToken).ConfigureAwait(false);

            _ = await aggregate.CallAsync(
                new NodeId("Pump1", pumpNs), new NodeId("Pump1.SourceBStop", pumpNs), cancellationToken)
                .ConfigureAwait(false);
            expected[2] = false;
            await AssertSourceFlagsAsync(sources, runningNodes, expected, cancellationToken).ConfigureAwait(false);

            _ = await aggregate.CallAsync(
                new NodeId("Pump1", pumpNs), new NodeId("Pump1.SourceAStart", pumpNs), cancellationToken)
                .ConfigureAwait(false);
            expected[0] = true;
            await AssertSourceFlagsAsync(sources, runningNodes, expected, cancellationToken).ConfigureAwait(false);

            _ = await aggregate.CallAsync(
                new NodeId("Pump1", pumpNs), new NodeId("Pump1.SourceBStart", pumpNs), cancellationToken)
                .ConfigureAwait(false);
            expected[2] = true;
            await AssertSourceFlagsAsync(sources, runningNodes, expected, cancellationToken).ConfigureAwait(false);

            NodeId[] signals =
            [
                new("Pump1.Events.SupervisionProcessFluid.Cavitation", sourceANs),
                new("Pump2.Events.SupervisionProcessFluid.Cavitation", sourceANs),
                new("Pump1.Events.SupervisionPumpOperation.MotorOverheat", sourceBNs),
                new("Pump2.Events.SupervisionPumpOperation.MotorOverheat", sourceBNs)
            ];
            for (int i = 0; i < signals.Length; i++)
            {
                await WriteBooleanAsync(sources[i], signals[i], true, cancellationToken).ConfigureAwait(false);
            }
            _ = await aggregate.CallAsync(
                new NodeId("Pump1", pumpNs), new NodeId("Pump1.SourceAReset", pumpNs), cancellationToken)
                .ConfigureAwait(false);
            expected[0] = false;
            await AssertSourceFlagsAsync(sources, signals, expected, cancellationToken).ConfigureAwait(false);

            _ = await aggregate.CallAsync(
                new NodeId("Pump2", pumpNs), new NodeId("Pump2.SourceBReset", pumpNs), cancellationToken)
                .ConfigureAwait(false);
            expected[3] = false;
            await AssertSourceFlagsAsync(sources, signals, expected, cancellationToken).ConfigureAwait(false);
        }

        private static async Task AssertSourceFlagsAsync(
            ManagedSession[] sessions,
            NodeId[] nodes,
            bool[] expected,
            CancellationToken cancellationToken)
        {
            Assert.That(nodes, Has.Length.EqualTo(expected.Length));
            Assert.That(sessions, Has.Length.EqualTo(expected.Length));
            for (int i = 0; i < nodes.Length; i++)
            {
                DataValue value = await ReadValueAsync(sessions[i], nodes[i], cancellationToken).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), nodes[i].ToString());
                Assert.That(value.WrappedValue.TryGetValue(out bool actual), Is.True, nodes[i].ToString());
                Assert.That(actual, Is.EqualTo(expected[i]), $"Source {i}: {nodes[i]}");
            }
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

        private static readonly string[] s_controlPairs =
            ["Pump1/SourceA", "Pump1/SourceB", "Pump2/SourceA", "Pump2/SourceB"];
        private static readonly string[] s_pumpNames = ["Pump1", "Pump2"];
        private static readonly string[] s_identityProperties =
            ["Manufacturer", "SerialNumber", "ProductInstanceUri"];
        private static readonly string[] s_processMeasurements =
            ["DifferentialPressure", "FluidTemperature", "Level", "MassFlow"];
        private static readonly string[] s_conditionMeasurements =
            ["BearingTemperature", "PumpPowerInput", "PumpEfficiency", "NumberOfStarts"];
        private static readonly string[] s_supervisionMembers =
        [
            "Events.SupervisionProcessFluid.Cavitation", "Events.SupervisionPumpOperation.MotorOverheat",
            "CavitationAlarm", "MotorOverheatAlarm"
        ];
        private static readonly string[] s_managementMembers =
        [
            "SourceARunning", "SourceBRunning",
            "SourceAStart", "SourceAStop", "SourceAReset", "SourceBStart", "SourceBStop", "SourceBReset",
            "CavitationAcknowledge", "CavitationConfirm", "MotorOverheatAcknowledge", "MotorOverheatConfirm",
            "CavitationAlarm", "MotorOverheatAlarm"
        ];
    }
}
