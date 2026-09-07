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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Configuration;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Exercises the provider-agnostic part of the eager client
    /// configuration load: the public
    /// <see cref="OpcUaClientOptions.ConfigurationProvider"/> surface and
    /// the <see cref="OpcUaClientOptions.LoadConfigurationOnStart"/> hosted
    /// service that awaits
    /// <see cref="IOpcUaApplicationConfigurationProvider.GetAsync"/> during
    /// host start. The supplied-document (<c>AddClient(configurationFile)</c>)
    /// cases live in <see cref="ClientConfigurationFileTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientEagerConfigurationLoadTests
    {
        [Test]
        public async Task LoadConfigurationOnStartAwaitsSharedProviderAsync()
        {
            ApplicationConfiguration configuration = CreateConfiguration();
            Mock<IOpcUaApplicationConfigurationProvider> configurationProvider =
                CreateProviderMock(configuration);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configurationProvider.Object);
            services.AddOpcUa().AddClient(options => options.LoadConfigurationOnStart = true);

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions options = provider.GetRequiredService<OpcUaClientOptions>();

            // The shared provider is exposed publicly, so an application can
            // load and read the configuration back without a session.
            Assert.That(
                options.ConfigurationProvider,
                Is.SameAs(configurationProvider.Object));

            IHostedService loader = GetLoader(provider);
            await loader.StartAsync(CancellationToken.None).ConfigureAwait(false);

            configurationProvider.Verify(
                p => p.GetAsync(It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(options.Configuration, Is.SameAs(configuration));

            await loader.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task LoadConfigurationOnStartIsNoOpForExplicitConfigurationAsync()
        {
            ApplicationConfiguration configuration = CreateConfiguration();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient(options =>
            {
                options.Configuration = configuration;
                options.LoadConfigurationOnStart = true;
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaClientOptions options = provider.GetRequiredService<OpcUaClientOptions>();

            // Nothing to load: an explicit configuration is already complete.
            Assert.That(options.ConfigurationProvider, Is.Null);

            IHostedService loader = GetLoader(provider);
            Assert.That(
                () => loader.StartAsync(CancellationToken.None),
                Throws.Nothing);
            Assert.That(options.Configuration, Is.SameAs(configuration));
        }

        [Test]
        public async Task LoadConfigurationOnStartCancellationAbortsHostStartAsync()
        {
            ApplicationConfiguration configuration = CreateConfiguration();
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var configurationProvider =
                new Mock<IOpcUaApplicationConfigurationProvider>();
            configurationProvider.Setup(p => p.Configuration).Returns(configuration);
            configurationProvider
                .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken ct) =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return configuration;
                });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configurationProvider.Object);
            services.AddOpcUa().AddClient(options => options.LoadConfigurationOnStart = true);

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService loader = GetLoader(provider);

            using var cts = new CancellationTokenSource();
            Task start = loader.StartAsync(cts.Token);
            await started.Task.ConfigureAwait(false);
            cts.Cancel();

            // The host's startup token is passed through to the load.
            Assert.That(
                async () => await start.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task LoadConfigurationOnStartBindsFromConfigurationSectionAsync()
        {
            IConfiguration configurationRoot = new ConfigurationBuilder()
                .Add(new MemoryConfigurationSource
                {
                    InitialData = new Dictionary<string, string?>
                    {
                        ["OpcUa:Client:LoadConfigurationOnStart"] = "true"
                    }
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(CreateProviderMock(CreateConfiguration()).Object);
            services.AddOpcUa().AddClient(configurationRoot);

            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<OpcUaClientOptions>().LoadConfigurationOnStart,
                Is.True);
            Assert.That(GetLoader(provider), Is.Not.Null);
        }

#if NET8_0_OR_GREATER
        [Test]
        public async Task HostStartAsyncCompletesTheConfigurationLoadAsync()
        {
            ApplicationConfiguration configuration = CreateConfiguration();
            Mock<IOpcUaApplicationConfigurationProvider> configurationProvider =
                CreateProviderMock(configuration);

            using IHost host = new HostBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddLogging();
                    services.AddSingleton(configurationProvider.Object);
                    services.AddOpcUa().AddClient(
                        options => options.LoadConfigurationOnStart = true);
                })
                .Build();

            await host.StartAsync().ConfigureAwait(false);
            try
            {
                // When StartAsync returns the configuration is loaded and
                // readable through the public options surface, which is what
                // user-interface hosts need before any session exists.
                OpcUaClientOptions options =
                    host.Services.GetRequiredService<OpcUaClientOptions>();
                Assert.That(
                    options.ConfigurationProvider!.Configuration,
                    Is.SameAs(configuration));
                Assert.That(options.Configuration, Is.SameAs(configuration));
                configurationProvider.Verify(
                    p => p.GetAsync(It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }
#endif

        private static IHostedService GetLoader(IServiceProvider provider)
        {
            return provider.GetServices<IHostedService>()
                .Single(service => service is ClientConfigurationLoaderHostedService);
        }

        private static Mock<IOpcUaApplicationConfigurationProvider> CreateProviderMock(
            ApplicationConfiguration configuration)
        {
            var configurationProvider =
                new Mock<IOpcUaApplicationConfigurationProvider>();
            configurationProvider.Setup(p => p.Configuration).Returns(configuration);
            configurationProvider
                .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(configuration);
            return configurationProvider;
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration(
                Opc.Ua.Tests.NUnitTelemetryContext.Create());
        }
    }
}
