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

#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Tests for <see cref="RecordProtectionGuard"/>: mirrored state must fail
    /// closed (refuse to start) when an external shared store is configured
    /// without an <see cref="IRecordProtector"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class RecordProtectionGuardTests
    {
        [Test]
        public void InMemoryStoreWithoutProtectorReturnsNull()
        {
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton<ISharedKeyValueStore>(new InMemorySharedKeyValueStore())
                .BuildServiceProvider();

            Assert.That(RecordProtectionGuard.ResolveProtectorOrThrow(services), Is.Null);
        }

        [Test]
        public void ExternalStoreWithoutProtectorThrows()
        {
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton(Mock.Of<ISharedKeyValueStore>())
                .BuildServiceProvider();

            Assert.That(
                () => RecordProtectionGuard.ResolveProtectorOrThrow(services),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ExternalStoreWithProtectorReturnsProtector()
        {
            IRecordProtector protector = Mock.Of<IRecordProtector>();
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton(Mock.Of<ISharedKeyValueStore>())
                .AddSingleton(protector)
                .BuildServiceProvider();

            Assert.That(
                RecordProtectionGuard.ResolveProtectorOrThrow(services),
                Is.SameAs(protector));
        }

        [Test]
        public void ExternalStoreWithExplicitNullProtectorIsAllowed()
        {
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton(Mock.Of<ISharedKeyValueStore>())
                .AddSingleton<IRecordProtector>(NullRecordProtector.Instance)
                .BuildServiceProvider();

            Assert.That(
                RecordProtectionGuard.ResolveProtectorOrThrow(services),
                Is.SameAs(NullRecordProtector.Instance));
        }

        [Test]
        public void NullServicesThrowsArgumentNull()
        {
            Assert.That(
                () => RecordProtectionGuard.ResolveProtectorOrThrow(null!),
                Throws.ArgumentNullException);
        }
    }
}
