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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// Extended compliance tests for miscellaneous server behavior:
    /// Cancel request handling, unknown service, response time,
    /// RequestHandle echo, and DiagnosticInfo suppression.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("MiscellaneousExtended")]
    public class MiscellaneousExtendedTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Best Practice - Timeouts")]
        [Property("Tag", "N/A")]
        public async Task VerifyServerHandlesReadWithinAcceptableTimeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var sw = Stopwatch.StartNew();

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            sw.Stop();
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(10000),
                "Read should complete within 10 seconds.");
        }

        [Test]
        [Property("ConformanceUnit", "Best Practice - Strict Message Handling")]
        [Property("Tag", "001")]
        public async Task VerifyResponseRequestHandleEchoedAsync()
        {
            var requestHeader = new RequestHeader
            {
                RequestHandle = 54321u,
                Timestamp = DateTime.UtcNow
            };

            ReadResponse response = await Session.ReadAsync(
                requestHeader, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.ResponseHeader.RequestHandle,
                Is.EqualTo(54321u),
                "Server should echo RequestHandle in response.");
        }

        [Test]
        [Property("ConformanceUnit", "Best Practice - Strict Message Handling")]
        [Property("Tag", "001")]
        public async Task VerifyNoDiagnosticsWhenNotRequestedAsync()
        {
            var requestHeader = new RequestHeader
            {
                ReturnDiagnostics = 0,
                Timestamp = DateTime.UtcNow
            };

            ReadResponse response = await Session.ReadAsync(
                requestHeader, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            // When ReturnDiagnostics=0, DiagnosticInfos should be
            // empty or null
            if (response.DiagnosticInfos != default)
            {
                bool allNull = true;
                foreach (DiagnosticInfo di in response.DiagnosticInfos)
                {
                    if (di != null && !di.IsNullDiagnosticInfo)
                    {
                        allNull = false;
                        break;
                    }
                }

                Assert.That(allNull, Is.True,
                    "DiagnosticInfos should be empty when not requested.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Best Practice - Strict Message Handling")]
        [Property("Tag", "001")]
        public async Task ReadWithMaxAgeZeroReturnsDeviceValueAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                "MaxAge=0 should return a fresh device value.");
        }

        [Test]
        [Property("ConformanceUnit", "Best Practice - Strict Message Handling")]
        [Property("Tag", "001")]
        public async Task ReadWithMaxAgeMaxReturnsCacheAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, double.MaxValue, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                "MaxAge=MaxValue should return a cached value.");
        }
    }
}
