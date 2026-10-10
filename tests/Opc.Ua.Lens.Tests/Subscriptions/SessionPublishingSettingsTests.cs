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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using UaLens.Subscriptions;

namespace UaLens.Tests.Subscriptions
{
    [TestFixture]
    public sealed class SessionPublishingSettingsTests
    {
        [TestCase(8, 30, "max,min")]
        [TestCase(1, 4, "min,max")]
        public void ApplyingLimitsNeverCreatesAnInvalidIntermediateRange(int minimum, int maximum, string order)
        {
            int currentMinimum = 2;
            int currentMaximum = 15;
            var writes = new List<string>();
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MaxPublishRequestCount).Returns(() => currentMaximum);
            session.SetupSet(value => value.MinPublishRequestCount = It.IsAny<int>()).Callback<int>(value =>
            {
                Assert.That(value, Is.InRange(1, currentMaximum));
                currentMinimum = value;
                writes.Add("min");
            });
            session.SetupSet(value => value.MaxPublishRequestCount = It.IsAny<int>()).Callback<int>(value =>
            {
                Assert.That(value, Is.GreaterThanOrEqualTo(currentMinimum));
                currentMaximum = value;
                writes.Add("max");
            });

            new SessionPublishingSettings(minimum, maximum).Apply(session.Object);

            Assert.That(currentMinimum, Is.EqualTo(minimum));
            Assert.That(currentMaximum, Is.EqualTo(maximum));
            Assert.That(string.Join(",", writes), Is.EqualTo(order));
        }

        [TestCase(0, 10)]
        [TestCase(5, 4)]
        [TestCase(-1, 3)]
        public void InvalidLimitsDoNotTouchTheSession(int minimum, int maximum)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);

            Assert.That(() => new SessionPublishingSettings(minimum, maximum).Apply(session.Object),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            session.VerifyNoOtherCalls();
        }
    }
}
