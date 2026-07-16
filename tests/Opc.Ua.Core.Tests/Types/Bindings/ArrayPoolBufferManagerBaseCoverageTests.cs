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
    /// <summary>
    /// Covers error handling and sizing branches in <see cref="ArrayPoolBufferManagerBase"/>.
    /// </summary>
    [TestFixture]
    [Category("BufferManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ArrayPoolBufferManagerBaseCoverageTests
    {
        /// <summary>
        /// Verifies that a failed take hook cannot leak its rented array.
        /// </summary>
        [Test]
        public void TakeBufferWhenTakeHookThrowsReturnsRentedBuffer()
        {
            var pool = new TrackingArrayPool();
            var manager = new TestBufferManager(
                nameof(TakeBufferWhenTakeHookThrowsReturnsRentedBuffer),
                32,
                NUnitTelemetryContext.Create(),
                pool,
                metadataByteCount: 0)
            {
                ThrowOnTake = true
            };

            Assert.That(
                () => manager.TakeBuffer(
                    16,
                    nameof(TakeBufferWhenTakeHookThrowsReturnsRentedBuffer)),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Take hook failed."));
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.OutstandingCount, Is.Zero);
            Assert.That(pool.LastReturned, Is.SameAs(pool.LastRented));
        }

        /// <summary>
        /// Verifies that null buffers do not invoke hooks or access the pool.
        /// </summary>
        [Test]
        public void NullBuffersSkipTransferAndReturnWork()
        {
            var pool = new TrackingArrayPool();
            var manager = new TestBufferManager(
                nameof(NullBuffersSkipTransferAndReturnWork),
                32,
                NUnitTelemetryContext.Create(),
                pool,
                metadataByteCount: 0);

            manager.TransferBuffer(null, nameof(NullBuffersSkipTransferAndReturnWork));
            manager.ReturnBuffer(null, nameof(NullBuffersSkipTransferAndReturnWork));

            Assert.That(manager.TransferHookCount, Is.Zero);
            Assert.That(manager.ReturnHookCount, Is.Zero);
            Assert.That(pool.RentCount, Is.Zero);
            Assert.That(pool.ReturnCount, Is.Zero);
        }

        /// <summary>
        /// Verifies validation of negative and over-limit requested sizes.
        /// </summary>
        /// <param name="size">The invalid requested size.</param>
        [TestCase(-1)]
        [TestCase(33)]
        public void RequestedSizeOutsideConfiguredRangeThrows(int size)
        {
            var manager = new TestBufferManager(
                nameof(RequestedSizeOutsideConfiguredRangeThrows),
                32,
                NUnitTelemetryContext.Create(),
                new TrackingArrayPool(),
                metadataByteCount: 0);

            Assert.That(
                () => manager.GetSuggestedBufferSize(size),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo(nameof(size)));
            Assert.That(
                () => manager.GetExpectedBufferSize(size),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo(nameof(size)));
            Assert.That(
                () => manager.TakeBuffer(
                    size,
                    nameof(RequestedSizeOutsideConfiguredRangeThrows)),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo(nameof(size)));
        }

        /// <summary>
        /// Verifies validation of a negative maximum size for both pool selection paths.
        /// </summary>
        [Test]
        public void ConstructorWithNegativeMaximumSizeThrows()
        {
            Assert.That(
                () => new TestBufferManager(
                    nameof(ConstructorWithNegativeMaximumSizeThrows),
                    -1,
                    NUnitTelemetryContext.Create(),
                    metadataByteCount: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("maxBufferSize"));
            Assert.That(
                () => new TestBufferManager(
                    nameof(ConstructorWithNegativeMaximumSizeThrows),
                    -1,
                    NUnitTelemetryContext.Create(),
                    new TrackingArrayPool(),
                    metadataByteCount: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("maxBufferSize"));
        }

        /// <summary>
        /// Verifies that the injected-pool constructor rejects a null pool.
        /// </summary>
        [Test]
        public void ConstructorWithNullInjectedPoolThrows()
        {
            Assert.That(
                () => new TestBufferManager(
                    nameof(ConstructorWithNullInjectedPoolThrows),
                    32,
                    NUnitTelemetryContext.Create(),
                    null!,
                    metadataByteCount: 0),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("arrayPool"));
        }

        /// <summary>
        /// Verifies that a null diagnostic name is normalized without affecting operation.
        /// </summary>
        [Test]
        public void ConstructorWithNullNameUsesEmptyName()
        {
            var manager = new TestBufferManager(
                null!,
                32,
                NUnitTelemetryContext.Create(),
                new TrackingArrayPool(),
                metadataByteCount: 0);

            Assert.That(manager.Name, Is.Empty);
        }

        /// <summary>
        /// Verifies exact sizing when a caller controls the array pool.
        /// </summary>
        [Test]
        public void InjectedPoolUsesExactExpectedAndRentSizes()
        {
            var pool = new TrackingArrayPool();
            var manager = new TestBufferManager(
                nameof(InjectedPoolUsesExactExpectedAndRentSizes),
                32,
                NUnitTelemetryContext.Create(),
                pool,
                metadataByteCount: 1);

            byte[] buffer = manager.TakeBuffer(
                16,
                nameof(InjectedPoolUsesExactExpectedAndRentSizes));

            Assert.That(manager.GetExpectedBufferSize(16), Is.EqualTo(17));
            Assert.That(pool.LastMinimumLength, Is.EqualTo(17));
            Assert.That(buffer, Has.Length.EqualTo(17));

            manager.ReturnBuffer(buffer, nameof(InjectedPoolUsesExactExpectedAndRentSizes));
        }

        /// <summary>
        /// Verifies pool-bucket sizing when the base class selects the pool.
        /// </summary>
        [Test]
        public void SelectedPoolRoundsExpectedSizesToPoolBuckets()
        {
            var manager = new TestBufferManager(
                nameof(SelectedPoolRoundsExpectedSizesToPoolBuckets),
                64,
                NUnitTelemetryContext.Create(),
                metadataByteCount: 0);

            Assert.That(manager.GetExpectedBufferSize(0), Is.EqualTo(16));
            Assert.That(manager.GetExpectedBufferSize(16), Is.EqualTo(16));
            Assert.That(manager.GetExpectedBufferSize(17), Is.EqualTo(32));
        }

        /// <summary>
        /// Verifies the inclusive shared-pool threshold with and without metadata.
        /// </summary>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="metadataByteCount">The metadata size.</param>
        [TestCase(1024 * 1024, 0)]
        [TestCase((1024 * 1024) - 1, 1)]
        public void PoolSelectionAtThresholdUsesSharedPool(
            int maxBufferSize,
            int metadataByteCount)
        {
            ArrayPool<byte> pool = ArrayPoolBufferManagerBase.CreateArrayPool(
                maxBufferSize,
                metadataByteCount);

            Assert.That(pool, Is.SameAs(ArrayPool<byte>.Shared));
        }

        /// <summary>
        /// Verifies that requests above the shared-pool threshold use a custom pool.
        /// </summary>
        /// <param name="maxBufferSize">The maximum payload size.</param>
        /// <param name="metadataByteCount">The metadata size.</param>
        [TestCase((1024 * 1024) + 1, 0)]
        [TestCase(1024 * 1024, 1)]
        public void PoolSelectionAboveThresholdUsesCustomPool(
            int maxBufferSize,
            int metadataByteCount)
        {
            ArrayPool<byte> pool = ArrayPoolBufferManagerBase.CreateArrayPool(
                maxBufferSize,
                metadataByteCount);

            Assert.That(pool, Is.Not.SameAs(ArrayPool<byte>.Shared));
        }

        /// <summary>
        /// Verifies both suggested-size decisions and constructor initialization.
        /// </summary>
        [Test]
        public void SuggestedSizeAccountsForMetadataBucketCrossing()
        {
            var crossingManager = new TestBufferManager(
                nameof(SuggestedSizeAccountsForMetadataBucketCrossing),
                16,
                NUnitTelemetryContext.Create(),
                new TrackingArrayPool(),
                metadataByteCount: 1);
            var sameBucketManager = new TestBufferManager(
                nameof(SuggestedSizeAccountsForMetadataBucketCrossing),
                15,
                NUnitTelemetryContext.Create(),
                new TrackingArrayPool(),
                metadataByteCount: 1);

            Assert.That(crossingManager.MaxSuggestedBufferSize, Is.EqualTo(15));
            Assert.That(crossingManager.GetSuggestedBufferSize(16), Is.EqualTo(15));
            Assert.That(sameBucketManager.MaxSuggestedBufferSize, Is.EqualTo(15));
            Assert.That(sameBucketManager.GetSuggestedBufferSize(15), Is.EqualTo(15));
        }

        /// <summary>
        /// Verifies that the rounding loops terminate when their power-of-two sentinel overflows.
        /// </summary>
        [Test]
        public void LargestSizesUseOverflowSentinelWithoutLoopingForever()
        {
            var manager = new TestBufferManager(
                nameof(LargestSizesUseOverflowSentinelWithoutLoopingForever),
                int.MaxValue,
                NUnitTelemetryContext.Create(),
                new TrackingArrayPool(),
                metadataByteCount: 0);

            Assert.That(manager.MaxSuggestedBufferSize, Is.EqualTo(int.MaxValue));
            Assert.That(manager.GetSuggestedBufferSize(int.MaxValue), Is.EqualTo(int.MaxValue));
            Assert.That(manager.GetExpectedBufferSize(int.MaxValue), Is.EqualTo(int.MaxValue));
            Assert.That(ArrayPoolBufferManagerBase.RoundUpToPoolBucket(int.MaxValue), Is.Zero);
        }

        /// <summary>
        /// Verifies checked arithmetic while constructing both pool-selection variants.
        /// </summary>
        [Test]
        public void ConstructorWhenMaximumAndMetadataOverflowThrows()
        {
            Assert.That(
                () => new TestBufferManager(
                    nameof(ConstructorWhenMaximumAndMetadataOverflowThrows),
                    int.MaxValue,
                    NUnitTelemetryContext.Create(),
                    metadataByteCount: 1),
                Throws.TypeOf<OverflowException>());
            Assert.That(
                () => new TestBufferManager(
                    nameof(ConstructorWhenMaximumAndMetadataOverflowThrows),
                    int.MaxValue,
                    NUnitTelemetryContext.Create(),
                    new TrackingArrayPool(),
                    metadataByteCount: 1),
                Throws.TypeOf<OverflowException>());
        }

        /// <summary>
        /// Verifies cancellation is observed before an array is rented.
        /// </summary>
        [Test]
        public void TakeBufferWithCanceledTokenDoesNotRent()
        {
            var pool = new TrackingArrayPool();
            var manager = new TestBufferManager(
                nameof(TakeBufferWithCanceledTokenDoesNotRent),
                32,
                NUnitTelemetryContext.Create(),
                pool,
                metadataByteCount: 0);

            Assert.That(
                () => manager.TakeBuffer(
                    16,
                    nameof(TakeBufferWithCanceledTokenDoesNotRent),
                    new CancellationToken(canceled: true)),
                Throws.TypeOf<OperationCanceledException>());
            Assert.That(pool.RentCount, Is.Zero);
        }

        /// <summary>
        /// Verifies the default hooks, lock methods and transfer path are harmless.
        /// </summary>
        [Test]
        public void DefaultHooksAllowTakeTransferLockUnlockAndReturn()
        {
            var pool = new TrackingArrayPool();
            var manager = new TestBufferManager(
                nameof(DefaultHooksAllowTakeTransferLockUnlockAndReturn),
                32,
                NUnitTelemetryContext.Create(),
                pool,
                metadataByteCount: 0);

            byte[] buffer = manager.TakeBuffer(
                16,
                nameof(DefaultHooksAllowTakeTransferLockUnlockAndReturn));
            manager.TransferBuffer(
                buffer,
                nameof(DefaultHooksAllowTakeTransferLockUnlockAndReturn));
            manager.Lock(buffer);
            manager.Unlock(buffer);
            manager.ReturnBuffer(
                buffer,
                nameof(DefaultHooksAllowTakeTransferLockUnlockAndReturn));

            Assert.That(manager.TakeHookCount, Is.EqualTo(1));
            Assert.That(manager.TransferHookCount, Is.EqualTo(1));
            Assert.That(manager.ReturnHookCount, Is.EqualTo(1));
            Assert.That(pool.OutstandingCount, Is.Zero);
        }

        /// <summary>
        /// Verifies that an empty array fails cookie initialization and is returned.
        /// </summary>
        [Test]
        public void CookieTakeWithEmptyBufferThrowsAndReturnsBuffer()
        {
            var pool = new TrackingArrayPool(_ => []);
            var manager = new TestBufferManager(
                nameof(CookieTakeWithEmptyBufferThrowsAndReturnsBuffer),
                0,
                NUnitTelemetryContext.Create(),
                pool,
                metadataByteCount: 1,
                useCookies: true);

            Assert.That(
                () => manager.TakeBuffer(
                    0,
                    nameof(CookieTakeWithEmptyBufferThrowsAndReturnsBuffer)),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer does not contain a cookie."));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.OutstandingCount, Is.Zero);
        }

        /// <summary>
        /// Verifies cookie state guards on the smallest valid cookie buffer.
        /// </summary>
        [Test]
        public void CookieOperationsGuardSingleByteBufferState()
        {
            var pool = new TrackingArrayPool(_ => new byte[1]);
            var manager = new TestBufferManager(
                nameof(CookieOperationsGuardSingleByteBufferState),
                0,
                NUnitTelemetryContext.Create(),
                pool,
                metadataByteCount: 1,
                useCookies: true);
            byte[] buffer = manager.TakeBuffer(
                0,
                nameof(CookieOperationsGuardSingleByteBufferState));

            Assert.That(
                () => manager.Unlock(buffer),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer is not locked."));

            manager.Lock(buffer);

            Assert.That(
                () => manager.Lock(buffer),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer is already locked."));
            Assert.That(
                () => manager.ReturnBuffer(
                    buffer,
                    nameof(CookieOperationsGuardSingleByteBufferState)),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer has been locked."));
            Assert.That(pool.ReturnCount, Is.Zero);
            Assert.That(pool.OutstandingCount, Is.EqualTo(1));

            manager.Unlock(buffer);
            manager.ReturnBuffer(
                buffer,
                nameof(CookieOperationsGuardSingleByteBufferState));

            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.OutstandingCount, Is.Zero);
        }

        private sealed class TestBufferManager : ArrayPoolBufferManagerBase
        {
            public TestBufferManager(
                string name,
                int maxBufferSize,
                ITelemetryContext telemetry,
                int metadataByteCount)
                : base(name, maxBufferSize, telemetry, metadataByteCount)
            {
            }

            public TestBufferManager(
                string name,
                int maxBufferSize,
                ITelemetryContext telemetry,
                ArrayPool<byte> arrayPool,
                int metadataByteCount,
                bool useCookies = false)
                : base(name, maxBufferSize, telemetry, arrayPool, metadataByteCount)
            {
                m_useCookies = useCookies;
            }

            public bool ThrowOnTake { get; set; }

            public int TakeHookCount { get; private set; }

            public int TransferHookCount { get; private set; }

            public int ReturnHookCount { get; private set; }

            public override void Lock(byte[] buffer)
            {
                if (m_useCookies)
                {
                    BufferCookie.Lock(buffer);
                    return;
                }

                base.Lock(buffer);
            }

            public override void Unlock(byte[] buffer)
            {
                if (m_useCookies)
                {
                    BufferCookie.Unlock(buffer);
                    return;
                }

                base.Unlock(buffer);
            }

            protected override void OnBufferTaken(byte[] buffer, string owner)
            {
                base.OnBufferTaken(buffer, owner);
                TakeHookCount++;

                if (ThrowOnTake)
                {
                    throw new InvalidOperationException("Take hook failed.");
                }

                if (m_useCookies)
                {
                    BufferCookie.Initialize(buffer);
                }
            }

            protected override void OnBufferTransferred(byte[] buffer, string owner)
            {
                base.OnBufferTransferred(buffer, owner);
                TransferHookCount++;
            }

            protected override void OnBufferReturning(byte[] buffer, string owner)
            {
                base.OnBufferReturning(buffer, owner);
                ReturnHookCount++;

                if (m_useCookies)
                {
                    BufferCookie.ValidateAndDestroy(buffer);
                }
            }

            private readonly bool m_useCookies;
        }

        private sealed class TrackingArrayPool : ArrayPool<byte>
        {
            public TrackingArrayPool(Func<int, byte[]> createBuffer = null)
            {
                m_createBuffer = createBuffer;
            }

            public int RentCount { get; private set; }

            public int ReturnCount { get; private set; }

            public int LastMinimumLength { get; private set; }

            public byte[] LastRented { get; private set; }

            public byte[] LastReturned { get; private set; }

            public int OutstandingCount => m_outstanding.Count;

            public override byte[] Rent(int minimumLength)
            {
                RentCount++;
                LastMinimumLength = minimumLength;
                LastRented = m_createBuffer?.Invoke(minimumLength) ?? new byte[minimumLength];
                m_outstanding.Add(LastRented);
                return LastRented;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                ReturnCount++;
                LastReturned = array;
                Assert.That(m_outstanding.Remove(array), Is.True);
            }

            private readonly Func<int, byte[]> m_createBuffer;
            private readonly HashSet<byte[]> m_outstanding = [];
        }
    }
}
