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
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for AliasName type hierarchy verification.
    /// AliasName support is optional; tests gracefully skip when the
    /// server does not expose the relevant nodes.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AliasName")]
    public class AliasNameTests : TestFixture
    {
        [Description("Verify AliasNameCategoryType (i=23456) exists in the server address space by reading its BrowseName attribute.")]
        [Test]
        public async Task VerifyAliasNameCategoryTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                AliasNameCategoryTypeNodeId, Attributes.BrowseName).ConfigureAwait(false);

            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Ignore("AliasNameCategoryType (i=23456) not supported by this server.");
            }

            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName, Is.Not.Null);
            Assert.That(browseName.Name, Is.EqualTo("AliasNameCategoryType"));
        }

        [Description("Verify AliasNameType (i=23455) exists in the server address space by reading its BrowseName attribute.")]
        [Test]
        public async Task VerifyAliasNameTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                AliasNameTypeNodeId, Attributes.BrowseName).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"AliasNameType (i=23455) must be present: {result.StatusCode}");

            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName, Is.Not.Null);
            Assert.That(browseName.Name, Is.EqualTo("AliasNameType"));
        }

        [Description("Browse the Objects folder looking for a child node with BrowseName \"Aliases\". The Aliases folder is optional.")]
        [Test]
        public async Task BrowseServerForAliasesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            ReferenceDescription aliasRef = null;
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                if (r.BrowseName.Name == "Aliases")
                {
                    aliasRef = r;
                    break;
                }
            }

            if (aliasRef == null)
            {
                Assert.Ignore("Server does not expose an 'Aliases' node under Objects.");
            }

            Assert.That(aliasRef.BrowseName.Name, Is.EqualTo("Aliases"));
        }

        [Description("Browse the Objects folder for a \"TagVariables\" child node. This is an optional feature of the AliasName model.")]
        [Test]
        public async Task VerifyTagVariablesObjectExistsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            ReferenceDescription tagRef = null;
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                if (r.BrowseName.Name == "TagVariables")
                {
                    tagRef = r;
                    break;
                }
            }

            if (tagRef == null)
            {
                Assert.Ignore("Server does not expose a 'TagVariables' node under Objects.");
            }

            Assert.That(tagRef.BrowseName.Name, Is.EqualTo("TagVariables"));
        }

        [Description("Verify the AliasFor reference type (i=23469) exists in the server address space by reading its BrowseName.")]
        [Test]
        public async Task VerifyAliasForReferenceTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                AliasForReferenceTypeNodeId, Attributes.BrowseName).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"AliasFor reference type (i=23469) must be present: {result.StatusCode}");

            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName, Is.Not.Null);
            Assert.That(browseName.Name, Is.EqualTo("AliasFor"));
        }

        [Description("Translate the browse path \"Objects → Server\" to verify the well-known Server node resolves correctly.")]
        [Test]
        public async Task TranslateBrowsePathForWellKnownNodeAsync()
        {
            ArrayOf<BrowsePath> browsePaths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server", 0)
                            }
                        }.ToArrayOf()
                    }
                }
            }.ToArrayOf();

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Path to Server node should resolve successfully.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            var targetId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, Session.NamespaceUris);
            Assert.That(targetId, Is.EqualTo(ObjectIds.Server));
        }

        [Description("Resolve the path \"Server/NamespaceArray\" via TranslateBrowsePaths.")]
        [Test]
        public async Task TranslateBrowsePathForNamespaceArrayAsync()
        {
            ArrayOf<BrowsePath> browsePaths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server", 0)
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("NamespaceArray", 0)
                            }
                        }.ToArrayOf()
                    }
                }
            }.ToArrayOf();

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Path to Server/NamespaceArray should resolve.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Description("Resolve the path \"Server/ServerStatus\" via TranslateBrowsePaths.")]
        [Test]
        public async Task TranslateBrowsePathForServerStatusAsync()
        {
            ArrayOf<BrowsePath> browsePaths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server", 0)
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("ServerStatus", 0)
                            }
                        }.ToArrayOf()
                    }
                }
            }.ToArrayOf();

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Path to Server/ServerStatus should resolve.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Description("Resolve the path \"Server/ServerStatus/State\" via TranslateBrowsePaths.")]
        [Test]
        public async Task TranslateBrowsePathForServerStateAsync()
        {
            ArrayOf<BrowsePath> browsePaths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server", 0)
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("ServerStatus", 0)
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("State", 0)
                            }
                        }.ToArrayOf()
                    }
                }
            }.ToArrayOf();

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Path to Server/ServerStatus/State should resolve.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Description("Attempt to resolve an invalid path \"Server/NonExistentChild\" and verify the server returns a failure status.")]
        [Test]
        public async Task TranslateBrowsePathInvalidPathAsync()
        {
            ArrayOf<BrowsePath> browsePaths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server", 0)
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentChild", 0)
                            }
                        }.ToArrayOf()
                    }
                }
            }.ToArrayOf();

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, browsePaths, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.False,
                "An invalid browse path should not resolve successfully.");
        }

        /// <summary>
        /// AliasNameCategoryType (i=23456)
        /// </summary>
        private static readonly NodeId AliasNameCategoryTypeNodeId = new(23456);

        /// <summary>
        /// AliasNameType (i=23455) - the ObjectType, not the data type at i=23468.
        /// </summary>
        private static readonly NodeId AliasNameTypeNodeId = new(23455);

        /// <summary>
        /// AliasFor ReferenceType (i=23469). The OPC UA spec defines "AliasFor"
        /// as the reference type connecting an alias to its target node;
        /// i=23470 is the Aliases Object instance.
        /// </summary>
        private static readonly NodeId AliasForReferenceTypeNodeId = new(23469);

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
