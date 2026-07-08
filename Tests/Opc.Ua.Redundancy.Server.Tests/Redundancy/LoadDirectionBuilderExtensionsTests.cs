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

#pragma warning disable CA2000, CA2007

#nullable enable

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;
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
            builder.Services.AddSingleton(System.TimeProvider.System);

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
    }
}
