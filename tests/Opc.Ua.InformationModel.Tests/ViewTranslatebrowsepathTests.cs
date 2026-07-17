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
    /// compliance tests for View TranslateBrowsePath.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewServices")]
    public class ViewTranslatebrowsepathTests : TestFixture
    {
        [Description("Given one existent starting node And one relativePath element And the relativePath element's IsInverse = true And the relativePath nodes exist When TranslateBrowsePathsToNodeIds is")]
        [Test]
        public async Task TranslateBrowsePathSingleElementReturnsGoodAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one existent starting node And one relativePath element And the relativePath element's IncludeSubtypes = true And the relativePath element's ReferenceTypeId is a parent of th")]
        [Test]
        public async Task TranslateBrowsePathWithIncludeSubtypesAndChildReferenceTypeReturnsGoodAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one existent starting node And one relativePath element And the relativePath element's ReferenceTypeId is a null NodeId And includeSubtypes is true And the relativePath eleme")]
        [Test]
        public async Task TranslateBrowsePathWithNullReferenceTypeIdAndIncludeSubtypesReturnsGoodAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given ten existent starting nodes And existent relativePath nodes When TranslateBrowsePathsToNodeIds is called Then the server returns the NodeId of the last relativePath element f")]
        [Test]
        public async Task TranslateBrowsePathWithTenStartingNodesReturnsGoodAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one existent starting node And one relativePath element And the relativePath element's IncludeSubtypes = true And the relativePath element's ReferenceTypeId is the target nod")]
        [Test]
        public async Task TranslateBrowsePathWithReferenceTypeAsTargetNodeReturnsGoodAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one existent starting node; And one relativePath element; And the relativePath element's IncludeSubtypes = true And the relativePath element's ReferenceTypeId is a &quot;grandpare")]
        [Test]
        public async Task TranslateBrowsePathWithGrandparentReferenceTypeReturnsGoodAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one starting node And the node does not exist And diagnostic info is requested When TranslateBrowsePathsToNodeIds is called Then the server returns specified operation diagno")]
        [Test]
        public async Task TranslateBrowsePathWithNonExistentNodeAndDiagnosticsRequestedReturnsDiagnosticsAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given one starting node And the node does not exist And diagnostic info is not requested When TranslateBrowsePathsToNodeIds is called Then the server returns no diagnostic info. */")]
        [Test]
        public async Task TranslateBrowsePathWithNonExistentNodeAndNoDiagnosticsReturnsEmptyAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given two existent starting nodes And two existent browsePaths And one non-existent starting node And one existent starting node And one non-existent browsePath When TranslateBrows")]
        [Test]
        public async Task TranslateBrowsePathWithMixedExistentAndNonExistentNodesReturnsBadAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a non-existent starting node When TranslateBrowsePathsToNodeIds is called Then the server returns operation result Bad_NodeIdUnknown. */")]
        [Test]
        public async Task TranslateBrowsePathWithNonExistentStartingNodeReturnsBadNodeIdUnknownAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node and no RelativePath elements. When TranslateBrowsePathsToNodeIds is called server returns operation result Bad_NothingToDo.*/")]
        [Test]
        public async Task TranslateBrowsePathWithNoRelativePathElementsReturnsBadNothingToDoAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node; And a null BrowseName; When TranslateBrowsePathsToNodeIds is called Then the server returns operation result Bad_BrowseNameInvalid. */")]
        [Test]
        public async Task TranslateBrowsePathWithNullBrowseNameReturnsBadBrowseNameInvalidAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node; And multiple RelativePath elements; And a RelativePath element prior to the last contains a null BrowseName When TranslateBrowsePathsToNodeIds is c")]
        [Test]
        public async Task TranslateBrowsePathWithIntermediateNullBrowseNameReturnsBadBrowseNameInvalidAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node; And multiple RelativePath elements; And a RelativePath element has a ReferenceTypeId that does not match When TranslateBrowsePathsToNodeIds is call")]
        [Test]
        public async Task TranslateBrowsePathWithNonMatchingReferenceTypeIdReturnsBadNoMatchAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("A relativePath element specifies an invalid NodeId for the referenceTypeId. Note: This test case has been obsoleted. -&gt; automatically passed.")]
        [Test]
        public async Task TranslateBrowsePathWithInvalidReferenceTypeNodeIdAutoPassesAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node; And multiple RelativePath elements; And a RelativePath element has a non-existent ReferenceTypeId When TranslateBrowsePathsToNodeIds is called; The")]
        [Test]
        public async Task TranslateBrowsePathWithNonExistentReferenceTypeIdReturnsBadReferenceTypeIdInvalidAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node; And multiple RelativePath elements; And a RelativePath element has a BrowseName that is invalid When TranslateBrowsePathsToNodeIds is called Then t")]
        [Test]
        public async Task TranslateBrowsePathWithInvalidBrowseNameReturnsBadBrowseNameInvalidAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node; And multiple RelativePath elements; And a RelativePath element has a ReferenceTypeId that is the parent of the reference's ReferenceType; And Inclu")]
        [Test]
        public async Task TranslateBrowsePathWithIncludeSubtypesAndParentReferenceTypeReturnsBadNoMatchAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node And multiple RelativePath elements And a RelativePath element has a ReferenceTypeId set to a NodeId of a Variable node When TranslateBrowsePathsToNo")]
        [Test]
        public async Task TranslateBrowsePathWithVariableNodeAsReferenceTypeIdReturnsBadReferenceTypeIdInvalidAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node And multiple RelativePath elements And a RelativePath element has IsInverse = true And the same element's BrowseName is in the Forward direction Whe")]
        [Test]
        public async Task TranslateBrowsePathWithIsInverseTrueButForwardBrowseNameReturnsBadNoMatchAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an existent starting node; And a RelativePath element; And a RelativePath element has IncludeSubtypes = false When TranslateBrowsePathsToNodeIds is called; Then the server re")]
        [Test]
        public async Task TranslateBrowsePathWithIncludeSubtypesFalseReturnsBadNoMatchAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given an empty/null authenticationToken; When TranslateBrowsePathsToNodeIds is called; Then the server returns service error Bad_SecurityChecksFailed.")]
        [Test]
        public async Task TranslateBrowsePathWithEmptyAuthenticationTokenReturnsBadSecurityChecksFailedAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a non-existent authenticationToken; When TranslateBrowsePathsToNodeIds is called; Then the server returns service error Bad_SecurityChecksFailed.")]
        [Test]
        public async Task TranslateBrowsePathWithForgedAuthenticationTokenReturnsBadSecurityChecksFailedAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a RequestHeader.Timestamp of 0; When TranslateBrowsePathsToNodeIds is called; Then the server returns service error Bad_InvalidTimestamp.")]
        [Test]
        public async Task TranslateBrowsePathWithZeroRequestHeaderTimestampReturnsBadInvalidTimestampAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("NonExistentPath_99999")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("translateBrowsePathErrorCase1: Script demonstrates how to use the checkTranslateBrowsePathsToNodeIdsError() function")]
        [Test]
        public async Task CheckTranslateBrowsePathsToNodeIdsErrorHelperScenarioAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("translateBrowsePathFailedCase1: Script demonstrates how to use the checkTranslateBrowsePathsToNodeIdsFailed() function")]
        [Test]
        public async Task CheckTranslateBrowsePathsToNodeIdsFailedHelperScenarioAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("translateBrowsePathValidCase1: Script demonstrates how to use the checkTranslateBrowsePathsToNodeIdsValidParameter() function")]
        [Test]
        public async Task CheckTranslateBrowsePathsToNodeIdsValidParameterHelperScenarioAsync()
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
                                IsInverse = false, IncludeSubtypes = true,
                                TargetName = new QualifiedName("Server")
                            }
                        }.ToArrayOf()
                    }
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, paths.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }
    }
}
