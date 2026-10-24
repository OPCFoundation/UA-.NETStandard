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
 *
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

using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers the session-owned notification lifetimes: provenance, uniqueness, retirement,
    /// transfer and clone continuation.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    public sealed class NotificationLifetimeRegistryTests
    {
        /// <summary>
        /// A created subscription stamps lifetimes with created provenance; a server reusing the
        /// id starts a new lifetime and retires the old one, even for the same sequence number.
        /// </summary>
        [Test]
        public void CreatedSubscriptionLifetimeEndsWhenTheIdIsReused()
        {
            using LifetimeSession session = CreateSession();
            session.CompleteCreateSubscription(7);

            NotificationLifetime first = session.StampNotificationLifetime(7, 1);
            Assert.Multiple(() =>
            {
                Assert.That(first.IsNull, Is.False);
                Assert.That(first.SubscriptionId, Is.EqualTo(7));
                Assert.That(first.SequenceNumber, Is.EqualTo(1));
                Assert.That(first.Provenance, Is.EqualTo(NotificationProvenance.Created));
                Assert.That(session.IsCurrentNotificationLifetime(first), Is.True);
                Assert.That(session.StampNotificationLifetime(7, 1), Is.EqualTo(first), "Stamping is a value.");
            });

            session.CompleteCreateSubscription(7);
            NotificationLifetime reused = session.StampNotificationLifetime(7, 1);

            Assert.Multiple(() =>
            {
                Assert.That(reused, Is.Not.EqualTo(first));
                Assert.That(session.IsCurrentNotificationLifetime(first), Is.False);
                Assert.That(session.IsCurrentNotificationLifetime(reused), Is.True);
            });
        }

        /// <summary>
        /// A subscription transferred into the session gets a lifetime of its own with transferred
        /// provenance; one received without being created or transferred is unknown but still
        /// unique; retirement ends both.
        /// </summary>
        [Test]
        public void TransferredAndUnknownSubscriptionsGetUniqueLifetimes()
        {
            using LifetimeSession session = CreateSession();
            session.CompleteTransferSubscriptions(3);

            NotificationLifetime transferred = session.StampNotificationLifetime(3, 5);
            NotificationLifetime unknown = session.StampNotificationLifetime(4, 5);

            Assert.Multiple(() =>
            {
                Assert.That(transferred.Provenance, Is.EqualTo(NotificationProvenance.Transferred));
                Assert.That(unknown.Provenance, Is.EqualTo(NotificationProvenance.Unknown));
                Assert.That(session.GetSubscriptionGeneration(3), Is.Not.Zero);
                Assert.That(session.GetSubscriptionGeneration(4), Is.Not.Zero);
                Assert.That(session.GetSubscriptionGeneration(3), Is.Not.EqualTo(session.GetSubscriptionGeneration(4)));
            });

            session.OnAcknowledgementsDiscarded(3);
            Assert.That(session.IsCurrentNotificationLifetime(transferred), Is.False);
            Assert.That(session.IsCurrentNotificationLifetime(unknown), Is.True);

            session.OnAcknowledgementsDiscarded(0);
            Assert.That(session.IsCurrentNotificationLifetime(unknown), Is.False);
        }

        /// <summary>
        /// A transfer of a subscription the session already owns (after an in-process recreate)
        /// keeps its lifetime.
        /// </summary>
        [Test]
        public void InProcessTransferKeepsTheLifetime()
        {
            using LifetimeSession session = CreateSession();
            session.CompleteCreateSubscription(9);
            NotificationLifetime before = session.StampNotificationLifetime(9, 2);

            session.CompleteTransferSubscriptions(9);

            Assert.That(session.IsCurrentNotificationLifetime(before), Is.True);
            Assert.That(session.StampNotificationLifetime(9, 2), Is.EqualTo(before));
            Assert.That(before.Provenance, Is.EqualTo(NotificationProvenance.Created));
        }

        /// <summary>
        /// A session cloned for recreation starts lifetimes of its own: the template's lifetimes end
        /// with the template, and lifetimes of different sessions never compare equal.
        /// </summary>
        [Test]
        public void CloneStartsNewLifetimesAndSessionsAreDistinct()
        {
            using LifetimeSession session = CreateSession();
            using LifetimeSession other = CreateSession();
            session.CompleteCreateSubscription(1);
            other.CompleteCreateSubscription(1);
            NotificationLifetime lifetime = session.StampNotificationLifetime(1, 1);

            using Session clone = session.CloneSession(session.TransportChannel, false);

            Assert.That(clone.IsCurrentNotificationLifetime(lifetime), Is.False);
            Assert.That(clone.StampNotificationLifetime(1, 1), Is.Not.EqualTo(lifetime));
            Assert.That(other.StampNotificationLifetime(1, 1), Is.Not.EqualTo(lifetime));
            Assert.That(other.IsCurrentNotificationLifetime(lifetime), Is.False);
        }

        /// <summary>
        /// The empty lifetime is never current and identifier zero is never stamped.
        /// </summary>
        [Test]
        public void NullLifetimeIsNeverCurrent()
        {
            using LifetimeSession session = CreateSession();

            Assert.That(default(NotificationLifetime).IsNull, Is.True);
            Assert.That(session.IsCurrentNotificationLifetime(default), Is.False);
            Assert.That(session.StampNotificationLifetime(0, 1).IsNull, Is.True);
            Assert.That(default(NotificationLifetime).ToString(), Is.EqualTo("(null)"));
        }

        /// <summary>
        /// Creates an unconnected session with a mocked transport.
        /// </summary>
        private static LifetimeSession CreateSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration { ApplicationCertificates = [] }
            };
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(value => value.MessageContext).Returns(configuration.CreateMessageContext());
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription("opc.tcp://localhost:4840"),
                EndpointConfiguration.Create(configuration));
            return new LifetimeSession(channel.Object, configuration, endpoint);
        }

        /// <summary>
        /// Completes subscription services as if the server answered them.
        /// </summary>
        private sealed class LifetimeSession(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint) : Session(channel, configuration, endpoint)
        {
            /// <summary>
            /// Completes a CreateSubscription that the server answered with the given id.
            /// </summary>
            public void CompleteCreateSubscription(uint subscriptionId)
            {
                RequestCompleted(
                    new CreateSubscriptionRequest { RequestHeader = new RequestHeader() },
                    new CreateSubscriptionResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                        SubscriptionId = subscriptionId
                    },
                    "CreateSubscription");
            }

            /// <summary>
            /// Completes a successful TransferSubscriptions of the given id.
            /// </summary>
            public void CompleteTransferSubscriptions(uint subscriptionId)
            {
                RequestCompleted(
                    new TransferSubscriptionsRequest
                    {
                        RequestHeader = new RequestHeader(),
                        SubscriptionIds = [subscriptionId]
                    },
                    new TransferSubscriptionsResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                        Results = [new TransferResult { StatusCode = StatusCodes.Good }]
                    },
                    "TransferSubscriptions");
            }
        }
    }
}
