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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Tests the two materialization extension points a protocol driver needs: contributing custom
    /// DataTypes that have no NodeSet to import, and resolving companion-specification NodeSets a
    /// Thing Description depends on but the server does not already know.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotMaterializationExtensibilityTests
    {
        [SetUp]
        public void SetUp()
        {
            m_registry = new WotRegistryService();
            m_host = new FakeWotProjectionHost();
            m_converter = new FakeWotDocumentConverter();
        }

        [TearDown]
        public void TearDown()
        {
            m_coordinator?.Dispose();
            m_registry.Dispose();
        }

        [Test]
        public async Task AContributorAddsNodesToEveryConvertedDocumentAsync()
        {
            var contributor = new RecordingContributor();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_host,
                documentConverter: m_converter,
                nodeSetContributors: [contributor]);

            await RegisterAsync("a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(contributor.Resources, Does.Contain("a"),
                    "A contributor must run once for the resource being materialized.");
                Assert.That(contributor.SawNodesBeforeContributing, Is.True,
                    "A contributor must run after conversion, so the converted nodes are present.");
            });
        }

        [Test]
        public async Task NoContributorLeavesMaterializationUnchangedAsync()
        {
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, m_host, documentConverter: m_converter);

            await RegisterAsync("a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "Registering no contributor must leave the previous behaviour untouched.");
        }

        [Test]
        public async Task AContributedDataTypeReachesTheProjectionAsync()
        {
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_host,
                documentConverter: m_converter,
                nodeSetContributors: [new DataTypeContributor()]);

            await RegisterAsync("a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            string projected = string.Join(
                "\n",
                m_host.Operations.Where(o => o.Document is not null).SelectMany(o => o.Document!.Sources)
                    .Select(s => Encoding.UTF8.GetString(s.NodeSetXml)));

            Assert.That(projected, Does.Contain(DataTypeContributor.BrowseName),
                "A DataType contributed before materialization must reach the projection, which " +
                "is what lets a uav:mapByFieldPath mapping resolve against a controller UDT.");
        }

        [Test]
        public async Task AnUnresolvedDependencyNamespaceIsReportedNotSilentlyDroppedAsync()
        {
            m_converter.RequiredNamespace = kDependencyNamespace;
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_host,
                documentConverter: m_converter,
                nodeSetResolver: new DecliningResolver());

            await RegisterAsync("a").ConfigureAwait(false);
            WotRefreshResult result = await m_coordinator
                .RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(
                result.Results.Any(r => r.Outcome != WoTOutcomeEnum.Success),
                Is.True,
                "A namespace nothing can resolve must surface, so an operator sees what is missing.");
        }

        [Test]
        public async Task AResolvedDependencyIsProjectedBeforeTheDocumentThatNeedsItAsync()
        {
            m_converter.RequiredNamespace = kDependencyNamespace;
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_host,
                documentConverter: m_converter,
                nodeSetResolver: new StubResolver(kDependencyNamespace));

            await RegisterAsync("a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            WotProjectionDocument? document = m_host.Operations.LastOrDefault(o => o.Document is not null)?.Document;
            Assert.That(document, Is.Not.Null);
            Assert.That(document!.Sources[0].Name, Is.EqualTo(kDependencyNamespace),
                "A resolved dependency must be materialized before the document that requires it.");
        }

        [Test]
        public async Task NoResolverLeavesAKnownDocumentUnaffectedAsync()
        {
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, m_host, documentConverter: m_converter);

            await RegisterAsync("a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(m_host.AddCount, Is.EqualTo(1),
                "A document with no unmet dependency must not need a resolver at all.");
        }

        private ValueTask<WotRegistryMutationResult> RegisterAsync(string resourceId)
        {
            return m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:" + resourceId))
            });
        }

        private const string kDependencyNamespace = "urn:test:dependency";

        private WotRegistryService m_registry = null!;
        private FakeWotProjectionHost m_host = null!;
        private FakeWotDocumentConverter m_converter = null!;
        private WotMaterializationCoordinator? m_coordinator;

        private sealed class RecordingContributor : IWotNodeSetContributor
        {
            public List<string> Resources { get; } = [];

            public bool SawNodesBeforeContributing { get; private set; }

            public ValueTask ContributeAsync(
                WotResource resource,
                UANodeSet nodeSet,
                CancellationToken cancellationToken = default)
            {
                Resources.Add(resource.ResourceId);
                SawNodesBeforeContributing = nodeSet.Items is { Length: > 0 };
                return default;
            }
        }

        private sealed class DataTypeContributor : IWotNodeSetContributor
        {
            public const string BrowseName = "ContributedUdt";

            public ValueTask ContributeAsync(
                WotResource resource,
                UANodeSet nodeSet,
                CancellationToken cancellationToken = default)
            {
                var dataType = new UADataType
                {
                    NodeId = "ns=1;i=9000",
                    BrowseName = "1:" + BrowseName,
                    DisplayName = [new Opc.Ua.Export.LocalizedText { Value = BrowseName }]
                };
                nodeSet.Items = [.. nodeSet.Items ?? [], dataType];
                return default;
            }
        }

        private sealed class DecliningResolver : IWotNodeSetResolver
        {
            public ValueTask<Stream?> TryResolveAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                // Returning null is the contract's way of declining; it is not an error.
                return new ValueTask<Stream?>((Stream?)null);
            }
        }

        private sealed class StubResolver : IWotNodeSetResolver
        {
            public StubResolver(string namespaceUri)
            {
                m_namespaceUri = namespaceUri;
            }

            public ValueTask<Stream?> TryResolveAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                if (!string.Equals(namespaceUri, m_namespaceUri, StringComparison.Ordinal))
                {
                    return new ValueTask<Stream?>((Stream?)null);
                }
                string xml =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                    "<NamespaceUris><Uri>" + m_namespaceUri + "</Uri></NamespaceUris>" +
                    "<Models><Model ModelUri=\"" + m_namespaceUri + "\" Version=\"1.0\" " +
                    "PublicationDate=\"2026-01-01T00:00:00Z\" /></Models>" +
                    "</UANodeSet>";
                return new ValueTask<Stream?>(
                    (Stream?)new MemoryStream(Encoding.UTF8.GetBytes(xml)));
            }

            private readonly string m_namespaceUri;
        }
    }
}
