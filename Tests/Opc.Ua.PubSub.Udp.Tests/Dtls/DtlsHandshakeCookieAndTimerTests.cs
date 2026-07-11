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
using System.Net;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 retransmission timers and HRR cookies from RFC 9147 §5.1 and §5.8.1.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 9147 §5.1")]
    [TestSpec("RFC 9147 §5.8.1")]
    public sealed class DtlsHandshakeCookieAndTimerTests
    {
        [Test]
        public void RetransmissionTimerDoublesUntilMaximumAndResets()
        {
            var timer = new DtlsRetransmissionTimer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));

            Assert.Multiple(() =>
            {
                Assert.That(timer.NextTimeout(), Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(timer.NextTimeout(), Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(timer.NextTimeout(), Is.EqualTo(TimeSpan.FromSeconds(4)));
                Assert.That(timer.NextTimeout(), Is.EqualTo(TimeSpan.FromSeconds(4)));
            });

            timer.Reset();
            Assert.That(timer.NextTimeout(), Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void HelloRetryCookieValidatesOnlyForSameEndpointAndClientHello()
        {
            byte[] key = [1, 2, 3, 4, 5];
            byte[] clientHello = [0x01, 0x02, 0x03];
            var endpoint = new IPEndPoint(IPAddress.Loopback, 4843);
            using var protector = new DtlsHelloRetryCookieProtector(key);

            byte[] cookie = protector.CreateCookie(endpoint, clientHello);

            Assert.Multiple(() =>
            {
                Assert.That(protector.ValidateCookie(endpoint, clientHello, cookie), Is.True);
                Assert.That(protector.ValidateCookie(new IPEndPoint(IPAddress.Loopback, 4844), clientHello, cookie), Is.False);
                Assert.That(protector.ValidateCookie(endpoint, [0xff], cookie), Is.False);
            });
        }
    }
}
