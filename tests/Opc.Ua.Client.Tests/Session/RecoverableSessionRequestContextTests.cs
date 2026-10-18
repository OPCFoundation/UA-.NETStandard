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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Exercises explicitly recoverable, send-once requests through real managed session recovery.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableSessionRequestContextTests
    {
        /// <summary>
        /// A replacement peer's trusted certificate must still identify the captured Server before a new send.
        /// </summary>
        [TestCase(Profiles.UaTcpTransport, "same-identity")]
        [TestCase(Profiles.UaTcpTransport, "different-application")]
        [TestCase(Profiles.UaTcpTransport, "missing-certificate")]
        [TestCase(Profiles.HttpsJsonTransport, "different-application")]
        [TestCase(Profiles.HttpsJsonTransport, "different-host")]
        [TestCase(Profiles.HttpsJsonTransport, "missing-certificate")]
        public async Task ReplacementPeerCertificateMustIdentifyTheCapturedServer(
            string profile, string replacementKind)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            const string applicationUri = "urn:localhost:original-rse-server";
            StatusCode expectedStatus = replacementKind switch
            {
                "missing-certificate" => StatusCodes.BadCertificateInvalid,
                "different-host" => StatusCodes.BadCertificateHostNameInvalid,
                "different-application" when profile == Profiles.UaTcpTransport => StatusCodes.BadCertificateUriInvalid,
                _ => StatusCodes.Good
            };
            using Certificate original = CertificateBuilder.Create("CN=Original RSE Server")
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["server.example.test"]))
                .CreateForRSA();
            using Certificate replacement = CertificateBuilder.Create("CN=Renewed RSE Server")
                .AddExtension(new X509SubjectAltNameExtension(
                    replacementKind == "different-application" ? "urn:different:trusted-server" : applicationUri,
                    [replacementKind == "different-host" ? "different.invalid" : "server.example.test"]))
                .CreateForRSA();
            await using var channels = new SessionChannelHarness(configureChannel: channel =>
            {
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                channel.Mock.Setup(value => value.ServerChannelCertificate).Returns(original.RawData);
            });
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint(
                profile == Profiles.UaTcpTransport
                    ? "opc.tcp://server.example.test:4840"
                    : "https://server.example.test:4840");
            endpoint.Description.TransportProfileUri = profile;
            endpoint.Description.Server.ApplicationUri = applicationUri;
            await using Client.ManagedSession owner = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = endpoint },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false,
                ct: timeout.Token).ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            ScriptedChannel transport = channels.CreatedChannels[0];
            int reads = 0;
            transport.RequestHandler = (request, _) =>
            {
                if (request is ReadRequest { NodesToRead.Count: 1 } read &&
                    read.NodesToRead[0].NodeId == new NodeId(100u))
                {
                    Interlocked.Increment(ref reads);
                }
                return new ValueTask<IServiceResponse>(transport.CreateResponse(request));
            };
            transport.Mock.Setup(value => value.ServerChannelCertificate)
                .Returns(replacementKind == "missing-certificate" ? [] : replacement.RawData);
            await channels.Manager.ReconnectAsync((IManagedTransportChannel)owner.TransportChannel, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(transport.Channel.EndpointDescription.Server.ApplicationUri, Is.EqualTo(applicationUri));
            if (expectedStatus == StatusCodes.Good)
            {
                _ = await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false);
                Assert.That(reads, Is.EqualTo(1));
                Assert.That(context.Invalidated.IsCompleted, Is.False);
            }
            else
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(expectedStatus));
                Assert.That(reads, Is.Zero);
                Assert.That(context.Invalidated.IsCompleted, Is.True);
            }
        }

        /// <summary>
        /// Fresh exchanges can follow verified reactivation without weakening a strict context.
        /// </summary>
        [Test]
        public async Task FreshRequestFollowsVerifiedSameSessionReactivation()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var channels = new SessionChannelHarness(
                configureChannel: channel =>
                    channel.Channel.MessageContext.ServerUris.Update(["urn:localhost:server"]));
            await using Client.ManagedSession owner = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = SessionChannelHarness.CreateEndpoint() },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false,
                ct: timeout.Token).ConfigureAwait(false);
            await using SessionRequestContext strict =
                await SessionRequestContext.CaptureAsync(owner, timeout.Token).ConfigureAwait(false);
            await using SessionRequestContext recoverable =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            NodeId originalSessionId = owner.SessionId;

            ReadResponse before = await ReadAsync(recoverable.Session, timeout.Token).ConfigureAwait(false);
            await channels.Manager.ReconnectAsync((IManagedTransportChannel)owner.TransportChannel, timeout.Token)
                .ConfigureAwait(false);
            ReadResponse after = await ReadAsync(recoverable.Session, timeout.Token).ConfigureAwait(false);

            Assert.That(before.Results, Has.Count.EqualTo(1));
            Assert.That(after.Results, Has.Count.EqualTo(1));
            Assert.That(owner.SessionId, Is.EqualTo(originalSessionId));
            Assert.That(recoverable.SessionId, Is.EqualTo(originalSessionId));
            Assert.That(recoverable.Invalidated.IsCompleted, Is.False);
            Assert.That(strict.Invalidated.IsCompleted, Is.True);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(strict.Session, timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        /// <summary>
        /// A fresh exchange waits for validated activation, without sending early or blocking the recovery worker.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FreshRequestWaitsForReactivationWithoutReplaying(bool cancelWait)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int activations = 0;
            int reads = 0;
            await using var channels = new SessionChannelHarness(configureChannel: channel =>
            {
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                channel.RequestHandler = async (request, ct) =>
                {
                    if (request is ActivateSessionRequest && Interlocked.Increment(ref activations) > 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    if (request is ReadRequest { NodesToRead.Count: 1 } read &&
                        read.NodesToRead[0].NodeId == new NodeId(100u))
                    {
                        Interlocked.Increment(ref reads);
                    }
                    return channel.CreateResponse(request);
                };
            });
            await using Client.ManagedSession owner = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = SessionChannelHarness.CreateEndpoint() },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false,
                ct: timeout.Token).ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            Task recovery = channels.Manager.ReconnectAsync(
                (IManagedTransportChannel)owner.TransportChannel, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Task<ReadResponse> request = ReadAsync(context.Session, waitCancellation.Token).AsTask();
                Assert.That(request.IsCompleted, Is.False);
                Assert.That(reads, Is.Zero);
                if (cancelWait)
                {
                    await waitCancellation.CancelAsync().ConfigureAwait(false);
                    _ = Assert.ThrowsAsync<TaskCanceledException>(async () => await request.ConfigureAwait(false));
                    Assert.That(reads, Is.Zero);
                }
                release.TrySetResult(true);
                await recovery.ConfigureAwait(false);
                ReadResponse response = cancelWait
                    ? await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false)
                    : await request.ConfigureAwait(false);
                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(reads, Is.EqualTo(1));
                Assert.That(context.Invalidated.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await recovery.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A lost mutation response escapes unchanged; recovery permits a different exchange, never a replay.
        /// </summary>
        [Test]
        public async Task LostMutationResponseIsNeverReplayed()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int invocations = 0;
            await using var channels = new SessionChannelHarness(configureChannel: channel =>
            {
                channel.Channel.MessageContext.ServerUris.Update(["urn:localhost:server"]);
                channel.RequestHandler = (request, _) =>
                {
                    if (request is CallRequest)
                    {
                        Interlocked.Increment(ref invocations);
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                    return new ValueTask<IServiceResponse>(channel.CreateResponse(request));
                };
            });
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await context.Session.CallAsync(null,
                    [new CallMethodRequest { ObjectId = new NodeId(1u), MethodId = new NodeId(2u) }],
                    timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            Assert.That(invocations, Is.EqualTo(1));
            await channels.Manager.ReconnectAsync((IManagedTransportChannel)owner.TransportChannel, timeout.Token)
                .ConfigureAwait(false);
            _ = await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false);
            Assert.That(invocations, Is.EqualTo(1));
            Assert.That(context.Invalidated.IsCompleted, Is.False);
        }

        /// <summary>
        /// A new Session invalidates the binding even if a Server happens to reuse its public identifiers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacementSessionIsNeverAdopted(bool reuseIdentifiers)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int activations = 0;
            await using var channels = new SessionChannelHarness(configureChannel: channel =>
            {
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                channel.RequestHandler = (request, _) =>
                {
                    if (request is ActivateSessionRequest && Interlocked.Increment(ref activations) == 2)
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                    }
                    IServiceResponse response = channel.CreateResponse(request);
                    if (reuseIdentifiers && response is CreateSessionResponse created)
                    {
                        created.SessionId = new NodeId("session-1", 1);
                        created.AuthenticationToken = new NodeId("token-1", 1);
                    }
                    return new ValueTask<IServiceResponse>(response);
                };
            });
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            NodeId originalSession = owner.SessionId;
            await channels.Manager.ReconnectAsync((IManagedTransportChannel)owner.TransportChannel, timeout.Token)
                .ConfigureAwait(false);

            Assert.That(channels.CreatedChannels[0].CreateSessionCount, Is.EqualTo(2));
            Assert.That(owner.SessionId == originalSession, Is.EqualTo(reuseIdentifiers));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        /// <summary>
        /// Identity-changing activation invalidates recovery even when the activation is rejected.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task IdentityChangeInvalidatesRecovery(bool activationFails)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var channels = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            endpoint.Description.UserIdentityTokens =
            [
                endpoint.Description.UserIdentityTokens[0],
                new UserTokenPolicy
                {
                    PolicyId = "username",
                    TokenType = UserTokenType.UserName,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            ];
            await using Client.ManagedSession owner = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = endpoint },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false,
                ct: timeout.Token).ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            ScriptedChannel transport = channels.CreatedChannels[0];
            transport.RequestHandler = (request, _) =>
            {
                if (activationFails && request is ActivateSessionRequest)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                return new ValueTask<IServiceResponse>(transport.CreateResponse(request));
            };
            var changedIdentity = new UserIdentity("synthetic-user", []);
            if (activationFails)
            {
                _ = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await owner.UpdateSessionAsync(changedIdentity, [], timeout.Token).ConfigureAwait(false));
            }
            else
            {
                await owner.UpdateSessionAsync(changedIdentity, [], timeout.Token).ConfigureAwait(false);
            }
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        /// <summary>
        /// Captured mappings cannot be changed through the owner or the borrowed local Session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NamespaceChangesAreRejectedBeforeSending(bool changeBorrowedSession)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            (changeBorrowedSession ? context.Session : owner).NamespaceUris.Append("urn:changed:mapping");
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
        }

        /// <summary>
        /// Changing encoding limits cannot silently change a retained operation's encoding contract.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [TestCase("string")]
        [TestCase("bytes")]
        [TestCase("array")]
        [TestCase("nesting")]
        [TestCase("recoveries")]
        public async Task ChangedEncodingLimitsAreRejectedBeforeSending(string limit)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            var source = (ServiceMessageContext)channels.CreatedChannels[0].Channel.MessageContext;
            switch (limit)
            {
                case "string":
                    source.MaxStringLength++;
                    break;
                case "bytes":
                    source.MaxByteStringLength++;
                    break;
                case "array":
                    source.MaxArrayLength++;
                    break;
                case "nesting":
                    source.MaxEncodingNestingLevels++;
                    break;
                case "recoveries":
                    source.MaxDecoderRecoveries++;
                    break;
                default:
                    throw new ArgumentException("Unknown encoding limit.", nameof(limit));
            }
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
        }

        /// <summary>
        /// Message capacity alone can shrink and recover, but cannot raise a captured finite safety ceiling.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MessageCapacityDoesNotInvalidateRecoveryOrRaiseCapturedCeiling(bool capturedUnlimited)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            var source = (ServiceMessageContext)channels.CreatedChannels[0].Channel.MessageContext;
            source.MaxMessageSize = capturedUnlimited ? 0 : 65_536;
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            source.MaxMessageSize = 32_768;
            _ = await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false);
            Assert.That(context.Invalidated.IsCompleted, Is.False);
            source.MaxMessageSize = 0;
            if (capturedUnlimited)
            {
                _ = await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            Assert.That(context.Invalidated.IsCompleted, Is.False);
            source.MaxMessageSize = 65_536;
            _ = await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false);
            Assert.That(context.Invalidated.IsCompleted, Is.False);
        }

        /// <summary>
        /// Replacing the encodeable factory remains an interpretation change, not a delivery-capacity adjustment.
        /// </summary>
        [Test]
        public async Task ChangedEncodingFactoryInvalidatesRecovery()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            ScriptedChannel channel = channels.CreatedChannels[0];
            IServiceMessageContext previous = channel.Channel.MessageContext;
            var replacement = new ServiceMessageContext(
                previous.Telemetry, ServiceMessageContext.CreateEmpty(previous.Telemetry).Factory)
            {
                NamespaceUris = previous.NamespaceUris,
                ServerUris = previous.ServerUris,
                MaxMessageSize = previous.MaxMessageSize,
                MaxStringLength = previous.MaxStringLength,
                MaxByteStringLength = previous.MaxByteStringLength,
                MaxArrayLength = previous.MaxArrayLength,
                MaxEncodingNestingLevels = previous.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = previous.MaxDecoderRecoveries
            };
            channel.Mock.Setup(value => value.MessageContext).Returns(replacement);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
        }

        /// <summary>
        /// A completed response remains a known result even if the caller cancels at the physical send boundary.
        /// </summary>
        [Test]
        public async Task CompletedMutationSurvivesLateCancellation()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            ScriptedChannel transport = channels.CreatedChannels[0];
            var expected = new CallMethodResult { StatusCode = StatusCodes.GoodCompletesAsynchronously };
            transport.RequestHandler = async (request, _) =>
            {
                if (request is not CallRequest)
                {
                    return transport.CreateResponse(request);
                }
                await cancellation.CancelAsync().ConfigureAwait(false);
                return new CallResponse { ResponseHeader = transport.CreateGoodHeader(), Results = [expected] };
            };
            CallResponse response = await context.Session.CallAsync(null,
                [new CallMethodRequest { ObjectId = new NodeId(1u), MethodId = new NodeId(2u) }],
                cancellation.Token).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.SameAs(expected));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
        }

        /// <summary>
        /// A borrowed Session never adopts changed security or a different transport encoding.
        /// </summary>
        [TestCase("policy")]
        [TestCase("mode")]
        [TestCase("mapping")]
        [TestCase("application")]
        public async Task SecurityOrMappingChangesInvalidateRecovery(string changedField)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await CreateOwnerAsync(channels, timeout.Token)
                .ConfigureAwait(false);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureRecoverableAsync(owner, timeout.Token).ConfigureAwait(false);
            EndpointDescription endpoint = channels.CreatedChannels[0].Channel.EndpointDescription;
            switch (changedField)
            {
                case "policy":
                    endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                    break;
                case "mode":
                    endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    break;
                case "mapping":
                    endpoint.TransportProfileUri = "urn:synthetic:different-encoding";
                    break;
                case "application":
                    endpoint.Server.ApplicationUri = "urn:synthetic:different-server";
                    break;
                default:
                    Assert.Fail("Unrecognized test case.");
                    break;
            }
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadAsync(context.Session, timeout.Token).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
        }

        /// <summary>
        /// Opens a real managed Session with network I/O supplied by the scripted transport boundary.
        /// </summary>
        private static Task<Client.ManagedSession> CreateOwnerAsync(
            SessionChannelHarness channels, CancellationToken ct)
        {
            return Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = SessionChannelHarness.CreateEndpoint() },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false,
                ct: ct);
        }

        /// <summary>
        /// Sends an ordinary generated request through the borrowed session.
        /// </summary>
        private static ValueTask<ReadResponse> ReadAsync(ISession session, CancellationToken ct)
        {
            return session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }], ct);
        }
    }
}
