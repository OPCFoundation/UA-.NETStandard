// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Proves that the borrowed client's actual service dispatch cannot adopt a replacement session.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SessionRequestContextTests
    {
        /// <summary>
        /// Identity changes notify old contexts before another capture can subscribe for the new generation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SessionIdentityNotificationExcludesNewCapturesAsync(bool reusedIdentity)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var identity = (Lock)typeof(Session)
                .GetField("m_requestContextIdentity", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(fixture.InnerSession)!;
            bool notified = false;
            bool captureExcluded = false;
            void ObserveChange()
            {
                notified = true;
                captureExcluded = identity.IsHeldByCurrentThread;
            }
            fixture.InnerSession.RequestContextChanged += ObserveChange;
            try
            {
                await using SessionRequestContext original =
                    await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
                if (reusedIdentity)
                {
                    fixture.InnerSession.SessionCreated(
                        NodeId.Parse("s=original-session"), NodeId.Parse("s=original-token"));
                }
                else
                {
                    fixture.Recreate();
                }

                Assert.That(notified, Is.True);
                Assert.That(captureExcluded, Is.True,
                    "A new capture must not enter between the identity change and its invalidation notification.");
                Assert.That(original.Invalidated.IsCompleted, Is.True);
                await using SessionRequestContext current =
                    await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
                Assert.That(current.Invalidated.IsCompleted, Is.False);
                Assert.That(current.SessionId, Is.EqualTo(fixture.InnerSession.SessionId));
                fixture.Handler = (_, _) => new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [new DataValue(Variant.From(19))]
                });

                ReadResponse response = await current.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value }],
                    CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(response.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(19));
                Assert.That(fixture.Requests, Has.Count.EqualTo(1));
                Assert.That(fixture.Requests[0].RequestHeader.AuthenticationToken, Is.EqualTo(NodeId.Parse(
                    reusedIdentity ? "s=original-token" : "s=replacement-token")));
                Assert.That(current.Invalidated.IsCompleted, Is.False);
            }
            finally
            {
                fixture.InnerSession.RequestContextChanged -= ObserveChange;
            }
        }

        /// <summary>
        /// Queues a request behind recovery after the caller observed the original identity.
        /// </summary>
        [Test]
        public async Task DispatchRejectsGenerationChangedWhileWaitingForGate()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ISession borrowed = context.Session;
            Session original = fixture.InnerSession;
            Assert.That(borrowed.SessionId, Is.EqualTo(original.SessionId));

            Task<HistoryReadResponse> request;
            using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
            {
                request = ReadHistoryAsync(borrowed).AsTask();
                Assert.That(request.IsCompleted, Is.False);
                fixture.Recreate();
            }

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await request.ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.InnerSession, Is.SameAs(original));
            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            Assert.That(context.SessionId, Is.EqualTo(NodeId.Parse("s=original-session")));
        }

        /// <summary>
        /// A reader whose drain is bypassed cannot cause the next call to use the new generation.
        /// </summary>
        [Test]
        public async Task RecoveryDrainBypassDoesNotRetargetLaterDispatch()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var reply = new TaskCompletionSource<IServiceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Handler = (_, _) => new ValueTask<IServiceResponse>(reply.Task);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);

            Task<HistoryReadResponse> first = ReadHistoryAsync(context.Session).AsTask();
            Task<AsyncReaderWriterLock.Releaser> recovery = fixture.ServiceGate.WriterLockAsync(
                TimeSpan.FromSeconds(1), fixture.TimeProvider, static () => true, CancellationToken.None).AsTask();
            try
            {
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(recovery.IsCompleted, Is.False);
                fixture.TimeProvider.Advance(TimeSpan.FromSeconds(1));
                using (await recovery.ConfigureAwait(false))
                {
                    fixture.Recreate();
                }
            }
            finally
            {
                reply.TrySetResult(new HistoryReadResponse { Results = [] });
            }
            _ = await first.ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            Assert.That(fixture.Requests[0].RequestHeader.AuthenticationToken,
                Is.EqualTo(NodeId.Parse("s=original-token")));
        }

        /// <summary>
        /// Namespace metadata stays captured and remapping is rejected before sending the request.
        /// </summary>
        [Test]
        public async Task BorrowedSessionKeepsOriginalNamespaceMapping()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            Assert.That(context.Session.NamespaceUris.GetString(1), Is.EqualTo("urn:request-context:original"));

            Task<HistoryReadResponse> request;
            using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
            {
                request = ReadHistoryAsync(context.Session).AsTask();
                fixture.InnerSession.NamespaceUris.Update(
                    ["http://opcfoundation.org/UA/", "urn:request-context:replacement"]);
            }

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await request.ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Session.NamespaceUris.GetString(1), Is.EqualTo("urn:request-context:original"));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// The captured endpoint uses physical metadata and does not invoke provider clone overrides.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedTransportEndpointKeepsPhysicalIdentity(bool confidential)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            byte[] serverCertificate = [1, 2, 3];
            string[] discoveryUrls = ["opc.tcp://physical:4840"];
            var translations = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["en"] = "Physical {0}",
                ["de"] = "Physisch {0}"
            };
            object[] arguments = ["server"];
            var application = new ProviderApplicationDescription
            {
                ApplicationUri = "urn:physical:server",
                ProductUri = "urn:physical:product",
                ApplicationName = new LocalizedText(translations,
                    new TranslationInfo("physical-name", "en", "Physical {0}", arguments)),
                DiscoveryUrls = discoveryUrls
            };
            var policy = new ProviderUserTokenPolicy
            {
                PolicyId = "physical-token",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var endpoint = new ProviderEndpointDescription
            {
                EndpointUrl = "opc.tcp://physical:4840",
                Server = application,
                ServerCertificate = ByteString.From(serverCertificate),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                UserIdentityTokens = new UserTokenPolicy[] { policy },
                TransportProfileUri = Profiles.UaTcpTransport,
                SecurityLevel = 5
            };
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(endpoint);

            await using SessionRequestContext context = confidential
                ? await SessionRequestContext.CaptureConfidentialAsync(fixture.ManagedSession)
                    .ConfigureAwait(false)
                : await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            EndpointDescription captured = context.Session.TransportChannel.EndpointDescription;
            endpoint.EndpointUrl = "opc.tcp://changed:4840";
            application.ApplicationUri = "urn:changed:server";
            discoveryUrls[0] = "opc.tcp://changed:4840";
            policy.PolicyId = "changed-token";
            serverCertificate[0] = 9;
            translations["en"] = "Changed {0}";
            translations["de"] = "Changed {0}";
            arguments[0] = "changed";

            Assert.That(fixture.ManagedSession.ConfiguredEndpoint.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(captured, Is.Not.SameAs(endpoint));
            Assert.That(captured.Server, Is.Not.SameAs(application));
            Assert.That(captured.UserIdentityTokens[0], Is.Not.SameAs(policy));
            Assert.That(captured.EndpointUrl, Is.EqualTo("opc.tcp://physical:4840"));
            Assert.That(captured.Server.ApplicationUri, Is.EqualTo("urn:physical:server"));
            Assert.That(captured.Server.ProductUri, Is.EqualTo("urn:physical:product"));
            Assert.That(captured.Server.ApplicationName.FilterByPreferredLocales(["en"]).Text,
                Is.EqualTo("Physical server"));
            Assert.That(captured.Server.ApplicationName.FilterByPreferredLocales(["de"]).Text,
                Is.EqualTo("Physisch server"));
            Assert.That(captured.Server.DiscoveryUrls[0], Is.EqualTo("opc.tcp://physical:4840"));
            Assert.That(captured.ServerCertificate.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(captured.UserIdentityTokens[0].PolicyId, Is.EqualTo("physical-token"));
            Assert.That(captured.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(captured.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(captured.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
            Assert.That(captured.SecurityLevel, Is.EqualTo(5));
        }

        /// <summary>
        /// Unsupported nested name ownership rejects capture without sending a service request.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedEndpointRejectsUnsupportedApplicationNameCopy(bool confidential)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                Server = new ApplicationDescription
                {
                    ApplicationNames =
                    [
                        new LocalizedText(new SortedDictionary<string, string> { ["en"] = "Physical server" })
                    ]
                }
            };
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(endpoint);
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await using SessionRequestContext context = confidential
                    ? await SessionRequestContext.CaptureConfidentialAsync(fixture.ManagedSession).ConfigureAwait(false)
                    : await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            }).ConfigureAwait(false);
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Confidential capture rejects missing physical metadata; ordinary capture retains configured metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturingChannelWithoutEndpointPreservesCapturePolicy(bool confidential)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns((EndpointDescription)null!);

            if (confidential)
            {
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    await using SessionRequestContext context =
                        await SessionRequestContext.CaptureConfidentialAsync(fixture.ManagedSession)
                            .ConfigureAwait(false);
                }))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            }
            else
            {
                await using SessionRequestContext context =
                    await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
                EndpointDescription captured = context.Session.TransportChannel.EndpointDescription;
                Assert.That(captured, Is.Not.SameAs(fixture.InnerSession.ConfiguredEndpoint.Description));
                Assert.That(captured.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840"));
                Assert.That(captured.Server.ApplicationUri, Is.EqualTo("urn:request-context:server"));
                Assert.That(context.Invalidated.IsCompleted, Is.False);
            }
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Only confidential dispatch reads physical security metadata again after capture.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DispatchReadsPhysicalEndpointOnlyForConfidentialContext(bool confidential)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            await using SessionRequestContext context = confidential
                ? await SessionRequestContext.CaptureConfidentialAsync(fixture.ManagedSession).ConfigureAwait(false)
                : await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var failure = new InvalidOperationException("Physical endpoint metadata is unavailable.");
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Throws(failure);
            fixture.Handler = (_, _) => new ValueTask<IServiceResponse>(new ReadResponse
            {
                Results = [new DataValue(Variant.From(19))]
            });

            if (confidential)
            {
                InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await context.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value }])
                        .ConfigureAwait(false));
                Assert.That(error, Is.SameAs(failure));
                Assert.That(fixture.Requests, Is.Empty);
            }
            else
            {
                ReadResponse response = await context.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value }])
                    .ConfigureAwait(false);
                Assert.That(response.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(19));
                Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            }
        }

        /// <summary>
        /// Capture preserves collection values after generated setters normalize null arrays to empty.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedEndpointPreservesNormalizedCollectionValues(bool empty)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var endpoint = new EndpointDescription
            {
                Server = new ApplicationDescription
                {
                    DiscoveryUrls = empty ? ArrayOf<string>.Empty : ArrayOf<string>.Null
                },
                ServerCertificate = empty ? ByteString.Empty : default,
                UserIdentityTokens = empty ? ArrayOf<UserTokenPolicy>.Empty : ArrayOf<UserTokenPolicy>.Null
            };
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(endpoint);

            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            EndpointDescription captured = context.Session.TransportChannel.EndpointDescription;

            Assert.That(endpoint.Server.DiscoveryUrls.IsNull, Is.False);
            Assert.That(endpoint.UserIdentityTokens.IsNull, Is.False);
            Assert.That(captured.Server.DiscoveryUrls.IsNull, Is.False);
            Assert.That(captured.ServerCertificate.IsNull, Is.EqualTo(!empty));
            Assert.That(captured.UserIdentityTokens.IsNull, Is.False);
            Assert.That(captured.Server.DiscoveryUrls.Count, Is.Zero);
            Assert.That(captured.ServerCertificate.Length, Is.Zero);
            Assert.That(captured.UserIdentityTokens.Count, Is.Zero);
        }

        /// <summary>
        /// Capture preserves null token policy entries in configured and physical metadata.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturingEndpointPreservesNullTokenPolicy(bool confidential)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            var policy = new ProviderUserTokenPolicy { PolicyId = "original" };
            UserTokenPolicy[] policies = [null!, policy];
            fixture.InnerSession.ConfiguredEndpoint.Description.UserIdentityTokens = policies;

            await using SessionRequestContext context = confidential
                ? await SessionRequestContext.CaptureConfidentialAsync(fixture.ManagedSession).ConfigureAwait(false)
                : await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            policy.PolicyId = "changed";
            policies[0] = new UserTokenPolicy();

            EndpointDescription configured = context.Session.ConfiguredEndpoint.Description;
            EndpointDescription physical = context.Session.TransportChannel.EndpointDescription;
            Assert.That(configured.UserIdentityTokens.Count, Is.EqualTo(2));
            Assert.That(physical.UserIdentityTokens.Count, Is.EqualTo(2));
            Assert.That(configured.UserIdentityTokens[0], Is.Null);
            Assert.That(physical.UserIdentityTokens[0], Is.Null);
            Assert.That(configured.UserIdentityTokens[1], Is.Not.SameAs(policy));
            Assert.That(physical.UserIdentityTokens[1], Is.Not.SameAs(policy));
            Assert.That(configured.UserIdentityTokens[1].PolicyId, Is.EqualTo("original"));
            Assert.That(physical.UserIdentityTokens[1].PolicyId, Is.EqualTo("original"));
            Assert.That(context.Invalidated.IsCompleted, Is.False);
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// A null server assignment is normalized by the generated setter before the physical endpoint is captured.
        /// </summary>
        [Test]
        public async Task CapturedEndpointCopiesNormalizedServerDescription()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var endpoint = new EndpointDescription { Server = null! };
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(endpoint);
            Assert.That(endpoint.Server, Is.Not.Null);

            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            EndpointDescription captured = context.Session.TransportChannel.EndpointDescription;

            Assert.That(captured.Server, Is.Not.SameAs(endpoint.Server));
            Assert.That(captured.Server.ApplicationUri, Is.EqualTo(endpoint.Server.ApplicationUri));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Public identifiers alone cannot make a captured context valid on another physical connection.
        /// </summary>
        [Test]
        public async Task PhysicalChannelReplacementRejectsReusedSessionIdentity()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var replacement = new Mock<ITransportChannel>();
            replacement.SetupGet(channel => channel.MessageContext).Returns(fixture.MessageContext);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            Task<HistoryReadResponse> request;
            using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
            {
                request = ReadHistoryAsync(context.Session).AsTask();
                fixture.ReplaceTransport(replacement.Object);
            }
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await request.ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(context.SessionId));
            Assert.That(fixture.Requests, Is.Empty);
            replacement.Verify(channel => channel.SendRequestAsync(
                It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A recreation is still a new generation if a server reuses both externally visible identifiers.
        /// </summary>
        [Test]
        public async Task GenerationCounterRejectsReusedIdentifiers()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            fixture.InnerSession.SessionCreated(NodeId.Parse("s=original-session"), NodeId.Parse("s=original-token"));
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// A caller-supplied header cannot replace the captured authentication context.
        /// </summary>
        [Test]
        public async Task ForeignAuthenticationHeaderIsRejectedBeforeDispatch()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await context.Session.ReadAsync(
                    new RequestHeader { AuthenticationToken = NodeId.Parse("s=foreign-token") },
                    0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value }],
                    CancellationToken.None).ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Borrowed generated services keep the owner's negotiated batching limits without sharing mutable options.
        /// </summary>
        [Test]
        public async Task BorrowedSessionPreservesNegotiatedBatchingLimits()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.InnerSession.OperationLimits.MaxNodesPerRead = 1;
            fixture.InnerSession.ServerCapabilities.MaxBrowseContinuationPoints = 7;
            fixture.Handler = (request, _) =>
            {
                var read = (ReadRequest)request;
                return new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = read.NodesToRead.ConvertAll(static _ => new DataValue(Variant.From(42)))
                });
            };
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            fixture.InnerSession.OperationLimits.MaxNodesPerRead = 10;
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = new NodeId(11u), AttributeId = Attributes.Value }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(context.Session.OperationLimits.MaxNodesPerRead, Is.EqualTo(1));
            Assert.That(context.Session.ServerCapabilities.MaxBrowseContinuationPoints, Is.EqualTo(7));
            Assert.That(context.Session.SystemContext.NamespaceUris.GetString(1),
                Is.EqualTo("urn:request-context:original"));
        }

        /// <summary>
        /// Disposal and a direct Close on the borrowed session affect only its local resources.
        /// </summary>
        [Test]
        public async Task ScopedDisposalDoesNotCloseOwner()
        {
            await using var fixture = new SessionRequestContextTestContext();
            SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                StatusCode closed = await context.Session.CloseAsync().ConfigureAwait(false);
                Assert.That(closed, Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(fixture.InnerSession.Connected, Is.True);
            Assert.That(fixture.ManagedSession.SessionId, Is.EqualTo(NodeId.Parse("s=original-session")));
            Assert.That(fixture.Requests, Is.Empty);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Channel.Verify(channel => channel.Dispose(), Times.Never);
        }

        /// <summary>
        /// A completed method result is not replaced by a later caller cancellation or session change.
        /// </summary>
        [Test]
        public async Task ScopedDispatchPreservesCommittedResponseAfterCancellation()
        {
            await using var fixture = new SessionRequestContextTestContext();
            using var cancellation = new CancellationTokenSource();
            var expected = new CallMethodResult { StatusCode = StatusCodes.GoodCompletesAsynchronously };
            fixture.Handler = async (_, _) =>
            {
                fixture.Recreate();
                await cancellation.CancelAsync().ConfigureAwait(false);
                return new CallResponse { Results = [expected] };
            };
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);

            CallResponse response = await context.ExecuteAsync(
                (session, ct) => session.CallAsync(null,
                    [new CallMethodRequest { ObjectId = new NodeId(1u), MethodId = new NodeId(2u) }], ct),
                cancellation.Token).ConfigureAwait(false);

            Assert.That(response.Results[0], Is.SameAs(expected));
            Assert.That(response.Results[0].StatusCode,
                Is.EqualTo(StatusCodes.GoodCompletesAsynchronously));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// The real managed transport path preserves a known result and does not follow channel recovery.
        /// </summary>
        [Test]
        public async Task ManagedTransportContextPreservesCommittedResultAndExpiresOnRecovery()
        {
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = SessionChannelHarness.CreateEndpoint() },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(owner.TransportChannel, Is.InstanceOf<IManagedTransportChannel>());
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(owner).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            var expected = new CallMethodResult { StatusCode = StatusCodes.GoodCompletesAsynchronously };
            ScriptedChannel transport = channels.CreatedChannels[0];
            transport.RequestHandler = async (request, _) =>
            {
                if (request is not CallRequest)
                {
                    return transport.CreateResponse(request);
                }
                calls++;
                await cancellation.CancelAsync().ConfigureAwait(false);
                return new CallResponse
                {
                    ResponseHeader = transport.CreateGoodHeader(),
                    Results = [expected]
                };
            };
            CallResponse response = await context.Session.CallAsync(
                null, [new CallMethodRequest { ObjectId = new NodeId(1u), MethodId = new NodeId(2u) }],
                cancellation.Token).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.SameAs(expected));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(cancellation.IsCancellationRequested, Is.True);

            await channels.Manager.ReconnectAsync((IManagedTransportChannel)owner.TransportChannel)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// Dispatch returns the original service failure without replaying the request.
        /// </summary>
        [Test]
        public async Task ScopedDispatchPreservesServiceFailureWithoutRetry()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = static (_, _) => throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Local disposal callbacks still run after invalidation even when the supplied token is cancelled.
        /// </summary>
        [Test]
        public async Task StaleExecuteStillRunsLocalCleanup()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            fixture.Recreate();
            bool cleaned = false;
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            int result = await context.ExecuteAsync((session, _) =>
            {
                Assert.That(session, Is.SameAs(context.Session));
                cleaned = true;
                return new ValueTask<int>(12);
            }, cancellation.Token).ConfigureAwait(false);
            Assert.That(cleaned, Is.True);
            Assert.That(result, Is.EqualTo(12));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// No fixed-generation guarantee is fabricated for an arbitrary ISession implementation.
        /// </summary>
        [Test]
        public async Task CapturingCustomSessionFailsClosed()
        {
            var custom = new Mock<ISession>();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await SessionRequestContext.CaptureAsync(custom.Object).ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            custom.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Sends an actual history request carrying a representative retained continuation point.
        /// </summary>
        private static ValueTask<HistoryReadResponse> ReadHistoryAsync(ISession session)
        {
            return session.HistoryReadAsync(
                null, new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Both, false,
                [new HistoryReadValueId { NodeId = new NodeId(3u, 1), ContinuationPoint = ByteString.From("point"u8) }],
                CancellationToken.None);
        }

        /// <summary>
        /// Configured metadata remains distinct from physical metadata and never calls provider clone overrides.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ConfiguredEndpointSnapshotAvoidsProviderClonesAsync(bool aliasClone, bool confidential)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            var physical = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://physical:4840",
                Server = new ApplicationDescription { ApplicationUri = "urn:physical" },
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(physical);
            var translations = new Dictionary<string, string>
            {
                ["en"] = "Configured {0}",
                ["de"] = "Konfiguriert {0}"
            };
            object[] arguments = ["original"];
            byte[] certificate = [1, 2];
            string[] urls = ["opc.tcp://configured:4840"];
            var policy = new ProviderUserTokenPolicy { PolicyId = "configured-policy" };
            var description = new ProviderEndpointDescription
            {
                AliasClone = aliasClone,
                EndpointUrl = "opc.tcp://configured:4840",
                Server = new ProviderApplicationDescription
                {
                    ApplicationUri = "urn:configured",
                    ApplicationName = new LocalizedText(translations,
                        new TranslationInfo("configured", "en", "Configured {0}", arguments)),
                    DiscoveryUrls = urls
                },
                ServerCertificate = ByteString.From(certificate),
                UserIdentityTokens = [policy]
            };
            var configuration = new ProviderEndpointConfiguration
            {
                AliasClone = aliasClone,
                OperationTimeout = 12345,
                UseBinaryEncoding = true,
                MaxStringLength = 23456,
                MaxByteStringLength = 24567,
                MaxArrayLength = 25678,
                MaxMessageSize = 34567,
                MaxBufferSize = 45678,
                ChannelLifetime = 56789,
                SecurityTokenLifetime = 67890,
                MaxEncodingNestingLevels = 17,
                MaxDecoderRecoveries = 19
            };
            ConfiguredEndpoint original = fixture.InnerSession.ConfiguredEndpoint;
            typeof(ConfiguredEndpoint).GetField("m_description", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(original, description);
            original.Configuration = configuration;

            await using SessionRequestContext context = confidential
                ? await SessionRequestContext.CaptureConfidentialAsync(fixture.ManagedSession).ConfigureAwait(false)
                : await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            EndpointDescription captured = context.Session.ConfiguredEndpoint.Description;
            EndpointConfiguration capturedConfiguration = context.Session.ConfiguredEndpoint.Configuration!;
            description.EndpointUrl = "opc.tcp://changed:4840";
            description.Server.ApplicationUri = "urn:changed";
            translations["en"] = "Changed {0}";
            translations["de"] = "Changed {0}";
            arguments[0] = "changed";
            certificate[0] = 9;
            urls[0] = "opc.tcp://changed:4840";
            policy.PolicyId = "changed";
            configuration.OperationTimeout = 1;
            configuration.UseBinaryEncoding = false;
            configuration.MaxStringLength = 5;
            configuration.MaxByteStringLength = 6;
            configuration.MaxArrayLength = 7;
            configuration.MaxMessageSize = 2;
            configuration.MaxBufferSize = 8;
            configuration.ChannelLifetime = 9;
            configuration.SecurityTokenLifetime = 10;
            configuration.MaxEncodingNestingLevels = 3;
            configuration.MaxDecoderRecoveries = 4;

            Assert.That(description.CloneCalls, Is.Zero);
            Assert.That(configuration.CloneCalls, Is.Zero);
            Assert.That(captured, Is.Not.SameAs(description));
            Assert.That(capturedConfiguration, Is.Not.SameAs(configuration));
            Assert.That(captured.EndpointUrl, Is.EqualTo("opc.tcp://configured:4840"));
            Assert.That(context.Session.TransportChannel.EndpointDescription.EndpointUrl,
                Is.EqualTo("opc.tcp://physical:4840"));
            Assert.That(captured.Server.ApplicationUri, Is.EqualTo("urn:configured"));
            Assert.That(captured.Server.ApplicationName.FilterByPreferredLocales(["en"]).Text,
                Is.EqualTo("Configured original"));
            Assert.That(captured.Server.ApplicationName.FilterByPreferredLocales(["de"]).Text,
                Is.EqualTo("Konfiguriert original"));
            Assert.That(captured.Server.DiscoveryUrls[0], Is.EqualTo("opc.tcp://configured:4840"));
            Assert.That(captured.ServerCertificate.Span[0], Is.EqualTo(1));
            Assert.That(captured.UserIdentityTokens[0].PolicyId, Is.EqualTo("configured-policy"));
            Assert.That(capturedConfiguration.OperationTimeout, Is.EqualTo(12345));
            Assert.That(capturedConfiguration.UseBinaryEncoding, Is.True);
            Assert.That(capturedConfiguration.MaxStringLength, Is.EqualTo(23456));
            Assert.That(capturedConfiguration.MaxByteStringLength, Is.EqualTo(24567));
            Assert.That(capturedConfiguration.MaxArrayLength, Is.EqualTo(25678));
            Assert.That(capturedConfiguration.MaxMessageSize, Is.EqualTo(34567));
            Assert.That(capturedConfiguration.MaxBufferSize, Is.EqualTo(45678));
            Assert.That(capturedConfiguration.ChannelLifetime, Is.EqualTo(56789));
            Assert.That(capturedConfiguration.SecurityTokenLifetime, Is.EqualTo(67890));
            Assert.That(capturedConfiguration.MaxEncodingNestingLevels, Is.EqualTo(17));
            Assert.That(capturedConfiguration.MaxDecoderRecoveries, Is.EqualTo(19));
            Assert.That(context.Session.TransportChannel.EndpointConfiguration.OperationTimeout, Is.EqualTo(12345));
        }

        /// <summary>
        /// Opaque identity mutations cannot change captured values or reach the physical dispatch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OpaqueIdentityMutationInvalidatesCapturedContextAsync(bool mutateAuthenticationToken)
        {
            await using var fixture = new SessionRequestContextTestContext();
            byte[] sessionBytes = [1, 2];
            byte[] tokenBytes = [3, 4];
            fixture.InnerSession.SessionCreated(
                new NodeId(ByteString.From(sessionBytes), 2), new NodeId(ByteString.From(tokenBytes), 3));
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            if (mutateAuthenticationToken)
            {
                tokenBytes[0] = 9;
            }
            else
            {
                sessionBytes[0] = 9;
            }

            Assert.That(context.SessionId, Is.EqualTo(new NodeId(ByteString.From(new byte[] { 1, 2 }), 2)));
            Assert.That(context.Session.SessionId, Is.EqualTo(context.SessionId));
            Assert.That(context.Session.AuthenticationToken,
                Is.EqualTo(new NodeId(ByteString.From(new byte[] { 3, 4 }), 3)));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await context.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value }])
                    .ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Snapshotting preserves null and empty opaque identifiers, including in a nonzero namespace.
        /// </summary>
        [TestCase(false, (ushort)0)]
        [TestCase(true, (ushort)0)]
        [TestCase(false, (ushort)2)]
        [TestCase(true, (ushort)2)]
        public void OpaqueIdentitySnapshotPreservesNullAndEmpty(bool empty, ushort namespaceIndex)
        {
            ByteString identifier = empty ? ByteString.Empty : default;
            var original = new NodeId(identifier, namespaceIndex);

            NodeId snapshot = SessionRequestContext.SnapshotNodeId(original);

            Assert.That(snapshot.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(snapshot.NamespaceIndex, Is.EqualTo(namespaceIndex));
            Assert.That(snapshot.IsNull, Is.EqualTo(namespaceIndex == 0));
            Assert.That(snapshot.TryGetValue(out ByteString captured), Is.True);
            Assert.That(captured.IsNull, Is.EqualTo(!empty));
            Assert.That(captured.Length, Is.Zero);
        }

        /// <summary>
        /// Represents configuration metadata whose clone can throw or return caller-owned state.
        /// </summary>
        private sealed class ProviderEndpointConfiguration : EndpointConfiguration
        {
            /// <summary>
            /// Gets or sets whether a clone returns the provider instance.
            /// </summary>
            public bool AliasClone { get; set; }

            /// <summary>
            /// Gets the number of provider clone invocations.
            /// </summary>
            public int CloneCalls { get; private set; }

            /// <inheritdoc/>
            public override object Clone()
            {
                CloneCalls++;
                return AliasClone ? this : throw new AssertionException("Provider configuration clone was invoked.");
            }
        }

        /// <summary>
        /// Represents endpoint metadata whose provider does not implement a detached clone.
        /// </summary>
        private sealed class ProviderEndpointDescription : EndpointDescription
        {
            /// <summary>
            /// Gets or sets whether a clone returns the provider instance.
            /// </summary>
            public bool AliasClone { get; set; }

            /// <summary>
            /// Gets the number of provider clone invocations.
            /// </summary>
            public int CloneCalls { get; private set; }

            /// <inheritdoc/>
            public override object Clone()
            {
                CloneCalls++;
                return AliasClone ? this :
                    throw new AssertionException("Provider endpoint clones must not be invoked.");
            }
        }

        /// <summary>
        /// Represents application metadata whose provider does not implement a detached clone.
        /// </summary>
        private sealed class ProviderApplicationDescription : ApplicationDescription
        {
            /// <inheritdoc/>
            public override object Clone()
            {
                throw new AssertionException("Provider application clones must not be invoked.");
            }
        }

        /// <summary>
        /// Represents token policy metadata whose provider does not implement a detached clone.
        /// </summary>
        private sealed class ProviderUserTokenPolicy : UserTokenPolicy
        {
            /// <inheritdoc/>
            public override object Clone()
            {
                throw new AssertionException("Provider token policy clones must not be invoked.");
            }
        }
    }
}
