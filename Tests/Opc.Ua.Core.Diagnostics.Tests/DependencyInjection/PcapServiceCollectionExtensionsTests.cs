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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Tests.DependencyInjection
{
    /// <summary>
    /// Behavioural tests for the DI extension methods that wire the OPC UA
    /// pcap binding into <c>Microsoft.Extensions.DependencyInjection</c>.
    /// </summary>
    [TestFixture]
    public sealed class PcapServiceCollectionExtensionsTests : TempDirectoryFixture
    {
        [Test]
        public void AddPcapThrowsOnNullServices()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddPcap(),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void AddPcapWithConfigureThrowsOnNullConfigure()
        {
            var services = new ServiceCollection();
            Action<PcapOptions>? configure = null;

            Assert.That(
                () => services.AddPcap(configure!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddPcapRegistersCoreSingletonsExactlyOnce()
        {
            var services = new ServiceCollection();

            services.AddPcap();

            int optionsCount = services.Count(d => d.ServiceType == typeof(PcapOptions));
            int registryCount = services.Count(d => d.ServiceType == typeof(IChannelCaptureRegistry));
            int factoryCount = services.Count(d => d.ServiceType == typeof(ICaptureSourceFactory));
            int managerCount = services.Count(d => d.ServiceType == typeof(CaptureSessionManager));

            Assert.That(optionsCount, Is.EqualTo(1));
            Assert.That(registryCount, Is.EqualTo(1));
            Assert.That(factoryCount, Is.EqualTo(1));
            Assert.That(managerCount, Is.EqualTo(1));
        }

        [Test]
        public void AddPcapRegistersServicesAsSingletons()
        {
            var services = new ServiceCollection();
            services.AddPcap();

            ServiceLifetime optionsLifetime = services
                .First(d => d.ServiceType == typeof(PcapOptions)).Lifetime;
            ServiceLifetime registryLifetime = services
                .First(d => d.ServiceType == typeof(IChannelCaptureRegistry)).Lifetime;
            ServiceLifetime factoryLifetime = services
                .First(d => d.ServiceType == typeof(ICaptureSourceFactory)).Lifetime;
            ServiceLifetime managerLifetime = services
                .First(d => d.ServiceType == typeof(CaptureSessionManager)).Lifetime;

            Assert.That(optionsLifetime, Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(registryLifetime, Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(factoryLifetime, Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(managerLifetime, Is.EqualTo(ServiceLifetime.Singleton));
        }

        [Test]
        public void AddPcapFormattersBuilderOverloadReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddPcapFormatters();

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(services.Any(d => d.ServiceType == typeof(TraceFormatterRegistry)), Is.True);
        }

        [Test]
        public void AddPcapReplayBuilderOverloadReturnsSameBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder returned = builder.AddPcapReplay();

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(services.Any(d => d.ServiceType == typeof(ReplaySessionManager)), Is.True);
        }

        [Test]
        public async Task AddPcapResolvesAllRequiredServices()
        {
            var services = new ServiceCollection();
            services.AddPcap();
            await using ServiceProvider provider = services.BuildServiceProvider();

            PcapOptions options = provider.GetRequiredService<PcapOptions>();
            IChannelCaptureRegistry registry = provider.GetRequiredService<IChannelCaptureRegistry>();
            ICaptureSourceFactory factory = provider.GetRequiredService<ICaptureSourceFactory>();
            await using CaptureSessionManager manager = provider.GetRequiredService<CaptureSessionManager>();

            Assert.That(options, Is.Not.Null);
            Assert.That(registry, Is.InstanceOf<ChannelCaptureRegistry>());
            Assert.That(factory, Is.InstanceOf<DefaultCaptureSourceFactory>());
            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public async Task AddPcapResolvesSameSingletonAcrossCalls()
        {
            var services = new ServiceCollection();
            services.AddPcap();
            await using ServiceProvider provider = services.BuildServiceProvider();

            IChannelCaptureRegistry registry1 = provider.GetRequiredService<IChannelCaptureRegistry>();
            IChannelCaptureRegistry registry2 = provider.GetRequiredService<IChannelCaptureRegistry>();
            CaptureSessionManager manager1 = provider.GetRequiredService<CaptureSessionManager>();
            CaptureSessionManager manager2 = provider.GetRequiredService<CaptureSessionManager>();

            Assert.That(registry2, Is.SameAs(registry1));
            Assert.That(manager2, Is.SameAs(manager1));
        }

        [Test]
        [Platform("Linux,MacOSX")]
        public async Task CaptureSessionManagerBaseFolderHasUserOnlyMode()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes are not available on Windows.");
                return;
            }

            string baseFolder = CreateTempPath("manager-base");
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(new ChannelCaptureRegistry()),
                baseFolder);

            Assert.That(
                File.GetUnixFileMode(baseFolder),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        }

        [Test]
        public async Task AddPcapInvokesUserConfigureCallback()
        {
            var services = new ServiceCollection();
            string desiredFolder = Path.Combine(Path.GetTempPath(), "pcap-di-test-" + Guid.NewGuid().ToString("N"));

            services.AddPcap(opts =>
            {
                opts.BaseFolder = desiredFolder;
                opts.MaxActiveSessions = 3;
            });
            await using ServiceProvider provider = services.BuildServiceProvider();

            PcapOptions options = provider.GetRequiredService<PcapOptions>();

            Assert.That(options.BaseFolder, Is.EqualTo(desiredFolder));
            Assert.That(options.MaxActiveSessions, Is.EqualTo(3));
        }

        [Test]
        public async Task AddPcapPropagatesMaxActiveSessionsToManager()
        {
            var services = new ServiceCollection();
            string desiredFolder = Path.Combine(Path.GetTempPath(), "pcap-di-cap-" + Guid.NewGuid().ToString("N"));

            services.AddPcap(opts =>
            {
                opts.BaseFolder = desiredFolder;
                opts.MaxActiveSessions = 2;
            });
            await using ServiceProvider provider = services.BuildServiceProvider();

            CaptureSessionManager manager = provider.GetRequiredService<CaptureSessionManager>();

            // The cap configured on PcapOptions must reach the manager
            // instance (this is what makes the option meaningful for
            // DI consumers; without the wiring the manager would
            // silently fall back to DefaultMaxActiveSessions).
            Assert.That(manager.MaxActiveSessions, Is.EqualTo(2));
        }

        [Test]
        public async Task AddPcapDefaultOptionsUsePerUserLocalAppData()
        {
            var services = new ServiceCollection();
            services.AddPcap();
            await using ServiceProvider provider = services.BuildServiceProvider();

            PcapOptions options = provider.GetRequiredService<PcapOptions>();

            Assert.That(
                options.BaseFolder,
                Does.StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            Assert.That(options.BaseFolder, Does.Contain("OPCFoundation"));
            Assert.That(options.BaseFolder, Does.Contain("opcua-pcap"));
            Assert.That(options.MaxActiveSessions,
                Is.EqualTo(CaptureSessionManager.DefaultMaxActiveSessions));
        }

        [Test]
        public async Task AddPcapInstallsPcapBindingIntoTransportRegistry()
        {
            var services = new ServiceCollection();
            services.AddPcap();

            await using ServiceProvider provider = services.BuildServiceProvider();
            var bindings = provider.GetRequiredService<ITransportBindingRegistry>();

            ITransportChannelFactory? binding = bindings.GetChannelFactory(
                Utils.UriSchemeOpcTcp);

            Assert.That(binding, Is.Not.Null);
            Assert.That(binding, Is.InstanceOf<PcapTransportChannelBinding>());
        }

        [Test]
        public void AddPcapFormattersThrowsOnNullServices()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddPcapFormatters(),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void AddPcapFormattersRegistersRegistryAsSingleton()
        {
            var services = new ServiceCollection();
            services.AddPcapFormatters();

            ServiceDescriptor descriptor = services
                .Single(d => d.ServiceType == typeof(TraceFormatterRegistry));

            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        }

        [Test]
        public void AddPcapFormattersResolvesNonEmptyRegistry()
        {
            var services = new ServiceCollection();
            services.AddPcapFormatters();
            using ServiceProvider provider = services.BuildServiceProvider();

            TraceFormatterRegistry registry1 = provider.GetRequiredService<TraceFormatterRegistry>();
            TraceFormatterRegistry registry2 = provider.GetRequiredService<TraceFormatterRegistry>();

            Assert.That(registry1, Is.SameAs(registry2));
            Assert.That(registry1.Available, Contains.Item(FormatKind.Pcap),
                "Default registry must contain a binary pcap formatter.");
            Assert.That(registry1.Available, Contains.Item(FormatKind.Json),
                "Default registry must contain a JSON formatter.");
        }

        [Test]
        public void AddPcapReplayThrowsOnNullServices()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddPcapReplay(),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void AddPcapReplayRegistersReplaySessionManagerAsSingleton()
        {
            var services = new ServiceCollection();
            services.AddPcapReplay();

            ServiceDescriptor descriptor = services
                .Single(d => d.ServiceType == typeof(ReplaySessionManager));

            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        }

        [Test]
        public async Task AddPcapReplayResolvesSameSingleton()
        {
            var services = new ServiceCollection();
            services.AddPcapReplay();
            await using ServiceProvider provider = services.BuildServiceProvider();

            ReplaySessionManager manager1 = provider.GetRequiredService<ReplaySessionManager>();
            ReplaySessionManager manager2 = provider.GetRequiredService<ReplaySessionManager>();

            Assert.That(manager2, Is.SameAs(manager1));
        }

        [Test]
        public void PcapOptionsDefaultsAreSane()
        {
            var options = new PcapOptions();

            Assert.That(options.BaseFolder, Does.Contain("opcua-pcap"));
            Assert.That(options.MaxActiveSessions, Is.GreaterThan(0));
            Assert.That(options.MaxActiveSessions,
                Is.EqualTo(CaptureSessionManager.DefaultMaxActiveSessions));
        }
    }
}
