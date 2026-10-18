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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

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
            var original = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 17 },
                TimestampsToReturn = TimestampsToReturn.Neither,
                NodesToRead =
                [
                    new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }
                ]
            };
            var request = new SubmitServiceRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 29 },
                ClientOperationId = Uuid.NewUuid(),
                Request = new ExtensionObject(original),
                ExecutionAllowance = 10_000,
                ResultRetentionTime = 30_000
            };
            byte[] encoded = json
                ? [.. JsonEncoder.EncodeMessage(request, new byte[4096], context)]
                : BinaryEncoder.EncodeMessage(request, context);
            SubmitServiceRequest decoded = json
                ? JsonDecoder.DecodeMessage<SubmitServiceRequest>(encoded, context)
                : BinaryDecoder.DecodeMessage<SubmitServiceRequest>(encoded, context);
            await Assert.That(decoded.ClientOperationId).IsEqualTo(request.ClientOperationId);
            await Assert.That(decoded.RequestHeader.RequestHandle).IsEqualTo(29u);
            await Assert.That(decoded.Request.TryGetValue(out ReadRequest? restored, context)).IsTrue();
            await Assert.That(restored!.RequestHeader.RequestHandle).IsEqualTo(17u);
            await Assert.That(decoded.ExecutionAllowance).IsEqualTo(10_000);
            decoded.Reuse();
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
                ControlServiceExecutionResponse receipt = await operation.AcknowledgeAsync(timeout.Token)
                    .ConfigureAwait(false);
                await Assert.That(receipt.Result).IsEqualTo(ServiceOperationControlResult.Applied);
                receipt.Reuse();
                Node capability = await session.ReadNodeAsync(new NodeId(1200100u), timeout.Token)
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
    }
}
