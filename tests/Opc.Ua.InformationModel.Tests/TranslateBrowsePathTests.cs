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
    /// compliance tests for View Service Set – TranslateBrowsePathsToNodeIds.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewTranslateBrowsePath")]
    public class TranslateBrowsePathTests : TestFixture
    {
        [Description("Translate a single-element path from RootFolder to Objects.")]
        [Test]
        public async Task TranslateBrowsePath001SingleElementPathAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Objects")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Single-element path to Objects should succeed.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            var targetId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, Session.NamespaceUris);
            Assert.That(targetId, Is.EqualTo(ObjectIds.ObjectsFolder));
        }

        [Description("Translate a multi-element path from RootFolder → Objects → Server.")]
        [Test]
        public async Task TranslateBrowsePath002MultiElementPathAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Objects")
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Multi-element path to Server should succeed.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            var targetId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, Session.NamespaceUris);
            Assert.That(targetId, Is.EqualTo(ObjectIds.Server));
        }

        [Description("Translate a path from RootFolder to Types folder.")]
        [Test]
        public async Task TranslateBrowsePath003PathToTypesFolderAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Types")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Path to Types folder should succeed.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            var targetId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, Session.NamespaceUris);
            Assert.That(targetId, Is.EqualTo(ObjectIds.TypesFolder));
        }

        [Description("Translate a path from RootFolder to Views folder.")]
        [Test]
        public async Task TranslateBrowsePath004PathToViewsFolderAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Views")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Path to Views folder should succeed.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            var targetId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, Session.NamespaceUris);
            Assert.That(targetId, Is.EqualTo(ObjectIds.ViewsFolder));
        }

        [Description("Translate two paths in one call: Root→Objects and Root→Types.")]
        [Test]
        public async Task TranslateBrowsePath005MultiplePathsInOneCallAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Objects")
                            }
                        }.ToArrayOf()
                    }
                },
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Types")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "First path (Objects) should succeed.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            Assert.That(StatusCode.IsGood(response.Results[1].StatusCode), Is.True,
                "Second path (Types) should succeed.");
            Assert.That(response.Results[1].Targets.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Description("Translate a deep path: Root → Objects → Server → ServerStatus.")]
        [Test]
        public async Task TranslateBrowsePath006DeepPathAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Objects")
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("ServerStatus")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Deep path to ServerStatus should succeed.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            var targetId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, Session.NamespaceUris);
            Assert.That(targetId, Is.EqualTo(VariableIds.Server_ServerStatus));
        }

        [Description("Use an invalid starting node. Expect BadNodeIdUnknown.")]
        [Test]
        public async Task TranslateBrowsePathErr001InvalidStartingNodeAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = Constants.InvalidNodeId,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("Objects")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "Invalid starting node should return BadNodeIdUnknown.");
        }

        [Description("Use an empty relative path (no elements). Expect a Bad status.")]
        [Test]
        public async Task TranslateBrowsePathErr002EmptyBrowsePathAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath()
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Empty browse path should return a Bad status.");
        }

        [Description("Translate a path with a non-existent target name. Expect BadNoMatch.")]
        [Test]
        public async Task TranslateBrowsePathErr003InvalidTargetNameAsync()
        {
            var paths = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.RootFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentChild_XYZ")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode,
                Is.EqualTo(StatusCodes.BadNoMatch),
                "Non-existent target name should return BadNoMatch.");
        }

        [Description("Translate a path from ObjectsFolder to Server.")]
        [Test]
        public async Task TranslateBrowsePath007PathFromObjectsToServerAsync()
        {
            var paths = new BrowsePath[]
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
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Path from ObjectsFolder to Server should succeed.");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));

            var targetId = ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, Session.NamespaceUris);
            Assert.That(targetId, Is.EqualTo(ObjectIds.Server));
        }
    }
}
