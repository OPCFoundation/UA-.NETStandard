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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    [TestFixture]
    [Category("Client")]
    public sealed class RetryAfterHintTests
    {
        [Test]
        public void ParseServerRetryAfterReadsToken()
        {
            Assert.That(
                RetryAfterHint.ParseServerRetryAfter("RetryAfterMs=2000"),
                Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void ParseServerRetryAfterReadsEmbeddedToken()
        {
            Assert.That(
                RetryAfterHint.ParseServerRetryAfter(
                    "The server is too busy to accept a connection. RetryAfterMs=1500."),
                Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
        }

        [Test]
        public void ParseServerRetryAfterReturnsNullWhenAbsent()
        {
            Assert.That(RetryAfterHint.ParseServerRetryAfter(null), Is.Null);
            Assert.That(RetryAfterHint.ParseServerRetryAfter(string.Empty), Is.Null);
            Assert.That(RetryAfterHint.ParseServerRetryAfter("no hint here"), Is.Null);
        }

        [Test]
        public void ParseServerRetryAfterReturnsNullForZeroOrMissingDigits()
        {
            Assert.That(RetryAfterHint.ParseServerRetryAfter("RetryAfterMs=0"), Is.Null);
            Assert.That(RetryAfterHint.ParseServerRetryAfter("RetryAfterMs=."), Is.Null);
        }

        [Test]
        public void ParseServerRetryAfterCapsPathologicalHint()
        {
            Assert.That(
                RetryAfterHint.ParseServerRetryAfter("RetryAfterMs=999999999999"),
                Is.EqualTo(RetryAfterHint.MaxRetryAfter));
        }

        [Test]
        public void ParseServerBusyRetryAfterReadsUaTcpErrorReason()
        {
            const string reason = "Server overloaded. RetryAfterMs=2500.";
            var result = new ServiceResult(
                null,
                StatusCodes.BadTcpServerTooBusy,
                LocalizedText.From($"Error received from remote host: {reason}"),
                reason);

            Assert.That(
                RetryAfterHint.ParseServerBusyRetryAfter(result),
                Is.EqualTo(TimeSpan.FromMilliseconds(2500)));
        }

        [Test]
        public void ParseServerBusyRetryAfterReadsBadServerTooBusy()
        {
            var result = new ServiceResult(
                null,
                StatusCodes.BadServerTooBusy,
                LocalizedText.From("RetryAfterMs=3000"),
                default(string));

            Assert.That(
                RetryAfterHint.ParseServerBusyRetryAfter(result),
                Is.EqualTo(TimeSpan.FromSeconds(3)));
        }

        [Test]
        public void ParseServerBusyRetryAfterIgnoresNonBusyStatus()
        {
            var result = new ServiceResult(
                null,
                StatusCodes.BadTcpInternalError,
                LocalizedText.From("RetryAfterMs=3000"),
                "RetryAfterMs=3000");

            Assert.That(RetryAfterHint.ParseServerBusyRetryAfter(result), Is.Null);
        }

        [Test]
        public void ParseServerBusyRetryAfterReadsNestedUaTcpErrorReason()
        {
            const string reason = "Server overloaded. RetryAfterMs=4000.";
            var inner = new ServiceResult(
                null,
                StatusCodes.BadTcpServerTooBusy,
                LocalizedText.From($"Error received from remote host: {reason}"),
                reason);
            var result = new ServiceResult(StatusCodes.BadSecureChannelClosed, inner);

            Assert.That(
                RetryAfterHint.ParseServerBusyRetryAfter(result),
                Is.EqualTo(TimeSpan.FromSeconds(4)));
        }

        [Test]
        public void ApplyReconnectDelayLowerBoundHonorsHint()
        {
            var policy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromSeconds(30)
            };

            TimeSpan delay = RetryAfterHint.ApplyReconnectDelayLowerBound(
                policy,
                policy.GetDelay(0),
                TimeSpan.FromSeconds(2));

            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void ApplyReconnectDelayLowerBoundClampsHintToPolicyMaximum()
        {
            var policy = new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromSeconds(5)
            };

            TimeSpan delay = RetryAfterHint.ApplyReconnectDelayLowerBound(
                policy,
                policy.GetDelay(0),
                TimeSpan.FromSeconds(30));

            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(5)));
        }
    }
}
