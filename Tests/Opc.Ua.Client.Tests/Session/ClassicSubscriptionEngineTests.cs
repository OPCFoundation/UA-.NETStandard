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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    [Category("ClassicSubscriptionEngine")]
    public class ClassicSubscriptionEngineTests
    {
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionEngineContext> m_mockContext;
        private SemaphoreSlim m_lock;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_lock = new SemaphoreSlim(1, 1);
            m_mockContext = new Mock<ISubscriptionEngineContext>();
            m_mockContext.Setup(c => c.SessionId)
                .Returns(new NodeId(1));
            m_mockContext.Setup(c => c.Disposed)
                .Returns(false);
            m_mockContext.Setup(c => c.Reconnecting)
                .Returns(false);
            m_mockContext.Setup(c => c.Connected)
                .Returns(false);
            m_mockContext.Setup(c => c.Closing)
                .Returns(false);
            m_mockContext.Setup(c => c.Telemetry)
                .Returns(m_telemetry);
            m_mockContext.Setup(c => c.Subscriptions)
                .Returns(new List<Subscription>());
            m_mockContext.Setup(c => c.OperationTimeout)
                .Returns(60000);
            m_mockContext.Setup(c => c.ReturnDiagnostics)
                .Returns(DiagnosticsMasks.None);
            m_mockContext.Setup(c => c.ReconnectLock)
                .Returns(m_lock);
            m_mockContext.Setup(c => c.GoodPublishRequestCount)
                .Returns(0);
        }

        [TearDown]
        public void TearDown()
        {
            m_lock.Dispose();
        }

        [Test]
        public void FactoryCreateReturnsClassicEngine()
        {
            ISubscriptionEngine engine =
                ClassicSubscriptionEngineFactory.Instance.Create(
                    m_mockContext.Object);

            Assert.That(engine, Is.Not.Null);
            Assert.That(
                engine,
                Is.InstanceOf<ClassicSubscriptionEngine>());

            engine.Dispose();
        }

        [Test]
        public void StartPublishingWithNoSubscriptionsDoesNothing()
        {
            m_mockContext.Setup(c => c.Subscriptions)
                .Returns(new List<Subscription>());

            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.StartPublishing(timeout: 5000, fullQueue: false);

            m_mockContext.Verify(
                c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void NotifySubscriptionsChangedTriggersPublishReEvaluation()
        {
            m_mockContext.Setup(c => c.Subscriptions)
                .Returns(new List<Subscription>());
            m_mockContext.Setup(c => c.Disposed)
                .Returns(false);

            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            // With no subscriptions, NotifySubscriptionsChanged
            // should not trigger a publish request.
            engine.NotifySubscriptionsChanged();

            m_mockContext.Verify(
                c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void MinPublishRequestCountDefaultsCorrectly()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.MinPublishRequestCount, Is.EqualTo(1));
            Assert.That(
                engine.MaxPublishRequestCount,
                Is.EqualTo(ushort.MaxValue));
        }

        [Test]
        public void MinMaxPublishRequestCountCanBeSet()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.MinPublishRequestCount = 5;
            engine.MaxPublishRequestCount = 50;

            Assert.That(
                engine.MinPublishRequestCount, Is.EqualTo(5));
            Assert.That(
                engine.MaxPublishRequestCount, Is.EqualTo(50));
        }

        [Test]
        public void MinPublishRequestCountRejectsInvalidValues()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.MinPublishRequestCount = 0,
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => engine.MinPublishRequestCount = 101,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void MaxPublishRequestCountRejectsInvalidValues()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.MaxPublishRequestCount = 0,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void PauseAndResumePublishing()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(engine.PausePublishing, Throws.Nothing);
            Assert.That(engine.ResumePublishing, Throws.Nothing);
        }

        [Test]
        public async Task StopPublishingAsyncDisposesCleanlyAsync()
        {
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);

            await engine.StopPublishingAsync().ConfigureAwait(false);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);
#pragma warning restore CA2000 // Dispose objects before losing scope

            Assert.That(engine.Dispose, Throws.Nothing);
            Assert.That(engine.Dispose, Throws.Nothing);
        }

        [Test]
        public void ConstructorThrowsOnNullContext()
        {
            Assert.That(
                () => new ClassicSubscriptionEngine(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void GoodPublishRequestCountDelegatesToContext()
        {
            m_mockContext.Setup(c => c.GoodPublishRequestCount)
                .Returns(42);

            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.GoodPublishRequestCount, Is.EqualTo(42));
        }

        [Test]
        public void BadPublishRequestCountIsZero()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.BadPublishRequestCount, Is.Zero);
        }

        [Test]
        public void MaxPublishRequestCountRespectsMinimum()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.MinPublishRequestCount = 10;
            engine.MaxPublishRequestCount = 5;

            // MaxPublishRequestCount getter returns the larger of
            // min and max to ensure min is always respected.
            Assert.That(
                engine.MaxPublishRequestCount,
                Is.GreaterThanOrEqualTo(
                    engine.MinPublishRequestCount));
        }
    }
}
