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
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests for the stateless UA-TCP / UA-SC chunk parsers in
    /// <see cref="TcpMessageParsers"/>.
    /// </summary>
    [TestFixture]
    [Category("TcpMessageParsersTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TcpMessageParsersTests
    {
        [Test]
        public void ReadReverseHelloMessageOversizedServerUriThrowsBadEncodingLimitsExceeded()
        {
            byte[] body = BuildReverseHelloBody(
                serverUri: new string('A', TcpMessageLimits.MaxEndpointUrlLength + 1),
                endpointUrl: "opc.tcp://localhost:4840");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => TcpMessageParsers.ReadReverseHelloMessage(new ArraySegment<byte>(body)));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadReverseHelloMessageOversizedEndpointUrlThrowsBadEncodingLimitsExceeded()
        {
            byte[] body = BuildReverseHelloBody(
                serverUri: "urn:test:server",
                endpointUrl: new string('B', TcpMessageLimits.MaxEndpointUrlLength + 1));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => TcpMessageParsers.ReadReverseHelloMessage(new ArraySegment<byte>(body)));

            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadReverseHelloMessageValidStringsAreAccepted()
        {
            const string serverUri = "urn:reverse:server";
            const string endpointUrl = "opc.tcp://gateway.example.com:4840/UA/Server";

            byte[] body = BuildReverseHelloBody(serverUri, endpointUrl);

            ReverseHelloMessage decoded = TcpMessageParsers.ReadReverseHelloMessage(
                new ArraySegment<byte>(body));

            Assert.That(decoded.ServerUri, Is.EqualTo(serverUri));
            Assert.That(decoded.EndpointUrl, Is.EqualTo(endpointUrl));
        }

        private static byte[] BuildReverseHelloBody(string serverUri, string endpointUrl)
        {
            using var encoder = new BinaryEncoder(ServiceMessageContext.CreateEmpty(null));
            encoder.WriteString(null, serverUri);
            encoder.WriteString(null, endpointUrl);
            return encoder.CloseAndReturnBuffer();
        }
    }
}
