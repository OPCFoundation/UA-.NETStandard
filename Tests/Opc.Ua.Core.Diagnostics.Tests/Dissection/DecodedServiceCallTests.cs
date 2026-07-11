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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Pcap.Dissection;

namespace Opc.Ua.Pcap.Tests.Dissection
{
    /// <summary>
    /// Init-only property and derived-property tests for
    /// <see cref="DecodedServiceCall"/>.
    /// </summary>
    [TestFixture]
    public sealed class DecodedServiceCallTests
    {
        [Test]
        public void DefaultsAreEmptyAndUnset()
        {
            var call = new DecodedServiceCall();

            Assert.That(call.ChannelId, Is.Zero);
            Assert.That(call.TokenId, Is.Zero);
            Assert.That(call.RequestId, Is.Zero);
            Assert.That(call.RequestTimestamp, Is.Default);
            Assert.That(call.ResponseTimestamp, Is.Null);
            Assert.That(call.RequestName, Is.Null);
            Assert.That(call.ResponseName, Is.Null);
            Assert.That(call.ResponseStatus, Is.Null);
            Assert.That(call.RequestBodySize, Is.Zero);
            Assert.That(call.ResponseBodySize, Is.Zero);
            Assert.That(call.RequestSummary, Is.Null);
            Assert.That(call.ResponseSummary, Is.Null);
            Assert.That(call.Annotations, Is.Empty);
            Assert.That(call.Latency, Is.Null);
        }

        [Test]
        public void LatencyIsComputedFromRequestAndResponseTimestamps()
        {
            var request = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset response = request.AddMilliseconds(250);

            var call = new DecodedServiceCall
            {
                RequestTimestamp = request,
                ResponseTimestamp = response
            };

            Assert.That(call.Latency, Is.Not.Null);
            Assert.That(call.Latency!.Value, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        }

        [Test]
        public void LatencyIsNullWhenResponseTimestampMissing()
        {
            var call = new DecodedServiceCall
            {
                RequestTimestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            Assert.That(call.Latency, Is.Null);
        }

        [Test]
        public void InitOnlyPropertiesAreAssignableViaObjectInitializer()
        {
            var ts1 = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
            DateTimeOffset ts2 = ts1.AddSeconds(1);

            var call = new DecodedServiceCall
            {
                ChannelId = 0xAABBCCDD,
                TokenId = 0x11223344,
                RequestId = 99,
                RequestTimestamp = ts1,
                ResponseTimestamp = ts2,
                RequestName = "ReadRequest",
                ResponseName = "ReadResponse",
                ResponseStatus = StatusCodes.BadTimeout,
                RequestBodySize = 128,
                ResponseBodySize = 256,
                RequestSummary = "Read 3 nodes",
                ResponseSummary = "3 values returned",
                Annotations = new Dictionary<string, string?>
                {
                    ["TraceId"] = "abc123",
                    ["Tag"] = null
                }
            };

            Assert.That(call.ChannelId, Is.EqualTo(0xAABBCCDDU));
            Assert.That(call.TokenId, Is.EqualTo(0x11223344U));
            Assert.That(call.RequestId, Is.EqualTo(99U));
            Assert.That(call.RequestTimestamp, Is.EqualTo(ts1));
            Assert.That(call.ResponseTimestamp, Is.EqualTo(ts2));
            Assert.That(call.RequestName, Is.EqualTo("ReadRequest"));
            Assert.That(call.ResponseName, Is.EqualTo("ReadResponse"));
            Assert.That(call.ResponseStatus, Is.Not.Null);
            Assert.That(call.ResponseStatus!.Value.Code, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(call.RequestBodySize, Is.EqualTo(128));
            Assert.That(call.ResponseBodySize, Is.EqualTo(256));
            Assert.That(call.RequestSummary, Is.EqualTo("Read 3 nodes"));
            Assert.That(call.ResponseSummary, Is.EqualTo("3 values returned"));
            Assert.That(call.Annotations, Has.Count.EqualTo(2));
            Assert.That(call.Annotations["TraceId"], Is.EqualTo("abc123"));
            Assert.That(call.Annotations["Tag"], Is.Null);
            Assert.That(call.Latency, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void LatencyCanBeNegativeWhenResponseTimestampBeforeRequest()
        {
            // The property surfaces the computed difference verbatim so a
            // caller can detect clock anomalies in their capture.
            var request = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset response = request.AddMilliseconds(-50);

            var call = new DecodedServiceCall
            {
                RequestTimestamp = request,
                ResponseTimestamp = response
            };

            Assert.That(call.Latency, Is.Not.Null);
            Assert.That(call.Latency!.Value, Is.LessThan(TimeSpan.Zero));
            Assert.That(call.Latency!.Value, Is.EqualTo(TimeSpan.FromMilliseconds(-50)));
        }
    }
}
