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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.DependencyInjection
{
    /// <summary>
    /// Behavioural tests for the <c>IHostedService</c> registered by
    /// <c>AddPcapFromEnvironment</c>. The hosted service is
    /// exercised directly so the tests do not depend on the
    /// <c>Microsoft.Extensions.Hosting</c> generic-host bootstrapping
    /// path; that path is covered by
    /// <see cref="AddPcapFromEnvironmentTests"/>.
    /// </summary>
    [TestFixture]
    public sealed class PcapEnvironmentAutoStartHostedServiceTests : TempDirectoryFixture
    {

        [Test]
        public async Task EmptySnapshotDoesNothing()
        {
            var registry = new ChannelCaptureRegistry();
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(registry),
                CreateTempPath("base"));

            await using var service = new PcapEnvironmentAutoStartHostedService(
                new PcapEnvironmentSnapshot(PcapFilePath: null, KeyLogFilePath: null),
                manager,
                registry);

            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(registry.CurrentObserver, Is.Null);
            Assert.That(manager.List(), Is.Empty);

            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task KeyLogOnlyInstallsStandaloneObserver()
        {
            var registry = new ChannelCaptureRegistry();
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(registry),
                CreateTempPath("base"));

            string keyLogPath = CreateTempPath("keys.uakeys.json");
            await using var service = new PcapEnvironmentAutoStartHostedService(
                new PcapEnvironmentSnapshot(PcapFilePath: null, KeyLogFilePath: keyLogPath),
                manager,
                registry);

            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(registry.CurrentObserver, Is.InstanceOf<StandaloneKeyLogObserver>());
            Assert.That(manager.List(), Is.Empty);

            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public async Task PcapFileSetStartsCaptureSession()
        {
            var registry = new ChannelCaptureRegistry();
            string sessionFolder = CreateTempPath("session");
            Directory.CreateDirectory(sessionFolder);
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(registry),
                sessionFolder);

            string pcapPath = Path.Combine(sessionFolder, "cap.pcap");
            await using var service = new PcapEnvironmentAutoStartHostedService(
                new PcapEnvironmentSnapshot(PcapFilePath: pcapPath, KeyLogFilePath: null),
                manager,
                registry);

            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(manager.List(), Has.Count.EqualTo(1));
            CaptureSession session = manager.List()[0];
            Assert.That(session.State, Is.EqualTo(CaptureSessionState.Running));
            Assert.That(registry.CurrentObserver, Is.Not.Null);

            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

            // Capture session is stopped; observer is uninstalled by the
            // capture source itself.
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public async Task BothVariablesUseExplicitKeyLogPath()
        {
            var registry = new ChannelCaptureRegistry();
            string sessionFolder = CreateTempPath("session");
            Directory.CreateDirectory(sessionFolder);
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(registry),
                sessionFolder);

            string pcapPath = Path.Combine(sessionFolder, "cap.pcap");
            string keyLogPath = Path.Combine(sessionFolder, "explicit.uakeys.json");

            await using var service = new PcapEnvironmentAutoStartHostedService(
                new PcapEnvironmentSnapshot(PcapFilePath: pcapPath, KeyLogFilePath: keyLogPath),
                manager,
                registry);

            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);

            CaptureSession session = manager.List().Single();
            Assert.That(session.Request.PcapFilePath, Is.EqualTo(pcapPath));
            Assert.That(session.Request.KeyLogFilePath, Is.EqualTo(keyLogPath));

            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task StopAsyncIsIdempotent()
        {
            var registry = new ChannelCaptureRegistry();
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(registry),
                CreateTempPath("base"));

            string keyLogPath = CreateTempPath("keys.uakeys.json");
            await using var service = new PcapEnvironmentAutoStartHostedService(
                new PcapEnvironmentSnapshot(PcapFilePath: null, KeyLogFilePath: keyLogPath),
                manager,
                registry);

            await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
            // Dispose pattern must also tolerate a final stop.
        }

        [Test]
        public void ConstructorThrowsOnNullSessionManager()
        {
            var registry = new ChannelCaptureRegistry();
            Assert.That(
                () => new PcapEnvironmentAutoStartHostedService(
                    new PcapEnvironmentSnapshot(null, null),
                    sessionManager: null!,
                    registry),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task ConstructorThrowsOnNullRegistry()
        {
            var registry = new ChannelCaptureRegistry();
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(registry),
                CreateTempPath("base"));

            Assert.That(
                () => new PcapEnvironmentAutoStartHostedService(
                    new PcapEnvironmentSnapshot(null, null),
                    manager,
                    registry: null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }

    /// <summary>
    /// End-to-end tests that exercise
    /// <c>AddPcapFromEnvironment</c> through process-wide
    /// environment variables. The variables are restored in
    /// <see cref="TearDown"/> so other test fixtures running in the same
    /// process are not affected.
    /// </summary>
    [TestFixture]
    public sealed class AddPcapFromEnvironmentTests : TempDirectoryFixture
    {
        private string? m_priorPcapFile;
        private string? m_priorKeyLogFile;

        [SetUp]
        public void CaptureEnvironmentAndBinding()
        {
            m_priorPcapFile = Environment.GetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaPcapFile);
            m_priorKeyLogFile = Environment.GetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaKeyLogFile);
            // Leave the env vars unset by default so each test only sees
            // what it explicitly opts into.
            Environment.SetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaPcapFile,
                value: null);
            Environment.SetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaKeyLogFile,
                value: null);
        }

        [TearDown]
        public void RestoreEnvironmentAndBinding()
        {
            Environment.SetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaPcapFile,
                m_priorPcapFile);
            Environment.SetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaKeyLogFile,
                m_priorKeyLogFile);
        }

        [Test]
        public void NullServicesThrows()
        {
            IServiceCollection? services = null;

            Assert.That(
                () => services!.AddPcapFromEnvironment(),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("services"));
        }

        [Test]
        public void NullConfigureThrows()
        {
            var services = new ServiceCollection();
            Action<PcapOptions>? configure = null;

            Assert.That(
                () => services.AddPcapFromEnvironment(configure!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public async Task NoEnvVarsRegistersHostedServiceButDoesNothingOnStart()
        {
            var services = new ServiceCollection();
            services.AddPcapFromEnvironment();
            await using ServiceProvider provider = services.BuildServiceProvider();

            IHostedService[] hostedServices = provider.GetServices<IHostedService>().ToArray();
            IHostedService autoStart = hostedServices.Single(static s
                => s is PcapEnvironmentAutoStartHostedService);

            await autoStart.StartAsync(CancellationToken.None).ConfigureAwait(false);

            var registry = provider.GetRequiredService<IChannelCaptureRegistry>();
            Assert.That(registry.CurrentObserver, Is.Null);

            await autoStart.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task KeyLogEnvVarInstallsObserverThroughDiPipeline()
        {
            string keyLogPath = CreateTempPath("keys.uakeys.json");
            Environment.SetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaKeyLogFile,
                keyLogPath);

            var services = new ServiceCollection();
            services.AddPcapFromEnvironment();
            await using ServiceProvider provider = services.BuildServiceProvider();

            IHostedService autoStart = provider.GetServices<IHostedService>()
                .Single(static s => s is PcapEnvironmentAutoStartHostedService);

            await autoStart.StartAsync(CancellationToken.None).ConfigureAwait(false);

            var registry = provider.GetRequiredService<IChannelCaptureRegistry>();
            Assert.That(registry.CurrentObserver, Is.InstanceOf<StandaloneKeyLogObserver>());

            await autoStart.StopAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public async Task PcapEnvVarOverridesBaseFolderToParentDirectory()
        {
            string sessionFolder = CreateTempPath("env-session");
            Directory.CreateDirectory(sessionFolder);
            string pcapPath = Path.Combine(sessionFolder, "cap.pcap");

            Environment.SetEnvironmentVariable(
                PcapEnvironmentVariableNames.OpcuaPcapFile,
                pcapPath);

            var services = new ServiceCollection();
            services.AddPcapFromEnvironment(options =>
            {
                // The user's BaseFolder is intentionally set to a path
                // that does not contain pcapPath; the env-var override
                // must win so the auto-start succeeds.
                options.BaseFolder = CreateTempPath("user-base");
            });
            await using ServiceProvider provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<PcapOptions>();
            Assert.That(
                options.BaseFolder,
                Is.EqualTo(Path.GetFullPath(sessionFolder)).IgnoreCase);
        }

        [Test]
        public async Task NeitherVarSetLeavesUserConfigureBaseFolder()
        {
            string userBase = CreateTempPath("user-base");
            Directory.CreateDirectory(userBase);

            var services = new ServiceCollection();
            services.AddPcapFromEnvironment(options
                => options.BaseFolder = userBase);
            await using ServiceProvider provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<PcapOptions>();
            Assert.That(options.BaseFolder, Is.EqualTo(userBase));
        }
    }
}
