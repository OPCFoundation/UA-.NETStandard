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
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Bindings
{
    [TestFixture]
    [Category("BufferManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class BufferManagerTests
    {
        [Test]
        public void SuggestedSizeAvoidsCrossingArrayPoolBucket()
        {
            var pool = new TrackingArrayPool();
            var manager = new BufferManager(
                nameof(SuggestedSizeAvoidsCrossingArrayPoolBucket),
                64 * 1024,
                NUnitTelemetryContext.Create(),
                pool);

            int suggestedSize = manager.GetSuggestedBufferSize(64 * 1024);
            byte[] buffer = manager.TakeBuffer(
                suggestedSize,
                nameof(SuggestedSizeAvoidsCrossingArrayPoolBucket));
            manager.ReturnBuffer(
                buffer,
                nameof(SuggestedSizeAvoidsCrossingArrayPoolBucket));

            Assert.That(suggestedSize, Is.EqualTo((64 * 1024) - kCookieLength));
            Assert.That(pool.LastMinimumLength, Is.EqualTo(64 * 1024));
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.OutstandingCount, Is.Zero);
        }

        [Test]
        public void SuggestedSizeUsesRequestedNegotiatedSize()
        {
            var manager = new BufferManager(
                nameof(SuggestedSizeUsesRequestedNegotiatedSize),
                64 * 1024,
                NUnitTelemetryContext.Create(),
                new TrackingArrayPool());

            Assert.That(
                manager.GetSuggestedBufferSize(8 * 1024),
                Is.EqualTo((8 * 1024) - kCookieLength));
            Assert.That(
                manager.GetSuggestedBufferSize((8 * 1024) - 1),
                Is.EqualTo((8 * 1024) - 1));
        }

        [Test]
        public void TakeAndReturnBalancesConcurrentOutstandingBuffers()
        {
            var pool = new TrackingArrayPool();
            var manager = new BufferManager(
                nameof(TakeAndReturnBalancesConcurrentOutstandingBuffers),
                8192,
                NUnitTelemetryContext.Create(),
                pool);
            var buffers = new List<byte[]>();

            for (int ii = 0; ii < 32; ii++)
            {
                buffers.Add(manager.TakeBuffer(
                    8191,
                    nameof(TakeAndReturnBalancesConcurrentOutstandingBuffers)));
            }

            foreach (byte[] buffer in buffers)
            {
                manager.ReturnBuffer(
                    buffer,
                    nameof(TakeAndReturnBalancesConcurrentOutstandingBuffers));
            }

            Assert.That(pool.RentCount, Is.EqualTo(32));
            Assert.That(pool.ReturnCount, Is.EqualTo(32));
            Assert.That(pool.PeakOutstandingCount, Is.EqualTo(32));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.DuplicateReturnCount, Is.Zero);
        }

        private sealed class TrackingArrayPool : ArrayPool<byte>
        {
            public override byte[] Rent(int minimumLength)
            {
                byte[] buffer = new byte[minimumLength];
                lock (m_lock)
                {
                    RentCount++;
                    LastMinimumLength = minimumLength;
                    m_outstanding.Add(buffer);
                    PeakOutstandingCount = Math.Max(PeakOutstandingCount, m_outstanding.Count);
                }
                return buffer;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                lock (m_lock)
                {
                    ReturnCount++;
                    if (!m_outstanding.Remove(array))
                    {
                        DuplicateReturnCount++;
                    }
                }
            }

            public int RentCount { get; private set; }

            public int ReturnCount { get; private set; }

            public int LastMinimumLength { get; private set; }

            public int OutstandingCount
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_outstanding.Count;
                    }
                }
            }

            public int PeakOutstandingCount { get; private set; }

            public int DuplicateReturnCount { get; private set; }

            private readonly Lock m_lock = new();
            private readonly HashSet<byte[]> m_outstanding = [];
        }

#if DEBUG
        private const int kCookieLength = 1;
#else
        private const int kCookieLength = 0;
#endif
    }
}
