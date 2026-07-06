/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

#pragma warning disable CA2000
#pragma warning disable CA2007

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    [Category("SessionEngineContext")]
    public sealed class SessionEngineContextTests
    {
        [Test]
        public void CapturedContextExposesSessionStateAndServices()
        {
            using TestSessionScope scope = CreateSessionScope();

            ISubscriptionEngineContext context = scope.Context;

            Assert.Multiple(() =>
            {
                Assert.That(context.Connected, Is.False);
                Assert.That(context.Reconnecting, Is.False);
                Assert.That(context.Closing, Is.False);
                Assert.That(context.Disposed, Is.False);
                Assert.That(context.DeleteSubscriptionsOnClose, Is.True);
                Assert.That(context.OperationTimeout, Is.Zero);
                Assert.That(context.ServerState, Is.EqualTo(ServerState.Running));
                Assert.That(context.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.None));
                Assert.That(context.Telemetry, Is.SameAs(scope.Telemetry));
                Assert.That(context.Subscriptions, Is.Empty);
                Assert.That(context.ReconnectLock, Is.Not.Null);
                Assert.That(context.GoodPublishRequestCount, Is.Zero);
                Assert.That(context.SubscriptionServiceSet, Is.SameAs(scope.Session));
                Assert.That(context.MonitoredItemServiceSet, Is.SameAs(scope.Session));
                Assert.That(context.MethodServiceSet, Is.SameAs(scope.Session));
            });
        }

        [Test]
        public void KeepAliveCallbacksAreForwardedToSessionEvents()
        {
            using TestSessionScope scope = CreateSessionScope();
            DateTime timestamp = DateTime.UtcNow;
            KeepAliveEventArgs? keepAliveArgs = null;

            scope.Session.KeepAlive += (_, e) => keepAliveArgs = e;

            scope.Context.OnKeepAlive(ServerState.Running, timestamp);

            Assert.That(keepAliveArgs, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(keepAliveArgs!.CurrentState, Is.EqualTo(ServerState.Running));
                Assert.That(keepAliveArgs.CurrentTime, Is.EqualTo(timestamp));
                Assert.That(scope.Context.ServerState, Is.EqualTo(ServerState.Running));
            });
        }

        [Test]
        public void KeepAliveErrorsAreForwardedToSessionEvents()
        {
            using TestSessionScope scope = CreateSessionScope();
            KeepAliveEventArgs? keepAliveArgs = null;

            scope.Session.KeepAlive += (_, e) => keepAliveArgs = e;

            scope.Context.OnKeepAliveError(new ServiceResult(StatusCodes.BadTimeout));

            Assert.That(keepAliveArgs, Is.Not.Null);
            ServiceResult status = keepAliveArgs!.Status!;
            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void PublishErrorsAreForwardedToSessionEvents()
        {
            using TestSessionScope scope = CreateSessionScope();
            PublishErrorEventArgs? publishErrorArgs = null;

            scope.Session.PublishError += (_, e) => publishErrorArgs = e;

            scope.Context.OnPublishError(
                new ServiceResult(StatusCodes.BadNoSubscription),
                subscriptionId: 123,
                sequenceNumber: 456);

            Assert.That(publishErrorArgs, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(publishErrorArgs!.Status.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
                Assert.That(publishErrorArgs.SubscriptionId, Is.EqualTo(123u));
                Assert.That(publishErrorArgs.SequenceNumber, Is.EqualTo(456u));
            });
        }

        [Test]
        public void PrepareAcknowledgementsWithoutCallbackSendsAllAcknowledgements()
        {
            using TestSessionScope scope = CreateSessionScope();
            var acknowledgements = new List<SubscriptionAcknowledgement>
            {
                new()
                {
                    SubscriptionId = 1,
                    SequenceNumber = 2
                }
            };

            (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending) =
                scope.Context.PrepareAcknowledgementsToSend(acknowledgements);

            Assert.Multiple(() =>
            {
                Assert.That(toSend, Is.SameAs(acknowledgements));
                Assert.That(updatedPending, Is.Empty);
            });
        }

        [Test]
        public void PrepareAcknowledgementsCallbackCanDeferAcknowledgements()
        {
            using TestSessionScope scope = CreateSessionScope();
            var acknowledgement = new SubscriptionAcknowledgement
            {
                SubscriptionId = 1,
                SequenceNumber = 2
            };
            var acknowledgements = new List<SubscriptionAcknowledgement>
            {
                acknowledgement
            };

            scope.Session.PublishSequenceNumbersToAcknowledge += (_, e) =>
            {
                e.AcknowledgementsToSend.Remove(acknowledgement);
                e.DeferredAcknowledgementsToSend.Add(acknowledgement);
            };

            (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending) =
                scope.Context.PrepareAcknowledgementsToSend(acknowledgements);

            Assert.Multiple(() =>
            {
                Assert.That(toSend, Is.SameAs(acknowledgements));
                Assert.That(toSend, Is.Empty);
                Assert.That(updatedPending, Has.Count.EqualTo(1));
                Assert.That(updatedPending[0], Is.SameAs(acknowledgement));
            });
        }

        [Test]
        public void PrepareAcknowledgementsCallbackFailureFallsBackToSendingAllAcknowledgements()
        {
            using TestSessionScope scope = CreateSessionScope();
            var acknowledgements = new List<SubscriptionAcknowledgement>
            {
                new()
                {
                    SubscriptionId = 1,
                    SequenceNumber = 2
                }
            };

            scope.Session.PublishSequenceNumbersToAcknowledge += (_, _) => throw new InvalidOperationException();

            (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending) =
                scope.Context.PrepareAcknowledgementsToSend(acknowledgements);

            Assert.Multiple(() =>
            {
                Assert.That(toSend, Is.SameAs(acknowledgements));
                Assert.That(updatedPending, Is.Empty);
            });
        }

        [Test]
        public void PublishNotificationsAreForwardedAsynchronously()
        {
            using TestSessionScope scope = CreateSessionScope();
            using var received = new ManualResetEventSlim();
            NotificationEventArgs? receivedArgs = null;
            var subscription = new Subscription(scope.Telemetry);
            SetSubscriptionId(subscription, 42);
            var args = new NotificationEventArgs(
                subscription,
                new NotificationMessage(),
                new ArrayOf<string>());

            scope.Session.Notification += (_, e) =>
            {
                receivedArgs = e;
                received.Set();
            };

            scope.Context.OnPublishNotification(subscription, args);

            Assert.That(received.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(receivedArgs, Is.SameAs(args));
        }

        [Test]
        public void PublishNotificationHandlerFailureIsSwallowed()
        {
            using TestSessionScope scope = CreateSessionScope();
            var subscription = new Subscription(scope.Telemetry);
            SetSubscriptionId(subscription, 42);
            var args = new NotificationEventArgs(
                subscription,
                new NotificationMessage(),
                new ArrayOf<string>());

            scope.Session.Notification += (_, _) => throw new InvalidOperationException();

            Assert.That(
                () => scope.Context.OnPublishNotification(subscription, args),
                Throws.Nothing);
        }

        [Test]
        public async Task ServiceMethodsDelegateToSessionAsync()
        {
            using TestSessionScope scope = CreateSessionScope();

            Assert.That(
                async () => await scope.Context.PublishAsync(
                    new RequestHeader(),
                    new ArrayOf<SubscriptionAcknowledgement>(),
                    CancellationToken.None),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(
                async () => await scope.Context.TransferSubscriptionsAsync(
                    null,
                    new ArrayOf<uint>(new uint[] { 1 }),
                    sendInitialValues: true,
                    CancellationToken.None),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(
                async () => await scope.Context.DeleteSubscriptionsAsync(
                    null,
                    new ArrayOf<uint>(new uint[] { 1 }),
                    CancellationToken.None),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(
                async () => await scope.Context.DeleteOrphanedSubscriptionAsync(1),
                Throws.Nothing);

            await Task.CompletedTask;
        }

        [Test]
        public void AsyncRequestTrackingDelegatesToSession()
        {
            using TestSessionScope scope = CreateSessionScope();
            Task task = Task.CompletedTask;

            Assert.That(
                () => scope.Context.AsyncRequestStarted(
                    task,
                    activity: null,
                    requestHandle: 1,
                    requestTypeId: DataTypes.PublishRequest),
                Throws.Nothing);
            Assert.That(
                () => scope.Context.AsyncRequestCompleted(
                    task,
                    requestHandle: 1,
                    requestTypeId: DataTypes.PublishRequest),
                Throws.Nothing);
        }

        private static TestSessionScope CreateSessionScope()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateClientConfiguration(telemetry);
            IServiceMessageContext messageContext = configuration.CreateMessageContext();
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(c => c.MessageContext).Returns(messageContext);

            var engineFactory = new CapturingEngineFactory();
            var session = new Session(
                channel.Object,
                configuration,
                CreateEndpoint(),
                engineFactory: engineFactory);

            return new TestSessionScope(session, engineFactory.Context!, telemetry);
        }

        private static ApplicationConfiguration CreateClientConfiguration(
            ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "SessionEngineContextTests",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:SessionEngineContextTests",
                ProductUri = "urn:localhost:SessionEngineContextTests",
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    MinSubscriptionLifetime = 10000
                },
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 6000
                }
            };
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            description.Server.ApplicationUri = description.EndpointUrl;
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(
                null,
                description,
                new EndpointConfiguration { OperationTimeout = 6000 })
            {
                UpdateBeforeConnect = false
            };
        }

        private static void SetSubscriptionId(Subscription subscription, uint id)
        {
            PropertyInfo idProperty = typeof(Subscription).GetProperty(nameof(Subscription.Id))!;
            idProperty.SetValue(subscription, id);
        }

        private sealed class TestSessionScope : IDisposable
        {
            public TestSessionScope(
                Session session,
                ISubscriptionEngineContext context,
                ITelemetryContext telemetry)
            {
                Session = session;
                Context = context;
                Telemetry = telemetry;
            }

            public Session Session { get; }

            public ISubscriptionEngineContext Context { get; }

            public ITelemetryContext Telemetry { get; }

            public void Dispose()
            {
                Session.Dispose();
            }
        }

        private sealed class CapturingEngineFactory : ISubscriptionEngineFactory
        {
            public ISubscriptionEngineContext? Context { get; private set; }

            public ISubscriptionEngine Create(ISubscriptionEngineContext context)
            {
                Context = context;
                return new NoOpSubscriptionEngine();
            }
        }

        private sealed class NoOpSubscriptionEngine : ISubscriptionEngine
        {
            public int GoodPublishRequestCount => 0;

            public int BadPublishRequestCount => 0;

            public int PublishWorkerCount => 0;

            public int MinPublishRequestCount { get; set; } = 1;

            public int MaxPublishRequestCount { get; set; } = ushort.MaxValue;

            public void Dispose()
            {
            }

            public void NotifySubscriptionsChanged()
            {
            }

            public void PausePublishing()
            {
            }

            public ValueTask RecreateSubscriptionsAsync(
                NodeId? previousSessionId,
                CancellationToken ct = default)
            {
                return default;
            }

            public void ResumePublishing()
            {
            }

            public void StartPublishing(int timeout, bool fullQueue)
            {
            }

            public ValueTask StopPublishingAsync(CancellationToken ct = default)
            {
                return default;
            }
        }
    }
}
