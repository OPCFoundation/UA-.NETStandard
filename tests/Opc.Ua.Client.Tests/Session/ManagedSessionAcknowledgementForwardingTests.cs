// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

#pragma warning disable CS0618 // tests exercise the obsolete acknowledgement event

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers how a managed session forwards the obsolete acknowledgement event from its bound session.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class ManagedSessionAcknowledgementForwardingTests
    {
        /// <summary>
        /// The bound session reports a deferral policy only while the application handles the
        /// event on the managed session, so the default Publish path keeps its fast path, and the
        /// forwarded event is raised with the managed session as sender.
        /// </summary>
        [Test]
        public async Task BoundSessionReportsAPolicyOnlyWhileTheApplicationHandlesTheEventAsync()
        {
            var factory = new CapturingEngineFactory();
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession session = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions
                {
                    Endpoint = SessionChannelHarness.CreateEndpoint(),
                    SubscriptionEngineFactory = factory
                },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            ISubscriptionEngineContext context = factory.Context!;
            var capability = (IAcknowledgementPolicyCapability)context;
            ISession? sender = null;
            void Handler(ISession s, PublishSequenceNumbersToAcknowledgeEventArgs _) => sender = s;

            Assert.That(capability.HasAcknowledgementPolicy, Is.False, "No application handler.");
            session.PublishSequenceNumbersToAcknowledge += Handler;
            Assert.That(capability.HasAcknowledgementPolicy, Is.True);
            context.PrepareAcknowledgementsToSend([new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 2 }]);
            Assert.That(sender, Is.SameAs(session));
            session.PublishSequenceNumbersToAcknowledge -= Handler;
            Assert.That(capability.HasAcknowledgementPolicy, Is.False);
        }

        /// <summary>
        /// Captures the context of the engine the bound session creates.
        /// </summary>
        private sealed class CapturingEngineFactory : ISubscriptionEngineFactory
        {
            public ISubscriptionEngineContext? Context { get; private set; }

            public ISubscriptionEngine Create(ISubscriptionEngineContext context)
            {
                Context = context;
                return DefaultSubscriptionEngineFactory.Instance.Create(context);
            }
        }
    }
}
