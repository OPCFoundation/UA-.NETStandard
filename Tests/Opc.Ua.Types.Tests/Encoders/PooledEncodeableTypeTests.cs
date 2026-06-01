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
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;

#nullable enable

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Tests for <see cref="PooledEncodeableType{T}"/> and the
    /// <see cref="IPooledEncodeable"/> reuse contract.
    /// </summary>
    [TestFixture]
    [Category("PooledEncodeableType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.Fixtures)]
    public sealed class PooledEncodeableTypeTests
    {
        [Test]
        public void RentReturnRentReusesTheSameInstance()
        {
            var activator = new TestPooledActivator();
            var rentedFirst = (TestPooled)activator.CreateInstance();
            rentedFirst.Reuse();
            var rentedSecond = (TestPooled)activator.CreateInstance();
            Assert.That(rentedSecond, Is.SameAs(rentedFirst));
        }

        [Test]
        public void DoubleReuseIsIdempotentAndDoesNotDoubleReturn()
        {
            var activator = new TestPooledActivator();
            var rented = (TestPooled)activator.CreateInstance();
            rented.Reuse();
            rented.Reuse();
            // After two reuses the pool should contain exactly one instance.
            var nextRent = (TestPooled)activator.CreateInstance();
            Assert.That(nextRent, Is.SameAs(rented));
            // The second rent in succession should be a fresh instance because
            // the pool already handed `rented` back out.
            var freshAfterPool = (TestPooled)activator.CreateInstance();
            Assert.That(freshAfterPool, Is.Not.SameAs(rented));
        }

        [Test]
        public void RentClearsTheSentinelSoSubsequentReuseDoesRealWork()
        {
            var activator = new TestPooledActivator();
            var rented = (TestPooled)activator.CreateInstance();
            rented.Touch();
            rented.Reuse();
            // Re-rent should clear the sentinel; touch again so we can verify
            // that the next Reuse runs (resets and returns).
            var rerented = (TestPooled)activator.CreateInstance();
            Assert.That(rerented, Is.SameAs(rented));
            rerented.Touch();
            Assert.That(rerented.IsTouched, Is.True);
            rerented.Reuse();
            // After Reuse, fields must be reset.
            Assert.That(rerented.IsTouched, Is.False);
        }

        [Test]
        public void ReturnsBeyondMaximumRetainedAreDroppedToGc()
        {
            const int cap = 4;
            var activator = new TestPooledActivator(cap);
            var rented = new TestPooled[cap + 4];
            for (int i = 0; i < rented.Length; i++)
            {
                rented[i] = (TestPooled)activator.CreateInstance();
            }
            for (int i = 0; i < rented.Length; i++)
            {
                rented[i].Reuse();
            }
            // The pool keeps exactly `cap` instances (one fast slot plus
            // cap-1 shared). Renting `cap` times should produce pool-stored
            // originals; the (cap+1)th rent falls through to `new T()`.
            int matched = 0;
            for (int i = 0; i < cap + 1; i++)
            {
                var instance = (TestPooled)activator.CreateInstance();
                for (int j = 0; j < rented.Length; j++)
                {
                    if (ReferenceEquals(instance, rented[j]))
                    {
                        matched++;
                        break;
                    }
                }
            }
            Assert.That(matched, Is.EqualTo(cap),
                "exactly `cap` instances should have been retained and re-rented");
        }

        [Test]
        public void ConcurrentRentReturnIsSafe()
        {
            var activator = new TestPooledActivator(maximumRetained: 64);
            const int iterations = 5000;
            const int threads = 8;
            var seen = new ConcurrentBag<TestPooled>();

            Parallel.For(0, threads, _ =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var instance = (TestPooled)activator.CreateInstance();
                    seen.Add(instance);
                    instance.Touch();
                    instance.Reuse();
                }
            });

            // Every observed instance must be in a clean state after Reuse.
            // Concurrent rent/return must not have left any instance in a
            // partially-reset state, and must not throw.
            foreach (TestPooled instance in seen)
            {
                Assert.That(instance.IsTouched, Is.False,
                    "Reuse() must reset the touch flag");
            }
        }

        [Test]
        public void CreateInstanceReturnsFreshInstanceWhenPoolIsEmpty()
        {
            var activator = new TestPooledActivator();
            var a = (TestPooled)activator.CreateInstance();
            var b = (TestPooled)activator.CreateInstance();
            Assert.That(a, Is.Not.SameAs(b));
        }

        [Test]
        public void ReuseResetsFieldsBeforeReturningToPool()
        {
            var activator = new TestPooledActivator();
            var rented = (TestPooled)activator.CreateInstance();
            rented.Touch();
            Assert.That(rented.IsTouched, Is.True);
            rented.Reuse();
            Assert.That(rented.IsTouched, Is.False);
        }

        [Test]
        public void MaximumRetainedReportsConfiguredValue()
        {
            var activator = new TestPooledActivator(maximumRetained: 7);
            Assert.That(activator.MaximumRetained, Is.EqualTo(7));
        }

        [Test]
        public void PooledActivatorConstructorRejectsNonPositiveBound()
        {
            Assert.That(() => new TestPooledActivator(maximumRetained: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new TestPooledActivator(maximumRetained: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated via PooledEncodeableType<T>.CreateInstance through the `new()` constraint.")]
        private sealed class TestPooled : IPooledEncodeable
        {
            private int m_pooledSentinel;
            private bool m_touched;
            private TestPooledActivator? m_activator;

            public ExpandedNodeId TypeId => ExpandedNodeId.Null;
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public bool IsTouched => m_touched;
            public void Touch()
            {
                m_touched = true;
            }

            public void Encode(IEncoder encoder)
            {
            }
            public void Decode(IDecoder decoder)
            {
            }
            public bool IsEqual(IEncodeable? encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return MemberwiseClone();
            }

            public void Reuse()
            {
                if (Interlocked.CompareExchange(ref m_pooledSentinel, 1, 0) != 0)
                {
                    return;
                }
                m_touched = false;
                m_activator?.Return(this);
            }

            internal void OnRent(TestPooledActivator activator)
            {
                m_activator = activator;
                Volatile.Write(ref m_pooledSentinel, 0);
            }
        }

        private sealed class TestPooledActivator : PooledEncodeableType<TestPooled>
        {
            public TestPooledActivator()
            {
            }

            public TestPooledActivator(int maximumRetained)
                : base(maximumRetained)
            {
            }

            public override XmlQualifiedName XmlName { get; }
                = new XmlQualifiedName("TestPooled", "urn:test");

            protected override void InitializeRent(TestPooled instance)
            {
                instance.OnRent(this);
            }
        }
    }
}
