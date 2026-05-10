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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for View Service Set – Browse.
    /// Based on test scripts: View Basic 2 001–007.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewBrowse")]
    public class BrowseTests : TestFixture
    {
        [Description("Browse Objects folder with BrowseDirection=Both. Should return both forward and inverse references.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "001")]
        public async Task Browse001DirectionBothAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Browse Both on Objects folder should return references.");
        }

        [Description("Browse Objects folder with BrowseDirection=Forward only.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "002")]
        public async Task Browse002DirectionForwardAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
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
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Browse Forward on Objects folder should return child references.");

            // All references should be forward (IsForward = true)
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.IsForward, Is.True,
                    "All references should be forward when BrowseDirection=Forward.");
            }
        }

        [Description("Browse Objects folder with BrowseDirection=Inverse. Should return the parent reference (Root).")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "003")]
        public async Task Browse003DirectionInverseAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Browse Inverse on Objects folder should return parent references.");

            // All references should be inverse (IsForward = false)
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.IsForward, Is.False,
                    "All references should be inverse when BrowseDirection=Inverse.");
            }
        }

        [Description("Browse with specific ReferenceTypeId filter (Organizes). Only Organizes references should be returned.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "004")]
        public async Task Browse004ReferenceTypeFilterAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Browse with Organizes filter on Objects folder should return references.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes),
                    "All references should be of type Organizes when filtered.");
            }
        }

        [Description("Browse with NodeClassMask filter for Object nodes only.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "006")]
        public async Task Browse005NodeClassMaskFilterAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Browse with Object class mask should return Object nodes.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.NodeClass, Is.EqualTo(NodeClass.Object),
                    "All returned nodes should be of class Object.");
            }
        }

        [Description("Browse the Objects folder and verify expected children. The Objects folder should contain the Server object.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "001")]
        public async Task Browse006ObjectsFolderContainsServerAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
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

            bool foundServer = false;
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                if (rd.BrowseName == new QualifiedName("Server"))
                {
                    foundServer = true;
                    break;
                }
            }

            Assert.That(foundServer, Is.True,
                "Objects folder should contain the Server object.");
        }

        [Description("Browse with RequestedMaxReferencesPerNode = 1 to force continuation points. Then use BrowseNext to retrieve remaining references.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "007")]
        public async Task Browse007ContinuationPointWithBrowseNextAsync()
        {
            BrowseResponse browseResponse = await Session.BrowseAsync(
                null,
                null,
                1,
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

            Assert.That(browseResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(browseResponse.Results[0].StatusCode), Is.True);

            int totalReferences = browseResponse.Results[0].References.Count;

            // If there are more references, a continuation point should be returned
            if (!browseResponse.Results[0].ContinuationPoint.IsEmpty)
            {
                // Use BrowseNext to get remaining references
                bool hasMore = true;
                ByteString continuationPoint = browseResponse.Results[0].ContinuationPoint;

                while (hasMore)
                {
                    BrowseNextResponse nextResponse = await Session.BrowseNextAsync(
                        null,
                        false,
                        new ByteString[] { continuationPoint }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(nextResponse.Results.Count, Is.EqualTo(1));
                    Assert.That(StatusCode.IsGood(nextResponse.Results[0].StatusCode), Is.True);

                    totalReferences += nextResponse.Results[0].References.Count;

                    continuationPoint = nextResponse.Results[0].ContinuationPoint;
                    hasMore = !continuationPoint.IsEmpty;
                }
            }

            Assert.That(totalReferences, Is.GreaterThan(1),
                "Objects folder should have more than one child, verifying BrowseNext works.");
        }

        [Description("Browse the Root node. Should have Objects, Types, and Views folders.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "001")]
        public async Task Browse008RootNodeChildrenAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.RootFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var childNames = new List<string>();
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                childNames.Add(r.BrowseName.Name);
            }

            Assert.That(childNames, Does.Contain("Objects"),
                "Root should contain Objects folder.");
            Assert.That(childNames, Does.Contain("Types"),
                "Root should contain Types folder.");
            Assert.That(childNames, Does.Contain("Views"),
                "Root should contain Views folder.");
        }

        [Description("Browse multiple nodes in a single request.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "007")]
        public async Task Browse009MultipleNodesAsync()
        {
            var browseDescs = new BrowseDescription[]
            {
                new() {
                    NodeId = ObjectIds.ObjectsFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                },
                new() {
                    NodeId = ObjectIds.TypesFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };

            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                browseDescs.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Browse result[{i}] should be Good.");
                Assert.That(response.Results[i].References.Count, Is.GreaterThan(0),
                    $"Browse result[{i}] should have references.");
            }
        }

        [Description("Browse with max 1 ref, obtain continuation point, then release it via BrowseNext with releaseContinuationPoints=true.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "010")]
        public async Task Browse010BrowseNextReleaseContinuationPointAsync()
        {
            BrowseResponse browseResponse = await Session.BrowseAsync(
                null,
                null,
                1,
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

            Assert.That(browseResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(browseResponse.Results[0].StatusCode), Is.True);

            ByteString continuationPoint = browseResponse.Results[0].ContinuationPoint;
            Assert.That(continuationPoint.IsEmpty, Is.False,
                "Should have a continuation point when max refs is 1.");

            // Release the continuation point
            BrowseNextResponse releaseResponse = await Session.BrowseNextAsync(
                null,
                true,
                new ByteString[] { continuationPoint }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(releaseResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(releaseResponse.Results[0].StatusCode), Is.True,
                "Releasing a continuation point should return Good.");
            Assert.That(releaseResponse.Results[0].ContinuationPoint.IsEmpty, Is.True,
                "No continuation point should remain after release.");
        }

        [Description("Browse ObjectsFolder with ResultMask=BrowseName only.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "010")]
        public async Task Browse011BrowseWithResultMaskBrowseNameOnlyAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Should return references even with BrowseName-only mask.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.BrowseName, Is.Not.Null,
                    "BrowseName should be populated when requested.");
            }
        }

        [Description("Browse ObjectsFolder with ResultMask=DisplayName only.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "010")]
        public async Task Browse012BrowseWithResultMaskDisplayNameOnlyAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.DisplayName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Should return references with DisplayName-only mask.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.DisplayName, Is.Not.Null,
                    "DisplayName should be populated when requested.");
            }
        }

        [Description("Browse ObjectsFolder with ResultMask=0 (none). Should still return references but with minimal information.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "010")]
        public async Task Browse013BrowseWithResultMaskNoneAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = 0
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Should still return references even with zero result mask.");
        }

        [Description("Browse ObjectsFolder with HierarchicalReferences and IncludeSubtypes=true. Should return references of subtypes like Organizes and HasComponent.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "005")]
        public async Task Browse014BrowseIncludeSubtypesTrueAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
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
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "IncludeSubtypes=true should return subtypes of HierarchicalReferences.");
        }

        [Description("Browse ObjectsFolder with HierarchicalReferences and IncludeSubtypes=false. Should return only exact HierarchicalReferences (likely none, since children are typically linked via Organizes or HasComponent subtypes).")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "015")]
        public async Task Browse015BrowseIncludeSubtypesFalseAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            int subtypeIncludedCount = response.Results[0].References.Count;
            Assert.That(subtypeIncludedCount, Is.LessThanOrEqualTo(0),
                "IncludeSubtypes=false with HierarchicalReferences should return no references " +
                "since children are linked via subtypes like Organizes.");
        }

        [Description("Browse Server node and verify mandatory children are present. ServerStatus and NamespaceArray must exist.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "018")]
        public async Task Browse016BrowseServerNodeMandatoryChildrenAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
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
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));

            var childNames = new List<string>();
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                childNames.Add(rd.BrowseName.Name);
            }

            Assert.That(childNames, Does.Contain("ServerStatus"),
                "Server should contain ServerStatus.");
            Assert.That(childNames, Does.Contain("NamespaceArray"),
                "Server should contain NamespaceArray.");
        }

        [Description("Browse ServerStatus children. Should have properties like CurrentTime, State, StartTime.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "018")]
        public async Task Browse017BrowseServerStatusChildrenAsync()
        {
            // First browse Server to find ServerStatus NodeId
            BrowseResponse serverResponse = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(serverResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(serverResponse.Results[0].StatusCode), Is.True);

            NodeId serverStatusNodeId = NodeId.Null;
            foreach (ReferenceDescription rd in serverResponse.Results[0].References)
            {
                if (rd.BrowseName.Name == "ServerStatus")
                {
                    serverStatusNodeId = ExpandedNodeId.ToNodeId(
                        rd.NodeId, Session.NamespaceUris);
                    break;
                }
            }

            Assert.That(serverStatusNodeId.IsNull, Is.False,
                "ServerStatus should be found under Server.");

            // Now browse ServerStatus children
            BrowseResponse statusResponse = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = serverStatusNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(statusResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(statusResponse.Results[0].StatusCode), Is.True);
            Assert.That(statusResponse.Results[0].References.Count, Is.GreaterThan(0));

            var childNames = new List<string>();
            foreach (ReferenceDescription rd in statusResponse.Results[0].References)
            {
                childNames.Add(rd.BrowseName.Name);
            }

            Assert.That(childNames, Does.Contain("CurrentTime"),
                "ServerStatus should contain CurrentTime.");
            Assert.That(childNames, Does.Contain("State"),
                "ServerStatus should contain State.");
        }

        [Description("Browse TypesFolder forward with Organizes. Should contain ObjectTypes, VariableTypes, DataTypes, ReferenceTypes.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "001")]
        public async Task Browse018BrowseTypesFolderAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.TypesFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var childNames = new List<string>();
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                childNames.Add(rd.BrowseName.Name);
            }

            Assert.That(childNames, Does.Contain("ObjectTypes"),
                "Types folder should contain ObjectTypes.");
            Assert.That(childNames, Does.Contain("VariableTypes"),
                "Types folder should contain VariableTypes.");
            Assert.That(childNames, Does.Contain("DataTypes"),
                "Types folder should contain DataTypes.");
            Assert.That(childNames, Does.Contain("ReferenceTypes"),
                "Types folder should contain ReferenceTypes.");
        }

        [Description("Browse an invalid node. Should return BadNodeIdUnknown.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "Err-002")]
        public async Task Browse019BrowseInvalidNodeAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "Browsing an invalid node should return BadNodeIdUnknown.");
        }

        [Description("Browse Server with HasProperty references only. All returned references should be HasProperty type.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "004")]
        public async Task Browse020BrowseHasPropertyReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Server should have HasProperty references.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty),
                    $"Reference '{rd.BrowseName}' should be HasProperty.");
            }
        }

        [Description("Browse Server with HasComponent references only. All returned references should be HasComponent type.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "004")]
        public async Task Browse021BrowseHasComponentReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Server should have HasComponent references.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent),
                    $"Reference '{rd.BrowseName}' should be HasComponent.");
            }
        }

        [Description("Browse Server with NodeClassMask=Variable. All returned references should be Variable class.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "006")]
        public async Task Browse022BrowseNodeClassMaskVariableAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Variable,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Server should have Variable children.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.NodeClass, Is.EqualTo(NodeClass.Variable),
                    $"Reference '{rd.BrowseName}' should be Variable class.");
            }
        }

        [Description("Browse MethodsFolder with NodeClassMask=Method. All returned references should be Method class.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "006")]
        public async Task Browse023BrowseNodeClassMaskMethodAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.MethodsFolder),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Method,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Methods folder should have Method children.");

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.NodeClass, Is.EqualTo(NodeClass.Method),
                    $"Reference '{rd.BrowseName}' should be Method class.");
            }
        }

        [Description("Browse two nodes with max 1 ref each, then call BrowseNext with both continuation points in a single request.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "007")]
        public async Task Browse024BrowseNextMultipleContinuationPointsAsync()
        {
            BrowseResponse browseResponse = await Session.BrowseAsync(
                null,
                null,
                1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new() {
                        NodeId = ObjectIds.TypesFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(browseResponse.Results.Count, Is.EqualTo(2));

            var continuationPoints = new List<ByteString>();
            for (int i = 0; i < browseResponse.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(browseResponse.Results[i].StatusCode), Is.True,
                    $"Browse result[{i}] should be Good.");

                if (!browseResponse.Results[i].ContinuationPoint.IsEmpty)
                {
                    continuationPoints.Add(browseResponse.Results[i].ContinuationPoint);
                }
            }

            Assert.That(continuationPoints, Has.Count.EqualTo(2),
                "Both nodes should have continuation points with max 1 ref.");

            BrowseNextResponse nextResponse = await Session.BrowseNextAsync(
                null,
                false,
                continuationPoints.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(nextResponse.Results.Count, Is.EqualTo(2));

            for (int i = 0; i < nextResponse.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(nextResponse.Results[i].StatusCode), Is.True,
                    $"BrowseNext result[{i}] should be Good.");
                Assert.That(nextResponse.Results[i].References.Count, Is.GreaterThan(0),
                    $"BrowseNext result[{i}] should return additional references.");
            }

            // Clean up any remaining continuation points
            var remainingCps = new List<ByteString>();
            for (int i = 0; i < nextResponse.Results.Count; i++)
            {
                if (!nextResponse.Results[i].ContinuationPoint.IsEmpty)
                {
                    remainingCps.Add(nextResponse.Results[i].ContinuationPoint);
                }
            }

            if (remainingCps.Count > 0)
            {
                await Session.BrowseNextAsync(
                    null,
                    true,
                    remainingCps.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Description("Browse with RequestedMaxReferencesPerNode=1 to get continuation point.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "005")]
        public async Task BrowseWithMaxRefsPerNodeOneGetsContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
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
            Assert.That(response.Results[0].References.Count,
                Is.LessThanOrEqualTo(1));

            if (!response.Results[0].ContinuationPoint.IsEmpty)
            {
                // Release the continuation point
                await Session.BrowseNextAsync(
                    null, true,
                    new ByteString[] { response.Results[0].ContinuationPoint }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Description("BrowseNext with valid continuation point returns next batch.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "006")]
        public async Task BrowseNextWithValidContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
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

            if (response.Results[0].ContinuationPoint.IsEmpty)
            {
                Assert.Ignore(
                    "Server returned all references without continuation point.");
            }

            BrowseNextResponse nextResp = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { response.Results[0].ContinuationPoint }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(nextResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(nextResp.Results[0].StatusCode), Is.True);

            // Clean up remaining continuation points
            if (!nextResp.Results[0].ContinuationPoint.IsEmpty)
            {
                await Session.BrowseNextAsync(
                    null, true,
                    new ByteString[] { nextResp.Results[0].ContinuationPoint }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Description("BrowseNext until all references returned.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "009")]
        public async Task BrowseNextUntilAllReferencesReturnedAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            int totalRefs = response.Results[0].References.Count;
            ByteString cp = response.Results[0].ContinuationPoint;

            for (int iterations = 0; !cp.IsEmpty && iterations < 100; iterations++)
            {
                BrowseNextResponse nextResp = await Session.BrowseNextAsync(
                    null, false,
                    new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(nextResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(nextResp.Results[0].StatusCode), Is.True);
                totalRefs += nextResp.Results[0].References.Count;
                cp = nextResp.Results[0].ContinuationPoint;
            }

            Assert.That(totalRefs, Is.GreaterThan(0),
                "Should have found at least one reference on Server.");
        }

        [Description("BrowseNext with ReleaseContinuationPoints=true releases the point.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "010")]
        public async Task BrowseNextReleaseContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
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

            ByteString cp = response.Results[0].ContinuationPoint;
            if (cp.IsEmpty)
            {
                Assert.Ignore("No continuation point to release.");
            }

            // Release it
            BrowseNextResponse releaseResp = await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(releaseResp.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(releaseResp.Results[0].StatusCode), Is.True);
        }

        [Description("BrowseNext with invalid continuation point returns BadContinuationPointInvalid.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "Err-009")]
        public async Task BrowseNextInvalidContinuationPointAsync()
        {
            var invalidCp = ByteString.From(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC });

            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { invalidCp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        [Description("Browse with RequestedMaxReferencesPerNode=0 returns all references.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "009")]
        public async Task BrowseWithMaxRefsZeroReturnsAllAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
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
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Description("Multiple concurrent browses with continuation points.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "007")]
        public async Task MultipleConcurrentBrowsesWithContinuationPointsAsync()
        {
            var descriptions = new BrowseDescription[]
            {
                new() {
                    NodeId = ObjectIds.ObjectsFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                },
                new() {
                    NodeId = ObjectIds.TypesFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                descriptions.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));

            // Release any continuation points
            var cps = new List<ByteString>();
            foreach (BrowseResult r in response.Results)
            {
                if (!r.ContinuationPoint.IsEmpty)
                {
                    cps.Add(r.ContinuationPoint);
                }
            }

            if (cps.Count > 0)
            {
                await Session.BrowseNextAsync(
                    null, true,
                    cps.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Description("Browse a node with many references and verify pagination works.")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "004")]
        public async Task BrowseNodeWithManyReferencesAsync()
        {
            // Types folder typically has many subtypes
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 2,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.TypesFolder,
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
            Assert.That(response.Results[0].References.Count,
                Is.LessThanOrEqualTo(2));

            // Clean up
            if (!response.Results[0].ContinuationPoint.IsEmpty)
            {
                await Session.BrowseNextAsync(
                    null, true,
                    new ByteString[] { response.Results[0].ContinuationPoint }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Description("Browse with View (if views exist, else Assert.Ignore).")]
        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "017")]
        public async Task BrowseWithViewAsync()
        {
            // Check if Views folder has any children
            BrowseResponse viewsResponse = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ViewsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (viewsResponse.Results[0].References.Count == 0)
            {
                Assert.Ignore("No views defined in the server.");
            }

            // Use the first view for browsing
            var viewId = ExpandedNodeId.ToNodeId(
                viewsResponse.Results[0].References[0].NodeId,
                Session.NamespaceUris);

            var viewDescription = new ViewDescription
            {
                ViewId = viewId,
                Timestamp = DateTimeUtc.MinValue,
                ViewVersion = 0
            };

            BrowseResponse response = await Session.BrowseAsync(
                null, viewDescription, 0,
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
            // The view may not be supported or may return various errors
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code == StatusCodes.BadViewIdUnknown ||
                response.Results[0].StatusCode.Code == StatusCodes.BadNodeIdUnknown ||
                response.Results[0].StatusCode.Code == StatusCodes.BadNodeNotInView ||
                response.Results[0].StatusCode.Code == StatusCodes.BadNothingToDo,
                Is.True,
                $"Unexpected status: {response.Results[0].StatusCode}");
        }

        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "001")]
        public async Task BrowseRootFolderAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.RootFolder,
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

            var names = response.Results[0].References.ToArray()
                .Select(r => r.BrowseName.Name).ToList();
            Assert.That(names, Does.Contain("Objects"));
            Assert.That(names, Does.Contain("Types"));
            Assert.That(names, Does.Contain("Views"));
        }

        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "006")]
        public async Task BrowseWithNodeClassMaskObjectsOnlyAsync()
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
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.NodeClass, Is.EqualTo(NodeClass.Object),
                    $"Expected only Object nodes but got {rd.NodeClass} for {rd.BrowseName}.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "010")]
        public async Task BrowseWithResultMaskBrowseNameOnlyAsync()
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
                        ResultMask = (uint)BrowseResultMask.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.BrowseName, Is.Not.Null,
                    "BrowseName should be returned.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "003")]
        public async Task BrowseInverseFromObjectsFolderAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));

            // Should find Root folder as parent
            bool foundRoot = response.Results[0].References.ToArray()
                .Any(r => r.BrowseName.Name == "Root");
            Assert.That(foundRoot, Is.True,
                "Inverse browse from Objects should find Root.");
        }

        [Test]
        [Property("ConformanceUnit", "View Basic 2")]
        [Property("Tag", "001")]
        public async Task BrowseServerDiagnosticsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerDiagnostics,
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
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "ServerDiagnostics should have children.");
        }
    }
}
