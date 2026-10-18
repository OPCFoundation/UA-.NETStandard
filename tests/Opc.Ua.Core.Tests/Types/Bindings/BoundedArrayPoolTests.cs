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
 *
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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Bindings
{
    /// <summary>
    /// Tests the bounded transport buffer pool.
    /// </summary>
    [TestFixture]
    [Category("BufferManager")]
    [Parallelizable]
    public sealed class BoundedArrayPoolTests
    {
        [Test]
        public void RentReturnsExactPowerOfTwoBuckets()
        {
            var pool = new BoundedArrayPool(1024, 4);

            Assert.That(pool.Rent(1), Has.Length.EqualTo(16));
            Assert.That(pool.Rent(16), Has.Length.EqualTo(16));
            Assert.That(pool.Rent(17), Has.Length.EqualTo(32));
            Assert.That(pool.Rent(1000), Has.Length.EqualTo(1024));
            Assert.That(pool.Rent(0), Is.Empty);
        }

        [Test]
        public void ReturnedArrayIsReused()
        {
            var pool = new BoundedArrayPool(1024, 4);
            byte[] first = pool.Rent(500);
            pool.Return(first);

            Assert.That(pool.GetRetainedCount(512), Is.EqualTo(1));
            Assert.That(pool.Rent(300), Is.SameAs(first));
            Assert.That(pool.GetRetainedCount(512), Is.Zero);
        }

        /// <summary>
        /// A thousand outstanding buffers come back to the pool, which ArrayPool.Shared cannot
        /// promise on a host with one processor: its per-processor partitions are much smaller.
        /// </summary>
        [Test]
        public void ThousandOutstandingBuffersAreAllRetainedAndReused()
        {
            var pool = new BoundedArrayPool(65536, 1024);
            byte[][] rented = [.. Enumerable.Range(0, 1000).Select(_ => pool.Rent(65535))];
            foreach (byte[] array in rented)
            {
                pool.Return(array);
            }

            Assert.That(pool.GetRetainedCount(65536), Is.EqualTo(1000));
            var reused = new HashSet<byte[]>(rented);
            for (int ii = 0; ii < rented.Length; ii++)
            {
                Assert.That(reused.Contains(pool.Rent(65535)), Is.True);
            }
            Assert.That(pool.GetRetainedCount(65536), Is.Zero);
        }

        [Test]
        public void ReturnBeyondTheBucketBoundDropsTheArray()
        {
            var pool = new BoundedArrayPool(64, 2);
            byte[] a = pool.Rent(64);
            byte[] b = pool.Rent(64);
            byte[] c = pool.Rent(64);
            pool.Return(a);
            pool.Return(b);
            pool.Return(c);

            Assert.That(pool.GetRetainedCount(64), Is.EqualTo(2));
            Assert.That(pool.Rent(64), Is.SameAs(a));
            Assert.That(pool.Rent(64), Is.SameAs(b));
            Assert.That(pool.Rent(64), Is.Not.SameAs(c));
        }

        [Test]
        public async Task ConcurrentReturnsNeverExceedTheBoundAsync()
        {
            const int bound = 8;
            var pool = new BoundedArrayPool(256, bound);
            byte[][] arrays = [.. Enumerable.Range(0, 64).Select(_ => pool.Rent(256))];

            await Task.WhenAll(arrays.Select(array => Task.Run(() => pool.Return(array))))
                .ConfigureAwait(false);

            Assert.That(pool.GetRetainedCount(256), Is.EqualTo(bound));
            var retained = new HashSet<byte[]>();
            for (int ii = 0; ii < bound; ii++)
            {
                retained.Add(pool.Rent(256));
            }
            Assert.That(retained, Has.Count.EqualTo(bound));
            Assert.That(pool.GetRetainedCount(256), Is.Zero);
        }

        [Test]
        public void OversizedRentIsAllocatedExactlyAndNotRetained()
        {
            var pool = new BoundedArrayPool(64, 4);
            byte[] large = pool.Rent(100);

            Assert.That(large, Has.Length.EqualTo(100));
            Assert.DoesNotThrow(() => pool.Return(large));
            Assert.That(pool.Rent(64), Is.Not.SameAs(large));
        }

        [Test]
        public void ReturnOfForeignArrayLengthThrows()
        {
            var pool = new BoundedArrayPool(64, 4);

            Assert.That(() => pool.Return(new byte[48]), Throws.ArgumentException);
            Assert.That(() => pool.Return(null!), Throws.ArgumentNullException);
            Assert.DoesNotThrow(() => pool.Return([]));
        }

        [Test]
        public void ClearArrayZeroesTheReturnedArray()
        {
            var pool = new BoundedArrayPool(64, 4);
            byte[] array = pool.Rent(64);
            array.AsSpan().Fill(0xAB);

            pool.Return(array, clearArray: true);

            Assert.That(pool.Rent(64).All(b => b == 0), Is.True);
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            Assert.That(() => new BoundedArrayPool(100, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new BoundedArrayPool(8, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new BoundedArrayPool(64, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new BoundedArrayPool(64, 4).Rent(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(16, 1024)]
        [TestCase(65536, 1024)]
        [TestCase(131072, 512)]
        [TestCase(1024 * 1024, 64)]
        [TestCase(16 * 1024 * 1024, 4)]
        [TestCase(128 * 1024 * 1024, 1)]
        public void DefaultBoundKeepsAtMost64MiBInTheLargestBucket(int maxArrayLength, int expected)
        {
            Assert.That(BoundedArrayPool.GetDefaultMaxArraysPerBucket(maxArrayLength), Is.EqualTo(expected));
        }

        [Test]
        public void SharedPoolIsKeyedByRoundedMaximumLength()
        {
            BoundedArrayPool pool = BoundedArrayPool.GetShared(40000);

            Assert.That(pool.MaxArrayLength, Is.EqualTo(65536));
            Assert.That(BoundedArrayPool.GetShared(65536), Is.SameAs(pool));
            Assert.That(BoundedArrayPool.GetShared(65537), Is.Not.SameAs(pool));
        }

        /// <summary>
        /// A buffer manager over the bounded pool reuses every chunk buffer of a deep pipeline
        /// instead of allocating one per response once more buffers are in flight than processors.
        /// The pool is the default one's configuration but private to the test, so parallel tests
        /// sharing the process-wide pool cannot take its arrays.
        /// </summary>
        [Test]
        public void FastBufferManagerReusesBuffersOfADeepPipeline()
        {
            var pool = new BoundedArrayPool(65536, BoundedArrayPool.GetDefaultMaxArraysPerBucket(65536));
            var manager = new FastBufferManager("pipeline", 65535, NUnitTelemetryContext.Create(), pool);
            var first = new HashSet<byte[]>();
            for (int ii = 0; ii < 200; ii++)
            {
                first.Add(manager.TakeBuffer(65535, "pipeline"));
            }
            foreach (byte[] buffer in first)
            {
                manager.ReturnBuffer(buffer, "pipeline");
            }
            Assert.That(pool.GetRetainedCount(65536), Is.EqualTo(200));

            for (int ii = 0; ii < 200; ii++)
            {
                Assert.That(first, Does.Contain(manager.TakeBuffer(65535, "pipeline")));
            }
            Assert.That(pool.GetRetainedCount(65536), Is.Zero);
        }
    }
}
