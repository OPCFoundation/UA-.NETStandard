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

using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers the optional acknowledgement observer without changing service outcomes.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class SessionAcknowledgementTests
    {
        /// <summary>Server acknowledgement statuses used by the completion tests.</summary>
        private static readonly StatusCode[] s_acknowledgementResults =
        [
            StatusCodes.Good,
            StatusCodes.BadSequenceNumberUnknown,
            StatusCodes.BadSubscriptionIdInvalid,
            StatusCodes.BadUnexpectedError
        ];

        /// <summary>
        /// A result preserves request identity and reports the server's status verbatim.
        /// </summary>
        [TestCaseSource(nameof(s_acknowledgementResults))]
        public void PublishCompletionPreservesAcknowledgementIdentity(StatusCode code)
        {
            using ObservationSession session = CreateSession();
            PublishAcknowledgementsCompletedEventArgs? observed = null;
            ((ISubscriptionAcknowledgementEvents)session).PublishAcknowledgementsCompleted +=
                (_, args) => observed = args;
            var acknowledgement = new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 9 };

            session.Complete([acknowledgement], [code]);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Acknowledgements[0], Is.SameAs(acknowledgement));
            Assert.That(observed.Results[0], Is.EqualTo(code));
        }

        /// <summary>
        /// Failed requests, malformed results and requests without ACKs have no completion notification.
        /// </summary>
        [TestCase("fault")]
        [TestCase("missing-response")]
        [TestCase("wrong-count")]
        [TestCase("empty")]
        public void InvalidPublishCompletionDoesNotReportAcknowledgements(string scenario)
        {
            using ObservationSession session = CreateSession();
            int calls = 0;
            session.PublishAcknowledgementsCompleted += (_, _) => calls++;
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = scenario == "empty"
                ? []
                : [new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 9 }];

            session.Complete(acknowledgements,
                scenario is "wrong-count" or "empty" ? [] : [StatusCodes.Good],
                scenario == "fault" ? StatusCodes.BadTimeout : StatusCodes.Good,
                scenario == "missing-response");

            Assert.That(calls, Is.Zero);
        }

        /// <summary>
        /// A failed observer does not suppress other observers or fail the service completion.
        /// </summary>
        [Test]
        public void ObserverFailureDoesNotChangePublishOutcome()
        {
            using ObservationSession session = CreateSession();
            int calls = 0;
            session.PublishAcknowledgementsCompleted += (_, _) => throw new InvalidOperationException("Observer failure.");
            session.PublishAcknowledgementsCompleted += (_, _) => calls++;

            Assert.DoesNotThrow(() => session.Complete(
                [new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 9 }],
                [StatusCodes.Good]));
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// An identity replacement retires state, while a same-session activation does not.
        /// </summary>
        [Test]
        public void SessionIdentityReplacementDiscardsAcknowledgementState()
        {
            using ObservationSession session = CreateSession();
            session.SessionCreated(new NodeId(1u), new NodeId(2u));
            var discarded = new List<uint>();
            session.SubscriptionAcknowledgementsDiscarded += (_, args) => discarded.Add(args.SubscriptionId);

            session.SessionCreated(new NodeId(1u), new NodeId(2u));
            Assert.That(discarded, Is.Empty);
            session.SessionCreated(new NodeId(3u), new NodeId(4u));
            Assert.That(discarded, Is.EqualTo(new uint[] { 0 }));
        }

        /// <summary>
        /// A recreate that transfers subscriptions keeps their acknowledgement state, so
        /// the session id change is not reported as a retirement of all state. Retirement
        /// is reported again once the transfer-preserving replacement has ended.
        /// </summary>
        [Test]
        public void TransferPreservingIdentityReplacementDoesNotDiscardAcknowledgementState()
        {
            using ObservationSession session = CreateSession();
            session.SessionCreated(new NodeId(1u), new NodeId(2u));
            var discarded = new List<uint>();
            session.SubscriptionAcknowledgementsDiscarded += (_, args) => discarded.Add(args.SubscriptionId);

            session.BeginRetainingAcknowledgements();
            try
            {
                session.SessionCreated(default, default);
                session.SessionCreated(new NodeId(3u), new NodeId(4u));
            }
            finally
            {
                session.EndRetainingAcknowledgements();
            }
            Assert.That(discarded, Is.Empty);

            session.SessionCreated(new NodeId(5u), new NodeId(6u));
            Assert.That(discarded, Is.EqualTo(new uint[] { 0 }));
        }

        /// <summary>
        /// The transfer decision follows the engine's own transfer setting: the classic
        /// engine uses the session's reconnect flag, the V2 engine its manager's flag.
        /// </summary>
        [TestCase(false, false, false, false)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, false, true, true)]
        [TestCase(true, true, false, false)]
        public void TransferDecisionFollowsTheActiveEngine(
            bool v2, bool transferOnReconnect, bool transferOnRecreate, bool expected)
        {
            using ObservationSession session = CreateSession(
                v2 ? DefaultSubscriptionEngineFactory.Instance : null);
            session.TransferSubscriptionsOnReconnect = transferOnReconnect;
            if (session.SubscriptionEngine is DefaultSubscriptionEngine
                {
                    SubscriptionManager: Subscriptions.SubscriptionManager manager
                })
            {
                manager.TransferSubscriptionsOnRecreate = transferOnRecreate;
            }

            Assert.That(session.WillTransferSubscriptionsOnRecreate(), Is.EqualTo(expected));
        }

        /// <summary>
        /// Every successful creation assigns a new, higher generation and retires only the
        /// older ones; retiring an id reports its current generation and forgets it.
        /// </summary>
        [Test]
        public void CreationAssignsIncreasingGenerationsAndRetirementReportsThem()
        {
            using ObservationSession session = CreateSession();
            var discarded = new List<(uint, long)>();
            session.SubscriptionAcknowledgementsDiscarded +=
                (_, args) => discarded.Add((args.SubscriptionId, args.Generation));

            session.CompleteCreation(7);
            session.CompleteCreation(8);
            session.CompleteCreation(7);
            Assert.That(session.GetSubscriptionGeneration(7), Is.EqualTo(3));
            Assert.That(session.GetSubscriptionGeneration(8), Is.EqualTo(2));
            Assert.That(session.GetSubscriptionGeneration(0), Is.EqualTo(3));

            session.OnAcknowledgementsDiscarded(7);
            Assert.That(session.GetSubscriptionGeneration(7), Is.Zero);
            session.OnAcknowledgementsDiscarded(8, 1);
            Assert.That(session.GetSubscriptionGeneration(8), Is.EqualTo(2),
                "Retiring an older generation must not forget a newer one.");
            session.SessionCreated(new NodeId(1u), new NodeId(2u));

            Assert.That(discarded, Is.EqualTo(new (uint, long)[]
            {
                (7, 0), (8, 1), (7, 2), (7, 3), (8, 1), (0, 3)
            }));
            Assert.That(session.GetSubscriptionGeneration(8), Is.Zero);
        }

        /// <summary>
        /// The single-argument constructor retires every generation.
        /// </summary>
        [Test]
        public void DiscardedWithoutGenerationRetiresEveryGeneration()
        {
            var args = new SubscriptionAcknowledgementsDiscardedEventArgs(7);

            Assert.That(args.Generation, Is.EqualTo(long.MaxValue));
        }

        /// <summary>
        /// Arguments created by applications report generation zero.
        /// </summary>
        [Test]
        public void DeferralArgumentsWithoutResolverReportGenerationZero()
        {
            var acknowledgement = new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 1 };
            var args = new PublishSequenceNumbersToAcknowledgeEventArgs([acknowledgement], []);

            Assert.That(args.GetSubscriptionGeneration(acknowledgement), Is.Zero);
            Assert.That(() => args.GetSubscriptionGeneration(null!), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Creation retires prior acknowledgement state even when the server reuses an identifier.
        /// </summary>
        [Test]
        public void SubscriptionCreationRetiresPriorStateForReusedIds()
        {
            using ObservationSession session = CreateSession();
            var discarded = new List<uint>();
            session.SubscriptionAcknowledgementsDiscarded += (_, args) => discarded.Add(args.SubscriptionId);

            session.CompleteCreation(7);
            session.CompleteCreation(7);

            Assert.That(discarded, Is.EqualTo(new uint[] { 7, 7 }));
        }

        /// <summary>
        /// Reconnection cloning preserves observer handlers only when requested.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ClonePreservesAcknowledgementObserversWhenRequested(bool copyHandlers)
        {
            using ObservationSession session = CreateSession();
            int calls = 0;
            session.SubscriptionAcknowledgementsDiscarded += (_, _) => calls++;
            using Session clone = session.CloneSession(session.TransportChannel, copyHandlers);

            clone.OnAcknowledgementsDiscarded(7);

            Assert.That(calls, Is.EqualTo(copyHandlers ? 1 : 0));
        }

        /// <summary>
        /// Creates an unconnected session with a mocked transport.
        /// </summary>
        private static ObservationSession CreateSession(ISubscriptionEngineFactory? engineFactory = null)
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
            return new ObservationSession(channel.Object, configuration, endpoint, engineFactory);
        }

        /// <summary>
        /// Makes the supported completion hook available for deterministic protocol-result tests.
        /// </summary>
        private sealed class ObservationSession(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            ISubscriptionEngineFactory? engineFactory) : Session(
                channel, configuration, endpoint, engineFactory: engineFactory)
        {
            /// <summary>
            /// Simulates the response that establishes a new subscription identifier.
            /// </summary>
            public void CompleteCreation(uint subscriptionId)
            {
                RequestCompleted(new CreateSubscriptionRequest(),
                    new CreateSubscriptionResponse
                    {
                        SubscriptionId = subscriptionId,
                        ResponseHeader = new ResponseHeader()
                    }, "CreateSubscription");
            }

            /// <summary>
            /// Simulates the completion of a Publish service request.
            /// </summary>
            public void Complete(ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                ArrayOf<StatusCode> results, StatusCode serviceResult = default, bool missingResponse = false)
            {
                RequestCompleted(
                    new PublishRequest { SubscriptionAcknowledgements = acknowledgements },
                    missingResponse ? null! : new PublishResponse
                    {
                        Results = results,
                        ResponseHeader = new ResponseHeader { ServiceResult = serviceResult }
                    }, "Publish");
            }
        }
    }
}
