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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Server;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.TestModels.AsyncExecution;
using Opc.Ua.Tests;
using Model = Opc.Ua.TestModels.AsyncExecution;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Exercises the actual native mappings rather than a scripted service transport.
    /// </summary>
    [TestFixture(false)]
    [TestFixture(true)]
    [NonParallelizable]
    public sealed class AsyncServiceTransportTests(bool json)
    {
        /// <summary>
        /// Retains a live Client certificate across configuration reload and repeated native channel recovery.
        /// </summary>
        [Test]
        public async Task ApplicationCertificateReloadPreservesNativeRecovery()
        {
            await using WireContext wire = await WireContext.CreateAsync(json, new AsyncExecutionScenario())
                .ConfigureAwait(false);
            NodeId type = ObjectTypeIds.RsaSha256ApplicationCertificateType;
            using CertificateEntry server = wire.Server.CertificateManager!
                .AcquireApplicationCertificateByType(type)!;
            await TrustAndRestrictAsync(wire, server.Certificate, []).ConfigureAwait(false);

            wire.Faults.Arm("submit");
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                wire.Owner, new ReadRequest
                {
                    NodesToRead =
                    [
                        new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_State,
                            AttributeId = Attributes.Value
                        }
                    ]
                }, wire.Timeout).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.SubmitAsync(10_000, 30_000, 0, wire.Timeout).ConfigureAwait(false));
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await wire.Owner.ReconnectAsync(null, null, recovery.Token).ConfigureAwait(false);
            _ = await operation.CollectAsync(2000, wire.Timeout).ConfigureAwait(false);
            Assert.That((await operation.Completion.ConfigureAwait(false)).GetValueOrThrow()
                .Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Runs the complete local GDS-role Client to gateway to device certificate cycle with recovery on both hops.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CertificateCycleAcrossRecoverableGateway(bool deviceJson)
        {
            var deviceScenario = new AsyncExecutionScenario();
            await using WireContext device = await WireContext.CreateAsync(
                deviceJson, deviceScenario, securityAdmin: true, role: "Device").ConfigureAwait(false);
            var forwarding = new AsyncCertificateForwarder(device.Owner, device.Telemetry);
            var gatewayScenario = new AsyncExecutionScenario { Certificates = forwarding };
            await using WireContext gateway = await WireContext.CreateAsync(
                json, gatewayScenario, securityAdmin: true, role: "Gateway").ConfigureAwait(false);
            NodeId groupId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            NodeId certificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType;
            using CertificateEntry before = device.Server.CertificateManager!
                .AcquireApplicationCertificateByType(certificateType)!;
            using var issuerFactory = new CertificateGroup(gateway.Telemetry);
            using ICertificateGroup authority = issuerFactory.Create(
                Path.Combine(gateway.RootPath, "test-ca", "authorities"),
                new CertificateGroupConfiguration
                {
                    SubjectName = "CN=Async Services Synthetic CA, O=OPC Foundation",
                    BaseStorePath = Path.Combine(gateway.RootPath, "test-ca"),
                    CertificateTypes = [nameof(ObjectTypeIds.RsaSha256ApplicationCertificateType)],
                    DefaultCertificateHashSize = 256,
                    CACertificateHashSize = 256
                });
            await authority.InitAsync(gateway.Timeout).ConfigureAwait(false);
            using Certificate issuer = authority.Certificates[certificateType]!.AddRef();
            using ICertificateStore authorityStore = authority.AuthoritiesStore.OpenStore(gateway.Telemetry);
            X509CRLCollection crls = await authorityStore.EnumerateCRLsAsync(issuer, ct: gateway.Timeout)
                .ConfigureAwait(false);
            await TrustAndRestrictAsync(device, issuer, crls).ConfigureAwait(false);
            await TrustAndRestrictAsync(gateway, issuer, crls).ConfigureAwait(false);

            NodeId downstreamSession = device.Owner.SessionId;
            NodeId upstreamSession = gateway.Owner.SessionId;
            var target = ExpandedNodeId.ToNodeId(Model.ObjectIds.DeviceManagement, gateway.Owner.NamespaceUris);
            var client = new AsyncServiceClient();
            ByteString entropy = new(Nonce.CreateRandomNonceData(32));
            device.Faults.Arm("submit");
            gateway.Faults.Arm("submit");
            await using AsyncServiceOperation<ByteString> csrOperation = await client.CreateMethodAsync(
                gateway.Owner,
                (session, ct) => new ServerConfigurationTypeClient(session, target, gateway.Telemetry)
                    .CreateSigningRequestAsync(groupId, certificateType, before.Certificate.Subject, true, entropy, ct),
                gateway.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ByteString> csrOutcome = await csrOperation.ExecuteAsync(
                60_000, 60_000, ct: gateway.Timeout).ConfigureAwait(false);
            ByteString csr = csrOutcome.GetValueOrThrow();
            Assert.That(csr.IsEmpty, Is.False);
            Assert.That(device.KeyGenerator.Calls, Is.EqualTo(1));
            Assert.That(gateway.KeyGenerator.Calls, Is.Zero);
            Assert.That(device.Faults.Drops, Is.EqualTo(1));
            Assert.That(gateway.Faults.Drops, Is.EqualTo(1));
            Assert.That(device.Owner.SessionId, Is.EqualTo(downstreamSession));
            Assert.That(gateway.Owner.SessionId, Is.EqualTo(upstreamSession));

            var application = new ApplicationRecordDataType
            {
                ApplicationUri = device.Configuration.ApplicationUri,
                ApplicationType = ApplicationType.Server,
                ApplicationNames = [new LocalizedText("Synthetic Async Device")]
            };
            await authority.VerifySigningRequestAsync(application, certificateType, csr, gateway.Timeout)
                .ConfigureAwait(false);
            using Certificate issued = await authority.SigningRequestAsync(
                application, certificateType, X509Utils.GetDomainsFromCertificate(before.Certificate).ToArray()!,
                csr, gateway.Timeout).ConfigureAwait(false);
            using RSA previousPublicKey = before.Certificate.GetRSAPublicKey()!;
            using RSA issuedPublicKey = issued.GetRSAPublicKey()!;
            Assert.That(issuedPublicKey.ExportParameters(false).Modulus,
                Is.Not.EqualTo(previousPublicKey.ExportParameters(false).Modulus));

            await using AsyncServiceOperation<bool> update = await client.CreateMethodAsync(
                gateway.Owner,
                (session, ct) => new ServerConfigurationTypeClient(session, target, gateway.Telemetry)
                    .UpdateCertificateAsync(groupId, certificateType, new ByteString(issued.RawData),
                        [new ByteString(issuer.RawData)], string.Empty, default, ct),
                gateway.Timeout).ConfigureAwait(false);
            AsyncServiceResult<bool> updated = await update.ExecuteAsync(60_000, 60_000, ct: gateway.Timeout)
                .ConfigureAwait(false);
            Assert.That(updated.GetValueOrThrow(), Is.True);

            await using SessionRequestContext strict = await SessionRequestContext.CaptureAsync(
                device.Owner, device.Timeout).ConfigureAwait(false);
            await using AsyncServiceOperation<ReadResponse> retained = await client.CreateReadAsync(
                device.Owner, new ReadRequest
                {
                    NodesToRead =
                    [
                        new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_State,
                            AttributeId = Attributes.Value
                        }
                    ]
                }, device.Timeout).ConfigureAwait(false);
            retained.RequireExplicitAcknowledgement();
            _ = await retained.SubmitAsync(60_000, 60_000, 2000, device.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> beforeRotation = await retained.Completion.WaitAsync(device.Timeout)
                .ConfigureAwait(false);
            await using AsyncServiceOperation<StatusCode> apply = await client.CreateMethodAsync(
                gateway.Owner,
                (session, ct) => new ServerConfigurationTypeClient(session, target, gateway.Telemetry)
                    .ApplyChangesAsync(ct),
                gateway.Timeout).ConfigureAwait(false);
            AsyncServiceResult<StatusCode> applied = await apply.ExecuteAsync(60_000, 60_000, ct: gateway.Timeout)
                .ConfigureAwait(false);
            Assert.That(applied.GetValueOrThrow(), Is.EqualTo(StatusCodes.Good));
            await device.Server.CurrentInstance.ConfigurationNodeManager
                .DrainPendingApplyChangesAsync(device.Timeout).ConfigureAwait(false);
            using CertificateEntry installed = device.Server.CertificateManager
                .AcquireApplicationCertificateByType(certificateType)!;
            Assert.That(installed.Certificate.RawData, Is.EqualTo(issued.RawData));
            Assert.That(installed.Certificate.HasPrivateKey, Is.True);
            using RSA installedKey = installed.Certificate.GetRSAPrivateKey()!;
            byte[] challenge = Nonce.CreateRandomNonceData(32);
            byte[] signature = installedKey.SignHash(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(issuedPublicKey.VerifyHash(challenge, signature, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1), Is.True);

            await device.Owner.ConfiguredEndpoint.UpdateFromServerAsync(
                device.ClientConfiguration, device.Telemetry, device.Timeout).ConfigureAwait(false);
            await device.RecoverAsync().ConfigureAwait(false);
            Assert.That(strict.Invalidated.IsCompleted, Is.True);
            Assert.That(device.Owner.SessionId, Is.EqualTo(downstreamSession));
            Assert.That(device.Owner.TransportChannel.ServerChannelCertificate, Is.EqualTo(issued.RawData));
            ServiceOperationInfoDataType afterRotation = await retained.CollectAsync(ct: device.Timeout)
                .ConfigureAwait(false);
            Assert.That(afterRotation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(retained.Invalidated.IsCompleted, Is.False);
            Assert.That(await retained.Completion.ConfigureAwait(false), Is.SameAs(beforeRotation));
            ControlServiceExecutionResponse receipt = await retained.AcknowledgeAsync(device.Timeout)
                .ConfigureAwait(false);
            Assert.That(receipt.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            receipt.Reuse();
            await using AsyncServiceOperation<(ArrayOf<NodeId>, ArrayOf<ByteString>)> certificates =
                await client.CreateMethodAsync(gateway.Owner,
                    (session, ct) => new ServerConfigurationTypeClient(session, target, gateway.Telemetry)
                        .GetCertificatesAsync(groupId, ct), gateway.Timeout).ConfigureAwait(false);
            (ArrayOf<NodeId> types, ArrayOf<ByteString> values) = (await certificates.ExecuteAsync(
                60_000, 60_000, ct: gateway.Timeout).ConfigureAwait(false)).GetValueOrThrow();
            int index = Array.IndexOf(types.ToArray()!, certificateType);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(values[index].Span.ToArray(), Is.EqualTo(issued.RawData));
            Assert.That(forwarding.OperationIds.Count, Is.EqualTo(4));
            Assert.That(device.Faults.Submissions, Is.EqualTo(5));
            Assert.That(gateway.Faults.Submissions, Is.EqualTo(4));
            Assert.That(
                device.KeyGenerator.Calls, Is.EqualTo(1), "No recovered hop may regenerate another private key.");
            Assert.That(gateway.Owner.SessionId, Is.EqualTo(upstreamSession));
        }

        /// <summary>
        /// Native transport protection does not confer SecurityAdmin or application-level method authorization.
        /// </summary>
        [Test]
        public async Task ProtectedCertificateCallStillRequiresSecurityAdmin()
        {
            await using WireContext wire = await WireContext.CreateAsync(json, new AsyncExecutionScenario())
                .ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ByteString> operation = await client.CreateMethodAsync(
                wire.Owner,
                (session, ct) => new ServerConfigurationTypeClient(
                    session, ObjectIds.ServerConfiguration, wire.Telemetry)
                    .CreateSigningRequestAsync(
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType, string.Empty, true, default, ct),
                wire.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ByteString> result = await operation.ExecuteAsync(
                10_000, 30_000, ct: wire.Timeout).ConfigureAwait(false);
            ServiceResultException denied = Assert.Throws<ServiceResultException>(() => result.GetValueOrThrow())!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(wire.KeyGenerator.Calls, Is.Zero);
            Assert.That(wire.SawTransportSecurity, Is.EqualTo(json));
        }

        /// <summary>
        /// Oversized original output produces one bounded retained failure over the actual selected mapping.
        /// </summary>
        [Test]
        public async Task OversizedNativeReadRetainsFailureWithoutExecutingAgain()
        {
            var scenario = new AsyncExecutionScenario { Blob = new ByteString(new byte[131_072]) };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                wire.Owner, new ReadRequest
                {
                    TimestampsToReturn = TimestampsToReturn.Neither,
                    NodesToRead =
                    [
                        new ReadValueId
                        {
                            NodeId = ExpandedNodeId.ToNodeId(Model.VariableIds.Probe_Blob, wire.Owner.NamespaceUris),
                            AttributeId = Attributes.Value
                        }
                    ]
                }, wire.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            AsyncServiceResult<ReadResponse> outcome = await operation.ExecuteAsync(
                10_000, 30_000, ct: wire.Timeout).ConfigureAwait(false);
            Assert.That(outcome.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ResultUnavailable));
            Assert.That(outcome.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadResponseTooLarge));
            Assert.That(outcome.OriginalResponse, Is.Null);
            _ = await operation.CollectAsync(ct: wire.Timeout).ConfigureAwait(false);
            Assert.That(scenario.BlobReads, Is.EqualTo(1));
            Assert.That(operation.LastOperation!.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ResultUnavailable));
            ControlServiceExecutionResponse receipt = await operation.AcknowledgeAsync(wire.Timeout)
                .ConfigureAwait(false);
            Assert.That(receipt.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            receipt.Reuse();
        }

        /// <summary>
        /// Positive initial/collection waits end while the independently owned Method remains gated.
        /// </summary>
        [Test]
        public async Task BoundedWaitsDoNotCancelOrRestartOwnedWork()
        {
            var scenario = new AsyncExecutionScenario { Delay = true };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            var probe = ExpandedNodeId.ToNodeId(Model.ObjectIds.Probe, wire.Owner.NamespaceUris);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<int> operation = await client.CreateMethodAsync(
                wire.Owner,
                (session, ct) => new ProbeTypeClient(session, probe, wire.Telemetry).DelayedAddAsync(5, ct),
                wire.Timeout).ConfigureAwait(false);
            ServiceOperationInfoDataType initial = await operation.SubmitAsync(
                10_000, 30_000, waitTime: 1, ct: wire.Timeout).ConfigureAwait(false);
            Assert.That(initial.State, Is.EqualTo(ServiceOperationState.Pending));
            await scenario.Entered.WaitAsync(wire.Timeout).ConfigureAwait(false);
            ServiceOperationInfoDataType bounded = await operation.CollectAsync(1, wire.Timeout).ConfigureAwait(false);
            Assert.That(bounded.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(bounded.ExecutionRemaining, Is.LessThanOrEqualTo(initial.ExecutionRemaining));
            Assert.That(bounded.CancellationRequested, Is.False);
            Assert.That(scenario.Invocations, Is.EqualTo(1));
            scenario.Release();
            AsyncServiceResult<int> outcome =
                await operation.WaitForResultAsync(ct: wire.Timeout).ConfigureAwait(false);
            Assert.That(outcome.GetValueOrThrow(), Is.EqualTo(12));
            Assert.That(scenario.Invocations, Is.EqualTo(1));
        }

        /// <summary>
        /// Native UACP receive limits and HTTPS response-body limits can shrink and recover without invalidating the
        /// handle.
        /// </summary>
        [Test]
        public async Task NativeDeliveryCapacityCanShrinkAndRecoverWithinTheOriginalSession()
        {
            var scenario = new AsyncExecutionScenario
            {
                Blob = new ByteString(new byte[96_000]),
                DelayBlob = true
            };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            NodeId sessionId = wire.Owner.SessionId;
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<ReadResponse> operation = await client.CreateReadAsync(
                wire.Owner, new ReadRequest
                {
                    TimestampsToReturn = TimestampsToReturn.Neither,
                    NodesToRead =
                    [
                        new ReadValueId
                        {
                            NodeId = ExpandedNodeId.ToNodeId(Model.VariableIds.Probe_Blob, wire.Owner.NamespaceUris),
                            AttributeId = Attributes.Value
                        }
                    ]
                }, wire.Timeout).ConfigureAwait(false);
            ServiceOperationInfoDataType admission = await operation.SubmitAsync(
                60_000, 60_000, ct: wire.Timeout).ConfigureAwait(false);
            Assert.That(admission.State, Is.EqualTo(ServiceOperationState.Pending));
            await scenario.Entered.WaitAsync(wire.Timeout).ConfigureAwait(false);
            await using SessionRequestContext strict = await SessionRequestContext.CaptureAsync(
                wire.Owner, wire.Timeout).ConfigureAwait(false);
            wire.Owner.ConfiguredEndpoint.Configuration!.MaxMessageSize = 65_536;
            await wire.RecoverAsync().ConfigureAwait(false);
            Assert.That(wire.Owner.TransportChannel.MessageContext.MaxMessageSize, Is.EqualTo(65_536));
            Assert.That(strict.Invalidated.IsCompleted, Is.True);
            scenario.Release();
            ServiceResultException deliveryFailure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CollectAsync(2000, wire.Timeout).ConfigureAwait(false))!;
            Assert.That(deliveryFailure.StatusCode,
                Is.EqualTo(StatusCodes.BadResponseTooLarge).Or.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(operation.Invalidated.IsCompleted, Is.False);
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(wire.Faults.Acknowledgements, Is.Zero);
            Assert.That(scenario.BlobReads, Is.EqualTo(1));
            wire.Owner.ConfiguredEndpoint.Configuration.MaxMessageSize = 131_072;
            await wire.RecoverAsync().ConfigureAwait(false);
            Assert.That(wire.Owner.SessionId, Is.EqualTo(sessionId));
            Assert.That(wire.Owner.TransportChannel.MessageContext.MaxMessageSize, Is.EqualTo(131_072));
            _ = await operation.CollectAsync(2000, wire.Timeout).ConfigureAwait(false);
            ReadResponse result = (await operation.Completion.WaitAsync(wire.Timeout).ConfigureAwait(false))
                .GetValueOrThrow();
            Assert.That(result.Results[0].WrappedValue.TryGetValue(out ByteString value), Is.True);
            Assert.That(value.Span.SequenceEqual(scenario.Blob.Span), Is.True);
            Assert.That(operation.Invalidated.IsCompleted, Is.False);
            Assert.That(scenario.BlobReads, Is.EqualTo(1));
            Assert.That(wire.Faults.Submissions, Is.EqualTo(1));
            Assert.That(wire.Faults.Acknowledgements, Is.EqualTo(1));
            byte[][] deliveries = [.. wire.Faults.OriginalResponses];
            Assert.That(deliveries, Has.Length.EqualTo(2));
            Assert.That(deliveries[1], Is.EqualTo(deliveries[0]));
            double[] remaining = [.. wire.Faults.CollectionRetention];
            Assert.That(remaining[1], Is.LessThanOrEqualTo(remaining[0]));
        }

        /// <summary>
        /// A batch returns one complete response with ordinary per-item errors and no rollback of successful effects.
        /// </summary>
        [Test]
        public async Task MixedCallBatchRetainsAllOriginalItemResults()
        {
            var scenario = new AsyncExecutionScenario { Delay = true };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            var probe = ExpandedNodeId.ToNodeId(Model.ObjectIds.Probe, wire.Owner.NamespaceUris);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<CallResponse> operation = await client.CreateCallAsync(
                wire.Owner, new CallRequest
                {
                    MethodsToCall =
                    [
                        new CallMethodRequest
                        {
                            ObjectId = probe,
                            MethodId = ExpandedNodeId.ToNodeId(
                                Model.MethodIds.Probe_DelayedAdd, wire.Owner.NamespaceUris),
                            InputArguments = [Variant.From(5)]
                        },
                        new CallMethodRequest
                        {
                            ObjectId = probe,
                            MethodId = MethodIds.Server_GetMonitoredItems
                        }
                    ]
                }, wire.Timeout).ConfigureAwait(false);
            _ = await operation.SubmitAsync(10_000, 30_000, ct: wire.Timeout).ConfigureAwait(false);
            await scenario.Entered.WaitAsync(wire.Timeout).ConfigureAwait(false);
            ServiceOperationInfoDataType waiting = await operation.CollectAsync(ct: wire.Timeout).ConfigureAwait(false);
            Assert.That(waiting.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(operation.Completion.IsCompleted, Is.False);
            scenario.Release();
            CallResponse result = (await operation.WaitForResultAsync(ct: wire.Timeout).ConfigureAwait(false))
                .GetValueOrThrow();
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Results[0].OutputArguments[0].TryGetValue(out int sum), Is.True);
            Assert.That(sum, Is.EqualTo(12));
            Assert.That(StatusCode.IsBad(result.Results[1].StatusCode), Is.True);
            Assert.That(scenario.Counter, Is.EqualTo(12));
            Assert.That(scenario.Invocations, Is.EqualTo(1));
        }

        /// <summary>
        /// Current False rejects the whole batch, while missing/True declarations and later policy changes can admit
        /// it.
        /// </summary>
        [TestCase(null)]
        [TestCase(false)]
        [TestCase(true)]
        public async Task CurrentMethodEligibilityIsCheckedBeforeAnyBatchEffect(bool? hint)
        {
            var scenario = new AsyncExecutionScenario { RecoverableHint = hint };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            var client = new AsyncServiceClient();
            CallRequest request = CreateEligibilityBatch(wire);
            if (hint == false)
            {
                PropertyState<bool> property = scenario.RecoveryProperty!;
                Assert.That(property.NodeId.NamespaceIndex, Is.GreaterThan(0));
                Assert.That(property.BrowseName.NamespaceIndex, Is.Zero);
                DataValue advertised = await wire.Owner.ReadValueAsync(property.NodeId, wire.Timeout)
                    .ConfigureAwait(false);
                Assert.That(advertised.WrappedValue.TryGetValue(out bool available), Is.True);
                Assert.That(available, Is.False);
                await using AsyncServiceOperation<CallResponse> rejected = await client.CreateCallAsync(
                    wire.Owner, request, wire.Timeout).ConfigureAwait(false);
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await rejected.SubmitAsync(10_000, 30_000, ct: wire.Timeout).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
                Assert.That(scenario.Invocations, Is.Zero);
                Assert.That(scenario.Counter, Is.EqualTo(7));
                property.Value = true;
            }
            await using AsyncServiceOperation<CallResponse> operation = await client.CreateCallAsync(
                wire.Owner, request, wire.Timeout).ConfigureAwait(false);
            CallResponse result = (await operation.ExecuteAsync(10_000, 30_000, ct: wire.Timeout)
                .ConfigureAwait(false)).GetValueOrThrow();
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(scenario.Invocations, Is.EqualTo(2));
            Assert.That(scenario.Counter, Is.EqualTo(17));
        }

        /// <summary>
        /// A denied Method retains its ordinary per-item failure rather than turning wrapping into an authorization
        /// transaction.
        /// </summary>
        [Test]
        public async Task MethodEligibilityDoesNotReplaceOrdinaryPerItemPermissionFailures()
        {
            var scenario = new AsyncExecutionScenario { RecoverableHint = false };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            scenario.DeclaredMethod!.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                }
            ];
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<CallResponse> operation = await client.CreateCallAsync(
                wire.Owner, CreateEligibilityBatch(wire), wire.Timeout).ConfigureAwait(false);
            CallResponse result = (await operation.ExecuteAsync(10_000, 30_000, ct: wire.Timeout)
                .ConfigureAwait(false)).GetValueOrThrow();
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(scenario.Invocations, Is.EqualTo(1));
            Assert.That(scenario.Counter, Is.EqualTo(12));
        }

        /// <summary>
        /// Combines an undeclared, side-effecting Method with the separately declared recovery-policy Method.
        /// </summary>
        private static CallRequest CreateEligibilityBatch(WireContext wire)
        {
            var probe = ExpandedNodeId.ToNodeId(Model.ObjectIds.Probe, wire.Owner.NamespaceUris);
            return new CallRequest
            {
                MethodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = probe,
                        MethodId = ExpandedNodeId.ToNodeId(Model.MethodIds.Probe_DelayedAdd, wire.Owner.NamespaceUris),
                        InputArguments = [Variant.From(5)]
                    },
                    new CallMethodRequest
                    {
                        ObjectId = probe,
                        MethodId = ExpandedNodeId.ToNodeId(Model.MethodIds.Probe_DeclaredAdd, wire.Owner.NamespaceUris),
                        InputArguments = [Variant.From(5)]
                    }
                ]
            };
        }

        /// <summary>
        /// A real replacement Server Session never inherits an existing handle or repeats its original invocation.
        /// </summary>
        [Test]
        public async Task ReplacementNativeSessionDoesNotAdoptAnOperation()
        {
            var scenario = new AsyncExecutionScenario { Delay = true, IgnoreCancellation = true };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            NodeId previous = wire.Owner.SessionId;
            var probe = ExpandedNodeId.ToNodeId(Model.ObjectIds.Probe, wire.Owner.NamespaceUris);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<int> operation = await client.CreateMethodAsync(
                wire.Owner,
                (session, ct) => new ProbeTypeClient(session, probe, wire.Telemetry).DelayedAddAsync(5, ct),
                wire.Timeout).ConfigureAwait(false);
            _ = await operation.SubmitAsync(10_000, 30_000, ct: wire.Timeout).ConfigureAwait(false);
            await scenario.Entered.WaitAsync(wire.Timeout).ConfigureAwait(false);
            ISession serverSession = wire.Server.CurrentInstance.SessionManager.GetSessions().Single();
            await wire.Server.CurrentInstance.SessionManager.CloseSessionAsync(serverSession.Id, wire.Timeout)
                .ConfigureAwait(false);
            await wire.Owner.ReconnectAsync(null, null, wire.Timeout).ConfigureAwait(false);
            Assert.That(wire.Owner.SessionId, Is.Not.EqualTo(previous));
            Assert.That(operation.Invalidated.IsCompleted, Is.True);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CollectAsync(ct: wire.Timeout).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(scenario.Invocations, Is.EqualTo(1));
            Assert.That(wire.Faults.Submissions, Is.EqualTo(1));
            scenario.Release();
        }

        /// <summary>
        /// Installs only synthetic public trust material through CertificateManager, then disables bootstrap
        /// auto-trust.
        /// </summary>
        private static async Task TrustAndRestrictAsync(
            WireContext wire, Certificate issuer, X509CRLCollection crls)
        {
            using CertificateEntry serverEntry = wire.Server.CertificateManager!
                .AcquireApplicationCertificateByType(ObjectTypeIds.RsaSha256ApplicationCertificateType)!;
            using CertificateEntry clientEntry = wire.ClientConfiguration.CertificateManager
                .AcquireApplicationCertificateByType(ObjectTypeIds.RsaSha256ApplicationCertificateType)!;
            using var serverPublic = Certificate.FromRawData(serverEntry.Certificate.RawData);
            using var clientPublic = Certificate.FromRawData(clientEntry.Certificate.RawData);
            using var issuerPublic = Certificate.FromRawData(issuer.RawData);
            foreach (TrustListIdentifier list in new[] { TrustListIdentifier.Peers, TrustListIdentifier.Https })
            {
                await using ITrustListTransaction clientTrust = await wire.ClientConfiguration.CertificateManager
                    .BeginUpdateAsync(list, wire.Timeout).ConfigureAwait(false);
                await clientTrust.AddTrustedCertificateAsync(serverPublic, wire.Timeout).ConfigureAwait(false);
                await clientTrust.AddTrustedCertificateAsync(issuerPublic, wire.Timeout).ConfigureAwait(false);
                await clientTrust.AddIssuerCertificateAsync(issuerPublic, wire.Timeout).ConfigureAwait(false);
                foreach (X509CRL crl in crls)
                {
                    await clientTrust.AddCrlAsync(crl, wire.Timeout).ConfigureAwait(false);
                }
                await clientTrust.CommitAsync(wire.Timeout).ConfigureAwait(false);

                await using ITrustListTransaction serverTrust = await wire.Server.CertificateManager
                    .BeginUpdateAsync(list, wire.Timeout).ConfigureAwait(false);
                await serverTrust.AddTrustedCertificateAsync(clientPublic, wire.Timeout).ConfigureAwait(false);
                await serverTrust.AddTrustedCertificateAsync(issuerPublic, wire.Timeout).ConfigureAwait(false);
                await serverTrust.AddIssuerCertificateAsync(issuerPublic, wire.Timeout).ConfigureAwait(false);
                foreach (X509CRL crl in crls)
                {
                    await serverTrust.AddCrlAsync(crl, wire.Timeout).ConfigureAwait(false);
                }
                await serverTrust.CommitAsync(wire.Timeout).ConfigureAwait(false);
            }
            wire.ClientConfiguration.SecurityConfiguration!.AutoAcceptUntrustedCertificates = false;
            await wire.ClientConfiguration.CertificateManager.UpdateAsync(
                wire.ClientConfiguration.SecurityConfiguration, wire.ClientConfiguration.ApplicationUri,
                wire.Timeout).ConfigureAwait(false);
            wire.Configuration.SecurityConfiguration!.AutoAcceptUntrustedCertificates = false;
            await wire.Server.CertificateManager.UpdateAsync(
                wire.Configuration.SecurityConfiguration, wire.Configuration.ApplicationUri,
                wire.Timeout).ConfigureAwait(false);
            await wire.RecoverAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Closes the actual transport after Server processing but before the selected reply is delivered.
        /// </summary>
        [TestCase("submit")]
        [TestCase("result")]
        [TestCase("ack")]
        public async Task LostReplyRecoversWithoutRepeatingTheMethod(string lostReply)
        {
            var scenario = new AsyncExecutionScenario { Delay = true };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            wire.Faults.Arm(lostReply);
            NodeId originalSession = wire.Owner.SessionId;
            var probe = ExpandedNodeId.ToNodeId(Model.ObjectIds.Probe, wire.Owner.NamespaceUris);
            var client = new AsyncServiceClient();
            await using AsyncServiceOperation<int> operation = await client.CreateMethodAsync(wire.Owner,
                (session, ct) => new ProbeTypeClient(session, probe, wire.Telemetry).DelayedAddAsync(35, ct),
                wire.Timeout).ConfigureAwait(false);
            if (lostReply == "submit")
            {
                _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await operation.SubmitAsync(20_000, 30_000, 0, wire.Timeout).ConfigureAwait(false));
                await wire.Faults.Dropped.WaitAsync(wire.Timeout).ConfigureAwait(false);
                Assert.That(operation.ServerToken.IsNull, Is.True);
                await wire.RecoverAsync().ConfigureAwait(false);
            }
            else
            {
                _ = await operation.SubmitAsync(20_000, 30_000, 0, wire.Timeout).ConfigureAwait(false);
            }
            await scenario.Entered.WaitAsync(wire.Timeout).ConfigureAwait(false);
            scenario.Release();
            if (lostReply == "result")
            {
                _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await operation.CollectAsync(2000, wire.Timeout).ConfigureAwait(false));
                await wire.Faults.Dropped.WaitAsync(wire.Timeout).ConfigureAwait(false);
                await wire.RecoverAsync().ConfigureAwait(false);
            }
            _ = await operation.CollectAsync(2000, wire.Timeout).ConfigureAwait(false);
            AsyncServiceResult<int> accepted = await operation.Completion.WaitAsync(wire.Timeout).ConfigureAwait(false);
            Assert.That(accepted.GetValueOrThrow(), Is.EqualTo(42));
            if (lostReply == "ack")
            {
                await wire.Faults.Dropped.WaitAsync(wire.Timeout).ConfigureAwait(false);
                Assert.That(operation.AcknowledgementFailure, Is.Not.Null);
                await wire.RecoverAsync().ConfigureAwait(false);
                ControlServiceExecutionResponse acknowledgement = await operation.AcknowledgeAsync(wire.Timeout)
                    .ConfigureAwait(false);
                Assert.That(acknowledgement.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
                acknowledgement.Reuse();
                Assert.That(accepted.GetValueOrThrow(), Is.EqualTo(42));
            }
            Assert.That(wire.Owner.SessionId, Is.EqualTo(originalSession));
            Assert.That(operation.Invalidated.IsCompleted, Is.False);
            Assert.That(scenario.Invocations, Is.EqualTo(1));
            Assert.That(scenario.Counter, Is.EqualTo(42));
            Assert.That(wire.Faults.Submissions, Is.EqualTo(1));
            Assert.That(wire.Faults.Drops, Is.EqualTo(1));
            if (lostReply == "result")
            {
                byte[][] originals = [.. wire.Faults.OriginalResponses];
                Assert.That(originals, Has.Length.EqualTo(2));
                Assert.That(originals[1], Is.EqualTo(originals[0]));
                uint[] handles = [.. wire.Faults.CollectionHandles];
                Assert.That(handles, Has.Length.EqualTo(2));
                Assert.That(handles[1], Is.Not.EqualTo(handles[0]));
            }
        }

        /// <summary>
        /// Real Read, Write and delayed generated Method calls use the declared envelopes over both mappings.
        /// </summary>
        [Test]
        public async Task ReadWriteAndDelayedCallRoundTripOverNativeTransport()
        {
            var scenario = new AsyncExecutionScenario { Delay = true };
            await using WireContext wire = await WireContext.CreateAsync(json, scenario).ConfigureAwait(false);
            var client = new AsyncServiceClient();
            var counter = ExpandedNodeId.ToNodeId(Model.VariableIds.Probe_Counter, wire.Owner.NamespaceUris);
            var probe = ExpandedNodeId.ToNodeId(Model.ObjectIds.Probe, wire.Owner.NamespaceUris);
            await using AsyncServiceOperation<ReadResponse> read = await client.CreateReadAsync(wire.Owner,
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 701 },
                    TimestampsToReturn = TimestampsToReturn.Both,
                    NodesToRead = [new ReadValueId { NodeId = counter, AttributeId = Attributes.Value }]
                }, wire.Timeout).ConfigureAwait(false);
            _ = await read.SubmitAsync(10_000, 30_000, 2000, wire.Timeout).ConfigureAwait(false);
            if (!read.Completion.IsCompleted)
            {
                _ = await read.CollectAsync(2000, wire.Timeout).ConfigureAwait(false);
            }
            ReadResponse first =
                (await read.Completion.WaitAsync(wire.Timeout).ConfigureAwait(false)).GetValueOrThrow();
            Assert.That(first.Results[0].WrappedValue.TryGetValue(out int initial), Is.True);
            Assert.That(initial, Is.EqualTo(7));
            Assert.That(first.ResponseHeader.RequestHandle, Is.EqualTo(701u));
            Assert.That(read.LastAcknowledgement!.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));

            await using AsyncServiceOperation<WriteResponse> write = await client.CreateWriteAsync(wire.Owner,
                new WriteRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 702 },
                    NodesToWrite =
                    [
                        new WriteValue
                        {
                            NodeId = counter,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(40))
                        }
                    ]
                }, wire.Timeout).ConfigureAwait(false);
            _ = await write.SubmitAsync(10_000, 30_000, 2000, wire.Timeout).ConfigureAwait(false);
            if (!write.Completion.IsCompleted)
            {
                _ = await write.CollectAsync(2000, wire.Timeout).ConfigureAwait(false);
            }
            WriteResponse written = (await write.Completion.WaitAsync(wire.Timeout).ConfigureAwait(false))
                .GetValueOrThrow();
            Assert.That(written.Results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(scenario.Counter, Is.EqualTo(40));

            await using AsyncServiceOperation<int> call = await client.CreateMethodAsync(wire.Owner,
                (session, ct) => new ProbeTypeClient(session, probe, wire.Telemetry).DelayedAddAsync(2, ct),
                wire.Timeout).ConfigureAwait(false);
            ServiceOperationInfoDataType pending = await call.SubmitAsync(10_000, 30_000, 0, wire.Timeout)
                .ConfigureAwait(false);
            Assert.That(pending.State, Is.EqualTo(ServiceOperationState.Pending));
            await scenario.Entered.WaitAsync(wire.Timeout).ConfigureAwait(false);
            Assert.That(scenario.Invocations, Is.EqualTo(1));
            scenario.Release();
            _ = await call.CollectAsync(2000, wire.Timeout).ConfigureAwait(false);
            AsyncServiceResult<int> completed = await call.Completion.WaitAsync(wire.Timeout).ConfigureAwait(false);
            Assert.That(completed.GetValueOrThrow(), Is.EqualTo(42));
            Assert.That(scenario.Counter, Is.EqualTo(42));
            Assert.That(scenario.Invocations, Is.EqualTo(1));
            Assert.That(call.LastAcknowledgement!.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(wire.Owner.Endpoint.TransportProfileUri,
                Is.EqualTo(json ? Profiles.HttpsJsonTransport : Profiles.UaTcpTransport));
            Assert.That(wire.SubmissionEncodings.ToArray(), Has.Length.EqualTo(3));
            Assert.That(wire.SubmissionEncodings.ToArray(),
                Is.All.EqualTo(json ? RequestEncoding.Json : RequestEncoding.Binary));
            Assert.That(wire.SawTransportSecurity, Is.EqualTo(json));
        }

        /// <summary>
        /// Owns synthetic PKI, free-port listeners and native managed Client connections for one test.
        /// </summary>
        private sealed class WireContext : IAsyncDisposable
        {
            private WireContext(bool useJson, AsyncExecutionScenario scenario)
            {
                m_scenario = scenario;
                Telemetry = NUnitTelemetryContext.Create();
                RootPath = Path.Combine(Path.GetTempPath(), "ua-async-" + Guid.NewGuid().ToString("N"));
                m_userName = "wire-" + Guid.NewGuid().ToString("N");
                m_password = new ByteString(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
                m_passwordId = new SecretIdentifier("management-password", m_secretStore.StoreType);
                m_secrets = new SecretRegistry(m_secretStore);
                Faults = new ResponseFaults(m_bindings);
                m_services = new ServiceCollection()
                    .AddSingleton<IPushCertificateKeyGenerator>(KeyGenerator)
                    .BuildServiceProvider();
                m_server = new ServerFixture<StandardServer>(telemetry =>
                {
                    var server = new DependencyInjectionStandardServer(m_services, telemetry, TimeProvider.System)
                    {
                        AsyncServices = new AsyncServiceExecutionOptions { Enabled = true },
                        AsyncServiceExecutionProviderFactory =
                            new DefaultAsyncServiceExecutionProviderFactory(m_observations),
                        ResponseMutator = Faults
                    };
                    server.AddNodeManager(new AsyncExecutionScenarioFactory(scenario));
                    return server;
                })
                {
                    AutoAccept = true,
                    AllNodeManagers = false,
                    SecurityNone = true,
                    UriScheme = useJson ? Utils.UriSchemeOpcHttps : Utils.UriSchemeOpcTcp,
                    HttpsMutualTls = !useJson,
                    TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
                };
                m_client = new ClientFixture(Telemetry);
            }

            public ManagedSession Owner => m_owner ?? throw new InvalidOperationException("Not connected.");
            public ITelemetryContext Telemetry { get; }
            public CancellationToken Timeout => m_timeout.Token;
            public ArrayOf<RequestEncoding> SubmissionEncodings => m_observations.Encodings.ToArray();
            public bool SawTransportSecurity => m_observations.SawTransportSecurity;
            public ResponseFaults Faults { get; }
            public StandardServer Server => m_server.Server;
            public ApplicationConfiguration Configuration => m_server.Config;
            public ApplicationConfiguration ClientConfiguration => m_client.Config;
            public string RootPath { get; }
            public CountingKeyGenerator KeyGenerator { get; } = new();

            public static async Task<WireContext> CreateAsync(
                bool useJson, AsyncExecutionScenario scenario, bool securityAdmin = false, string role = "Probe")
            {
                var context = new WireContext(useJson, scenario);
                try
                {
                    await context.m_secretStore.SetAsync(
                        context.m_passwordId, context.m_password.Memory, context.Timeout).ConfigureAwait(false);
                    await context.m_server.LoadConfigurationAsync(context.RootPath).ConfigureAwait(false);
                    context.m_server.Config.ApplicationUri = "urn:async:" + role + ":" + Guid.NewGuid().ToString("N");
                    context.m_server.Config.TransportQuotas!.MaxMessageSize = 131_072;
                    context.m_server.Config.ServerConfiguration!.UserTokenPolicies =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "synthetic-user",
                            TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
                        }
                    ];
                    StandardServer server = await context.m_server.StartAsync().ConfigureAwait(false);
                    context.Faults.MessageContext = server.MessageContext;
                    server.CurrentInstance.IdentityRegistry.Register(new UserNamePasswordAuthenticator((token, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (token.UserName != context.m_userName ||
                            !context.m_password.Span.SequenceEqual(token.DecryptedPassword))
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        return new ValueTask<IUserIdentity>(new UserIdentity(token));
                    }));
                    if (securityAdmin)
                    {
                        ServiceResult mapping = server.CurrentInstance.RoleManager!.AddIdentity(
                            ObjectIds.WellKnownRole_SecurityAdmin, new IdentityMappingRuleType
                            {
                                CriteriaType = IdentityCriteriaType.UserName,
                                Criteria = context.m_userName
                            });
                        Assert.That(ServiceResult.IsGood(mapping), Is.True);
                    }
                    await context.m_client.LoadClientConfigurationAsync(context.RootPath, "Async" + role + "Client")
                        .ConfigureAwait(false);
                    context.m_client.Config.SecurityConfiguration!.RejectSHA1SignedCertificates = true;
                    context.m_client.Config.TransportQuotas!.MaxMessageSize = 131_072;
                    string profile = useJson ? Profiles.HttpsJsonTransport : Profiles.UaTcpTransport;
                    EndpointDescription description = server.GetEndpoints().Find(endpoint =>
                        endpoint.TransportProfileUri == profile &&
                        endpoint.SecurityMode == (
                            useJson ? MessageSecurityMode.None : MessageSecurityMode.SignAndEncrypt) &&
                        endpoint.SecurityPolicyUri == (
                            useJson ? SecurityPolicies.None : SecurityPolicies.Aes256_Sha256_RsaPss))
                        ?? throw new InvalidOperationException("The selected native transport was not advertised.");
                    var configuration = EndpointConfiguration.Create(context.m_client.Config);
                    configuration.OperationTimeout = 5000;
                    configuration.UseBinaryEncoding = !useJson;
                    var endpoint = new ConfiguredEndpoint(null, CoreUtils.Clone(description)!, configuration)
                    {
                        UpdateBeforeConnect = false
                    };
                    context.m_manager = new ClientChannelManager(
                        context.m_client.Config, context.Telemetry, context.m_bindings);
                    context.m_owner = await ManagedSession.CreateAsync(
                        new ManagedSessionOptions
                        {
                            Endpoint = endpoint,
                            IdentityProvider = new UserNamePasswordIdentityProvider(
                                context.m_userName, context.m_secrets, context.m_passwordId)
                        },
                        context.m_client.Config,
                        new DefaultSessionFactory(context.Telemetry),
                        telemetry: context.Telemetry,
                        channelManager: context.m_manager,
                        updateBeforeConnect: false,
                        ct: context.Timeout).ConfigureAwait(false);
                    context.Owner.KeepAliveInterval = 60_000;
                    return context;
                }
                catch
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public async Task RecoverAsync()
            {
                NodeId sessionId = Owner.SessionId;
                string[] namespaces = Owner.NamespaceUris.ToArray();
                string[] servers = Owner.ServerUris.ToArray();
                await Owner.ReconnectAsync(null, null, Timeout).ConfigureAwait(false);
                Assert.That(Owner.SessionId, Is.EqualTo(sessionId), "Recovery must reactivate, not recreate.");
                Assert.That(Owner.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
                Assert.That(Owner.ServerUris.ToArray(), Is.EqualTo(servers));
                Assert.That(Owner.TransportChannel.MessageContext.NamespaceUris.ToArray(), Is.EqualTo(namespaces),
                    "A recovered native encoder must retain the Session namespace mapping.");
                Assert.That(Owner.TransportChannel.MessageContext.ServerUris.ToArray(), Is.EqualTo(servers),
                    "A recovered native encoder must retain the Session server mapping.");
            }

            public async ValueTask DisposeAsync()
            {
                m_scenario.Release();
                try
                {
                    if (m_owner != null)
                    {
                        await m_owner.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (m_manager != null)
                        {
                            await m_manager.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            await m_server.StopAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            await m_client.DisposeAsync().ConfigureAwait(false);
                            await m_services.DisposeAsync().ConfigureAwait(false);
                            _ = await m_secretStore.RemoveAsync(m_passwordId).ConfigureAwait(false);
                            m_timeout.Dispose();
                            if (Directory.Exists(RootPath))
                            {
                                Directory.Delete(RootPath, recursive: true);
                            }
                        }
                    }
                }
            }

            private readonly ServerFixture<StandardServer> m_server;
            private readonly ClientFixture m_client;
            private readonly AsyncExecutionScenario m_scenario;
            private readonly string m_userName;
            private readonly ByteString m_password;
            private readonly InMemorySecretStore m_secretStore = new();
            private readonly SecretRegistry m_secrets;
            private readonly SecretIdentifier m_passwordId;
            private readonly ServiceProvider m_services;
            private readonly TransportObservations m_observations = new();
            private readonly NativeChannelBindings m_bindings = new();
            private readonly CancellationTokenSource m_timeout = new(TimeSpan.FromSeconds(45));
            private ClientChannelManager? m_manager;
            private ManagedSession? m_owner;
        }

        /// <summary>
        /// Counts real entropy-mixed key generation through the public Push Management provider seam.
        /// </summary>
        private sealed class CountingKeyGenerator : IPushCertificateKeyGenerator
        {
            public int Calls => Volatile.Read(ref m_calls);

            public Certificate CreateApplicationCertificate(
                PushCertificateKeyGenerationRequest request, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_calls);
                return m_inner.CreateApplicationCertificate(request, cancellationToken);
            }

            private readonly AdditionalEntropyCertificateKeyGenerator m_inner = new();
            private int m_calls;
        }

        /// <summary>
        /// Retains the actual native transport solely to simulate a physical interruption at a response boundary.
        /// </summary>
        private sealed class NativeChannelBindings : ITransportChannelBindings
        {
            public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
            {
                ITransportChannel? channel = m_registry.Create(uriScheme, telemetry);
                Volatile.Write(ref m_current, channel);
                return channel;
            }

            public async ValueTask CloseCurrentAsync()
            {
                ITransportChannel channel = Volatile.Read(ref m_current) ??
                    throw new InvalidOperationException("No native channel is open.");
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            private readonly ITransportChannelBindings m_registry = TestTransportBindings.WithAllSchemes();
            private ITransportChannel? m_current;
        }

        /// <summary>
        /// Drops a real reply before native wire serialization, never fabricating an original Service result.
        /// </summary>
        private sealed class ResponseFaults(NativeChannelBindings channels) : IServiceResponseMutator
        {
            public IServiceMessageContext MessageContext { get; set; } = null!;
            public ConcurrentQueue<byte[]> OriginalResponses { get; } = new();
            public ConcurrentQueue<uint> CollectionHandles { get; } = new();
            public ConcurrentQueue<double> CollectionRetention { get; } = new();
            public Task Dropped => m_dropped.Task;
            public int Submissions => Volatile.Read(ref m_submissions);
            public int Drops => Volatile.Read(ref m_drops);
            public int Acknowledgements => Volatile.Read(ref m_acknowledgements);

            public void Arm(string point)
            {
                m_point = point;
                Volatile.Write(ref m_armed, 1);
            }

            public async ValueTask<IServiceResponse> MutateResponseAsync(
                IServiceRequest request, IServiceResponse response, CancellationToken cancellationToken = default)
            {
                if (request is SubmitServiceRequest)
                {
                    Interlocked.Increment(ref m_submissions);
                }
                if (request is ControlServiceExecutionRequest { Action: ServiceOperationControlAction.Acknowledge })
                {
                    Interlocked.Increment(ref m_acknowledgements);
                }
                if (request is GetServiceResultRequest &&
                    response is GetServiceResultResponse collection &&
                    collection.Result.TryGetValue(out IServiceResponse? original))
                {
                    OriginalResponses.Enqueue(BinaryEncoder.EncodeMessage(original, MessageContext));
                    CollectionHandles.Enqueue(collection.ResponseHeader.RequestHandle);
                    CollectionRetention.Enqueue(collection.Operation.ResultRemaining);
                }
                bool match = m_point switch
                {
                    "submit" => request is SubmitServiceRequest && response is SubmitServiceResponse,
                    "result" => response is GetServiceResultResponse
                    {
                        Operation.State: ServiceOperationState.OutcomeAvailable
                    },
                    "ack" => request is ControlServiceExecutionRequest
                    {
                        Action: ServiceOperationControlAction.Acknowledge
                    } &&
                        response is ControlServiceExecutionResponse,
                    _ => false
                };
                if (match && Interlocked.CompareExchange(ref m_armed, 0, 1) == 1)
                {
                    await channels.CloseCurrentAsync().ConfigureAwait(false);
                    Interlocked.Increment(ref m_drops);
                    m_dropped.TrySetResult(true);
                }
                return response;
            }

            private readonly TaskCompletionSource<bool> m_dropped =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private string? m_point;
            private int m_armed;
            private int m_submissions;
            private int m_drops;
            private int m_acknowledgements;
        }

        /// <summary>
        /// Observes the actual trusted Server transport context, not merely the Client's selected profile name.
        /// </summary>
        private sealed class TransportObservations : IAsyncServiceAuthorizationPolicy
        {
            public ConcurrentQueue<RequestEncoding> Encodings { get; } = new();
            public bool SawTransportSecurity => Volatile.Read(ref m_transportSecurity) != 0;

            public ValueTask AuthorizeAsync(
                OperationContext context, Uuid clientOperationId, NodeId serviceId,
                AsyncServiceAccess access, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (access == AsyncServiceAccess.Submit)
                {
                    Encodings.Enqueue(context.ChannelContext!.MessageEncoding);
                    if (context.ChannelContext.HasTransportSecurity)
                    {
                        Volatile.Write(ref m_transportSecurity, 1);
                    }
                }
                return default;
            }

            private int m_transportSecurity;
        }
    }
}
