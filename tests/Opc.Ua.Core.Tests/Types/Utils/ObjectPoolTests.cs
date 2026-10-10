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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Tests for the bounded lock-free <see cref="ObjectPool{T}"/>.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [Parallelizable]
    public class ObjectPoolTests
    {
        /// <summary>
        /// A pool must be able to retain at least one object.
        /// </summary>
        [Test]
        public void NonPositiveCapacityIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ObjectPool<object>(() => new object(), 0));
        }

        /// <summary>
        /// A returned object is handed out again before a new one is created.
        /// </summary>
        [Test]
        public void ReturnedObjectIsReused()
        {
            int created = 0;
            var pool = new ObjectPool<object>(() => { created++; return new object(); }, 4);

            object first = pool.Get();
            pool.Return(first);
            object second = pool.Get();

            Assert.That(second, Is.SameAs(first));
            Assert.That(created, Is.EqualTo(1));
        }

        /// <summary>
        /// The pool retains at most its capacity; objects returned beyond it are dropped.
        /// </summary>
        [Test]
        public void RetainsAtMostTheCapacity()
        {
            int capacity = Math.Min(3, Environment.ProcessorCount * 4);
            var pool = new ObjectPool<object>(() => new object(), capacity);
            var rented = new List<object>();
            for (int ii = 0; ii < capacity + 5; ii++)
            {
                rented.Add(pool.Get());
            }
            foreach (object item in rented)
            {
                pool.Return(item);
            }

            var reused = new HashSet<object>();
            for (int ii = 0; ii < capacity + 5; ii++)
            {
                object item = pool.Get();
                if (rented.Contains(item))
                {
                    reused.Add(item);
                }
            }

            Assert.That(reused, Has.Count.EqualTo(capacity));
        }

        /// <summary>
        /// The capacity is capped at four objects per processor.
        /// </summary>
        [Test]
        public void CapacityIsCappedByProcessorCount()
        {
            int cap = Environment.ProcessorCount * 4;
            var pool = new ObjectPool<object>(() => new object(), cap * 10);
            var rented = new List<object>();
            for (int ii = 0; ii < cap * 2; ii++)
            {
                rented.Add(pool.Get());
            }
            foreach (object item in rented)
            {
                pool.Return(item);
            }

            int reused = 0;
            for (int ii = 0; ii < cap * 2; ii++)
            {
                if (rented.Contains(pool.Get()))
                {
                    reused++;
                }
            }

            Assert.That(reused, Is.EqualTo(cap));
        }

        /// <summary>
        /// Under concurrent Get and Return an object is never held by two renters at once.
        /// </summary>
        [Test]
        public void ConcurrentRentalsNeverShareAnObject()
        {
            var pool = new ObjectPool<Rental>(() => new Rental(), 64);
            int duplicates = 0;

            Parallel.For(0, Environment.ProcessorCount * 2, _ =>
            {
                for (int ii = 0; ii < 20000; ii++)
                {
                    Rental rental = pool.Get();
                    if (Interlocked.Exchange(ref rental.InUse, 1) != 0)
                    {
                        Interlocked.Increment(ref duplicates);
                    }
                    Thread.SpinWait(ii % 8);
                    Volatile.Write(ref rental.InUse, 0);
                    pool.Return(rental);
                }
            });

            Assert.That(duplicates, Is.Zero);
        }

        /// <summary>
        /// Concurrent use does not lose capacity: once quiet, the pool still serves its retained
        /// objects without creating new ones.
        /// </summary>
        [Test]
        public void ConcurrentUseDoesNotLoseCapacity()
        {
            int created = 0;
            var pool = new ObjectPool<object>(
                () =>
                {
                    Interlocked.Increment(ref created);
                    return new object();
                },
                Environment.ProcessorCount * 4);
            var seen = new ConcurrentDictionary<object, bool>();

            Parallel.For(0, Environment.ProcessorCount, _ =>
            {
                for (int ii = 0; ii < 10000; ii++)
                {
                    object item = pool.Get();
                    seen.TryAdd(item, true);
                    pool.Return(item);
                }
            });

            int createdAfterLoad = Volatile.Read(ref created);
            var rented = new List<object>();
            for (int ii = 0; ii < Math.Min(createdAfterLoad, Environment.ProcessorCount * 4); ii++)
            {
                rented.Add(pool.Get());
            }

            Assert.That(Volatile.Read(ref created), Is.EqualTo(createdAfterLoad));
            Assert.That(rented, Is.All.Matches<object>(seen.ContainsKey));
        }

        private sealed class Rental
        {
            public int InUse;
        }
    }
}
