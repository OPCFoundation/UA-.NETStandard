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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for <see cref="AliasNameNodeManager"/> built around a
    /// mocked <see cref="IServerInternal"/> following the pattern used by
    /// <c>AsyncCustomNodeManagerTests</c>.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameNodeManagerTests
    {
        private const string c_namespaceUri = "http://example.org/AliasNames/";

        private Mock<IServerInternal> m_mockServer;
        private NamespaceTable m_namespaceTable;
        private ApplicationConfiguration m_configuration;
        private InMemoryAliasNameStore m_store;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(c_namespaceUri);

            var stringTable = new StringTable();
            var typeTable = new TypeTable(m_namespaceTable);

            ITelemetryContext telemetry = Opc.Ua.Tests.NUnitTelemetryContext.Create();

            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(stringTable);
            m_mockServer.Setup(s => s.TypeTree).Returns(typeTable);
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.Telemetry).Returns(telemetry);
            m_mockServer.Setup(s => s.DefaultSystemContext)
                .Returns(new ServerSystemContext(m_mockServer.Object));

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            // No additional type-tree subtype seeding is needed because
            // the tests pass NodeId.Null / References as the reference
            // type filter — InMemoryAliasNameStore short-circuits in that
            // case and never invokes ITypeTable.IsTypeOf.

            var categoryId = new NodeId("MyCategory", 1);
            var descriptor = new AliasNameCategoryDescriptor(
                categoryId,
                new QualifiedName("MyCategory", 1),
                AliasNameCapabilities.All);
            m_store = new InMemoryAliasNameStore([descriptor]);
        }

        [TearDown]
        public void TearDown()
        {
            m_store?.Dispose();
        }

        private AliasNameNodeManager CreateManager(
            AliasNameNodeManagerOptions options = null)
        {
            return new AliasNameNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_store,
                options ?? new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false
                });
        }

        [Test]
        public void CreateAddressSpaceBuildsCategoryWithAllOptionalChildren()
        {
            using AliasNameNodeManager manager = CreateManager();
            var externalReferences =
                new Dictionary<NodeId, IList<IReference>>();
            manager.CreateAddressSpace(externalReferences);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(
                    new NodeId("MyCategory", 1));
            Assert.That(category, Is.Not.Null);
            Assert.That(category.FindAlias, Is.Not.Null);
            Assert.That(category.FindAliasVerbose, Is.Not.Null);
            Assert.That(category.AddAliasesToCategory, Is.Not.Null);
            Assert.That(category.DeleteAliasesFromCategory, Is.Not.Null);
            Assert.That(category.LastChange, Is.Not.Null);

            Assert.That(externalReferences.TryGetValue(
                ObjectIds.Aliases, out IList<IReference> refs), Is.True);
            bool hasOrganizes = false;
            foreach (IReference r in refs)
            {
                if (r.ReferenceTypeId.Equals(ReferenceTypeIds.Organizes)
                    && r.TargetId.Equals(category.NodeId))
                {
                    hasOrganizes = true;
                    break;
                }
            }
            Assert.That(hasOrganizes, Is.True,
                "Expected an Organizes external reference from Aliases to category.");
        }

        [Test]
        public void OmittingCapabilitiesSkipsOptionalChildren()
        {
            m_store.Dispose();
            var minimal = new AliasNameCategoryDescriptor(
                new NodeId("Minimal", 1),
                new QualifiedName("Minimal", 1),
                AliasNameCapabilities.None);
            m_store = new InMemoryAliasNameStore([minimal]);

            using AliasNameNodeManager manager = CreateManager();
            manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(
                    new NodeId("Minimal", 1));
            Assert.That(category, Is.Not.Null);
            Assert.That(category.FindAlias, Is.Not.Null,
                "Mandatory FindAlias must always be present.");
            Assert.That(category.FindAliasVerbose, Is.Null);
            Assert.That(category.AddAliasesToCategory, Is.Null);
            Assert.That(category.DeleteAliasesFromCategory, Is.Null);
            Assert.That(category.LastChange, Is.Null);
        }

        [Test]
        public async Task FindAliasHandlerReturnsStoreAliasesAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            var categoryId = new NodeId("MyCategory", 1);
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("Alpha",
                        new ExpandedNodeId("Tgt", 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);
            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant("%"),
                    new Variant(NodeId.Null)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await category.FindAlias!.CallAsync(
                manager.SystemContext,
                categoryId,
                input,
                argumentErrors,
                output,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.Null.Or.EqualTo(ServiceResult.Good));
            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(output[0].TryGetStructure(
                out ArrayOf<AliasNameDataType> aliases), Is.True);
            Assert.That(aliases.Count, Is.EqualTo(1));
            Assert.That(aliases[0].AliasName.Name, Is.EqualTo("Alpha"));
        }

        [Test]
        public async Task LastChangeReflectsStoreUpdatesAsync()
        {
            using AliasNameNodeManager manager = CreateManager();
            manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);

            uint initial = (category.LastChange!.Value);
            await m_store.AddAliasesAsync(categoryId,
                [
                    new AliasAddRequest("LC1",
                        new ExpandedNodeId("Tgt", 1),
                        null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(category.LastChange.Value, Is.Not.EqualTo(initial));
        }

        private static readonly string[] s_singleX = ["X"];
        private static readonly ExpandedNodeId[] s_singleT = [new("T", 1)];
        private static readonly string[] s_singleEmpty = [""];

        [Test]
        public async Task AddAliasesAnonymousIsRejectedWithBadUserAccessDeniedAsync()
        {
            using AliasNameNodeManager manager = CreateManager(
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = false,
                    RequireSecurityAdminForMutations = true
                });
            manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            var categoryId = new NodeId("MyCategory", 1);
            AliasNameCategoryState category = manager
                .FindPredefinedNode<AliasNameCategoryState>(categoryId);

            var input = new ArrayOf<Variant>(
                new[]
                {
                    new Variant(s_singleX.ToArrayOf()),
                    new Variant(s_singleT.ToArrayOf()),
                    new Variant(s_singleEmpty.ToArrayOf()),
                    new Variant(ReferenceTypeIds.AliasFor)
                });
            var argumentErrors = new List<ServiceResult>();
            var output = new List<Variant>
            {
                new Variant(System.Array.Empty<StatusCode>().ToArrayOf())
            };

            ServiceResult result = await category.AddAliasesToCategory!.CallAsync(
                manager.SystemContext,
                categoryId,
                input,
                argumentErrors,
                output,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }
    }
}
