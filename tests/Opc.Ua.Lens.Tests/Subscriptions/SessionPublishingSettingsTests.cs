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
using Opc.Ua.Tests;
using UaLens.Subscriptions;

namespace UaLens.Tests.Subscriptions
{
    [TestFixture]
    public sealed class SessionPublishingSettingsTests
    {
        [TestCase(8, 30, "max,min")]
        [TestCase(1, 4, "min,max")]
        [TestCase(101, 101, "max,min")]
        public void ApplyingLimitsNeverCreatesAnInvalidIntermediateRange(int minimum, int maximum, string order)
        {
            int currentMinimum = 2;
            int currentMaximum = 15;
            var writes = new List<string>();
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MinPublishRequestCount).Returns(() => currentMinimum);
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

        /// <summary>
        /// Engine-specific rejection must not retain the first successful property write.
        /// </summary>
        [TestCase(101, 101)]
        [TestCase(1, ushort.MaxValue + 1)]
        public void ClassicRejectionRestoresThePreviousLimits(int minimum, int maximum)
        {
            using ClassicSubscriptionEngine engine = CreateClassicEngine();
            engine.MinPublishRequestCount = 2;
            engine.MaxPublishRequestCount = 15;
            Mock<ISession> session = CreateSession(engine);

            Assert.That(() => new SessionPublishingSettings(minimum, maximum).Apply(session.Object),
                Throws.InstanceOf<ArgumentOutOfRangeException>());

            Assert.That(engine.MinPublishRequestCount, Is.EqualTo(2));
            Assert.That(engine.MaxPublishRequestCount, Is.EqualTo(15));
        }

        /// <summary>
        /// The complete supported Classic range remains usable.
        /// </summary>
        [TestCase(1, 1)]
        [TestCase(100, ushort.MaxValue)]
        public void SupportedClassicBoundariesAreApplied(int minimum, int maximum)
        {
            using ClassicSubscriptionEngine engine = CreateClassicEngine();
            Mock<ISession> session = CreateSession(engine);

            new SessionPublishingSettings(minimum, maximum).Apply(session.Object);

            Assert.That(engine.MinPublishRequestCount, Is.EqualTo(minimum));
            Assert.That(engine.MaxPublishRequestCount, Is.EqualTo(maximum));
        }

        /// <summary>
        /// Either setter order restores the old pair while preserving the original failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RejectedSecondWriteRestoresBothLimits(bool maximumFirst)
        {
            int minimum = maximumFirst ? 8 : 1;
            int maximum = maximumFirst ? 30 : 4;
            int currentMinimum = 2;
            int currentMaximum = 15;
            var failure = new ArgumentOutOfRangeException(maximumFirst ? "minimum" : "maximum");
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MinPublishRequestCount).Returns(() => currentMinimum);
            session.SetupGet(value => value.MaxPublishRequestCount).Returns(() => currentMaximum);
            session.SetupSet(value => value.MinPublishRequestCount = It.IsAny<int>()).Callback<int>(value =>
            {
                if (maximumFirst && value == minimum)
                {
                    throw failure;
                }
                Assert.That(value, Is.InRange(1, currentMaximum));
                currentMinimum = value;
            });
            session.SetupSet(value => value.MaxPublishRequestCount = It.IsAny<int>()).Callback<int>(value =>
            {
                if (!maximumFirst && value == maximum)
                {
                    throw failure;
                }
                Assert.That(value, Is.GreaterThanOrEqualTo(currentMinimum));
                currentMaximum = value;
            });

            ArgumentOutOfRangeException? observed = Assert.Throws<ArgumentOutOfRangeException>(
                () => new SessionPublishingSettings(minimum, maximum).Apply(session.Object));

            Assert.That(observed, Is.SameAs(failure));
            Assert.That(currentMinimum, Is.EqualTo(2));
            Assert.That(currentMaximum, Is.EqualTo(15));
        }

        /// <summary>
        /// Failed restoration is explicit and retains both exceptions for the existing UI error logger.
        /// </summary>
        [Test]
        public void RollbackFailureReportsBothErrors()
        {
            var applyError = new ArgumentOutOfRangeException("minimum");
            var rollbackError = new InvalidOperationException("The session no longer accepts changes.");
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MinPublishRequestCount).Returns(2);
            session.SetupProperty(value => value.MaxPublishRequestCount, 15);
            session.SetupSet(value => value.MinPublishRequestCount = It.IsAny<int>()).Callback<int>(value =>
            {
                if (value == 8)
                {
                    throw applyError;
                }
                throw rollbackError;
            });

            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                () => new SessionPublishingSettings(8, 30).Apply(session.Object));

            Assert.That(failure!.Message, Does.Contain("could not be restored"));
            Assert.That(failure.InnerException, Is.TypeOf<AggregateException>());
            var errors = (AggregateException)failure.InnerException!;
            Assert.That(errors.InnerExceptions, Is.EqualTo(new Exception[] { applyError, rollbackError }));
        }

        /// <summary>
        /// Creates the real Classic validation behavior without starting a publish loop.
        /// </summary>
        private static ClassicSubscriptionEngine CreateClassicEngine()
        {
            var context = new Mock<ISubscriptionEngineContext>();
            context.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            return new ClassicSubscriptionEngine(context.Object);
        }

        /// <summary>
        /// Exposes the engine's actual limits through the same session boundary used by UaLens.
        /// </summary>
        private static Mock<ISession> CreateSession(ClassicSubscriptionEngine engine)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MinPublishRequestCount).Returns(() => engine.MinPublishRequestCount);
            session.SetupGet(value => value.MaxPublishRequestCount).Returns(() => engine.MaxPublishRequestCount);
            session.SetupSet(value => value.MinPublishRequestCount = It.IsAny<int>())
                .Callback<int>(value => engine.MinPublishRequestCount = value);
            session.SetupSet(value => value.MaxPublishRequestCount = It.IsAny<int>())
                .Callback<int>(value => engine.MaxPublishRequestCount = value);
            return session;
        }
    }
}
