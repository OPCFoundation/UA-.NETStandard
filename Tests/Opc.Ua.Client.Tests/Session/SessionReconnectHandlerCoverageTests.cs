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
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Additional deterministic coverage for <see cref="SessionReconnectHandler"/> that focuses
    /// on the synchronous surface: constants, initial state, reconnect-period arithmetic,
    /// cancellation, disposal and argument-guard branches. These tests never start the reconnect
    /// timer loop and never touch a live session or the network.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SessionReconnectHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionReconnectHandlerCoverageTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ReconnectPeriodConstantsHaveExpectedValues()
        {
            Assert.That(SessionReconnectHandler.MinReconnectPeriod, Is.EqualTo(500));
            Assert.That(SessionReconnectHandler.MaxReconnectPeriod, Is.EqualTo(30000));
            Assert.That(SessionReconnectHandler.DefaultReconnectPeriod, Is.EqualTo(1000));
            Assert.That(SessionReconnectHandler.MinReconnectOperationTimeout, Is.EqualTo(5000));
        }

        [Test]
        public void NewHandlerStartsInReadyState()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Ready));
        }

        [Test]
        public void NewHandlerHasNoSession()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.Session, Is.Null);
        }

        [Test]
        public void ConstructorWithTimeProviderStartsReady()
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: true,
                maxReconnectPeriod: 10000,
                timeProvider: TimeProvider.System);

            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Ready));
        }

        [TestCase(0, 500)]
        [TestCase(100, 500)]
        [TestCase(500, 500)]
        [TestCase(1000, 1000)]
        [TestCase(30000, 30000)]
        [TestCase(60000, 60000)]
        public void CheckedReconnectPeriodWithoutMaxClampsToMinimum(int input, int expected)
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.CheckedReconnectPeriod(input), Is.EqualTo(expected));
        }

        [TestCase(100, 500)]
        [TestCase(1000, 1000)]
        [TestCase(4000, 4000)]
        public void CheckedReconnectPeriodWithoutMaxIgnoresExponentialBackoff(int input, int expected)
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(
                handler.CheckedReconnectPeriod(input, exponentialBackoff: true),
                Is.EqualTo(expected));
        }

        [TestCase(100, 500)]
        [TestCase(500, 500)]
        [TestCase(5000, 5000)]
        [TestCase(10000, 10000)]
        [TestCase(20000, 10000)]
        public void CheckedReconnectPeriodWithMaxClampsWithinBounds(int input, int expected)
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 10000);

            Assert.That(handler.CheckedReconnectPeriod(input), Is.EqualTo(expected));
        }

        [TestCase(100, 500)]
        [TestCase(1000, 2000)]
        [TestCase(5000, 10000)]
        [TestCase(8000, 10000)]
        public void CheckedReconnectPeriodWithMaxAppliesExponentialBackoff(int input, int expected)
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 10000);

            Assert.That(
                handler.CheckedReconnectPeriod(input, exponentialBackoff: true),
                Is.EqualTo(expected));
        }

        [Test]
        public void CheckedReconnectPeriodClampsToGlobalMaximum()
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 50000);

            Assert.That(handler.CheckedReconnectPeriod(100000), Is.EqualTo(30000));
            Assert.That(handler.CheckedReconnectPeriod(1000), Is.EqualTo(1000));
        }

        [Test]
        public void CheckedReconnectPeriodWithMaxBelowMinimumDisablesBackoff()
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 100);

            Assert.That(handler.CheckedReconnectPeriod(200), Is.EqualTo(500));
            Assert.That(
                handler.CheckedReconnectPeriod(2000, exponentialBackoff: true),
                Is.EqualTo(2000));
        }

        [Test]
        public void JitteredReconnectPeriodStaysWithinTenPercent()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            for (int i = 0; i < 200; i++)
            {
                int jittered = handler.JitteredReconnectPeriod(10000);

                Assert.That(jittered, Is.InRange(9000, 11000));
            }
        }

        [Test]
        public void CancelReconnectWhenIdleDoesNotThrowAndStaysReady()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(() => handler.CancelReconnect(), Throws.Nothing);
            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Ready));
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            Assert.That(() => handler.Dispose(), Throws.Nothing);
            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Disposed));
        }

        [Test]
        public void CancelReconnectAfterDisposeDoesNotThrow()
        {
            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            Assert.That(() => handler.CancelReconnect(), Throws.Nothing);
            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Disposed));
        }

        [Test]
        public void BeginReconnectAfterDisposeThrowsServiceResultException()
        {
            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                handler.BeginReconnect(null, 1000, (_, _) => { }));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void BeginReconnectWithNonSessionAfterDisposeThrowsNotSupportedException()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.SessionId).Returns(NodeId.Null);

            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            Assert.That(
                () => handler.BeginReconnect(session.Object, 1000, (_, _) => { }),
                Throws.TypeOf<NotSupportedException>());
        }
    }
}
