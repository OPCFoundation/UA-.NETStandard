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
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS

using System.Globalization;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Verifies that the Arrow decoder bounds its out-of-band schema-message cache so a peer
    /// sending many distinct schemaId values cannot exhaust memory.
    /// </summary>
    [TestFixture]
    public sealed class ArrowDecoderSchemaMessageBoundsTests
    {
        /// <summary>
        /// Caching more distinct schema messages than the cap keeps the cache bounded.
        /// </summary>
        [Test]
        public void CacheSchemaBeyondCapacityBoundsTheCache()
        {
            var decoder = new ArrowNetworkMessageDecoder();
            int overflow = ArrowNetworkMessageDecoder.MaxCachedSchemaMessages + 50;
            byte[] message = [1, 2, 3];
            for (int i = 0; i < overflow; i++)
            {
                decoder.CacheSchema("schema-" + i.ToString(CultureInfo.InvariantCulture), message);
            }

            Assert.That(
                decoder.CachedSchemaMessageCount,
                Is.EqualTo(ArrowNetworkMessageDecoder.MaxCachedSchemaMessages));
        }
    }
}

#endif
