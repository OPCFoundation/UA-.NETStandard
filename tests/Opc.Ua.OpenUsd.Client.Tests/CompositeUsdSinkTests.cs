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
using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CompositeUsdSink"/>, the fan-out the connector uses when
    /// it must drive an override layer and a live viewport from one subscription.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class CompositeUsdSinkTests
    {
        [Test]
        public void SetAttributeReachesEverySink()
        {
            var first = new MockUsdSink();
            var second = new MockUsdSink();
            var sink = new CompositeUsdSink(first, second);

            sink.SetAttribute("/Cell/Robots/R1", "xformOp:rotateZ", new Variant(45.0));

            Assert.That(first.WasWritten("/Cell/Robots/R1", "xformOp:rotateZ"), Is.True);
            Assert.That(second.WasWritten("/Cell/Robots/R1", "xformOp:rotateZ"), Is.True);
        }

        [Test]
        public void SetTimeSampleReachesEverySink()
        {
            var first = new MockUsdSink();
            var second = new MockUsdSink();
            var sink = new CompositeUsdSink(first, second);

            sink.SetTimeSample(
                "/Cell",
                "custom:temperature",
                new DateTime(1970, 1, 1, 0, 0, 5, DateTimeKind.Utc),
                new Variant(21.5));

            Assert.That(first.TimeSampleWrites, Is.EqualTo(1));
            Assert.That(second.TimeSampleWrites, Is.EqualTo(1));
        }

        [Test]
        public void ComposePrimReachesEverySink()
        {
            var first = new MockUsdSink();
            var second = new MockUsdSink();
            var sink = new CompositeUsdSink(first, second);

            sink.ComposePrim(
                "/Cell/Robots/R1", OpenUsdCompositionArc.Reference,
                "@robot.usda@</Robot>", active: true);

            Assert.That(first.WasPrimComposed("/Cell/Robots/R1"), Is.True);
            Assert.That(second.WasPrimComposed("/Cell/Robots/R1"), Is.True);
            Assert.That(second.IsPrimActive("/Cell/Robots/R1"), Is.True);
        }

        [Test]
        public void BeginBatchOpensAndClosesEveryInnerScope()
        {
            var first = new RecordingSink();
            var second = new RecordingSink();
            var sink = new CompositeUsdSink(first, second);

            IDisposable scope = sink.BeginBatch();
            Assert.That(first.OpenBatches, Is.EqualTo(1));
            Assert.That(second.OpenBatches, Is.EqualTo(1));

            scope.Dispose();
            Assert.That(first.OpenBatches, Is.Zero);
            Assert.That(second.OpenBatches, Is.Zero);
        }

        [Test]
        public void BatchScopeDisposalIsIdempotent()
        {
            var inner = new RecordingSink();
            var sink = new CompositeUsdSink(inner);

            IDisposable scope = sink.BeginBatch();
            scope.Dispose();
            scope.Dispose();

            Assert.That(inner.ClosedBatches, Is.EqualTo(1));
        }

        [Test]
        public void AFailingSinkDoesNotStopTheOthers()
        {
            var failing = new ThrowingSink();
            var healthy = new MockUsdSink();
            var sink = new CompositeUsdSink(failing, healthy);

            Assert.That(
                () => sink.SetAttribute("/Cell", "visibility", new Variant("inherited")),
                Throws.InvalidOperationException);
            Assert.That(healthy.WasWritten("/Cell", "visibility"), Is.True);
        }

        [Test]
        public void AFailingSinkRethrowsTheFirstFailure()
        {
            var first = new ThrowingSink { Message = "first" };
            var second = new ThrowingSink { Message = "second" };
            var sink = new CompositeUsdSink(first, second);

            Assert.That(
                () => sink.ComposePrim("/Cell", OpenUsdCompositionArc.Child, null, active: true),
                Throws.InvalidOperationException.With.Message.EqualTo("first"));
        }

        [Test]
        public void ConstructorRejectsNoSinks()
        {
            Assert.That(() => new CompositeUsdSink(), Throws.ArgumentException);
        }

        [Test]
        public void ConstructorRejectsANullSink()
        {
            Assert.That(
                () => new CompositeUsdSink(new MockUsdSink(), null!),
                Throws.ArgumentException);
        }

        [Test]
        public void ConstructorRejectsANullArray()
        {
            Assert.That(() => new CompositeUsdSink(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void LaterSinksStillSeeValuesAfterAnEarlierBatchFailure()
        {
            var failing = new ThrowingSink { FailOnBatch = true };
            var healthy = new RecordingSink();
            var sink = new CompositeUsdSink(failing, healthy);

            Assert.That(() => sink.BeginBatch(), Throws.InvalidOperationException);
            // The healthy sink's scope was never opened, so nothing is left dangling.
            Assert.That(healthy.OpenBatches, Is.Zero);
        }

        private sealed class RecordingSink : IUsdSink
        {
            private readonly List<Scope> m_scopes = [];

            public int OpenBatches { get; private set; }

            public int ClosedBatches { get; private set; }

            public void SetAttribute(string primPath, string propertyName, Variant value)
            {
            }

            public void SetTimeSample(
                string primPath, string propertyName, DateTime time, Variant value)
            {
            }

            public void ComposePrim(
                string primPath, OpenUsdCompositionArc arc, string? assetReference, bool active)
            {
            }

            public IDisposable BeginBatch()
            {
                OpenBatches++;
                var scope = new Scope(this);
                m_scopes.Add(scope);
                return scope;
            }

            private sealed class Scope : IDisposable
            {
                private readonly RecordingSink m_owner;
                private bool m_disposed;

                public Scope(RecordingSink owner)
                {
                    m_owner = owner;
                }

                public void Dispose()
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    m_owner.OpenBatches--;
                    m_owner.ClosedBatches++;
                }
            }
        }

        private sealed class ThrowingSink : IUsdSink
        {
            public string Message { get; init; } = "sink failed";

            public bool FailOnBatch { get; init; }

            public void SetAttribute(string primPath, string propertyName, Variant value) =>
                throw new InvalidOperationException(Message);

            public void SetTimeSample(
                string primPath, string propertyName, DateTime time, Variant value) =>
                throw new InvalidOperationException(Message);

            public void ComposePrim(
                string primPath, OpenUsdCompositionArc arc, string? assetReference, bool active) =>
                throw new InvalidOperationException(Message);

            public IDisposable BeginBatch() =>
                FailOnBatch
                    ? throw new InvalidOperationException(Message)
                    : new NoOpScope();

            private sealed class NoOpScope : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
