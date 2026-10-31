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
