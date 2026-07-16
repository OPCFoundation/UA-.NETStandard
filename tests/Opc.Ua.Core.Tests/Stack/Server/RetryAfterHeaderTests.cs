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

#nullable enable

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
    }
}
