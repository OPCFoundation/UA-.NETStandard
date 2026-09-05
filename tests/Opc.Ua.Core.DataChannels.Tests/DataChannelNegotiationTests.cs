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

using System;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Checks the parameter revision rules of Part 4 errata 5.1 and
    /// 5.1.1, and the offer rules of clause 6.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelNegotiationTests
    {
        private static DataChannelSourceCapabilities Source(
            uint maxFrameSize = 0,
            byte priority = 3,
            DataChannelDirection direction = DataChannelDirection.Bidirectional)
        {
            return new DataChannelSourceCapabilities
            {
                Direction = direction,
                SupportedDeliveryModes =
                [
                    DataChannelDeliveryMode.ReliableOrdered,
                    DataChannelDeliveryMode.PartiallyReliable
                ],
                ContentType = "video/H264",
                MaxFrameSize = maxFrameSize,
                Priority = priority
            };
        }

        private static DataChannelServerCapabilities Server(uint maxFrameSize = 65536)
        {
            return new DataChannelServerCapabilities
            {
                MaxFrameSize = maxFrameSize,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes =
                [
                    DataChannelDeliveryMode.ReliableOrdered,
                    DataChannelDeliveryMode.PartiallyReliable
                ]
            };
        }

        /// <summary>
        /// Negotiation refuses to proceed without the capabilities it revises
        /// against.
        /// </summary>
        /// <remarks>
        /// Both are what every limit in the revised parameters is derived
        /// from. Continuing without them would produce a channel whose frame
        /// size and credit were bounded by nothing.
        /// </remarks>
        [Test]
        public void RevisingWithoutCapabilitiesIsRefused()
        {
            var requested = new DataChannelParametersDataType();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => DataChannelNegotiator.TryRevise(
                        requested,
                        null!,
                        Server(),
                        0,
                        true,
                        out _,
                        out _),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("source"));
                Assert.That(
                    () => DataChannelNegotiator.TryRevise(
                        requested,
                        Source(),
                        null!,
                        0,
                        true,
                        out _,
                        out _),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("server"));
            });
        }

        /// <summary>
        /// A mutation check needs the parameters currently in force.
        /// </summary>
        [Test]
        public void AMutationCheckWithoutParametersInForceIsRefused()
        {
            Assert.That(
                () => DataChannelNegotiator.IsMutation(null!, new DataChannelParametersDataType()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("inForce"));
        }

        /// <summary>
        /// A modify that carries no parameters changes nothing.
        /// </summary>
        /// <remarks>
        /// Direction and DeliveryMode are immutable, so the question a
        /// mutation check answers is whether the request would change one.
        /// An absent request cannot.
        /// </remarks>
        [Test]
        public void AnAbsentRequestIsNotAMutation()
        {
            var inForce = new DataChannelParametersDataType
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered
            };

            Assert.That(DataChannelNegotiator.IsMutation(inForce, null), Is.False);
        }

        // DCS-004: Direction is not revised, it is rejected.
        [Test]
        public void DcS004UnsupportedDirectionIsRejected()
        {
            var requested = new DataChannelParametersDataType
            {
                Direction = DataChannelDirection.Bidirectional
            };

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelNegotiator.TryRevise(
                        requested,
                        Source(direction: DataChannelDirection.SourceToSink),
                        Server(),
                        0,
                        true,
                        out _,
                        out StatusCode error),
                    Is.False);
                Assert.That(
                    error,
                    Is.EqualTo((StatusCode)StatusCodes.BadDataChannelDirectionUnsupported));
            });
        }

        // DCS-005: a delivery mode absent from SupportedDeliveryModes is
        // rejected rather than silently substituted.
        [Test]
        public void DcS005UnsupportedDeliveryModeIsRejected()
        {
            var requested = new DataChannelParametersDataType
            {
                DeliveryMode = DataChannelDeliveryMode.Unreliable
            };

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelNegotiator.TryRevise(
                        requested,
                        Source(),
                        Server(),
                        0,
                        true,
                        out _,
                        out StatusCode error),
                    Is.False);
                Assert.That(
                    error,
                    Is.EqualTo((StatusCode)StatusCodes.BadDeliveryModeUnsupported));
            });
        }

        // DCS-006: MaxFrameSize is revised down, never up.
        [Test]
        public void DcS006MaxFrameSizeIsRevisedDownNeverUp()
        {
            var requested = new DataChannelParametersDataType { MaxFrameSize = 1024 * 1024 };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(maxFrameSize: 8192),
                    Server(maxFrameSize: 65536),
                    transportMaxFrameSize: 4096,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(
                revised.MaxFrameSize,
                Is.EqualTo(4096u),
                "the least of the requested, source, server and transport bounds");
        }

        // DCS-007: a zero means no preference and yields a usable value.
        [Test]
        public void DcS007ZeroMeansNoPreference()
        {
            var requested = new DataChannelParametersDataType { MaxFrameSize = 0 };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(maxFrameSize: 8192),
                    Server(),
                    0,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(revised.MaxFrameSize, Is.EqualTo(8192u));
        }

        // DCS-008: InitialCredit is revised up to at least MaxFrameSize,
        // because a window smaller than one frame is a deadlock.
        [Test]
        public void DcS008InitialCreditIsAtLeastOneFrame()
        {
            var requested = new DataChannelParametersDataType
            {
                InitialCredit = 1,
                MaxFrameSize = 8192
            };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(),
                    Server(),
                    0,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(
                revised.InitialCredit,
                Is.GreaterThanOrEqualTo(revised.MaxFrameSize));
        }

        // DCS-009: a Priority above seven is revised to seven.
        [Test]
        public void DcS009PriorityAboveSevenIsRevisedToSeven()
        {
            var requested = new DataChannelParametersDataType { Priority = 200 };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(),
                    Server(),
                    0,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(revised.Priority, Is.EqualTo(DataChannelConstants.MaxPriority));
        }

        [Test]
        public void PriorityZeroIsARealValueNotASentinel()
        {
            var requested = new DataChannelParametersDataType { Priority = 0 };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(priority: 5),
                    Server(),
                    0,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(
                revised.Priority,
                Is.Zero,
                "zero is the lowest priority, not the no preference encoding");
        }

        [Test]
        public void PriorityTwoFiftyFiveSelectsTheSourceDefault()
        {
            var requested = new DataChannelParametersDataType
            {
                Priority = DataChannelConstants.NoPriorityPreference
            };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(priority: 5),
                    Server(),
                    0,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(revised.Priority, Is.EqualTo((byte)5));
        }

        [Test]
        public void RetransmitsAndDeadlineAreZeroedOnAReliableTransport()
        {
            var requested = new DataChannelParametersDataType
            {
                DeliveryMode = DataChannelDeliveryMode.PartiallyReliable,
                MaxRetransmits = 3,
                FrameDeadline = 250
            };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(),
                    Server(),
                    0,
                    transportIsReliable: true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(revised.MaxRetransmits, Is.Zero);
                Assert.That(revised.FrameDeadline, Is.Zero);
            });
        }

        [Test]
        public void RetransmitsAndDeadlineSurviveOnALossyTransport()
        {
            var requested = new DataChannelParametersDataType
            {
                DeliveryMode = DataChannelDeliveryMode.PartiallyReliable,
                MaxRetransmits = 3,
                FrameDeadline = 250
            };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(),
                    Server(),
                    0,
                    transportIsReliable: false,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(revised.MaxRetransmits, Is.EqualTo((ushort)3));
                Assert.That(revised.FrameDeadline, Is.EqualTo(250d));
            });
        }

        [Test]
        public void AContentTypeTheSourceCannotProduceIsRejected()
        {
            var requested = new DataChannelParametersDataType { ContentType = "audio/opus" };

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelNegotiator.TryRevise(
                        requested,
                        Source(),
                        Server(),
                        0,
                        true,
                        out _,
                        out StatusCode error),
                    Is.False);
                Assert.That(
                    error,
                    Is.EqualTo((StatusCode)StatusCodes.BadContentTypeUnsupported));
            });
        }

        [Test]
        public void AWildcardContentTypeIsNarrowedToWhatTheSourceProduces()
        {
            var requested = new DataChannelParametersDataType { ContentType = "video/*" };

            Assert.That(
                DataChannelNegotiator.TryRevise(
                    requested,
                    Source(),
                    Server(),
                    0,
                    true,
                    out DataChannelParametersDataType revised,
                    out _),
                Is.True);

            Assert.That(revised.ContentType, Is.EqualTo("video/H264"));
        }

        // DCS-019: Direction and DeliveryMode are immutable on a live
        // channel, because both change what the receiver's pipeline is.
        [Test]
        public void DcS019DirectionAndDeliveryModeAreImmutable()
        {
            var inForce = new DataChannelParametersDataType
            {
                Direction = DataChannelDirection.SourceToSink,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered
            };

            Assert.Multiple(() =>
            {
                Assert.That(
                    DataChannelNegotiator.IsMutation(
                        inForce,
                        new DataChannelParametersDataType
                        {
                            Direction = DataChannelDirection.Bidirectional,
                            DeliveryMode = DataChannelDeliveryMode.ReliableOrdered
                        }),
                    Is.True);
                Assert.That(
                    DataChannelNegotiator.IsMutation(
                        inForce,
                        new DataChannelParametersDataType
                        {
                            Direction = DataChannelDirection.SourceToSink,
                            DeliveryMode = DataChannelDeliveryMode.Unreliable
                        }),
                    Is.True);
                Assert.That(
                    DataChannelNegotiator.IsMutation(
                        inForce,
                        new DataChannelParametersDataType
                        {
                            Direction = DataChannelDirection.SourceToSink,
                            DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                            Priority = 4
                        }),
                    Is.False);
            });
        }

        // DCS-017: an offer is single use.
        [Test]
        public void DcS017AnOfferIsSingleUse()
        {
            var registry = new DataChannelOfferRegistry();
            var source = new NodeId(42u);

            DataChannelOfferDataType offer = registry.Create(
                source,
                new DataChannelParametersDataType(),
                TimeSpan.FromMinutes(1));

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryRedeem(offer.OfferId, source, out _), Is.True);
                Assert.That(registry.TryRedeem(offer.OfferId, source, out _), Is.False);
            });
        }

        // DCS-018: an offer lapses at its expiration.
        [Test]
        public void DcS018AnOfferExpires()
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var registry = new DataChannelOfferRegistry(clock);
            var source = new NodeId(42u);

            DataChannelOfferDataType offer = registry.Create(
                source,
                new DataChannelParametersDataType(),
                TimeSpan.FromSeconds(30));

            clock.Advance(TimeSpan.FromSeconds(31));

            Assert.That(registry.TryRedeem(offer.OfferId, source, out _), Is.False);
        }

        [Test]
        public void AnOfferDoesNotMatchADifferentSource()
        {
            var registry = new DataChannelOfferRegistry();

            DataChannelOfferDataType offer = registry.Create(
                new NodeId(42u),
                new DataChannelParametersDataType(),
                TimeSpan.FromMinutes(1));

            Assert.That(registry.TryRedeem(offer.OfferId, new NodeId(43u), out _), Is.False);
        }

        [Test]
        public void ExpiredOffersArePurgedSoAnUnsubscribedClientLeaksNothing()
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var registry = new DataChannelOfferRegistry(clock);

            for (int ii = 0; ii < 5; ii++)
            {
                registry.Create(
                    new NodeId((uint)ii),
                    new DataChannelParametersDataType(),
                    TimeSpan.FromSeconds(10));
            }

            clock.Advance(TimeSpan.FromSeconds(11));

            Assert.Multiple(() =>
            {
                Assert.That(registry.PurgeExpired(), Is.EqualTo(5));
                Assert.That(registry.Count, Is.Zero);
            });
        }
    }
}
