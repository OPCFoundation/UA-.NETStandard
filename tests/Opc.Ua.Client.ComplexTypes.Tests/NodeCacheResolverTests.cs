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

using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.ComplexTypes.Tests
{
    /// <summary>
    /// Node cache resolver tests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class NodeCacheResolverTests : ClientTestFramework
    {
        public NodeCacheResolverTests()
            : base(Utils.UriSchemeOpcTcp)
        {
        }

        public NodeCacheResolverTests(string uriScheme = Utils.UriSchemeOpcTcp)
            : base(uriScheme)
        {
        }

        public static readonly NodeId[] TypeSystems =
        [
            ObjectIds.OPCBinarySchema_TypeSystem,
            ObjectIds.XmlSchema_TypeSystem
        ];

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUpAsync();
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        public async Task LoadStandardDataTypeSystemAsync()
        {
            var nodeResolver = new NodeCacheResolver(Session, Telemetry);
            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    {
                        System.Collections.Generic.IReadOnlyDictionary<NodeId, DataDictionary> t
                    = await nodeResolver
                            .LoadDataTypeSystem(ObjectIds.ObjectAttributes_Encoding_DefaultBinary)
                            .ConfigureAwait(false);
                    });
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            System.Collections.Generic.IReadOnlyDictionary<NodeId, DataDictionary> typeSystem
                = await nodeResolver
                .LoadDataTypeSystem()
                .ConfigureAwait(false);
            Assert.That(typeSystem, Is.Not.Null);
            typeSystem = await nodeResolver
                .LoadDataTypeSystem(ObjectIds.OPCBinarySchema_TypeSystem)
                .ConfigureAwait(false);
            Assert.That(typeSystem, Is.Not.Null);
            typeSystem = await nodeResolver.LoadDataTypeSystem(ObjectIds.XmlSchema_TypeSystem)
                .ConfigureAwait(false);
            Assert.That(typeSystem, Is.Not.Null);
        }

        [Test]
        public async Task LoadDataTypesAsyncStopsOnCyclicSubtypeGraphAsync()
        {
            NodeId root = new NodeId(2, "Root");
            NodeId childA = new NodeId(2, "A");
            NodeId childB = new NodeId(2, "B");

            var nodeCache = new Mock<INodeCache>(MockBehavior.Strict);
            ArrayOf<ExpandedNodeId> rootExpanded = [NodeId.ToExpandedNodeId(root, Session.NamespaceUris)];
            ArrayOf<ExpandedNodeId> childAExpanded = [NodeId.ToExpandedNodeId(childA, Session.NamespaceUris)];
            ArrayOf<ExpandedNodeId> childBExpanded = [NodeId.ToExpandedNodeId(childB, Session.NamespaceUris)];

            nodeCache.Setup(x => x.GetReferencesAsync(
                    It.IsAny<ArrayOf<ExpandedNodeId>>(),
                    It.IsAny<ArrayOf<NodeId>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<ExpandedNodeId> ids, ArrayOf<NodeId> _, bool _, bool _, CancellationToken _) =>
                {
                    var result = new ArrayOf<INode>();
                    foreach (ExpandedNodeId expandedNodeId in ids)
                    {
                        if (expandedNodeId == rootExpanded[0])
                        {
                            result.Add(new Node { NodeId = childA, BrowseName = new QualifiedName("A") });
                        }
                        else if (expandedNodeId == childAExpanded[0])
                        {
                            result.Add(new Node { NodeId = childB, BrowseName = new QualifiedName("B") });
                        }
                        else if (expandedNodeId == childBExpanded[0])
                        {
                            result.Add(new Node { NodeId = childA, BrowseName = new QualifiedName("A") });
                        }
                    }
                    return new ValueTask<ArrayOf<INode>>(result);
                });

            var nodeResolver = new NodeCacheResolver(Session, nodeCache.Object, Telemetry);
            ArrayOf<INode> dataTypes = await nodeResolver.LoadDataTypesAsync(
                NodeId.ToExpandedNodeId(root, Session.NamespaceUris),
                nestedSubTypes: true,
                filterUATypes: false,
                ct: CancellationToken.None).ConfigureAwait(false);

            Assert.That(dataTypes.Count, Is.EqualTo(2));
            Assert.That(dataTypes.Any(n => n.BrowseName.Name == "A"), Is.True);
            Assert.That(dataTypes.Any(n => n.BrowseName.Name == "B"), Is.True);
        }

        [Test]
        public async Task GetEnumTypeArrayAsyncUsesNamedPropertyAsync()
        {
            NodeId typeId = new NodeId(2, "MyEnum");
            NodeId unrelatedPropertyId = new NodeId(2, "NodeVersion");
            NodeId enumValuesPropertyId = new NodeId(2, "EnumValues");

            var expected = new ArrayOf<ExtensionObject>();
            var nodeCache = new Mock<INodeCache>(MockBehavior.Strict);
            nodeCache.Setup(x => x.GetReferencesAsync(
                    NodeId.ToExpandedNodeId(typeId, Session.NamespaceUris),
                    ReferenceTypeIds.HasProperty,
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<INode>>(new ArrayOf<INode>
                {
                    new Node { NodeId = unrelatedPropertyId, BrowseName = new QualifiedName("NodeVersion") },
                    new Node { NodeId = enumValuesPropertyId, BrowseName = new QualifiedName("EnumValues") }
                }));
            nodeCache.Setup(x => x.GetValueAsync(
                    enumValuesPropertyId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataValue>(new DataValue(new Variant(expected))));

            var resolver = new NodeCacheResolver(Session, nodeCache.Object, Telemetry);
            Variant result = await resolver.GetEnumTypeArrayAsync(
                NodeId.ToExpandedNodeId(typeId, Session.NamespaceUris),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.TryGetValue(out ArrayOf<ExtensionObject> actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        [Order(110)]
        [TestCaseSource(nameof(TypeSystems))]
        public async Task LoadAllServerDataTypeSystemsAsync(NodeId dataTypeSystem)
        {
            // find the dictionary for the description.
            var browser = new Browser(Session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = false,
                NodeClassMask = 0
            };

            ArrayOf<ReferenceDescription> references =
                await browser.BrowseAsync(dataTypeSystem).ConfigureAwait(false);
            Assert.That(references.IsNull, Is.False);

            ILogger logger = Telemetry.CreateLogger<NodeCacheResolverTests>();
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("  Found {Count} references", references.Count);
            }

            // read all type dictionaries in the type system
            var nodeResolver = new NodeCacheResolver(Session, Telemetry);
            foreach (ReferenceDescription r in references.ToList())
            {
                var dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("  ReadDictionary {Name} {Id}", r.BrowseName.Name, dictionaryId);
                }
                DataDictionary dictionaryToLoad = await nodeResolver
                    .LoadDictionaryAsync(dictionaryId, r.BrowseName.Name)
                    .ConfigureAwait(false);

                // internal API for testing only
                byte[] dictionary = await nodeResolver.ReadDictionaryAsync(dictionaryId)
                    .ConfigureAwait(false);
                dictionaryToLoad.Validate(dictionary, true, logger);
            }
        }
    }
}
