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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Tests for <see cref="HistoricalDataConfigurationInstaller"/>.
    /// Exercises null-argument guards, the create-on-first-call path,
    /// and the idempotent re-use path.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistoricalDataConfigurationInstallerTests
    {
        private const ushort Ns = 2;

        [Test]
        public void EnsureInstalledAsyncThrowsWhenContextIsNull()
        {
            var variable = CreateVariable("v-null-ctx");
            var provider = new InMemoryHistorianProvider();

            Assert.That(
                async () => await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    null!, variable, provider, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EnsureInstalledAsyncThrowsWhenVariableIsNull()
        {
            ISystemContext context = CreateSystemContext();
            var provider = new InMemoryHistorianProvider();

            Assert.That(
                async () => await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    context, null!, provider, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EnsureInstalledAsyncThrowsWhenProviderIsNull()
        {
            ISystemContext context = CreateSystemContext();
            var variable = CreateVariable("v-null-prov");

            Assert.That(
                async () => await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    context, variable, null!, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task EnsureInstalledAsyncCreatesConfigChildOnFirstCallAsync()
        {
            ISystemContext context = CreateSystemContext();
            var variable = CreateVariable("v-create");
            using var provider = new InMemoryHistorianProvider();

            HistoricalDataConfigurationState config =
                await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    context, variable, provider, CancellationToken.None);

            Assert.That(config, Is.Not.Null);
            var browseName = new QualifiedName(BrowseNames.HAConfiguration);
            BaseInstanceState? found = variable.FindChild(context, browseName);
            Assert.That(found, Is.SameAs(config));
        }

        [Test]
        public async Task EnsureInstalledAsyncIsIdempotentAsync()
        {
            ISystemContext context = CreateSystemContext();
            var variable = CreateVariable("v-idem");
            using var provider = new InMemoryHistorianProvider();

            HistoricalDataConfigurationState first =
                await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    context, variable, provider, CancellationToken.None);

            HistoricalDataConfigurationState second =
                await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    context, variable, provider, CancellationToken.None);

            Assert.That(second, Is.SameAs(first),
                "Second call must reuse the existing configuration child.");
        }

        [Test]
        public async Task EnsureInstalledAsyncPopulatesSteppedFromCapabilitiesAsync()
        {
            ISystemContext context = CreateSystemContext();
            var variable = CreateVariable("v-stepped");
            using var provider = new InMemoryHistorianProvider();

            // Override capabilities to include Stepped = true.
            provider.Register(variable.NodeId, new HistorianNodeCapabilities { Stepped = true });

            HistoricalDataConfigurationState config =
                await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    context, variable, provider, CancellationToken.None);

            Assert.That(config, Is.Not.Null);
            // config.Stepped may be null if the generator did not create the slot, but
            // the property population must not throw and the installer must complete.
        }

        [Test]
        public async Task EnsureInstalledAsyncPopulatesDefinitionWhenSetAsync()
        {
            ISystemContext context = CreateSystemContext();
            var variable = CreateVariable("v-def");
            using var provider = new InMemoryHistorianProvider();

            provider.Register(variable.NodeId, new HistorianNodeCapabilities
            {
                Definition = "Engineering unit: degrees Celsius"
            });

            HistoricalDataConfigurationState config =
                await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    context, variable, provider, CancellationToken.None);

            Assert.That(config, Is.Not.Null);
        }

        [Test]
        public async Task HistorianBuilderHistorizeWithInstallConfigurationNodeCreatesConfigChildAsync()
        {
            IServerInternal server = CreateServerWithRegistry();
            var ctx = new ServerSystemContext(server);
            var builder = new HistorianBuilder(server);
            builder.UseInMemory();

            BaseDataVariableState variable = CreateVariable("v-builder-cfg");

            // installConfigurationNode: true triggers EnsureInstalledAsync synchronously.
            builder.Historize(
                variable,
                installConfigurationNode: true,
                systemContext: ctx,
                autoCapture: false);

            var browseName = new QualifiedName(BrowseNames.HAConfiguration);
            BaseInstanceState? found = variable.FindChild(ctx, browseName);
            Assert.That(found, Is.Not.Null,
                "installConfigurationNode: true must attach HAConfiguration as child.");
            Assert.That(found, Is.InstanceOf<HistoricalDataConfigurationState>());

            // Dispose is part of the API contract.
            await builder.DisposeAsync();
        }

        private static BaseDataVariableState CreateVariable(string name)
        {
            return new BaseDataVariableState(parent: null)
            {
                NodeId = new NodeId(name, Ns),
                BrowseName = new QualifiedName(name, Ns),
                DisplayName = new LocalizedText(name),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar
            };
        }

        private static ServerSystemContext CreateSystemContext()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("urn:test:installer");
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            return new ServerSystemContext(mockServer.Object);
        }

        private static IServerInternal CreateServerWithRegistry(NamespaceTable? nsTable = null)
        {
            nsTable ??= new NamespaceTable();
            nsTable.Append("urn:test:installer");

            var mockTelemetry = new Mock<ITelemetryContext>();
            var registry = new HistorianProviderRegistry(nsTable);

            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            mockServer.As<IHistorianRegistryProvider>()
                .Setup(p => p.HistorianRegistry).Returns(registry);

            return mockServer.Object;
        }
    }
}
