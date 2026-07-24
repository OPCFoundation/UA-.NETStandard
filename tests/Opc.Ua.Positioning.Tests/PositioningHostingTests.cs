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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Positioning.Client;
using Opc.Ua.Positioning.Server;
using Opc.Ua.Positioning.Server.Hosting;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Positioning.Tests
{
    [TestFixture]
    [Category("Positioning")]
    [NonParallelizable]
    public sealed class PositioningHostingTests
    {
        [Test]
        public void StandaloneRegistrationAddsFactoryProvidersAndRejectsDuplicates()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder server = services
                .AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "PositioningHostingTest";
                    options.AutoAcceptUntrustedCertificates = true;
                });
            IPositioningServerBuilder positioning = server
                .AddPositioningServer();
            var globalProvider = new TestGlobalProvider();
            var relativeProvider = new TestRelativeProvider();
            positioning
                .AddGlobalPositionProvider(_ => globalProvider)
                .AddRelativeSpatialLocationProvider(_ => relativeProvider);

            using ServiceProvider provider = services.BuildServiceProvider();
            PositioningNodeManagerFactory factory =
                provider.GetRequiredService<PositioningNodeManagerFactory>();
            ArrayOf<IGlobalPositionProvider> globalProviders = provider
                .GetServices<IGlobalPositionProvider>()
                .ToArray()
                .ToArrayOf();
            ArrayOf<IRelativeSpatialLocationProvider> relativeProviders =
                provider
                    .GetServices<IRelativeSpatialLocationProvider>()
                    .ToArray()
                    .ToArrayOf();

            Assert.Multiple(() =>
            {
                Assert.That(positioning.Services, Is.SameAs(services));
                Assert.That(factory, Is.Not.Null);
                Assert.That(globalProviders, Has.Count.EqualTo(1));
                Assert.That(relativeProviders, Has.Count.EqualTo(1));
                Assert.That(
                    () => server.AddPositioningServer(),
                    Throws.TypeOf<InvalidOperationException>());
            });
        }

        [Test]
        public async Task CompositeRegistrationRunsOnlyMatchingConfiguratorAsync()
        {
            await using var fixture = new PositioningServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var services = new ServiceCollection();
            int matchingCalls = 0;
            int otherCalls = 0;
            IPositioningServerBuilder positioning = services
                .AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "PositioningCompositeTest";
                    options.AutoAcceptUntrustedCertificates = true;
                })
                .AddPositioningFor<PositioningNodeManager>();
            positioning
                .ConfigurePositioningFor<PositioningNodeManager>(_ =>
                {
                    matchingCalls++;
                    return default;
                })
                .ConfigurePositioningFor<FileSystemNodeManager>(_ =>
                {
                    otherCalls++;
                    return default;
                });

            using ServiceProvider provider = services.BuildServiceProvider();
            IPositioningPostSetupRunner runner =
                provider.GetRequiredService<IPositioningPostSetupRunner>();
            await runner.RunAsync(
                fixture.Manager,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(matchingCalls, Is.EqualTo(1));
                Assert.That(otherCalls, Is.Zero);
            });
        }

        [Test]
        public void ClientRegistrationAddsBothManagedSessionFactories()
        {
            var services = new ServiceCollection();
            var configuration = new ApplicationConfiguration(
                DefaultTelemetry.Create(_ => { }))
            {
                ApplicationName = "PositioningClientHostingTest",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration(),
                TransportQuotas = new TransportQuotas()
            };
            services.AddOpcUa()
                .AddClient(options => options.Configuration = configuration)
                .AddPositioningClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            Func<CancellationToken, Task<RelativeSpatialLocationClient>>
                relativeFactory = provider.GetRequiredService<
                    Func<CancellationToken, Task<RelativeSpatialLocationClient>>>();
            Func<CancellationToken, Task<GlobalPositioningClient>>
                globalFactory = provider.GetRequiredService<
                    Func<CancellationToken, Task<GlobalPositioningClient>>>();

            Assert.Multiple(() =>
            {
                Assert.That(relativeFactory, Is.Not.Null);
                Assert.That(globalFactory, Is.Not.Null);
            });
        }

        private sealed class TestGlobalProvider : IGlobalPositionProvider
        {
            public ValueTask<GlobalPositionSample> ReadAsync(
                string sourceId,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public IAsyncEnumerable<GlobalPositionSample> WatchAsync(
                string sourceId,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class TestRelativeProvider :
            IRelativeSpatialLocationProvider
        {
            public ValueTask<RelativeSpatialLocationSample> ReadAsync(
                string sourceId,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public IAsyncEnumerable<RelativeSpatialLocationSample> WatchAsync(
                string sourceId,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
