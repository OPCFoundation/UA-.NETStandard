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

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Storage;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises generated experimental codecs, optional model activation, typed handles and DI under NativeAOT.
    /// </summary>
    public sealed class AsyncServicesAotTests
    {
        /// <summary>
        /// Generated envelope factories and embedded original requests remain available without reflection.
        /// </summary>
        [Test]
        [Arguments(false)]
        [Arguments(true)]
        public async Task GeneratedRecoverableEnvelopeRoundTrips(bool json)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            var context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse);
            ushort applicationNamespace = context.NamespaceUris.GetIndexOrAppend("urn:rse-aot:synthetic");
            context.Factory.Builder.AddOpcUaRse().Commit();
            var original = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 17 },
                TimestampsToReturn = TimestampsToReturn.Neither,
                NodesToRead =
                [
                    new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }
                ]
            };
            var submission = new SubmitRequestDataType
            {
                ClientOperationId = Uuid.NewUuid(),
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                Request = new ExtensionObject(original),
                ExecutionAllowance = 10_000,
                ResultRetentionTime = 30_000
            };
            var request = new CallRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 29 },
                MethodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = ExpandedNodeId.ToNodeId(
                            RseObjectIds.RecoverableExecutionManager, context.NamespaceUris),
                        MethodId = new NodeId("synthetic-live-submit", applicationNamespace),
                        InputArguments = [Variant.FromStructure(submission)]
                    }
                ]
            };
            byte[] encoded = json
                ? [.. JsonEncoder.EncodeMessage(request, new byte[4096], context)]
                : BinaryEncoder.EncodeMessage(request, context);
            CallRequest decoded = json
                ? JsonDecoder.DecodeMessage<CallRequest>(encoded, context)
                : BinaryDecoder.DecodeMessage<CallRequest>(encoded, context);
            await Assert.That(decoded.MethodsToCall[0].InputArguments[0].TryGetValue<SubmitRequestDataType>(
                out SubmitRequestDataType? restoredSubmission, context)).IsTrue();
            await Assert.That(restoredSubmission!.ClientOperationId).IsEqualTo(submission.ClientOperationId);
            await Assert.That(restoredSubmission.TypeId).IsEqualTo(new ExpandedNodeId(15u, RseNamespaces.Rse));
            await Assert.That(restoredSubmission.BinaryEncodingId)
                .IsEqualTo(new ExpandedNodeId(16u, RseNamespaces.Rse));
            await Assert.That(decoded.RequestHeader.RequestHandle).IsEqualTo(29u);
            await Assert.That(restoredSubmission.Request.TryGetValue(out ReadRequest? restored, context)).IsTrue();
            await Assert.That(restored!.RequestHeader.RequestHandle).IsEqualTo(17u);
            await Assert.That(restoredSubmission.ExecutionAllowance).IsEqualTo(10_000);
            decoded.Reuse();
        }

        /// <summary>
        /// Revalidates persisted interpretation against fresh generated registrations without reflection.
        /// </summary>
        [Test]
        public async Task QualifiedRecoverableInterpretationSurvivesFreshFactoryAndRejectsUriDrift()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            using var telemetryLifetime = telemetry as IDisposable;
            ServiceMessageContext originalContext = CreateQualifiedContext(telemetry);
            var compatibility = new RecoverableExecutionTypeCompatibility();
            RecoverableExecutionTypeContext captured = await compatibility.CaptureAsync(originalContext)
                .ConfigureAwait(false);
            var original = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = 73,
                    Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    ServiceResult = StatusCodes.Good
                },
                Results = [new DataValue(Variant.From(42))]
            };
            ByteString encoded = captured.Encode(original);
            ServiceMessageContext current = CreateQualifiedContext(telemetry);
            RecoverableExecutionTypeContext restored = await compatibility.RestoreAsync(current, captured.Witness)
                .ConfigureAwait(false);
            ReadResponse decoded = restored.Decode<ReadResponse>(encoded);
            try
            {
                await Assert.That(captured.Witness.Length).IsEqualTo(37);
                await Assert.That(restored.Witness).IsEqualTo(captured.Witness);
                await Assert.That(decoded.ResponseHeader.RequestHandle).IsEqualTo(73u);
                await Assert.That(decoded.ResponseHeader.Timestamp).IsEqualTo(original.ResponseHeader.Timestamp);
                await Assert.That(decoded.Results.Count).IsEqualTo(1);
                await Assert.That(decoded.Results[0].WrappedValue.TryGetValue(out int value)).IsTrue();
                await Assert.That(value).IsEqualTo(42);
                await Assert.That(restored.Encode(decoded)).IsEqualTo(encoded);
            }
            finally
            {
                decoded.Reuse();
                original.Reuse();
            }
            current.NamespaceUris.GetIndexOrAppend("urn:rse-aot:changed-interpretation");
            _ = await Assert.That(
                async () => _ = await compatibility.RestoreAsync(current, captured.Witness).ConfigureAwait(false))
                .Throws<ServiceResultException>();
        }

        /// <summary>
        /// Actual native file barriers, protected reopening and unchanged capacity rejection remain AOT-safe.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task ProtectedDurableStoreReopensAndKeepsCleanupAvailableUnderAot()
        {
            string parent = Path.Combine(Path.GetTempPath(), "ua-rse-store-aot-" + Guid.NewGuid().ToString("N"));
            string directory = Path.Combine(parent, "store");
            Directory.CreateDirectory(parent);
            var secrets = new InMemorySecretStore();
            var keyId = new SecretIdentifier("rse-native-test", InMemorySecretStore.DefaultStoreType);
            byte[] generated = RandomNumberGenerator.GetBytes(32);
            try
            {
                await secrets.SetAsync(keyId, generated).ConfigureAwait(false);
                using ISecret key = await secrets.GetAsync(keyId).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The generated test key is unavailable.");
                using var protector = new AesCbcHmacRecordProtector(key.Bytes);
                var value = ByteString.From(1, 2, 3, 4);
                await using (var store = new FileSharedKeyValueStore(directory, "urn:rse-aot:store", protector, 4096))
                {
                    await store.VerifyDurabilityAsync().ConfigureAwait(false);
                    await Assert.That(await store.TryCompareAndSwapAsync("original", default, value)
                        .ConfigureAwait(false)).IsEqualTo(SharedKeyValueStoreWriteResult.Applied);
                    await Assert.That(await store.TryCompareAndSwapAsync(
                        "overflow", default, new ByteString(new byte[4096])).ConfigureAwait(false))
                        .IsEqualTo(SharedKeyValueStoreWriteResult.CapacityRejected);
                    await store.VerifyDurabilityAsync().ConfigureAwait(false);
                }
                await using var reopened =
                    new FileSharedKeyValueStore(directory, "urn:rse-aot:store", protector, 4096);
                (bool found, ByteString restored) = await reopened.TryGetAsync("original").ConfigureAwait(false);
                await Assert.That(found).IsTrue();
                await Assert.That(restored).IsEqualTo(value);
                (bool overflow, _) = await reopened.TryGetAsync("overflow").ConfigureAwait(false);
                await Assert.That(overflow).IsFalse();
                await Assert.That(await reopened.TryCompareAndSwapAsync("original", value, default)
                    .ConfigureAwait(false)).IsEqualTo(SharedKeyValueStoreWriteResult.Applied);
                (bool retained, _) = await reopened.TryGetAsync("original").ConfigureAwait(false);
                await Assert.That(retained).IsFalse();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(generated);
                _ = await secrets.RemoveAsync(keyId).ConfigureAwait(false);
                Directory.Delete(parent, recursive: true);
            }
        }

        /// <summary>
        /// A live Server installs the optional model and executes, collects and acknowledges a typed Read.
        /// </summary>
        [Test]
        public async Task TypedRecoverableReadAndOptionalModelAreAotSafe()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            string root = Path.Combine(Path.GetTempPath(), "ua-async-aot-" + Guid.NewGuid().ToString("N"));
            var fixture = new AotServerFixture<StandardServer>(
                context => new StandardServer(context)
                {
                    AsyncServices = new AsyncServiceExecutionOptions { Enabled = true }
                }, telemetry)
            {
                AutoAccept = true,
                SecurityNone = true
            };
            try
            {
                await fixture.LoadConfigurationAsync(Path.Combine(root, "server")).ConfigureAwait(false);
                await fixture.StartAsync().ConfigureAwait(false);
                await using var application = new ApplicationInstance(telemetry)
                {
                    ApplicationName = "AsyncAotClient",
                    ApplicationType = ApplicationType.Client
                };
                string clientRoot = Path.Combine(root, "client");
                ArrayOf<CertificateIdentifier> certificates =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        "CN=AsyncAotClient, O=OPC Foundation", CertificateStoreType.Directory, clientRoot);
                ApplicationConfiguration configuration = await application
                    .Build("urn:localhost:OPCFoundation:AsyncAotClient", "urn:OPCFoundation:AsyncAotClient")
                    .AsClient()
                    .AddSecurityConfiguration(certificates, clientRoot)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .CreateAsync().ConfigureAwait(false);
                bool installed = await application.CheckApplicationInstanceCertificatesAsync(
                    true, CertificateFactory.DefaultLifeTime, ct: timeout.Token).ConfigureAwait(false);
                await Assert.That(installed).IsTrue();
                EndpointDescription description = fixture.Server.GetEndpoints().Find(endpoint =>
                    endpoint.TransportProfileUri == Profiles.UaTcpTransport &&
                    endpoint.SecurityMode == MessageSecurityMode.None)!;
                var endpoint = new ConfiguredEndpoint(
                    null, CoreUtils.Clone(description)!, EndpointConfiguration.Create(configuration));
                await using ManagedSession session = await ManagedSession.CreateAsync(
                    new ManagedSessionOptions
                    {
                        Endpoint = endpoint,
                        RecoverableExecution = new RecoverableSessionOptions()
                    },
                    configuration, new DefaultSessionFactory(telemetry), telemetry: telemetry,
                    updateBeforeConnect: false, ct: timeout.Token).ConfigureAwait(false);
                var services = new ServiceCollection();
                services.AddExperimentalAsyncServiceClient();
                services.AddOpcUa().AddServer(options => options.AsyncServices.Enabled = false)
                    .WithExperimentalAsyncServices();
                await using ServiceProvider provider = services.BuildServiceProvider();
                await Assert.That(provider.GetRequiredService<IOptions<OpcUaServerOptions>>()
                    .Value.AsyncServices.Enabled).IsTrue();
                await Assert.That(provider.GetRequiredService<IAsyncServiceExecutionProviderFactory>())
                    .IsNotNull();
                AsyncServiceClient client = provider.GetRequiredService<AsyncServiceClient>();
                await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                    session, new ReadRequest
                    {
                        TimestampsToReturn = TimestampsToReturn.Neither,
                        NodesToRead =
                        [
                            new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_State,
                                AttributeId = Attributes.Value
                            }
                        ]
                    }, timeout.Token).ConfigureAwait(false);
                operation.RequireExplicitAcknowledgement();
                AsyncServiceResult<ReadResponse> result = await operation.ExecuteAsync(
                    10_000, 30_000, ct: timeout.Token).ConfigureAwait(false);
                await Assert.That(result.GetValueOrThrow().Results[0].StatusCode).IsEqualTo(StatusCodes.Good);
                ServiceOperationInfoDataType collected = await operation.CollectAsync(ct: timeout.Token)
                    .ConfigureAwait(false);
                await Assert.That(collected.State).IsEqualTo(ServiceOperationState.OutcomeAvailable);
                ControlResponseDataType receipt = await operation.AcknowledgeAsync(timeout.Token)
                    .ConfigureAwait(false);
                await Assert.That(receipt.Result).IsEqualTo(ServiceOperationControlResult.Applied);
                receipt.Reuse();
                Node capability = await session.ReadNodeAsync(ExpandedNodeId.ToNodeId(
                    RseObjectIds.RecoverableExecutionManager_Capabilities, session.NamespaceUris), timeout.Token)
                    .ConfigureAwait(false);
                await Assert.That(capability.NodeClass).IsEqualTo(NodeClass.Object);
                await using RecoverableAcknowledgementScope acknowledgement = session.BeginAcknowledgementScope(true);
                DataValue current = await session.ReadValueAsync(
                    VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false);
                await Assert.That(current.StatusCode).IsEqualTo(StatusCodes.Good);
                await Assert.That(acknowledgement.Receipt).IsNotNull();
                await acknowledgement.AcknowledgeAsync(timeout.Token).ConfigureAwait(false);
                await Assert.That(acknowledgement.Receipt!.IsAcknowledged).IsTrue();
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Creates independent standard/RSE registrations and exactly matching URI tables.
        /// </summary>
        private static ServiceMessageContext CreateQualifiedContext(ITelemetryContext telemetry)
        {
            IEncodeableFactory factory = EncodeableFactory.Create();
            factory.Builder.AddOpcUaRse().Commit();
            var context = new ServiceMessageContext(telemetry, factory)
            {
                MaxMessageSize = 65_536
            };
            context.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse);
            return context;
        }
    }
}
