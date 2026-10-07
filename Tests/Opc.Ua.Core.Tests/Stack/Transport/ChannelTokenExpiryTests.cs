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

using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests the expiry rules of a <see cref="ChannelToken"/> (OPC 10000-4 5.6.2.1).
    /// </summary>
    [TestFixture]
    [Category("Transport")]
    [Parallelizable]
    public class ChannelTokenExpiryTests
    {
        private const int kLifetime = 100_000;

        [Test]
        public void TokenWithinItsLifetimeIsNotExpired()
        {
            using ChannelToken token = CreateToken(elapsed: kLifetime / 2);

            Assert.That(token.Expired, Is.False);
            Assert.That(token.IsExpired(0), Is.False);
            Assert.That(token.IsExpired(TcpMessageLimits.TokenExpiryGracePeriod), Is.False);
        }

        [Test]
        public void ExpiredTokenIsAcceptedWithinTheGracePeriod()
        {
            using ChannelToken token = CreateToken(elapsed: kLifetime * 120 / 100);

            Assert.That(token.Expired, Is.True);
            Assert.That(token.IsExpired(0), Is.True);
            Assert.That(token.IsExpired(TcpMessageLimits.TokenExpiryGracePeriod), Is.False);
        }

        [Test]
        public void ExpiredTokenIsRejectedAfterTheGracePeriod()
        {
            using ChannelToken token = CreateToken(elapsed: kLifetime * 130 / 100);

            Assert.That(token.IsExpired(TcpMessageLimits.TokenExpiryGracePeriod), Is.True);
        }

        [Test]
        public void ActivationIsRequiredBeforeTheTokenExpires()
        {
            using ChannelToken token = CreateToken(elapsed: kLifetime * 96 / 100);

            Assert.That(token.ActivationRequired, Is.True);
            Assert.That(token.Expired, Is.False);
        }

        private static ChannelToken CreateToken(int elapsed)
        {
            return new ChannelToken
            {
                Lifetime = kLifetime,
                CreatedAtTickCount = HiResClock.TickCount - elapsed
            };
        }
    }
}
