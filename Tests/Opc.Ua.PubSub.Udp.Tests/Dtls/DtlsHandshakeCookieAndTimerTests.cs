/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
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
                Assert.That(protector.ValidateCookie(endpoint, new byte[] { 0xff }, cookie), Is.False);
            });
        }
    }
}
