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

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
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
    public sealed class TracingCookieBufferManagerCoverageTests
    {
        [Test]
        public void TracingTakeBufferWritesSequentialAllocationMetadata()
        {
            var pool = new ExactArrayPool();
            var manager = new TracingBufferManager(
                nameof(TracingTakeBufferWritesSequentialAllocationMetadata),
                64,
                NUnitTelemetryContext.Create(),
                pool);

            byte[] first = manager.TakeBuffer(11, null!);
            byte[] second = manager.TakeBuffer(
                12,
                nameof(TracingTakeBufferWritesSequentialAllocationMetadata));

            Assert.That(TracingBufferManager.ReadAllocationId(first), Is.EqualTo(1));
            Assert.That(TracingBufferManager.ReadAllocationId(second), Is.EqualTo(2));

            manager.ReturnBuffer(first, null!);
            manager.ReturnBuffer(
                second,
                nameof(TracingTakeBufferWritesSequentialAllocationMetadata));
            Assert.That(pool.ReturnCount, Is.EqualTo(2));
        }

        [Test]
        public void TracingTransferReportsAgedOwnershipAndReturnDiagnostics()
        {
            var provider = new CapturingLoggerProvider();
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                builder => builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddProvider(provider));
            var pool = new ExactArrayPool();
            var utcNow = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var manager = new TracingBufferManager(
                nameof(TracingTransferReportsAgedOwnershipAndReturnDiagnostics),
                64,
                telemetry,
                pool,
                () => utcNow);
            byte[] aged = manager.TakeBuffer(16, "initial");

            manager.TransferBuffer(aged, "second");
            Assert.That(
                provider.Messages,
                Has.None.Contains("*** TRANSFERRED ***"));

            utcNow = utcNow.AddSeconds(6);
            byte[] firstTrigger = manager.TakeBuffer(8, "firstTrigger");
            manager.ReturnBuffer(firstTrigger, "firstTrigger");
            byte[] secondTrigger = manager.TakeBuffer(8, "secondTrigger");
            manager.ReturnBuffer(secondTrigger, "secondTrigger");

            Assert.That(
                provider.Messages.Count(
                    message => message.Contains("Age=6", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                provider.Messages,
                Has.Some.Contains("Owner=initial/second; Size=0 KB; Age=6"));

            manager.TransferBuffer(aged, "third");
            manager.ReturnBuffer(aged, "releaser");

            Assert.That(
                provider.Messages,
                Has.Some.Contains(
                    "Owner=initial/second/third; Size=0 KB; *** TRANSFERRED ***"));
            Assert.That(
                provider.Messages,
                Has.Some.Contains(
                    "Owner=initial/second/third; ReleasedBy=releaser; Size=0 KB; *** RETURNED ***"));
        }

        [Test]
        public void TracingAllocationAtNonReportingAgeDoesNotLogAge()
        {
            var provider = new CapturingLoggerProvider();
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                builder => builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddProvider(provider));
            var pool = new ExactArrayPool();
            var utcNow = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var manager = new TracingBufferManager(
                nameof(TracingAllocationAtNonReportingAgeDoesNotLogAge),
                64,
                telemetry,
                pool,
                () => utcNow);
            byte[] aged = manager.TakeBuffer(16, "aged");

            utcNow = utcNow.AddSeconds(5);
            byte[] trigger = manager.TakeBuffer(8, "trigger");
            manager.ReturnBuffer(trigger, "trigger");

            Assert.That(provider.Messages, Has.None.Contains("Age="));
            manager.ReturnBuffer(aged, "aged");
        }

        [TestCase("Server", 0xFA)]
        [TestCase("Client", 0xFC)]
        public void TracingReturnFillsPayloadAndReturnsToPool(string name, int expectedFill)
        {
            var pool = new ExactArrayPool();
            var manager = new TracingBufferManager(
                name,
                64,
                NUnitTelemetryContext.Create(),
                pool);
            byte[] buffer = manager.TakeBuffer(
                11,
                nameof(TracingReturnFillsPayloadAndReturnsToPool));

            manager.ReturnBuffer(
                buffer,
                nameof(TracingReturnFillsPayloadAndReturnsToPool));

            Assert.That(buffer.Take(buffer.Length - 5), Is.All.EqualTo((byte)expectedFill));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.LastReturned, Is.SameAs(buffer));
        }

        [Test]
        public void TracingCorruptedAllocationIdsRecoverByBufferIdentity()
        {
            var provider = new CapturingLoggerProvider();
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                builder => builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddProvider(provider));
            var pool = new ExactArrayPool();
            var utcNow = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var manager = new TracingBufferManager(
                nameof(TracingCorruptedAllocationIdsRecoverByBufferIdentity),
                64,
                telemetry,
                pool,
                () => utcNow);
            byte[] transferBuffer = manager.TakeBuffer(8, "transfer");
            WriteAllocationId(transferBuffer, int.MaxValue);

            Assert.That(
                () => manager.TransferBuffer(transferBuffer, "unknown"),
                Throws.Nothing);
            manager.ReturnBuffer(transferBuffer, "transfer");

            byte[] returnBuffer = manager.TakeBuffer(8, "return");
            WriteAllocationId(returnBuffer, 0);
            Assert.That(
                () => manager.ReturnBuffer(returnBuffer, "unknown"),
                Throws.Nothing);

            utcNow = utcNow.AddSeconds(6);
            byte[] trigger = manager.TakeBuffer(8, "trigger");
            manager.ReturnBuffer(trigger, "trigger");

            Assert.That(pool.ReturnCount, Is.EqualTo(3));
            Assert.That(
                provider.Messages,
                Has.None.Contains("Owner=return; Size=0 KB; Age=6"));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void TracingReadAllocationIdWithoutMetadataThrows(int length)
        {
            Assert.That(
                () => TracingBufferManager.ReadAllocationId(new byte[length]),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer does not contain tracing metadata."));
        }

        [Test]
        public void TracingReadAllocationIdWithZeroMetadataReturnsZero()
        {
            Assert.That(
                TracingBufferManager.ReadAllocationId(new byte[5]),
                Is.Zero);
        }

        [Test]
        public void TracingLockAndUnlockDelegateToCookieValidation()
        {
            var manager = new TracingBufferManager(
                nameof(TracingLockAndUnlockDelegateToCookieValidation),
                64,
                NUnitTelemetryContext.Create(),
                new ExactArrayPool());
            byte[] buffer = manager.TakeBuffer(
                8,
                nameof(TracingLockAndUnlockDelegateToCookieValidation));

            Assert.That(() => manager.Unlock(buffer), Throws.TypeOf<InvalidOperationException>());
            manager.Lock(buffer);
            Assert.That(() => manager.Lock(buffer), Throws.TypeOf<InvalidOperationException>());
            manager.Unlock(buffer);
            manager.ReturnBuffer(
                buffer,
                nameof(TracingLockAndUnlockDelegateToCookieValidation));
        }

        [Test]
        public void TracingConstructorWithNullClockThrows()
        {
            Assert.That(
                () => new TracingBufferManager(
                    nameof(TracingConstructorWithNullClockThrows),
                    64,
                    NUnitTelemetryContext.Create(),
                    new ExactArrayPool(),
                    null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("utcNow"));
        }

        [Test]
        public void CookieTakeLockUnlockAndReturnUsesCookieMetadata()
        {
            var pool = new ExactArrayPool();
            var manager = new CookieBufferManager(
                nameof(CookieTakeLockUnlockAndReturnUsesCookieMetadata),
                64,
                NUnitTelemetryContext.Create(),
                pool);
            byte[] buffer = manager.TakeBuffer(
                8,
                nameof(CookieTakeLockUnlockAndReturnUsesCookieMetadata));

            Assert.That(() => manager.Unlock(buffer), Throws.TypeOf<InvalidOperationException>());
            manager.Lock(buffer);
            Assert.That(() => manager.Lock(buffer), Throws.TypeOf<InvalidOperationException>());
            manager.Unlock(buffer);
            manager.ReturnBuffer(
                buffer,
                nameof(CookieTakeLockUnlockAndReturnUsesCookieMetadata));

            Assert.That(pool.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.LastReturned, Is.SameAs(buffer));
            Assert.That(
                () => manager.Lock(buffer),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CookieReturnWhileLockedThrowsAndKeepsBufferRented()
        {
            var pool = new ExactArrayPool();
            var manager = new CookieBufferManager(
                nameof(CookieReturnWhileLockedThrowsAndKeepsBufferRented),
                64,
                NUnitTelemetryContext.Create(),
                pool);
            byte[] buffer = manager.TakeBuffer(
                8,
                nameof(CookieReturnWhileLockedThrowsAndKeepsBufferRented));
            manager.Lock(buffer);

            Assert.That(
                () => manager.ReturnBuffer(
                    buffer,
                    nameof(CookieReturnWhileLockedThrowsAndKeepsBufferRented)),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer has been locked."));
            Assert.That(pool.ReturnCount, Is.Zero);

            manager.Unlock(buffer);
            manager.ReturnBuffer(
                buffer,
                nameof(CookieReturnWhileLockedThrowsAndKeepsBufferRented));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
        }

        [Test]
        public void CookieOperationsWithEmptyBufferThrow()
        {
            var pool = new ExactArrayPool();
            var manager = new CookieBufferManager(
                nameof(CookieOperationsWithEmptyBufferThrow),
                64,
                NUnitTelemetryContext.Create(),
                pool);
            byte[] empty = [];

            Assert.That(
                () => manager.Lock(empty),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer does not contain a cookie."));
            Assert.That(
                () => manager.Unlock(empty),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer does not contain a cookie."));
            Assert.That(
                () => manager.ReturnBuffer(empty, nameof(CookieOperationsWithEmptyBufferThrow)),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("Buffer does not contain a cookie."));
            Assert.That(pool.ReturnCount, Is.Zero);
        }

        private static void WriteAllocationId(byte[] buffer, int allocationId)
        {
            byte[] bytes = BitConverter.GetBytes(allocationId);
            Array.Copy(bytes, 0, buffer, buffer.Length - 5, bytes.Length);
        }

        private sealed class ExactArrayPool : ArrayPool<byte>
        {
            public int ReturnCount { get; private set; }

            public byte[]? LastReturned { get; private set; }

            public override byte[] Rent(int minimumLength)
            {
                return new byte[minimumLength];
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                ReturnCount++;
                LastReturned = array;
            }
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public List<string> Messages { get; } = [];

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(Messages);
            }

            public void Dispose()
            {
            }

            private sealed class CapturingLogger : ILogger
            {
                public CapturingLogger(List<string> messages)
                {
                    m_messages = messages;
                }

                public IDisposable BeginScope<TState>(TState state)
                    where TState : notnull
                {
                    return null!;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    m_messages.Add(formatter(state, exception));
                }

                private readonly List<string> m_messages;
            }
        }
    }
}
