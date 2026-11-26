// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CA2000, CA2007

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="LoadDirectionBuilderExtensions.UseServerLoadDirection"/>: the factory lambdas
    /// resolve, both <c>StrongEligibility</c> branches register, and the <c>Resolve*</c> helpers fall back or
    /// use the registered service.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class LoadDirectionBuilderExtensionsTests
    {
        [Test]
        public void UseServerLoadDirectionThrowsOnNullBuilder()
        {
            Assert.That(
                () => LoadDirectionBuilderExtensions.UseServerLoadDirection(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task StrongEligibilityResolvesDirectorAndStartupTasksWithFallbacksAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton<ISharedKeyValueStore>(new InMemorySharedKeyValueStore());

            builder.UseServerLoadDirection(o =>
            {
                o.BalancingEndpointUrl = "opc.tcp://lb:4840";
                o.StrongEligibility = true;
            });

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(sp.GetRequiredService<ServerLoadDirector>(), Is.Not.Null);
                Assert.That(sp.GetRequiredService<IGetEndpointsDirector>(), Is.InstanceOf<ServerLoadDirector>());
                Assert.That(sp.GetServices<IStrongKeyspaceProvider>().ToArray(), Is.Not.Empty);
                Assert.That(
                    sp.GetServices<IServerStartupTask>().ToArray(),
                    Has.Length.GreaterThanOrEqualTo(2));
            });
        }

        [Test]
        public async Task WeakEligibilityUsesRegisteredServicesAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton<ISharedKeyValueStore>(new InMemorySharedKeyValueStore());
            builder.Services.AddSingleton<IServiceLevelProvider>(new ConstantServiceLevelProvider(200));
            builder.Services.AddSingleton<IRecordProtector>(NullRecordProtector.Instance);
            builder.Services.AddSingleton(TimeProvider.System);

            builder.UseServerLoadDirection(o =>
            {
                o.BalancingEndpointUrl = "opc.tcp://lb:4840";
                o.StrongEligibility = false;
            });

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(sp.GetRequiredService<ServerLoadDirector>(), Is.Not.Null);
                // Weak eligibility does not register a strong-keyspace provider.
                Assert.That(sp.GetServices<IStrongKeyspaceProvider>().ToArray(), Is.Empty);
                Assert.That(
                    sp.GetServices<IServerStartupTask>().ToArray(),
                    Has.Length.GreaterThanOrEqualTo(2));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NonlocalStoreRejectsMissingOrNoOpProtectionAsync(bool registerNoOp)
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(Mock.Of<ISharedKeyValueStore>());
            if (registerNoOp)
            {
                builder.Services.AddSingleton<IRecordProtector>(NullRecordProtector.Instance);
            }
            builder.UseServerLoadDirection();
            await using ServiceProvider services = builder.Services.BuildServiceProvider();

            Assert.That(() => services.GetServices<IServerStartupTask>().ToArray(),
                Throws.InvalidOperationException.With.Message.Contains("authenticated IRecordProtector"));
        }

        [Test]
        public async Task NonlocalStoreAcceptsAuthenticatedProtectorAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton(Mock.Of<ISharedKeyValueStore>());
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            builder.Services.AddSingleton<IRecordProtector>(protector);
            builder.UseServerLoadDirection();
            await using ServiceProvider services = builder.Services.BuildServiceProvider();

            Assert.That(services.GetServices<IServerStartupTask>().ToArray(), Has.Length.EqualTo(2));
        }
    }
}
