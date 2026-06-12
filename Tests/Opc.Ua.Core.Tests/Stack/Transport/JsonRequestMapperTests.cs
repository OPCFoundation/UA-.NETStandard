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

#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Smoke tests for <see cref="JsonRequestMapper"/> — the shared JSON
    /// encode / decode helper used by the upcoming HTTPS-JSON and
    /// WSS opcua+uajson handlers. Comprehensive coverage lives in the
    /// p5-unit-jsonmapper expansion.
    /// </summary>
    [TestFixture]
    [Category("JsonRequestMapper")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonRequestMapperTests
    {
        [Test]
        public async Task RoundTripsServiceFaultViaJsonAsync()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var response = new ServiceFault
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.BadInternalError,
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 42
                }
            };

            byte[] encoded = JsonRequestMapper.EncodeResponse(response, context);
            Assert.That(encoded, Has.Length.GreaterThan(0));

            string payload = Encoding.UTF8.GetString(encoded);
            Assert.That(payload, Does.Contain("TypeId"));
            Assert.That(payload, Does.Contain("Body"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task EncodeResponseToStreamWritesSameBytesAsArrayHelper()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var response = new GetEndpointsResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good,
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 7
                }
            };

            byte[] expected = JsonRequestMapper.EncodeResponse(response, context);

            using var stream = new MemoryStream();
            await JsonRequestMapper.EncodeResponseAsync(response, context, stream, CancellationToken.None)
                .ConfigureAwait(false);
            byte[] actual = stream.ToArray();

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void DecodeRequestRejectsMalformedBody()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            byte[] junk = Encoding.UTF8.GetBytes("this is not json");
            using var stream = new MemoryStream(junk);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await JsonRequestMapper.DecodeRequestAsync(stream, context, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadDecodingError));
        }
    }
}
