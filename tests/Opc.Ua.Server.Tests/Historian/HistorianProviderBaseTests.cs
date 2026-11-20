// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Tests for <see cref="HistorianProviderBase"/>, which is an abstract class
    /// with virtual default implementations of the core <see cref="IHistorianProvider"/>
    /// methods and a protected <c>RepeatStatus</c> helper.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianProviderBaseTests
    {
        /// <summary>
        /// Verifies that the base historian provider reports historizing enabled by default.
        /// </summary>
        [Test]
        public async Task IsHistorizingAsyncReturnsTrueByDefaultAsync()
        {
            IHistorianProvider provider = new ConcreteProvider();
            var nodeId = new NodeId("any", 1);

            bool result = await provider.IsHistorizingAsync(nodeId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that the base historian provider advertises read-only capabilities by default.
        /// </summary>
        [Test]
        public async Task GetCapabilitiesAsyncReturnsReadOnlyByDefaultAsync()
        {
            IHistorianProvider provider = new ConcreteProvider();
            var nodeId = new NodeId("any", 1);

            HistorianNodeCapabilities caps =
                await provider.GetCapabilitiesAsync(nodeId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(caps, Is.SameAs(HistorianNodeCapabilities.ReadOnly));
        }

        /// <summary>
        /// Verifies that repeated-status creation fills every entry with the requested code.
        /// </summary>
        [Test]
        public void RepeatStatusReturnsArrayFilledWithGivenCode()
        {
            ArrayOf<StatusCode> result =
                ConcreteProvider.RepeatStatusPublic(
                    StatusCodes.Good,
                    5);

            Assert.That(result, Has.Count.EqualTo(5));
            foreach (StatusCode sc in result)
            {
                Assert.That(sc, Is.EqualTo(StatusCodes.Good));
            }
        }

        /// <summary>
        /// Verifies that repeating a status zero times returns an empty collection.
        /// </summary>
        [Test]
        public void RepeatStatusReturnsEmptyListForCountZero()
        {
            ArrayOf<StatusCode> result =
                ConcreteProvider.RepeatStatusPublic(
                    StatusCodes.BadInvalidArgument,
                    0);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies that repeated-status creation preserves the exact supplied status code.
        /// </summary>
        [Test]
        public void RepeatStatusPreservesSpecificCode()
        {
            ArrayOf<StatusCode> result =
                ConcreteProvider.RepeatStatusPublic(
                    StatusCodes.BadHistoryOperationUnsupported,
                    3);

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
            Assert.That(result[2], Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        /// <summary>
        /// Minimal concrete subclass that exposes the protected
        /// <see cref="HistorianProviderBase.RepeatStatus"/> helper for testing.
        /// </summary>
        private sealed class ConcreteProvider : HistorianProviderBase
        {
            public static ArrayOf<StatusCode> RepeatStatusPublic(
                StatusCode code,
                int count)
            {
                return RepeatStatus(code, count);
            }
        }
    }
}
