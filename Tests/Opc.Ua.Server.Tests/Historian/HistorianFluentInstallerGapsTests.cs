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

#nullable enable

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Gap-coverage tests for <see cref="HistorianBuilder"/>,
    /// <see cref="HistorianFluentExtensions"/>, and
    /// <see cref="HistoricalDataConfigurationInstaller"/>. Targets
    /// branches not covered by <c>HistorianFluentTests</c> or
    /// <c>HistorianCoverageTests</c>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianFluentInstallerGapsTests
    {
        private const ushort Ns = 2;

        [Test]
        public void HistorianBuilderHistorizeNullVariableThrowsArgumentNullException()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);
            builder.UseInMemory();

            Assert.That(
                () => builder.Historize(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void HistorianBuilderRegisterForNamespaceBindsToNamespaceUri()
        {
            var nsTable = new NamespaceTable();
            const string nsUri = "urn:test:ns-bind";
            ushort nsIndex = (ushort)nsTable.Append(nsUri);

            IServerInternal server = CreateServerWithRegistry(nsTable);
            var builder = new HistorianBuilder(server);
            InMemoryHistorianProvider provider = builder.UseInMemory();

            builder.RegisterForNamespace(nsUri);

            var nodeInNs = new NodeId("node.in.ns", nsIndex);
            var nodeOutsideNs = new NodeId("node.outside", 0);
            IHistorianProviderRegistry registry =
                ((IHistorianRegistryProvider)server).HistorianRegistry;

            Assert.That(registry.Resolve(nodeInNs), Is.SameAs(provider),
                "Node in the registered namespace should resolve to the provider.");
            Assert.That(registry.Resolve(nodeOutsideNs), Is.Null,
                "Node outside the namespace should not resolve to the provider.");
        }

        [Test]
        public void HistorianBuilderRegisterForNamespaceWithoutProviderThrows()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);

            Assert.That(
                () => builder.RegisterForNamespace("urn:some:ns"),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task HistorianBuilderDisposeAsyncCompletesWithoutErrorAsync()
        {
            IServerInternal server = CreateServerWithRegistry();
            var ctx = new ServerSystemContext(server);
            var builder = new HistorianBuilder(server);
            builder.UseInMemory();

            BaseDataVariableState variable = CreateVariable("disp.var");
            builder.Historize(
                variable,
                systemContext: ctx,
                autoCapture: true);

            // DisposeAsync should flush the sink, detach handlers,
            // and complete without throwing.
            await builder.DisposeAsync().ConfigureAwait(false);

            // A second dispose is also safe (idempotent).
            await builder.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public void HistorianBuilderHistorizeWithAutoCaptureFalseDoesNotRequireSystemContext()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);
            builder.UseInMemory();

            BaseDataVariableState variable = CreateVariable("nocap.var");

            // autoCapture: false skips AttachAutoCapture, so no
            // systemContext is needed — passing null is valid.
            builder.Historize(
                variable,
                systemContext: null,
                autoCapture: false);

            Assert.That(variable.Historizing, Is.True);
            Assert.That(
                (byte)(variable.AccessLevel & AccessLevels.HistoryRead),
                Is.EqualTo(AccessLevels.HistoryRead));
        }

        [Test]
        public void HistorianBuilderHistorizeInstallConfigOnBrowseAttachesPopulateHandler()
        {
            IServerInternal server = CreateServerWithRegistry();
            var builder = new HistorianBuilder(server);
            builder.UseInMemory();

            BaseDataVariableState variable = CreateVariable("browse.var");
            builder.Historize(
                variable,
                installConfigurationOnBrowse: true,
                autoCapture: false);

            Assert.That(variable.OnPopulateBrowser, Is.Not.Null,
                "installConfigurationOnBrowse should attach an OnPopulateBrowser handler.");
        }

        [Test]
        public void FluentUseHistorianWithNullBuilderThrowsArgumentNullException()
        {
            INodeManagerBuilder? nullBuilder = null;

            Assert.That(
                () => HistorianFluentExtensions.UseHistorian(nullBuilder!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void HistorianBuilderHistorizeCreatesAnnotationsPropertyWhenCapabilitiesAdvertise()
        {
            IServerInternal server = CreateServerWithRegistry();
            var ctx = new ServerSystemContext(server);
            var builder = new HistorianBuilder(server);
            builder.UseInMemory();

            BaseDataVariableState variable = CreateVariable("annot.var");
            var capabilities = new HistorianNodeCapabilities
            {
                InsertAnnotation = true
            };

            builder.Historize(
                variable,
                systemContext: ctx,
                capabilities: capabilities,
                autoCapture: false);

            var browseName = new QualifiedName(BrowseNames.Annotations);
            BaseInstanceState? annotations = variable.FindChild(ctx, browseName);
            Assert.That(annotations, Is.Not.Null,
                "Annotations property should be created when InsertAnnotation is true.");
            Assert.That(annotations, Is.InstanceOf<PropertyState>());
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

        private static IServerInternal CreateServerWithRegistry(
            NamespaceTable? nsTable = null)
        {
            nsTable ??= new NamespaceTable();

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
