// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Unit tests for <see cref="RetryAfterHeader"/>, which carries a server
    /// retry-after hint in a <see cref="ResponseHeader.AdditionalHeader"/>.
    /// </summary>
    [TestFixture]
    [Category("RetryAfterHeader")]
    [Parallelizable]
    public class RetryAfterHeaderTests
    {
        [Test]
        public void AttachAndReadRoundTrips()
        {
            var header = new ResponseHeader();

            RetryAfterHeader.AttachTo(header, TimeSpan.FromSeconds(2));

            Assert.That(RetryAfterHeader.Read(header), Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void ReadReturnsNullWhenAbsent()
        {
            Assert.That(RetryAfterHeader.Read(new ResponseHeader()), Is.Null);
            Assert.That(RetryAfterHeader.Read(null), Is.Null);
        }

        [Test]
        public void AttachIgnoresNonPositiveDelay()
        {
            var header = new ResponseHeader();

            RetryAfterHeader.AttachTo(header, TimeSpan.Zero);

            Assert.That(RetryAfterHeader.Read(header), Is.Null);
        }

        [Test]
        public void AttachRoundsUpToWholeMilliseconds()
        {
            var header = new ResponseHeader();

            RetryAfterHeader.AttachTo(header, TimeSpan.FromTicks(15_001));

            // 1.5001 ms rounds up to 2 ms.
            Assert.That(
                RetryAfterHeader.Read(header),
                Is.EqualTo(TimeSpan.FromMilliseconds(2)));
        }

        [Test]
        public void AttachMergesWithExistingParameters()
        {
            var header = new ResponseHeader
            {
                AdditionalHeader = new ExtensionObject(
                    new AdditionalParametersType
                    {
                        Parameters =
                        [
                            new KeyValuePair
                            {
                                Key = QualifiedName.From("Other"),
                                Value = Variant.From((long)7)
                            }
                        ]
                    })
            };

            RetryAfterHeader.AttachTo(header, TimeSpan.FromMilliseconds(1500));

            Assert.That(
                RetryAfterHeader.Read(header),
                Is.EqualTo(TimeSpan.FromMilliseconds(1500)));

            Assert.That(
                header.AdditionalHeader.TryGetValue(out AdditionalParametersType? parameters),
                Is.True);

            int count = 0;
            foreach (KeyValuePair unused in parameters!.Parameters)
            {
                count++;
            }
            Assert.That(count, Is.EqualTo(2));
        }

        [TestCase(long.MaxValue)]
        [TestCase(1_000_000_000_000_000L)]
        public void ReadClampsOversizedPeerValue(long milliseconds)
        {
            // An unbounded peer value overflowed TimeSpan.FromMilliseconds.
            var header = new ResponseHeader
            {
                AdditionalHeader = new ExtensionObject(
                    new AdditionalParametersType
                    {
                        Parameters =
                        [
                            new KeyValuePair
                            {
                                Key = QualifiedName.From(AdditionalParameterNames.RetryAfterMs),
                                Value = Variant.From(milliseconds)
                            }
                        ]
                    })
            };

            Assert.That(RetryAfterHeader.Read(header), Is.EqualTo(RetryAfterHint.MaxRetryAfter));
        }
    }
}
