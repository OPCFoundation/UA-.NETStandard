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
using Opc.Ua.Bindings.Pcap.Bindings;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Capture.Sources;
using Opc.Ua.Bindings.Pcap.DependencyInjection;
using Opc.Ua.Bindings.Pcap.Formats;
using Opc.Ua.Bindings.Pcap.Models;
using Opc.Ua.Bindings.Pcap.Replay;

namespace Opc.Ua.Bindings.Pcap.Tests.DependencyInjection
{
    /// <summary>
    /// Behavioural tests for the DI extension methods that wire the OPC UA
    /// pcap binding into <c>Microsoft.Extensions.DependencyInjection</c>.
    /// </summary>
    [TestFixture]
    public sealed class PcapServiceCollectionExtensionsTests : TempDirectoryFixture
    {
        [Test]
        public void AddOpcUaBindingsPcapThrowsOnNullServices()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddOpcUaBindingsPcap(),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void AddOpcUaBindingsPcapWithConfigureThrowsOnNullConfigure()
        {
            var services = new ServiceCollection();
            Action<PcapOptions>? configure = null;

            Assert.That(
                () => services.AddOpcUaBindingsPcap(configure!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddOpcUaBindingsPcapRegistersCoreSingletonsExactlyOnce()
        {
            var services = new ServiceCollection();

            services.AddOpcUaBindingsPcap();

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
        public void AddOpcUaBindingsPcapRegistersServicesAsSingletons()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcap();

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
        public async Task AddOpcUaBindingsPcapResolvesAllRequiredServices()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcap();
            await using ServiceProvider provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<PcapOptions>();
            var registry = provider.GetRequiredService<IChannelCaptureRegistry>();
            var factory = provider.GetRequiredService<ICaptureSourceFactory>();
            await using var manager = provider.GetRequiredService<CaptureSessionManager>();

            Assert.That(options, Is.Not.Null);
            Assert.That(registry, Is.InstanceOf<ChannelCaptureRegistry>());
            Assert.That(factory, Is.InstanceOf<DefaultCaptureSourceFactory>());
            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public async Task AddOpcUaBindingsPcapResolvesSameSingletonAcrossCalls()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcap();
            await using ServiceProvider provider = services.BuildServiceProvider();

            var registry1 = provider.GetRequiredService<IChannelCaptureRegistry>();
            var registry2 = provider.GetRequiredService<IChannelCaptureRegistry>();
            var manager1 = provider.GetRequiredService<CaptureSessionManager>();
            var manager2 = provider.GetRequiredService<CaptureSessionManager>();

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
        public async Task AddOpcUaBindingsPcapInvokesUserConfigureCallback()
        {
            var services = new ServiceCollection();
            string desiredFolder = Path.Combine(Path.GetTempPath(), "pcap-di-test-" + Guid.NewGuid().ToString("N"));

            services.AddOpcUaBindingsPcap(opts =>
            {
                opts.BaseFolder = desiredFolder;
                opts.MaxActiveSessions = 3;
            });
            await using ServiceProvider provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<PcapOptions>();

            Assert.That(options.BaseFolder, Is.EqualTo(desiredFolder));
            Assert.That(options.MaxActiveSessions, Is.EqualTo(3));
        }

        [Test]
        public async Task AddOpcUaBindingsPcapPropagatesMaxActiveSessionsToManager()
        {
            var services = new ServiceCollection();
            string desiredFolder = Path.Combine(Path.GetTempPath(), "pcap-di-cap-" + Guid.NewGuid().ToString("N"));

            services.AddOpcUaBindingsPcap(opts =>
            {
                opts.BaseFolder = desiredFolder;
                opts.MaxActiveSessions = 2;
            });
            await using ServiceProvider provider = services.BuildServiceProvider();

            var manager = provider.GetRequiredService<CaptureSessionManager>();

            // The cap configured on PcapOptions must reach the manager
            // instance (this is what makes the option meaningful for
            // DI consumers; without the wiring the manager would
            // silently fall back to DefaultMaxActiveSessions).
            Assert.That(manager.MaxActiveSessions, Is.EqualTo(2));
        }

        [Test]
        public async Task AddOpcUaBindingsPcapDefaultOptionsUsePerUserLocalAppData()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcap();
            await using ServiceProvider provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<PcapOptions>();

            Assert.That(
                options.BaseFolder,
                Does.StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            Assert.That(options.BaseFolder, Does.Contain("OPCFoundation"));
            Assert.That(options.BaseFolder, Does.Contain("opcua-pcap"));
            Assert.That(options.MaxActiveSessions,
                Is.EqualTo(CaptureSessionManager.DefaultMaxActiveSessions));
        }

        [Test]
        public async Task AddOpcUaBindingsPcapInstallsPcapBindingIntoTransportRegistry()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcap();

            await using ServiceProvider provider = services.BuildServiceProvider();
            var bindings = provider.GetRequiredService<ITransportBindingRegistry>();

            ITransportChannelFactory? binding = bindings.GetChannelFactory(
                Opc.Ua.Utils.UriSchemeOpcTcp);

            Assert.That(binding, Is.Not.Null);
            Assert.That(binding, Is.InstanceOf<PcapTransportChannelBinding>());
        }

        [Test]
        public void AddOpcUaBindingsPcapFormattersThrowsOnNullServices()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddOpcUaBindingsPcapFormatters(),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void AddOpcUaBindingsPcapFormattersRegistersRegistryAsSingleton()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcapFormatters();

            ServiceDescriptor descriptor = services
                .Single(d => d.ServiceType == typeof(TraceFormatterRegistry));

            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        }

        [Test]
        public void AddOpcUaBindingsPcapFormattersResolvesNonEmptyRegistry()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcapFormatters();
            using ServiceProvider provider = services.BuildServiceProvider();

            var registry1 = provider.GetRequiredService<TraceFormatterRegistry>();
            var registry2 = provider.GetRequiredService<TraceFormatterRegistry>();

            Assert.That(registry1, Is.SameAs(registry2));
            Assert.That(registry1.Available, Contains.Item(FormatKind.Pcap),
                "Default registry must contain a binary pcap formatter.");
            Assert.That(registry1.Available, Contains.Item(FormatKind.Json),
                "Default registry must contain a JSON formatter.");
        }

        [Test]
        public void AddOpcUaBindingsPcapReplayThrowsOnNullServices()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddOpcUaBindingsPcapReplay(),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void AddOpcUaBindingsPcapReplayRegistersReplaySessionManagerAsSingleton()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcapReplay();

            ServiceDescriptor descriptor = services
                .Single(d => d.ServiceType == typeof(ReplaySessionManager));

            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        }

        [Test]
        public async Task AddOpcUaBindingsPcapReplayResolvesSameSingleton()
        {
            var services = new ServiceCollection();
            services.AddOpcUaBindingsPcapReplay();
            await using ServiceProvider provider = services.BuildServiceProvider();

            var manager1 = provider.GetRequiredService<ReplaySessionManager>();
            var manager2 = provider.GetRequiredService<ReplaySessionManager>();

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
