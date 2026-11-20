/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

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
