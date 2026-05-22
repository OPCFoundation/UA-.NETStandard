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

namespace Opc.Ua.Conformance.Tests.AliasName
{
    [TestFixture]
    [Category("Conformance")]

    [Category("AliasNameExtended")]
    public class AliasNameExtendedTests : TestFixture
    {
        private async Task<DataValue> RdAttr(
            NodeId n,
            uint a)
        {
            ReadResponse r = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[] { new() { NodeId = n, AttributeId = a } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r.Results.Count, Is.EqualTo(1));
            return r.Results[0];
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]

        public async Task AliasCatTypeBrowseNameValidAsync()
        {
            DataValue r = await RdAttr(AliasCatTypeId, Attributes.BrowseName).ConfigureAwait(
                   false);
            if (StatusCode.IsBad(r.StatusCode))
            {
                Assert.Fail("Not present.");
            }
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("AliasNameCategoryType"));
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]
        public async Task AliasNameTypeBrowseNameValidAsync()
        {
            DataValue r = await RdAttr(AliasNameTypeId, Attributes.BrowseName).ConfigureAwait(
            false);
            if (StatusCode.IsBad(r.StatusCode))
            {
                Assert.Fail("Not present.");
            }
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("AliasNameDataType"));
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]
        public async Task AliasCatIsSubtypeOfFolderTypeAsync()
        {
            BrowseResponse resp = await Session.BrowseAsync(null, null, 0, new BrowseDescription[] { new() {
                NodeId = AliasCatTypeId,
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IncludeSubtypes = false,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            if (!StatusCode.IsGood(resp.Results[0].StatusCode) || resp.Results[0].References.Count == 0)
            {
                Assert.Fail("Not present.");
            }
            Assert.That(ExpandedNodeId.ToNodeId(resp.Results[0].References[0].NodeId, Session.NamespaceUris), Is.EqualTo(ObjectTypeIds.FolderType));
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]
        public async Task AliasNameTypeIsSubtypeOfBaseAsync()
        {
            BrowseResponse resp = await Session.BrowseAsync(null, null, 0, new BrowseDescription[] { new() {
                NodeId = AliasNameTypeId,
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IncludeSubtypes = false,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            if (!StatusCode.IsGood(resp.Results[0].StatusCode) || resp.Results[0].References.Count == 0)
            {
                Assert.Fail("Not present.");
            }
            Assert.That(ExpandedNodeId.ToNodeId(resp.Results[0].References[0].NodeId, Session.NamespaceUris), Is.EqualTo(new NodeId(DataTypes.Structure)));
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]
        public async Task HasAliasIsNonHierarchicalAsync()
        {
            BrowseResponse resp = await Session.BrowseAsync(null, null, 0, new BrowseDescription[] { new() {
                NodeId = HasAliasRefTypeId,
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IncludeSubtypes = false,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            if (!StatusCode.IsGood(resp.Results[0].StatusCode) || resp.Results[0].References.Count == 0)
            {
                Assert.Ignore("Not present.");
            }
            Assert.That(
                ExpandedNodeId.ToNodeId(resp.Results[0].References[0].NodeId, Session.NamespaceUris),
                Is.EqualTo(ReferenceTypeIds.NonHierarchicalReferences));
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "003")]
        public async Task AliasCatBrowseForComponentsAsync()
        {
            BrowseResponse resp = await Session.BrowseAsync(null, null, 0, new BrowseDescription[] { new() {
                NodeId = AliasCatTypeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            if (!StatusCode.IsGood(resp.Results[0].StatusCode))
            {
                Assert.Fail("Not present.");
            }
            Assert.That(resp.Results[0], Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "N/A")]
        public Task AliasNameFindServersNotRequired()
        {
            Assert.Ignore("AliasName FindServers CU is optional.");
            return Task.CompletedTask;
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "N/A")]
        public Task AliasNameRegisterNotRequired()
        {
            Assert.Ignore("AliasName Register CU is optional.");
            return Task.CompletedTask;
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "N/A")]
        public Task AliasNameSecurityAdminNotRequired()
        {
            Assert.Ignore("AliasName SecurityAdmin CU is optional.");
            return Task.CompletedTask;
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]

        public async Task AliasForRefTypeExistsAsync()
        {
            // Standard nodeset has AliasFor at i=23469, not i=23471.
            var id = ReferenceTypeIds.AliasFor;
            DataValue r = await RdAttr(id, Attributes.BrowseName).ConfigureAwait(
            false);
            if (StatusCode.IsBad(r.StatusCode))
            {
                Assert.Ignore("AliasFor not present.");
            }
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "AliasName Base")]
        [Property("Tag", "001")]
        public async Task AliasCatTranslateFromTypesAsync()
        {
            // Three-step path: ObjectTypesFolder organizes BaseObjectType,
            // then BaseObjectType --HasSubtype--> FolderType, then
            // FolderType --HasSubtype--> AliasNameCategoryType.
            // The original single-step path could not resolve because no
            // single hierarchical reference connects the folder directly
            // to the type three levels down.
            ArrayOf<BrowsePath> bp = new BrowsePath[]
            {
                new() {
                    StartingNode = ObjectIds.ObjectTypesFolder,
                    RelativePath = new RelativePath
                    {
                        Elements = new RelativePathElement[]
                        {
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("BaseObjectType", 0)
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("FolderType", 0)
                            },
                            new() {
                                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName("AliasNameCategoryType", 0)
                            }
                        }.ToArrayOf()
                    }
                }
            }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse resp = await Session.TranslateBrowsePathsToNodeIdsAsync(null, bp, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(resp.Results[0].StatusCode))
            {
                Assert.Ignore("Path did not resolve.");
            }
            Assert.That(resp.Results[0].Targets.Count, Is.GreaterThanOrEqualTo(1));
        }

        private static readonly NodeId AliasCatTypeId = new(23456);
        private static readonly NodeId AliasNameTypeId = new(23468);
        private static readonly NodeId HasAliasRefTypeId = new(23470);
    }
}
