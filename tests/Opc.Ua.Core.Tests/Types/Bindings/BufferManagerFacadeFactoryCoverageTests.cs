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
    [TestFixture]
    [Category("BufferManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class BufferManagerFacadeFactoryCoverageTests
    {
        [Test]
        public void BufferManagerWithNullImplementationThrowsArgumentNullException()
        {
            Assert.That(
                () => new BufferManager(null),
                Throws.ArgumentNullException
                    .With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("bufferManager"));
        }

        [Test]
        public void BufferManagerForwardsFacadeMembersToInnerManager()
        {
            var inner = new SpyBufferManager();
            var manager = new BufferManager(inner);
            byte[] lockedBuffer = new byte[1];
            using var cts = new CancellationTokenSource();

            byte[] synchronousBuffer = manager.TakeBuffer(17, "synchronous");
            byte[] cancellableBuffer = manager.TakeBuffer(19, "cancellable", cts.Token);
            manager.TransferBuffer(null, "transferred");
            manager.Lock(lockedBuffer);
            manager.Unlock(lockedBuffer);
            manager.ReturnBuffer(null, "returned");

            Assert.That(manager.Name, Is.EqualTo(inner.Name));
            Assert.That(manager.MaxSuggestedBufferSize, Is.EqualTo(inner.MaxSuggestedBufferSize));
            Assert.That(manager.GetSuggestedBufferSize(23), Is.EqualTo(inner.SuggestedBufferSize));
            Assert.That(manager.GetExpectedBufferSize(29), Is.EqualTo(inner.ExpectedBufferSize));
            Assert.That(synchronousBuffer, Is.SameAs(inner.RentedBuffer));
            Assert.That(cancellableBuffer, Is.SameAs(inner.RentedBuffer));
            Assert.That(inner.SynchronousRentCount, Is.EqualTo(1));
            Assert.That(inner.SynchronousRentSize, Is.EqualTo(17));
            Assert.That(inner.SynchronousRentOwner, Is.EqualTo("synchronous"));
            Assert.That(inner.CancellableRentCount, Is.EqualTo(1));
            Assert.That(inner.CancellableRentSize, Is.EqualTo(19));
            Assert.That(inner.CancellableRentOwner, Is.EqualTo("cancellable"));
            Assert.That(inner.CancellableRentToken, Is.EqualTo(cts.Token));
            Assert.That(inner.SuggestedSizeArgument, Is.EqualTo(23));
            Assert.That(inner.ExpectedSizeArguments, Is.EqualTo([29]));
            Assert.That(inner.TransferBufferArgument, Is.Null);
            Assert.That(inner.TransferOwnerArgument, Is.EqualTo("transferred"));
            Assert.That(inner.LockBufferArgument, Is.SameAs(lockedBuffer));
            Assert.That(inner.UnlockBufferArgument, Is.SameAs(lockedBuffer));
            Assert.That(inner.ReturnedBuffers, Has.Count.EqualTo(1));
            Assert.That(inner.ReturnedBuffers[0], Is.Null);
            Assert.That(inner.ReturnOwners, Is.EqualTo(["returned"]));
        }

        [Test]
        public void LimitingBufferManagerForwardsCompatibleFacadeMembersToInnerManager()
        {
            var inner = new SpyBufferManager();
            using var limiter = new BufferManagerMemoryLimiter(2 * inner.ExpectedBufferSize);
            var manager = new LimitingBufferManager(inner, limiter);
            byte[] lockedBuffer = new byte[1];
            using var cts = new CancellationTokenSource();

            Assert.That(manager.Name, Is.EqualTo(inner.Name));
            Assert.That(manager.MaxSuggestedBufferSize, Is.EqualTo(inner.MaxSuggestedBufferSize));
            Assert.That(manager.GetSuggestedBufferSize(31), Is.EqualTo(inner.SuggestedBufferSize));
            Assert.That(manager.GetExpectedBufferSize(37), Is.EqualTo(inner.ExpectedBufferSize));

            byte[] synchronousBuffer = manager.TakeBuffer(41, "synchronous");
            manager.ReturnBuffer(synchronousBuffer, "synchronous");
            byte[] cancellableBuffer = manager.TakeBuffer(43, "cancellable", cts.Token);
            manager.TransferBuffer(null, "transferred");
            manager.Lock(lockedBuffer);
            manager.Unlock(lockedBuffer);
            manager.ReturnBuffer(null, "null");
            manager.ReturnBuffer(cancellableBuffer, "cancellable");

            Assert.That(synchronousBuffer, Is.SameAs(inner.RentedBuffer));
            Assert.That(cancellableBuffer, Is.SameAs(inner.RentedBuffer));
            Assert.That(inner.SynchronousRentCount, Is.Zero);
            Assert.That(inner.CancellableRentCount, Is.EqualTo(2));
            Assert.That(inner.CancellableRentSizes, Is.EqualTo([41, 43]));
            Assert.That(inner.CancellableRentOwners, Is.EqualTo(["synchronous", "cancellable"]));
            Assert.That(inner.CancellableRentTokens[0], Is.EqualTo(CancellationToken.None));
            Assert.That(inner.CancellableRentTokens[1], Is.EqualTo(cts.Token));
            Assert.That(inner.SuggestedSizeArgument, Is.EqualTo(31));
            Assert.That(inner.ExpectedSizeArguments, Is.EqualTo([37, 41, 43]));
            Assert.That(inner.TransferBufferArgument, Is.Null);
            Assert.That(inner.TransferOwnerArgument, Is.EqualTo("transferred"));
            Assert.That(inner.LockBufferArgument, Is.SameAs(lockedBuffer));
            Assert.That(inner.UnlockBufferArgument, Is.SameAs(lockedBuffer));
            Assert.That(inner.ReturnedBuffers, Is.EqualTo([synchronousBuffer, cancellableBuffer]));
            Assert.That(inner.ReturnOwners, Is.EqualTo(["synchronous", "cancellable"]));
        }

        [TestCase(BufferManagerImplementationKind.Fast, false)]
        [TestCase(BufferManagerImplementationKind.Fast, true)]
        [TestCase(BufferManagerImplementationKind.Cookie, false)]
        [TestCase(BufferManagerImplementationKind.Cookie, true)]
        [TestCase(BufferManagerImplementationKind.MemoryTracing, false)]
        [TestCase(BufferManagerImplementationKind.MemoryTracing, true)]
        public void BufferManagerImplementationSelectorCreatesRequestedKind(
            BufferManagerImplementationKind implementationKind,
            bool useCustomPool)
        {
            ArrayPool<byte> arrayPool = useCustomPool ? new ExactArrayPool() : null;

            IBufferManager manager = BufferManager.CreateImplementation(
                nameof(BufferManagerImplementationSelectorCreatesRequestedKind),
                1024,
                NUnitTelemetryContext.Create(),
                implementationKind,
                arrayPool);

            Assert.That(manager.GetType(), Is.EqualTo(GetImplementationType(implementationKind)));
        }

        [Test]
        public void BufferManagerImplementationSelectorWithInvalidKindThrowsInvalidOperationException()
        {
            Assert.That(
                () => BufferManager.CreateImplementation(
                    nameof(BufferManagerImplementationSelectorWithInvalidKindThrowsInvalidOperationException),
                    1024,
                    NUnitTelemetryContext.Create(),
                    (BufferManagerImplementationKind)int.MaxValue),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void FactoryWithNullAndDefaultOptionsSelectsBuildDefault()
        {
            using var nullOptionsFactory = new DefaultBufferManagerFactory(null);
            using var defaultOptionsFactory = new DefaultBufferManagerFactory();

            IBufferManager nullOptionsManager = nullOptionsFactory.Create(
                "null",
                1024,
                NUnitTelemetryContext.Create());
            IBufferManager defaultOptionsManager = defaultOptionsFactory.Create(
                "default",
                1024,
                NUnitTelemetryContext.Create());
            Type expectedType = GetImplementationType(BufferManager.GetDefaultImplementationKind());

            Assert.That(nullOptionsManager.GetType(), Is.EqualTo(expectedType));
            Assert.That(defaultOptionsManager.GetType(), Is.EqualTo(expectedType));
        }

        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        public void FactoryWithNegativeMemoryLimitThrowsArgumentOutOfRangeException(long memoryLimit)
        {
            var options = new BufferManagerFactoryOptions
            {
                MaxOutstandingBytesPerProcess = memoryLimit
            };

            Assert.That(
                () => new DefaultBufferManagerFactory(options),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("options"));
        }

        [Test]
        public void FactoryWithInvalidImplementationKindThrowsArgumentOutOfRangeException()
        {
            using var factory = new DefaultBufferManagerFactory(
                new BufferManagerFactoryOptions
                {
                    ImplementationKind = (BufferManagerImplementationKind)int.MaxValue
                });

            Assert.That(
                () => factory.Create(
                    nameof(FactoryWithInvalidImplementationKindThrowsArgumentOutOfRangeException),
                    1024,
                    NUnitTelemetryContext.Create()),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo("implementationKind"));
        }

        [Test]
        public void FactoryClonesOptionsBeforeCreatingManagers()
        {
            var options = new BufferManagerFactoryOptions
            {
                ImplementationKind = BufferManagerImplementationKind.Fast,
                MaxOutstandingBytesPerProcess = 0
            };
            using var factory = new DefaultBufferManagerFactory(options);
            options.ImplementationKind = BufferManagerImplementationKind.Cookie;
            options.MaxOutstandingBytesPerProcess = 1;

            IBufferManager manager = factory.Create(
                nameof(FactoryClonesOptionsBeforeCreatingManagers),
                1024,
                NUnitTelemetryContext.Create());

            Assert.That(manager, Is.TypeOf<FastBufferManager>());
        }

        [TestCase(BufferManagerImplementationKind.Auto)]
        [TestCase(BufferManagerImplementationKind.Fast)]
        [TestCase(BufferManagerImplementationKind.Cookie)]
        [TestCase(BufferManagerImplementationKind.MemoryTracing)]
        public void FactoryInnerSelectorCreatesRequestedImplementation(
            BufferManagerImplementationKind implementationKind)
        {
            IBufferManager manager = DefaultBufferManagerFactory.CreateInnerManager(
                nameof(FactoryInnerSelectorCreatesRequestedImplementation),
                1024,
                NUnitTelemetryContext.Create(),
                implementationKind);
            BufferManagerImplementationKind expectedKind =
                implementationKind == BufferManagerImplementationKind.Auto
                    ? BufferManager.GetDefaultImplementationKind()
                    : implementationKind;

            Assert.That(manager.GetType(), Is.EqualTo(GetImplementationType(expectedKind)));
        }

        [Test]
        public void FactoryWithPositiveMemoryLimitCreatesLimitingManager()
        {
            using var factory = new DefaultBufferManagerFactory(
                new BufferManagerFactoryOptions
                {
                    ImplementationKind = BufferManagerImplementationKind.Fast,
                    MaxOutstandingBytesPerProcess = 1024
                });

            IBufferManager manager = factory.Create(
                nameof(FactoryWithPositiveMemoryLimitCreatesLimitingManager),
                1024,
                NUnitTelemetryContext.Create());

            Assert.That(manager, Is.TypeOf<LimitingBufferManager>());
        }

        private static Type GetImplementationType(BufferManagerImplementationKind implementationKind)
        {
            switch (implementationKind)
            {
                case BufferManagerImplementationKind.Fast:
                    return typeof(FastBufferManager);
                case BufferManagerImplementationKind.Cookie:
                    return typeof(CookieBufferManager);
                case BufferManagerImplementationKind.MemoryTracing:
                    return typeof(TracingBufferManager);
                default:
                    throw new AssertionException(
                        $"Unexpected implementation kind: {implementationKind}.");
            }
        }

        private sealed class SpyBufferManager : IBufferManager
        {
            public string Name => nameof(SpyBufferManager);

            public int MaxSuggestedBufferSize => 128;

            public int SuggestedBufferSize => 47;

            public int ExpectedBufferSize => 64;

            public byte[] RentedBuffer { get; } = new byte[64];

            public int SuggestedSizeArgument { get; private set; }

            public List<int> ExpectedSizeArguments { get; } = [];

            public int SynchronousRentCount { get; private set; }

            public int SynchronousRentSize { get; private set; }

            public string SynchronousRentOwner { get; private set; }

            public int CancellableRentCount => CancellableRentSizes.Count;

            public int CancellableRentSize => CancellableRentSizes[^1];

            public string CancellableRentOwner =>
                CancellableRentOwners[^1];

            public CancellationToken CancellableRentToken =>
                CancellableRentTokens[^1];

            public List<int> CancellableRentSizes { get; } = [];

            public List<string> CancellableRentOwners { get; } = [];

            public List<CancellationToken> CancellableRentTokens { get; } =
                [];

            public byte[] TransferBufferArgument { get; private set; }

            public string TransferOwnerArgument { get; private set; }

            public byte[] LockBufferArgument { get; private set; }

            public byte[] UnlockBufferArgument { get; private set; }

            public List<byte[]> ReturnedBuffers { get; } = [];

            public List<string> ReturnOwners { get; } = [];

            public int GetSuggestedBufferSize(int size)
            {
                SuggestedSizeArgument = size;
                return SuggestedBufferSize;
            }

            public int GetExpectedBufferSize(int size)
            {
                ExpectedSizeArguments.Add(size);
                return ExpectedBufferSize;
            }

            public byte[] TakeBuffer(int size, string owner)
            {
                SynchronousRentCount++;
                SynchronousRentSize = size;
                SynchronousRentOwner = owner;
                return RentedBuffer;
            }

            public byte[] TakeBuffer(int size, string owner, CancellationToken ct)
            {
                CancellableRentSizes.Add(size);
                CancellableRentOwners.Add(owner);
                CancellableRentTokens.Add(ct);
                return RentedBuffer;
            }

            public void TransferBuffer(byte[] buffer, string owner)
            {
                TransferBufferArgument = buffer;
                TransferOwnerArgument = owner;
            }

            public void Lock(byte[] buffer)
            {
                LockBufferArgument = buffer;
            }

            public void Unlock(byte[] buffer)
            {
                UnlockBufferArgument = buffer;
            }

            public void ReturnBuffer(byte[] buffer, string owner)
            {
                ReturnedBuffers.Add(buffer);
                ReturnOwners.Add(owner);
            }
        }

        private sealed class ExactArrayPool : ArrayPool<byte>
        {
            public override byte[] Rent(int minimumLength)
            {
                return new byte[minimumLength];
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
            }
        }
    }
}
