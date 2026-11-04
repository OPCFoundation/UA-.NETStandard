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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises existing push certificate operations on a borrowed, generation-bound encrypted session.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class BorrowedPushConfigurationClientTests
    {
        /// <summary>
        /// CSR, certificate update and apply use the original token without elevation or connection ownership.
        /// </summary>
        [TestCase("raw")]
        [TestCase("managed")]
        [TestCase("borrowed")]
        public async Task CertificateOperationsKeepOriginalIdentityAndDoNotCloseCallerConnection(string shape)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
            await using SessionRequestContext outer = await SessionRequestContext
                .CaptureConfidentialAsync(fixture.ManagedSession).ConfigureAwait(false);
            ISession session = shape switch
            {
                "raw" => fixture.InnerSession,
                "borrowed" => outer.Session,
                _ => fixture.ManagedSession
            };
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), session).ConfigureAwait(false);
            client.AdminCredentials = new UserIdentity();
            ByteString nonce = ByteString.From(new byte[] { 4, 5, 6 });
            ByteString certificate = ByteString.From(new byte[] { 7, 8, 9 });
            NodeId group = Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            NodeId type = Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;

            ByteString csr = await client.CreateSigningRequestAsync(
                group, type, "CN=Device", false, nonce).ConfigureAwait(false);
            bool apply = await client.UpdateCertificateAsync(
                group, type, certificate, string.Empty, default, []).ConfigureAwait(false);
            await client.ApplyChangesAsync().ConfigureAwait(false);

            Assert.That(csr, Is.EqualTo(s_csr));
            Assert.That(apply, Is.True);
            List<CallMethodRequest> calls = Calls(fixture);
            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(calls[0].MethodId,
                Is.EqualTo(Ua.MethodIds.ServerConfigurationType_CreateSigningRequest));
            Assert.That(calls[0].InputArguments, Is.EqualTo(new Variant[]
            {
                Variant.From(group), Variant.From(type), Variant.From("CN=Device"),
                Variant.From(false), Variant.From(nonce)
            }));
            Assert.That(calls[1].MethodId,
                Is.EqualTo(Ua.MethodIds.ServerConfigurationType_UpdateCertificate));
            Assert.That(calls[1].InputArguments[2].TryGetValue(out ByteString sent), Is.True);
            Assert.That(sent, Is.EqualTo(certificate));
            Assert.That(calls[2].MethodId, Is.EqualTo(Ua.MethodIds.ServerConfigurationType_ApplyChanges));
            await client.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
            Assert.That(outer.Invalidated.IsCompleted, Is.False);
            Assert.That(fixture.Requests.Exists(request => request is CloseSessionRequest), Is.False);
        }

        /// <summary>
        /// A successful download does not hide a failure to release its remote file handle.
        /// </summary>
        [Test]
        public async Task BorrowedTrustListReadReportsCloseFailureAfterSuccessfulTransfer()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, fixture.MessageContext, true))
            {
                encoder.WriteEncodeable(null, new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All });
            }
            ByteString payload = ByteString.From(stream.ToArray().AsSpan());
            var closeError = new ServiceResultException(StatusCodes.BadTimeout);
            int reads = 0;
            fixture.Handler = (request, _) =>
            {
                if (request is BrowseRequest)
                {
                    return new ValueTask<IServiceResponse>(Respond(request));
                }
                NodeId method = ((CallRequest)request).MethodsToCall[0].MethodId;
                if (method == Ua.MethodIds.FileType_Close)
                {
                    throw closeError;
                }
                ArrayOf<Variant> outputs = method == Ua.MethodIds.TrustListType_OpenWithMasks
                    ? [Variant.From(42u)]
                    : [Variant.From(reads++ == 0 ? payload : ByteString.Empty)];
                return new ValueTask<IServiceResponse>(
                    new CallResponse { Results = [new CallMethodResult { OutputArguments = outputs }] });
            };
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadTrustListAsync().ConfigureAwait(false));

            Assert.That(error, Is.SameAs(closeError));
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(Calls(fixture), Has.Count.EqualTo(4));
        }

        /// <summary>
        /// Reconnection, identity replacement and endpoint retargeting are unavailable on a borrowed client.
        /// </summary>
        [Test]
        public async Task BorrowedClientRejectsConnectionManagementWithoutDispatch()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false);

            ServiceResultException? connect = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync("opc.tcp://replacement:4840").ConfigureAwait(false));
            ServiceResultException? reconnect = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync().ConfigureAwait(false));
            ServiceResultException? disconnect = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.DisconnectAsync().ConfigureAwait(false));
            ServiceResultException? endpoint = Assert.Throws<ServiceResultException>(() =>
                client.Endpoint = fixture.InnerSession.ConfiguredEndpoint);

            Assert.That(connect!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(reconnect!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(disconnect!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(endpoint!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
        }

        /// <summary>
        /// A remote ConfigurationFile update is rejected before the borrowed session receives any request.
        /// </summary>
        [Test]
        public async Task BorrowedClientRejectsConfigurationFileUpdateWithoutDispatch()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false);

            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.UpdateConfigurationFileAsync(
                    ByteString.From([1]),
                    [new ConfigurationUpdateTargetType
                    {
                        Path = "ApplicationIdentity",
                        UpdateType = ConfigurationUpdateType.InsertOrReplace
                    }],
                    revertAfterTime: 1000,
                    restartDelayTime: 0).ConfigureAwait(false));

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
        }

        /// <summary>
        /// The existing TrustList writer keeps the admitted token and uploads the requested complete trust list.
        /// </summary>
        [Test]
        public async Task TrustListUpdateUsesExistingFileTransferOnCapturedSession()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Handler = (request, _) =>
            {
                if (request is BrowseRequest)
                {
                    return new ValueTask<IServiceResponse>(Respond(request));
                }
                var call = (CallRequest)request;
                NodeId method = call.MethodsToCall[0].MethodId;
                ArrayOf<Variant> outputs = method == Ua.MethodIds.FileType_Open ? [Variant.From(42u)]
                    : method == Ua.MethodIds.TrustListType_CloseAndUpdate ? [Variant.From(true)] : [];
                return new ValueTask<IServiceResponse>(
                    new CallResponse { Results = [new CallMethodResult { OutputArguments = outputs }] });
            };
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false);
            ByteString trusted = ByteString.From(new byte[] { 8, 9 });
            var trust = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = [trusted]
            };

            bool apply = await client.UpdateTrustListAsync(trust).ConfigureAwait(false);

            Assert.That(apply, Is.True);
            List<CallMethodRequest> calls = Calls(fixture);
            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(calls[0].MethodId, Is.EqualTo(Ua.MethodIds.FileType_Open));
            Assert.That(calls[1].MethodId, Is.EqualTo(Ua.MethodIds.FileType_Write));
            Assert.That(calls[2].MethodId, Is.EqualTo(Ua.MethodIds.TrustListType_CloseAndUpdate));
            foreach (CallMethodRequest call in calls)
            {
                Assert.That(
                    call.ObjectId,
                    Is.EqualTo(Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList));
            }
            Assert.That(calls[1].InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(42u));
            Assert.That(calls[1].InputArguments[1].TryGetValue(out ByteString payload), Is.True);
            using var stream = new MemoryStream(payload.ToArray()!);
            using var decoder = new BinaryDecoder(stream, fixture.MessageContext);
            var decoded = new TrustListDataType();
            decoded.Decode(decoder);
            Assert.That(decoded.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
            Assert.That(decoded.TrustedCertificates, Is.EqualTo(new[] { trusted }.ToArrayOf()));
            Assert.That(decoded.IssuerCertificates.IsEmpty, Is.True);
        }

        /// <summary>
        /// Failed transfers close the same handle independently of cancellation and retain both failures.
        /// </summary>
        [TestCase("read", false, false)]
        [TestCase("read", false, true)]
        [TestCase("read", true, false)]
        [TestCase("read", true, true)]
        [TestCase("write", false, false)]
        [TestCase("write", false, true)]
        [TestCase("write", true, false)]
        [TestCase("write", true, true)]
        [TestCase("commit", false, false)]
        [TestCase("commit", false, true)]
        [TestCase("commit", true, false)]
        [TestCase("commit", true, true)]
        public async Task BorrowedTrustListFailureClosesHandleAndPreservesErrors(
            string operation, bool cancelTransfer, bool failClose)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            using var cancellation = new CancellationTokenSource();
            Exception transferError = cancelTransfer
                ? new OperationCanceledException(cancellation.Token)
                : new ServiceResultException(StatusCodes.BadCommunicationError);
            var closeError = new ServiceResultException(StatusCodes.BadTimeout);
            bool cleanupTokenUsable = false;
            NodeId failedMethod = operation == "read" ? Ua.MethodIds.FileType_Read
                : operation == "commit" ? Ua.MethodIds.TrustListType_CloseAndUpdate : Ua.MethodIds.FileType_Write;
            fixture.Handler = (request, token) =>
            {
                if (request is BrowseRequest)
                {
                    return new ValueTask<IServiceResponse>(Respond(request));
                }
                NodeId method = ((CallRequest)request).MethodsToCall[0].MethodId;
                if (method == failedMethod)
                {
                    if (cancelTransfer)
                    {
                        cancellation.Cancel();
                    }
                    throw transferError;
                }
                if (method == Ua.MethodIds.FileType_Close)
                {
                    cleanupTokenUsable = token.CanBeCanceled && !token.IsCancellationRequested;
                    if (failClose)
                    {
                        throw closeError;
                    }
                }
                ArrayOf<Variant> outputs = method == Ua.MethodIds.FileType_Open ||
                    method == Ua.MethodIds.TrustListType_OpenWithMasks ? [Variant.From(42u)] : [];
                return new ValueTask<IServiceResponse>(
                    new CallResponse { Results = [new CallMethodResult { OutputArguments = outputs }] });
            };
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false);
            Func<Task> transfer = async () =>
            {
                if (operation == "read")
                {
                    await client.ReadTrustListAsync(ct: cancellation.Token).ConfigureAwait(false);
                }
                else
                {
                    await client.UpdateTrustListAsync(
                        new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All }, cancellation.Token)
                        .ConfigureAwait(false);
                }
            };

            if (failClose)
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await transfer().ConfigureAwait(false));
                Assert.That(error!.InnerExceptions, Is.EqualTo(new[] { transferError, closeError }));
            }
            else if (cancelTransfer)
            {
                OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await transfer().ConfigureAwait(false));
                Assert.That(error, Is.SameAs(transferError));
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await transfer().ConfigureAwait(false));
                Assert.That(error, Is.SameAs(transferError));
            }
            Assert.That(cleanupTokenUsable, Is.True);
            List<CallMethodRequest> calls = Calls(fixture);
            Assert.That(calls, Has.Count.EqualTo(operation == "commit" ? 4 : 3));
            CallMethodRequest close = calls[calls.Count - 1];
            Assert.That(close.MethodId, Is.EqualTo(Ua.MethodIds.FileType_Close));
            Assert.That(close.InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(42u));
            Assert.That(fixture.Requests.Exists(request => request is CloseSessionRequest), Is.False);
        }

        /// <summary>
        /// Follow-up operations cannot transfer an earlier admission to another physical or logical session.
        /// </summary>
        [TestCase("recreate")]
        [TestCase("transport")]
        [TestCase("namespace")]
        [TestCase("downgrade")]
        public async Task BorrowedClientNeverDispatchesAfterContextReplacement(string mutation)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false);
            var replacement = new Mock<ITransportChannel>();
            switch (mutation)
            {
                case "recreate":
                    fixture.Recreate();
                    break;
                case "transport":
                    fixture.ReplaceTransport(replacement.Object);
                    break;
                case "namespace":
                    fixture.InnerSession.NamespaceUris.Append("urn:replacement");
                    break;
                case "downgrade":
                    fixture.Channel.Object.EndpointDescription.SecurityMode = MessageSecurityMode.Sign;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ApplyChangesAsync().ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(mutation == "downgrade"
                ? StatusCodes.BadSecurityModeInsufficient : StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
            replacement.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An unencrypted physical channel is rejected before resolving certificate methods.
        /// </summary>
        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task BorrowedClientRejectsUnencryptedSessionBeforeAnyDispatch(MessageSecurityMode mode)
        {
            await using var fixture = new SessionRequestContextTestContext(securityMode: mode);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ServerPushConfigurationClient.CaptureAsync(
                    new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Cancellation and local disposal never cause reconnection or close the caller's connection.
        /// </summary>
        [Test]
        public async Task BorrowedClientHonorsCancellationAndRejectsOperationsAfterDisposal()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            await using ServerPushConfigurationClient client = await ServerPushConfigurationClient.CaptureAsync(
                new ApplicationConfiguration(fixture.Telemetry), fixture.ManagedSession).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await client.ApplyChangesAsync(cancellation.Token).ConfigureAwait(false));
            await client.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await client.ApplyChangesAsync().ConfigureAwait(false));

            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
        }

        /// <summary>
        /// AddGdsClient exposes a capture factory without replacing its existing connection-owning client.
        /// </summary>
        [Test]
        public async Task DependencyInjectedCaptureFactoryPreservesExistingOwnedClientRegistration()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
            var services = new ServiceCollection();
            services.AddSingleton(new ApplicationConfiguration(fixture.Telemetry));
            services.AddOpcUa().AddGdsClient();
            await using ServiceProvider provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<
                Func<ISession, CancellationToken, ValueTask<ServerPushConfigurationClient>>>();
            await using ServerPushConfigurationClient borrowed =
                await factory(fixture.ManagedSession, CancellationToken.None).ConfigureAwait(false);
            ServerPushConfigurationClient owned = provider.GetRequiredService<ServerPushConfigurationClient>();

            Assert.That(owned, Is.Not.SameAs(borrowed));
            Assert.That(provider.GetRequiredService<IServerPushConfigurationClient>(), Is.SameAs(owned));
            await borrowed.ApplyChangesAsync().ConfigureAwait(false);
            Assert.That(Calls(fixture), Has.Count.EqualTo(1));
            Assert.That(owned.Session, Is.Null);
        }

        /// <summary>
        /// Collects calls and checks the authentication token used by every actual wire request.
        /// </summary>
        private static List<CallMethodRequest> Calls(SessionRequestContextTestContext fixture)
        {
            var calls = new List<CallMethodRequest>();
            foreach (IServiceRequest request in fixture.Requests)
            {
                Assert.That(request.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("original-token", 0)));
                if (request is CallRequest call)
                {
                    calls.Add(call.MethodsToCall[0]);
                }
                else
                {
                    Assert.That(request, Is.TypeOf<BrowseRequest>());
                }
            }
            return calls;
        }

        /// <summary>
        /// Answers generated resolution and standard certificate method calls without a network or server.
        /// </summary>
        private static IServiceResponse Respond(IServiceRequest request)
        {
            if (request is BrowseRequest)
            {
                return new BrowseResponse { Results = [new BrowseResult { References = [] }] };
            }
            if (request is CallRequest call)
            {
                NodeId method = call.MethodsToCall[0].MethodId;
                ArrayOf<Variant> outputs = method == Ua.MethodIds.ServerConfigurationType_CreateSigningRequest
                    ? [Variant.From(s_csr)]
                    : method == Ua.MethodIds.ServerConfigurationType_UpdateCertificate ? [Variant.From(true)] : [];
                return new CallResponse { Results = [new CallMethodResult { OutputArguments = outputs }] };
            }
            throw new InvalidOperationException("Unexpected borrowed push request.");
        }

        /// <summary>The CSR returned by the scripted device.</summary>
        private static readonly ByteString s_csr = ByteString.From(new byte[] { 1, 2, 3 });
    }
}
