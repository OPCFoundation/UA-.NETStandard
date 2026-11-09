/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the owned ConfigurationFile update exchange on scripted session generations without sockets.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ConfigurationFileUpdateClientTests
    {
        /// <summary>
        /// ConfigurationFile is opened in its supported read/write mode and uploaded with exact version and targets.
        /// </summary>
        [Test]
        public async Task UpdateUsesSupportedOpenModeAndPreservesContentVersionAndTargets()
        {
            await using var fixture = new UpdateFixture();
            fixture.RequiresConfirmation = false;
            await fixture.ConnectAsync().ConfigureAwait(false);

            ConfigurationFileUpdateResult result = await fixture.UpdateAsync().ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(ConfigurationFileUpdateOutcome.Applied));
            Assert.That(result.NewVersion, Is.EqualTo(8));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.UpdateId, Is.EqualTo(Uuid.Empty));
            Assert.That(fixture.ConnectionCount, Is.EqualTo(1));
            CallMethodRequest open = fixture.FirstCalls.Single(call => call.MethodId == Ua.MethodIds.FileType_Open);
            Assert.That(open.InputArguments[0].TryGetValue(out byte mode), Is.True);
            Assert.That(mode, Is.EqualTo((byte)(OpenFileMode.Read | OpenFileMode.Write)));
            CallMethodRequest write = fixture.FirstCalls.Single(call => call.MethodId == Ua.MethodIds.FileType_Write);
            Assert.That(write.InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(42));
            Assert.That(write.InputArguments[1].TryGetValue(out ByteString content), Is.True);
            Assert.That(content, Is.EqualTo(UpdateFixture.Content));
            CallMethodRequest update = fixture.FirstCalls.Single(call =>
                call.MethodId == Ua.MethodIds.ConfigurationFileType_CloseAndUpdate);
            Assert.That(update.InputArguments[1].TryGetValue(out uint version), Is.True);
            Assert.That(version, Is.EqualTo(7));
            Assert.That(update.InputArguments[2].TryGetStructure(
                out ArrayOf<ConfigurationUpdateTargetType> targets), Is.True);
            Assert.That(targets, Is.EqualTo(UpdateFixture.Targets));
            Assert.That(fixture.SecondCalls, Is.Empty);
        }

        /// <summary>
        /// Confirmation follows the returned identifier on a new session without repeating the upload or update.
        /// </summary>
        [Test]
        public async Task ManagedServerEndpointReceivesUpdateAndConfirmationOnSeparateSessions()
        {
            await using var fixture = new UpdateFixture();
            await fixture.ConnectAsync().ConfigureAwait(false);

            ConfigurationFileUpdateResult result = await fixture.UpdateAsync().ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(ConfigurationFileUpdateOutcome.Confirmed));
            Assert.That(result.UpdateId, Is.EqualTo(fixture.UpdateId));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            fixture.AssertSingleUpdateAndConfirmation();
        }

        /// <summary>
        /// A managed application's file is resolved on both sessions without using the host's configuration node.
        /// </summary>
        [Test]
        public async Task ManagedApplicationUpdateRetainsSelectedOwnerAfterReconnect()
        {
            await using var fixture = new UpdateFixture();
            fixture.ApplicationConfigurationId =
                new ExpandedNodeId("application", "urn:managed:configuration:namespace");
            fixture.RegisterApplicationNamespaceOnBothGenerations();
            await fixture.ConnectAsync().ConfigureAwait(false);

            ConfigurationFileUpdateResult result = await fixture.UpdateAsync().ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(ConfigurationFileUpdateOutcome.Confirmed));
            Assert.That(result.UpdateId, Is.EqualTo(fixture.UpdateId));
            fixture.AssertSingleUpdateAndConfirmation();
        }

        /// <summary>
        /// An unknown application namespace is rejected before upload rather than redirected to the host.
        /// </summary>
        [Test]
        public async Task ManagedApplicationUnknownNamespaceDoesNotMutateHostConfiguration()
        {
            await using var fixture = new UpdateFixture();
            fixture.ApplicationConfigurationId =
                new ExpandedNodeId("application", "urn:managed:configuration:unknown");
            await fixture.ConnectAsync().ConfigureAwait(false);

            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.UpdateAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(fixture.FirstCalls, Is.Empty);
            Assert.That(fixture.SecondCalls, Is.Empty);
            Assert.That(fixture.ConnectionCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Application updates require namespace-stable local instance addresses.
        /// </summary>
        [TestCase("null")]
        [TestCase("index")]
        [TestCase("remote")]
        [TestCase("standard")]
        public async Task ManagedApplicationUpdateRejectsUnstableOrNonApplicationAddress(string address)
        {
            await using var fixture = new UpdateFixture();
            fixture.ApplicationConfigurationId = address switch
            {
                "null" => ExpandedNodeId.Null,
                "index" => new ExpandedNodeId("application", 2),
                "remote" => new ExpandedNodeId("application", "urn:managed:configuration:namespace", 1),
                "standard" => new ExpandedNodeId("application", Namespaces.OpcUa),
                _ => throw new ArgumentOutOfRangeException(nameof(address))
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await fixture.Client.UpdateApplicationConfigurationFileAsync(
                    fixture.ApplicationConfigurationId, UpdateFixture.Content, UpdateFixture.Targets, 30_000, 0)
                    .ConfigureAwait(false));
            Assert.That(fixture.FirstCalls, Is.Empty);
            Assert.That(fixture.ConnectionCount, Is.Zero);
        }

        /// <summary>
        /// The restart interval elapses before reconnect and confirmation, even when staging already returned.
        /// </summary>
        [Test]
        public async Task ReconnectWaitsForRestartDelayBeforeConfirming()
        {
            await using var fixture = new UpdateFixture();
            await fixture.ConnectAsync().ConfigureAwait(false);

            Task<ConfigurationFileUpdateResult> pending = fixture.UpdateAsync(restartDelayTime: 1000).AsTask();
            await fixture.RestartDelayScheduled.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(fixture.ConnectionCount, Is.EqualTo(1));
            Assert.That(fixture.SecondCalls, Is.Empty);
            fixture.First.TimeProvider.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(fixture.ConnectionCount, Is.EqualTo(1));
            fixture.First.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));
            ConfigurationFileUpdateResult result = await pending.ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(ConfigurationFileUpdateOutcome.Confirmed));
            fixture.AssertSingleUpdateAndConfirmation();
        }

        /// <summary>
        /// Missing durable server support is reported without reconnecting or attempting confirmation.
        /// </summary>
        [Test]
        public async Task UnsupportedRecoveryReturnsRejectionWithoutReconnect()
        {
            await using var fixture = new UpdateFixture();
            fixture.UpdateStatus = StatusCodes.BadNotSupported;
            await fixture.ConnectAsync().ConfigureAwait(false);

            ConfigurationFileUpdateResult result = await fixture.UpdateAsync().ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(ConfigurationFileUpdateOutcome.Rejected));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(result.UpdateId, Is.EqualTo(Uuid.Empty));
            Assert.That(fixture.ConnectionCount, Is.EqualTo(1));
            Assert.That(fixture.FirstCalls.Count(call =>
                call.MethodId == Ua.MethodIds.ConfigurationFileType_CloseAndUpdate), Is.EqualTo(1));
            Assert.That(fixture.SecondCalls, Is.Empty);
        }

        /// <summary>
        /// A lost update response is reported as unknown and is never retried or guessed into confirmation.
        /// </summary>
        [Test]
        public async Task LostUpdateResponseDoesNotRepeatCloseAndUpdate()
        {
            await using var fixture = new UpdateFixture();
            fixture.UpdateStatus = StatusCodes.BadTimeout;
            await fixture.ConnectAsync().ConfigureAwait(false);

            ConfigurationFileUpdateResult result = await fixture.UpdateAsync().ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(ConfigurationFileUpdateOutcome.OutcomeUnknown));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(result.UpdateId, Is.EqualTo(Uuid.Empty));
            Assert.That(fixture.ConnectionCount, Is.EqualTo(1));
            Assert.That(fixture.FirstCalls.Count(call =>
                call.MethodId == Ua.MethodIds.ConfigurationFileType_CloseAndUpdate), Is.EqualTo(1));
            Assert.That(fixture.SecondCalls, Is.Empty);
        }

        /// <summary>
        /// Reconnection failure retains the update identifier for provider recovery without repeating any mutation.
        /// </summary>
        [Test]
        public async Task FailedReconnectRetainsPendingConfirmationIdentifier()
        {
            await using var fixture = new UpdateFixture();
            fixture.FailReconnect = true;
            await fixture.ConnectAsync().ConfigureAwait(false);

            ConfigurationFileUpdateResult result = await fixture.UpdateAsync().ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(ConfigurationFileUpdateOutcome.ConfirmationPending));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(result.UpdateId, Is.EqualTo(fixture.UpdateId));
            Assert.That(result.NewVersion, Is.EqualTo(8));
            Assert.That(fixture.ConnectionCount, Is.EqualTo(2));
            Assert.That(fixture.FirstCalls.Count(call =>
                call.MethodId == Ua.MethodIds.ConfigurationFileType_CloseAndUpdate), Is.EqualTo(1));
            Assert.That(fixture.SecondCalls, Is.Empty);
        }

        /// <summary>
        /// A rejected confirmation remains pending, while a lost response leaves its outcome unknown.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedConfirmationPreservesIdentifierWithoutRepeatingUpdate(bool responseLost)
        {
            await using var fixture = new UpdateFixture();
            fixture.ConfirmationStatus = responseLost ? StatusCodes.BadTimeout : StatusCodes.BadInvalidArgument;
            await fixture.ConnectAsync().ConfigureAwait(false);

            ConfigurationFileUpdateResult result = await fixture.UpdateAsync().ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(responseLost
                ? ConfigurationFileUpdateOutcome.OutcomeUnknown
                : ConfigurationFileUpdateOutcome.ConfirmationPending));
            Assert.That(result.StatusCode, Is.EqualTo(fixture.ConfirmationStatus));
            Assert.That(result.UpdateId, Is.EqualTo(fixture.UpdateId));
            Assert.That(result.NewVersion, Is.EqualTo(8));
            fixture.AssertSingleUpdateAndConfirmation();
        }

        /// <summary>
        /// Provides two real sessions over scripted transports and records every dispatched configuration call.
        /// </summary>
        private sealed class UpdateFixture : IAsyncDisposable
        {
            /// <summary>
            /// Creates independent transport generations with the same authorized identity and controllable clock.
            /// </summary>
            public UpdateFixture()
            {
                First = new SessionRequestContextTestContext(securityMode: MessageSecurityMode.SignAndEncrypt);
                First.InnerSession.PublishRequestCancelDelayOnCloseSession = 0;
                First.InnerSession.ConfiguredEndpoint.Description.EndpointUrl =
                    "opc.tcp://managed.example.test:4840";
                First.InnerSession.ConfiguredEndpoint.Description.Server.ApplicationUri = "urn:managed:configuration";
                m_updateClock = new DelayObservingTimeProvider(First.TimeProvider);
                m_second = new SessionRequestContextTestContext(securityMode: MessageSecurityMode.SignAndEncrypt);
                m_reconnected = new Session(m_second.Channel.Object, First.InnerSession, copyEventHandlers: false);
                m_reconnected.SessionCreated(new NodeId("reconnected-session", 0), new NodeId("reconnected-token", 0));
                First.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request, reconnected: false));
                m_second.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request, reconnected: true));
                var factory = new Mock<ISessionFactory>();
                factory.Setup(value => value.CreateAsync(
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(), true, true,
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                    .Returns<ApplicationConfiguration, ConfiguredEndpoint, bool, bool, string, uint,
                        IUserIdentity, ArrayOf<string>, CancellationToken>((_, endpoint, _, _, _, _, identity, _, _) =>
                    {
                        Assert.That(identity, Is.SameAs(First.InnerSession.Identity));
                        Assert.That(endpoint.Description.EndpointUrl,
                            Is.EqualTo("opc.tcp://managed.example.test:4840/"));
                        Assert.That(endpoint.Description.Server.ApplicationUri,
                            Is.EqualTo("urn:managed:configuration"));
                        ConnectionCount++;
                        if (ConnectionCount > 1 && FailReconnect)
                        {
                            throw new ServiceResultException(StatusCodes.BadNoCommunication);
                        }
                        return Task.FromResult<ISession>(ConnectionCount == 1 ? First.InnerSession : m_reconnected);
                    });
                Client = new ServerPushConfigurationClient(
                    new ApplicationConfiguration(First.Telemetry) { ApplicationName = "ConfigurationUpdateTests" },
                    new GdsClientOptions { MaxConnectAttempts = 1 }, factory.Object,
                    timeProvider: m_updateClock)
                {
                    AdminCredentials = First.InnerSession.Identity
                };
            }

            /// <summary>
            /// Gets the initial dispatch context and deterministic clock.
            /// </summary>
            public SessionRequestContextTestContext First { get; }

            /// <summary>
            /// Gets the owned push client.
            /// </summary>
            public ServerPushConfigurationClient Client { get; }

            /// <summary>
            /// Gets or sets the managed configuration owner, or null for the management server itself.
            /// </summary>
            public ExpandedNodeId ApplicationConfigurationId { get; set; }

            /// <summary>
            /// Gets or sets whether the server requires confirmation.
            /// </summary>
            public bool RequiresConfirmation { get; set; } = true;

            /// <summary>
            /// Gets or sets the update service result.
            /// </summary>
            public StatusCode UpdateStatus { get; set; } = StatusCodes.Good;

            /// <summary>
            /// Gets or sets the confirmation service result.
            /// </summary>
            public StatusCode ConfirmationStatus { get; set; } = StatusCodes.Good;

            /// <summary>
            /// Gets or sets whether the new session cannot connect.
            /// </summary>
            public bool FailReconnect { get; set; }

            /// <summary>
            /// Gets the number of factory connection attempts.
            /// </summary>
            public int ConnectionCount { get; private set; }

            /// <summary>
            /// Gets the exact provider-retained update identifier.
            /// </summary>
            public Uuid UpdateId { get; } = Uuid.NewUuid();

            /// <summary>
            /// Gets the signal that the client's restart delay has actually been scheduled.
            /// </summary>
            public Task RestartDelayScheduled => m_updateClock.Scheduled;

            /// <summary>
            /// Gets the calls sent on the uploading session.
            /// </summary>
            public CallMethodRequest[] FirstCalls => First.Requests.OfType<CallRequest>()
                .SelectMany(request => request.MethodsToCall.ToList()).ToArray();

            /// <summary>
            /// Gets the calls sent on the new session.
            /// </summary>
            public CallMethodRequest[] SecondCalls => m_second.Requests.OfType<CallRequest>()
                .SelectMany(request => request.MethodsToCall.ToList()).ToArray();

            /// <summary>
            /// Connects to a fully selected endpoint without performing discovery or opening a socket.
            /// </summary>
            public ValueTask ConnectAsync()
            {
                return Client.ConnectAsync(First.InnerSession.ConfiguredEndpoint);
            }

            /// <summary>
            /// Publishes the same application namespace at different indexes on the two session generations.
            /// </summary>
            public void RegisterApplicationNamespaceOnBothGenerations()
            {
                ushort firstIndex = First.InnerSession.NamespaceUris.GetIndexOrAppend(
                    ApplicationConfigurationId.NamespaceUri!);
                m_reconnected.NamespaceUris.GetIndexOrAppend("urn:managed:configuration:second-generation");
                ushort secondIndex = m_reconnected.NamespaceUris.GetIndexOrAppend(
                    ApplicationConfigurationId.NamespaceUri!);
                Assert.That(firstIndex, Is.Not.EqualTo(secondIndex));
            }

            /// <summary>
            /// Uploads one exact versioned update with the selected rollback and restart intervals.
            /// </summary>
            public ValueTask<ConfigurationFileUpdateResult> UpdateAsync(double restartDelayTime = 0)
            {
                return ApplicationConfigurationId.IsNull
                    ? Client.UpdateConfigurationFileAsync(Content, Targets, 30_000, restartDelayTime)
                    : Client.UpdateApplicationConfigurationFileAsync(
                        ApplicationConfigurationId, Content, Targets, 30_000, restartDelayTime);
            }

            /// <summary>
            /// Checks that only the uploading session mutates and only the new session confirms the returned id.
            /// </summary>
            public void AssertSingleUpdateAndConfirmation()
            {
                Assert.That(ConnectionCount, Is.EqualTo(2));
                Assert.That(FirstCalls.Count(call =>
                    call.MethodId == Ua.MethodIds.ConfigurationFileType_CloseAndUpdate), Is.EqualTo(1));
                Assert.That(FirstCalls.Count(call =>
                    call.MethodId == Ua.MethodIds.ConfigurationFileType_ConfirmUpdate), Is.Zero);
                Assert.That(SecondCalls, Has.Length.EqualTo(1));
                Assert.That(SecondCalls[0].MethodId, Is.EqualTo(Ua.MethodIds.ConfigurationFileType_ConfirmUpdate));
                Assert.That(SecondCalls[0].InputArguments[0].TryGetValue(out Uuid identifier), Is.True);
                Assert.That(identifier, Is.EqualTo(UpdateId));
                Assert.That(First.Requests.OfType<CloseSessionRequest>().Count(), Is.EqualTo(1));
                Assert.That(m_second.Requests.OfType<CallRequest>().Single().RequestHeader.AuthenticationToken,
                    Is.EqualTo(new NodeId("reconnected-token", 0)));
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await Client.DisposeAsync().ConfigureAwait(false);
                await m_reconnected.DisposeAsync().ConfigureAwait(false);
                await m_second.DisposeAsync().ConfigureAwait(false);
                await First.DisposeAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Resolves the file and version and implements only the expected configuration exchange.
            /// </summary>
            private IServiceResponse Respond(IServiceRequest request, bool reconnected)
            {
                if (request is CloseSessionRequest)
                {
                    return new CloseSessionResponse();
                }
                if (request is TranslateBrowsePathsToNodeIdsRequest translate)
                {
                    Assert.That(translate.BrowsePaths, Has.Count.EqualTo(1));
                    string? child = translate.BrowsePaths[0].RelativePath.Elements[0].TargetName.Name;
                    Assert.That(child, Is.EqualTo(Ua.BrowseNames.ConfigurationFile).Or.EqualTo("CurrentVersion"));
                    Assert.That(translate.BrowsePaths[0].StartingNode,
                        Is.EqualTo(child == Ua.BrowseNames.ConfigurationFile
                            ? ExpandedNodeId.ToNodeId(
                                ApplicationConfigurationId.IsNull
                                    ? Ua.ObjectIds.ServerConfiguration
                                    : ApplicationConfigurationId,
                                reconnected ? m_reconnected.NamespaceUris : First.InnerSession.NamespaceUris)
                            : new NodeId("configuration-file", 1)));
                    var targetId = new ExpandedNodeId(
                        child == Ua.BrowseNames.ConfigurationFile ? "configuration-file" : "configuration-version", 1);
                    return new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = [new BrowsePathResult
                        {
                            Targets = [new BrowsePathTarget
                            {
                                TargetId = targetId,
                                RemainingPathIndex = uint.MaxValue
                            }]
                        }]
                    };
                }
                if (request is ReadRequest read)
                {
                    Assert.That(read.NodesToRead, Has.Count.EqualTo(1));
                    Assert.That(read.NodesToRead[0].NodeId, Is.EqualTo(new NodeId("configuration-version", 1)));
                    return new ReadResponse { Results = [new DataValue(Variant.From(7u))] };
                }
                if (request is not CallRequest call || call.MethodsToCall.Count != 1)
                {
                    throw new InvalidOperationException("Unexpected ConfigurationFile request.");
                }
                CallMethodRequest method = call.MethodsToCall[0];
                Assert.That(method.ObjectId, Is.EqualTo(new NodeId("configuration-file", 1)));
                ArrayOf<Variant> outputs;
                if (method.MethodId == Ua.MethodIds.FileType_Open)
                {
                    Assert.That(method.InputArguments[0].TryGetValue(out byte mode), Is.True);
                    Assert.That(mode, Is.EqualTo((byte)(OpenFileMode.Read | OpenFileMode.Write)));
                    outputs = [Variant.From(42u)];
                }
                else if (method.MethodId == Ua.MethodIds.FileType_Write)
                {
                    outputs = [];
                }
                else if (method.MethodId == Ua.MethodIds.ConfigurationFileType_CloseAndUpdate)
                {
                    Assert.That(reconnected, Is.False);
                    if (StatusCode.IsBad(UpdateStatus))
                    {
                        throw new ServiceResultException(UpdateStatus);
                    }
                    outputs = [Variant.From(ArrayOf.Wrapped<StatusCode>(StatusCodes.Good)), Variant.From(8u),
                        Variant.From(RequiresConfirmation ? UpdateId : Uuid.Empty)];
                }
                else if (method.MethodId == Ua.MethodIds.ConfigurationFileType_ConfirmUpdate)
                {
                    Assert.That(reconnected, Is.True);
                    if (StatusCode.IsBad(ConfirmationStatus))
                    {
                        throw new ServiceResultException(ConfirmationStatus);
                    }
                    outputs = [];
                }
                else
                {
                    throw new InvalidOperationException("Unexpected ConfigurationFile method.");
                }
                return new CallResponse { Results = [new CallMethodResult { OutputArguments = outputs }] };
            }

            /// <summary>The exact serialized configuration supplied to the server.</summary>
            public static readonly ByteString Content = ByteString.From(new byte[] { 1, 2, 3 });

            /// <summary>The explicit selected configuration target.</summary>
            public static readonly ArrayOf<ConfigurationUpdateTargetType> Targets =
                [new ConfigurationUpdateTargetType
                {
                    Path = "ApplicationIdentity",
                    UpdateType = ConfigurationUpdateType.InsertOrReplace
                }];

            /// <summary>The second transport's request recorder.</summary>
            private readonly SessionRequestContextTestContext m_second;

            /// <summary>The actual replacement session carrying the original authorized identity.</summary>
            private readonly Session m_reconnected;

            /// <summary>The clock observing the restart delay rather than session teardown timers.</summary>
            private readonly DelayObservingTimeProvider m_updateClock;
        }

        /// <summary>
        /// Signals timer creation so tests advance synthetic time only after the client starts its restart delay.
        /// </summary>
        private sealed class DelayObservingTimeProvider : TimeProvider
        {
            /// <summary>
            /// Borrows the fixture's synthetic clock.
            /// </summary>
            public DelayObservingTimeProvider(TimeProvider inner)
            {
                m_inner = inner;
            }

            /// <summary>
            /// Gets the completion signal for delay scheduling.
            /// </summary>
            public Task Scheduled => m_scheduled.Task;

            /// <inheritdoc/>
            public override ITimer CreateTimer(
                TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer = m_inner.CreateTimer(callback, state, dueTime, period);
                m_scheduled.TrySetResult(true);
                return timer;
            }

            /// <summary>The caller-owned synthetic clock.</summary>
            private readonly TimeProvider m_inner;

            /// <summary>The signal completed after the timer has been registered.</summary>
            private readonly TaskCompletionSource<bool> m_scheduled =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
