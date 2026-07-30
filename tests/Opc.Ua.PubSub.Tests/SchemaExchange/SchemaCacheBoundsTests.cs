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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Verifies that <see cref="SchemaCache"/> bounds its cached-schema count so a peer
    /// announcing many distinct schemas cannot exhaust memory.
    /// </summary>
    [TestFixture]
    public sealed class SchemaCacheBoundsTests
    {
        /// <summary>
        /// Adding more distinct schemas than the cap keeps the cache bounded and retains the newest.
        /// </summary>
        [Test]
        public void AddBeyondCapacityBoundsTheCacheAndKeepsNewest()
        {
            var cache = new SchemaCache();
            int overflow = SchemaCache.MaxCachedSchemas + 50;
            ByteString lastId = default;
            for (int i = 0; i < overflow; i++)
            {
                // A distinct real Avro schema per iteration. These must be genuine schema
                // documents: the Avro SchemaId is the CRC-64-AVRO of the Parsing Canonical Form,
                // so a non-schema input has no defined fingerprint and is now rejected instead of
                // being hashed as raw bytes.
                ByteString schema = ByteString.From(System.Text.Encoding.UTF8.GetBytes(
                    "{\"type\":\"record\",\"name\":\"Bounded" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "\",\"fields\":[{\"name\":\"value\",\"type\":\"int\"}]}"));
                ByteString id = SchemaCache.ComputeSchemaId(schema, SchemaCache.AvroFormat);
                cache.Add(id, schema, SchemaCache.AvroFormat);
                lastId = id;
            }

            Assert.Multiple(() =>
            {
                Assert.That(cache.Count, Is.EqualTo(SchemaCache.MaxCachedSchemas));
                Assert.That(cache.TryGet(lastId, out _), Is.True);
            });
        }
    }
}
