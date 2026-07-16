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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

            Assert.That(suggestedSize, Is.EqualTo((64 * 1024) - kDefaultCookieLength));
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
                Is.EqualTo((8 * 1024) - kDefaultCookieLength));
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

        [Test]
        public void FactorySelectsRequestedImplementationAndAutoDefault()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            IBufferManager fast = new DefaultBufferManagerFactory(
                new BufferManagerFactoryOptions
                {
                    ImplementationKind = BufferManagerImplementationKind.Fast
                }).Create(nameof(FastBufferManager), 1024, telemetry);
            IBufferManager cookie = new DefaultBufferManagerFactory(
                new BufferManagerFactoryOptions
                {
                    ImplementationKind = BufferManagerImplementationKind.Cookie
                }).Create(nameof(CookieBufferManager), 1024, telemetry);
            IBufferManager tracing = new DefaultBufferManagerFactory(
                new BufferManagerFactoryOptions
                {
                    ImplementationKind = BufferManagerImplementationKind.MemoryTracing
                }).Create(nameof(TracingBufferManager), 1024, telemetry);
            IBufferManager auto = new DefaultBufferManagerFactory().Create(nameof(auto), 1024, telemetry);

            Assert.That(fast, Is.TypeOf<FastBufferManager>());
            Assert.That(cookie, Is.TypeOf<CookieBufferManager>());
            Assert.That(tracing, Is.TypeOf<TracingBufferManager>());
            Assert.That(auto.GetType(), Is.EqualTo(s_defaultImplementationType));
        }

        [Test]
        public void ImplementationsUseExpectedCookieLengthsAndRentSizes()
        {
            AssertImplementationSizing(
                new FastBufferManager(
                    nameof(FastBufferManager),
                    4096,
                    NUnitTelemetryContext.Create(),
                    new TrackingArrayPool()),
                expectedCookieLength: 0);
            AssertImplementationSizing(
                new CookieBufferManager(
                    nameof(CookieBufferManager),
                    4096,
                    NUnitTelemetryContext.Create(),
                    new TrackingArrayPool()),
                expectedCookieLength: 1);
            AssertImplementationSizing(
                new TracingBufferManager(
                    nameof(TracingBufferManager),
                    4096,
                    NUnitTelemetryContext.Create(),
                    new TrackingArrayPool()),
                expectedCookieLength: 5);
        }

        [Test]
        public void CustomPoolExpectedSizeCoversActualPowerOfTwoBucket()
        {
            const int requestedSize = (1024 * 1024) + 1;
            var manager = new FastBufferManager(
                nameof(CustomPoolExpectedSizeCoversActualPowerOfTwoBucket),
                requestedSize,
                NUnitTelemetryContext.Create());

            byte[] buffer = manager.TakeBuffer(
                requestedSize,
                nameof(CustomPoolExpectedSizeCoversActualPowerOfTwoBucket));

            Assert.That(manager.GetExpectedBufferSize(requestedSize), Is.GreaterThanOrEqualTo(buffer.Length));
            manager.ReturnBuffer(
                buffer,
                nameof(CustomPoolExpectedSizeCoversActualPowerOfTwoBucket));
        }

        [Test]
        public async Task LimiterBlocksSecondRentUntilFirstReturned()
        {
            const int bufferLength = 32;
            var manager = new LimitingBufferManager(
                new DelegateBufferManager(bufferLength, bufferLength),
                new BufferManagerMemoryLimiter(bufferLength));

            byte[] first = manager.TakeBuffer(bufferLength, nameof(LimiterBlocksSecondRentUntilFirstReturned));
            using var secondStarted = new ManualResetEventSlim(false);
            Task<byte[]> second = Task.Run(() =>
            {
                secondStarted.Set();
                return manager.TakeBuffer(bufferLength, nameof(LimiterBlocksSecondRentUntilFirstReturned));
            });

            Assert.That(secondStarted.Wait(TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(await CompletesWithinAsync(second, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false), Is.False);

            manager.ReturnBuffer(first, nameof(LimiterBlocksSecondRentUntilFirstReturned));

            Assert.That(await CompletesWithinAsync(second, TimeSpan.FromSeconds(2)).ConfigureAwait(false), Is.True);
            manager.ReturnBuffer(await second.ConfigureAwait(false), nameof(LimiterBlocksSecondRentUntilFirstReturned));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void LimiterConstructorWithNonPositiveBudgetThrows(int maxOutstandingBytes)
        {
            Assert.That(
                () => new BufferManagerMemoryLimiter(maxOutstandingBytes),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo(nameof(maxOutstandingBytes)));
        }

        [Test]
        public void LimiterReturnsOversizedBufferAndReleasesReservation()
        {
            const int expectedLength = 16;
            const int actualLength = 32;
            int attempts = 0;
            var returnedBufferLengths = new List<int>();
            var manager = new LimitingBufferManager(
                new DelegateBufferManager(
                    expectedLength,
                    actualLength,
                    takeBuffer: static (expectedSize, owner, currentAttempt) =>
                        new byte[currentAttempt == 1 ? actualLength : expectedSize],
                    getNextAttempt: () => Interlocked.Increment(ref attempts),
                    returnBuffer: (buffer, owner) => returnedBufferLengths.Add(buffer.Length)),
                new BufferManagerMemoryLimiter(expectedLength));

            Assert.That(
                () => manager.TakeBuffer(
                    expectedLength,
                    nameof(LimiterReturnsOversizedBufferAndReleasesReservation)),
                Throws.TypeOf<InvalidOperationException>());

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            byte[] buffer = manager.TakeBuffer(
                expectedLength,
                nameof(LimiterReturnsOversizedBufferAndReleasesReservation),
                cts.Token);
            manager.ReturnBuffer(
                buffer,
                nameof(LimiterReturnsOversizedBufferAndReleasesReservation));

            Assert.That(buffer, Has.Length.EqualTo(expectedLength));
            Assert.That(returnedBufferLengths, Is.EqualTo(new[] { actualLength, expectedLength }));
        }

        [Test]
        public async Task FactorySharesProcessBudgetAcrossManagers()
        {
            const int maxOutstandingBytes = 32;
            var factory = new DefaultBufferManagerFactory(
                new BufferManagerFactoryOptions
                {
                    ImplementationKind = BufferManagerImplementationKind.Fast,
                    MaxOutstandingBytesPerProcess = maxOutstandingBytes
                });
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IBufferManager firstManager = factory.Create("first", maxOutstandingBytes, telemetry);
            IBufferManager secondManager = factory.Create("second", maxOutstandingBytes, telemetry);

            byte[] first = firstManager.TakeBuffer(17, nameof(FactorySharesProcessBudgetAcrossManagers));
            using var secondStarted = new ManualResetEventSlim(false);
            Task<byte[]> second = Task.Run(() =>
            {
                secondStarted.Set();
                return secondManager.TakeBuffer(17, nameof(FactorySharesProcessBudgetAcrossManagers));
            });

            Assert.That(secondStarted.Wait(TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(await CompletesWithinAsync(second, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false), Is.False);

            firstManager.ReturnBuffer(first, nameof(FactorySharesProcessBudgetAcrossManagers));

            Assert.That(await CompletesWithinAsync(second, TimeSpan.FromSeconds(2)).ConfigureAwait(false), Is.True);
            secondManager.ReturnBuffer(await second.ConfigureAwait(false), nameof(FactorySharesProcessBudgetAcrossManagers));
        }

        [Test]
        public async Task LimiterAccountsActualBufferLengthAfterRent()
        {
            const int expectedLength = 32;
            const int actualLength = 16;
            const int maxOutstandingBytes = 48;
            var manager = new LimitingBufferManager(
                new DelegateBufferManager(expectedLength, actualLength),
                new BufferManagerMemoryLimiter(maxOutstandingBytes));

            byte[] first = manager.TakeBuffer(actualLength, nameof(LimiterAccountsActualBufferLengthAfterRent));
            Task<byte[]> second = Task.Run(() =>
                manager.TakeBuffer(actualLength, nameof(LimiterAccountsActualBufferLengthAfterRent)));

            Assert.That(await CompletesWithinAsync(second, TimeSpan.FromSeconds(2)).ConfigureAwait(false), Is.True);

            byte[] secondBuffer = await second.ConfigureAwait(false);
            manager.ReturnBuffer(secondBuffer, nameof(LimiterAccountsActualBufferLengthAfterRent));
            manager.ReturnBuffer(first, nameof(LimiterAccountsActualBufferLengthAfterRent));
        }

        [Test]
        public void LimiterRejectsSingleRentAboveCap()
        {
            var manager = new LimitingBufferManager(
                new DelegateBufferManager(expectedBufferSize: 64, actualBufferSize: 64),
                new BufferManagerMemoryLimiter(32));

            Assert.That(
                () => manager.TakeBuffer(64, nameof(LimiterRejectsSingleRentAboveCap)),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void LimiterReleasesReservationWhenInnerTakeFails()
        {
            int attempts = 0;
            var manager = new LimitingBufferManager(
                new DelegateBufferManager(
                    expectedBufferSize: 32,
                    actualBufferSize: 32,
                    takeBuffer: static (expectedSize, owner, currentAttempt) =>
                    {
                        if (currentAttempt == 1)
                        {
                            throw new InvalidOperationException("boom");
                        }

                        return new byte[expectedSize];
                    },
                    getNextAttempt: () => Interlocked.Increment(ref attempts)),
                new BufferManagerMemoryLimiter(32));

            Assert.That(
                () => manager.TakeBuffer(32, nameof(LimiterReleasesReservationWhenInnerTakeFails)),
                Throws.TypeOf<InvalidOperationException>());

            byte[] buffer = manager.TakeBuffer(32, nameof(LimiterReleasesReservationWhenInnerTakeFails));
            manager.ReturnBuffer(buffer, nameof(LimiterReleasesReservationWhenInnerTakeFails));
        }

        [Test]
        public async Task LimiterRejectsForeignAndDoubleReturnWithoutUnderflow()
        {
            const int bufferLength = 32;
            var manager = new LimitingBufferManager(
                new DelegateBufferManager(bufferLength, bufferLength),
                new BufferManagerMemoryLimiter(bufferLength));

            byte[] initial = manager.TakeBuffer(bufferLength, nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow));

            Assert.That(
                () => manager.ReturnBuffer(new byte[bufferLength], nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow)),
                Throws.TypeOf<InvalidOperationException>());

            manager.ReturnBuffer(initial, nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow));

            Assert.That(
                () => manager.ReturnBuffer(initial, nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow)),
                Throws.TypeOf<InvalidOperationException>());

            byte[] first = manager.TakeBuffer(bufferLength, nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow));
            using var secondStarted = new ManualResetEventSlim(false);
            Task<byte[]> second = Task.Run(() =>
            {
                secondStarted.Set();
                return manager.TakeBuffer(bufferLength, nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow));
            });

            Assert.That(secondStarted.Wait(TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(await CompletesWithinAsync(second, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false), Is.False);

            manager.ReturnBuffer(first, nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow));

            Assert.That(await CompletesWithinAsync(second, TimeSpan.FromSeconds(2)).ConfigureAwait(false), Is.True);
            manager.ReturnBuffer(await second.ConfigureAwait(false), nameof(LimiterRejectsForeignAndDoubleReturnWithoutUnderflow));
        }

        [Test]
        public void LimiterBlockedRentObservesCancellation()
        {
            const int bufferLength = 32;
            var manager = new LimitingBufferManager(
                new DelegateBufferManager(bufferLength, bufferLength),
                new BufferManagerMemoryLimiter(bufferLength));
            byte[] first = manager.TakeBuffer(
                bufferLength,
                nameof(LimiterBlockedRentObservesCancellation));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            Assert.That(
                () => manager.TakeBuffer(
                    bufferLength,
                    nameof(LimiterBlockedRentObservesCancellation),
                    cts.Token),
                Throws.InstanceOf<OperationCanceledException>());

            manager.ReturnBuffer(first, nameof(LimiterBlockedRentObservesCancellation));
        }

        [Test]
        public void LimiterRejectsReentrantRentDuringReturn()
        {
            const int bufferLength = 32;
            var pool = new ReentrantArrayPool(bufferLength);
            var inner = new FastBufferManager(
                nameof(LimiterRejectsReentrantRentDuringReturn),
                bufferLength,
                NUnitTelemetryContext.Create(),
                pool);
            var manager = new LimitingBufferManager(
                inner,
                new BufferManagerMemoryLimiter(bufferLength));
            byte[] first = manager.TakeBuffer(
                bufferLength,
                nameof(LimiterRejectsReentrantRentDuringReturn));
            pool.OnReturned = () => Assert.That(
                    () => manager.TakeBuffer(
                        bufferLength,
                        nameof(LimiterRejectsReentrantRentDuringReturn)),
                    Throws.TypeOf<InvalidOperationException>());

            manager.ReturnBuffer(first, nameof(LimiterRejectsReentrantRentDuringReturn));

            pool.OnReturned = null;
            byte[] next = manager.TakeBuffer(
                bufferLength,
                nameof(LimiterRejectsReentrantRentDuringReturn));
            manager.ReturnBuffer(next, nameof(LimiterRejectsReentrantRentDuringReturn));
        }

        private static void AssertImplementationSizing(IBufferManager manager, int expectedCookieLength)
        {
            var trackingPool = (TrackingArrayPool)GetPrivateField(manager, "m_arrayPool");
            const int requestedPayloadSize = 1024;

            int expectedRentLength = requestedPayloadSize + expectedCookieLength;
            byte[] buffer = manager.TakeBuffer(requestedPayloadSize, nameof(AssertImplementationSizing));
            manager.ReturnBuffer(buffer, nameof(AssertImplementationSizing));

            Assert.That(manager.GetExpectedBufferSize(requestedPayloadSize), Is.EqualTo(expectedRentLength));
            Assert.That(buffer, Has.Length.EqualTo(expectedRentLength));
            Assert.That(trackingPool.LastMinimumLength, Is.EqualTo(expectedRentLength));
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            Type type = instance.GetType();

            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);

                if (field != null)
                {
                    return field.GetValue(instance) ?? throw new AssertionException("Field value was null.");
                }

                type = type.BaseType;
            }

            throw new AssertionException($"Field '{fieldName}' was not found.");
        }

        private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
        {
            Task completedTask = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            return ReferenceEquals(completedTask, task);
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

        private sealed class DelegateBufferManager : IBufferManager
        {
            public DelegateBufferManager(
                int expectedBufferSize,
                int actualBufferSize,
                Func<int, string, int, byte[]> takeBuffer = null,
                Func<int> getNextAttempt = null,
                Action<byte[], string> returnBuffer = null)
            {
                MaxSuggestedBufferSize = expectedBufferSize;
                m_actualBufferSize = actualBufferSize;
                m_takeBuffer = takeBuffer;
                m_getNextAttempt = getNextAttempt;
                m_returnBuffer = returnBuffer;
            }

            public string Name => nameof(DelegateBufferManager);

            public int MaxSuggestedBufferSize { get; }

            public int GetSuggestedBufferSize(int size)
            {
                return size;
            }

            public int GetExpectedBufferSize(int size)
            {
                return MaxSuggestedBufferSize;
            }

            public byte[] TakeBuffer(int size, string owner)
            {
                int attempt = m_getNextAttempt?.Invoke() ?? 0;

                if (m_takeBuffer != null)
                {
                    return m_takeBuffer(size, owner, attempt);
                }

                return new byte[m_actualBufferSize];
            }

            public byte[] TakeBuffer(
                int size,
                string owner,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return TakeBuffer(size, owner);
            }

            public void TransferBuffer(byte[] buffer, string owner)
            {
            }

            public void Lock(byte[] buffer)
            {
            }

            public void Unlock(byte[] buffer)
            {
            }

            public void ReturnBuffer(byte[] buffer, string owner)
            {
                m_returnBuffer?.Invoke(buffer, owner);
            }

            private readonly int m_actualBufferSize;
            private readonly Func<int, string, int, byte[]> m_takeBuffer;
            private readonly Func<int> m_getNextAttempt;
            private readonly Action<byte[], string> m_returnBuffer;
        }

        private sealed class ReentrantArrayPool : ArrayPool<byte>
        {
            public ReentrantArrayPool(int bufferLength)
            {
                m_buffer = new byte[bufferLength];
            }

            public Action OnReturned { get; set; }

            public override byte[] Rent(int minimumLength)
            {
                if (minimumLength > m_buffer.Length || m_rented)
                {
                    throw new InvalidOperationException("The test pool has no available buffer.");
                }
                m_rented = true;
                return m_buffer;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                Assert.That(array, Is.SameAs(m_buffer));
                Assert.That(m_rented, Is.True);
                m_rented = false;
                OnReturned?.Invoke();
            }

            private readonly byte[] m_buffer;
            private bool m_rented;
        }

#if TRACK_MEMORY
        private const int kDefaultCookieLength = 5;
        private static readonly Type s_defaultImplementationType = typeof(TracingBufferManager);
#elif DEBUG
        private const int kDefaultCookieLength = 1;
        private static readonly Type s_defaultImplementationType = typeof(CookieBufferManager);
#else
        private const int kDefaultCookieLength = 0;
        private static readonly Type s_defaultImplementationType = typeof(FastBufferManager);
#endif
    }
}
