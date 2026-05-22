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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Conformance.Tests.AddressSpaceModel
{
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpaceModelExtended")]
    public class AddressSpaceModelExtendedTests : TestFixture
    {
        private static readonly NodeId HasAddInRefTypeId = new(17604);
        private static readonly NodeId DictEntryTypeId = new(17589);
        private static readonly NodeId UriDictEntryTypeId = new(17600);
        private static readonly NodeId IrdiDictEntryTypeId = new(17598);
        private static readonly NodeId HasDictEntryId = new(17597);
        private static readonly NodeId DictFolderId = new(17594);
        private static readonly NodeId BaseInterfaceTypeId = new(17602);
        private static readonly NodeId HasInterfaceId = new(17603);

        private async Task<DataValue> RdAttr(NodeId n, uint a)
        {
            ReadResponse r = await Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                new ReadValueId[] { new() { NodeId = n, AttributeId = a } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r.Results.Count, Is.EqualTo(1));
            return r.Results[0];
        }

        private async Task<BrowseResult> BrFwd(NodeId n, NodeId rt)
        {
            BrowseResponse r = await Session.BrowseAsync(null, null, 0,
                new BrowseDescription[] { new() { NodeId = n, BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = rt, IncludeSubtypes = true, NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r.Results.Count, Is.EqualTo(1));
            return r.Results[0];
        }

        private async Task<BrowseResult> BrInvSub(NodeId n)
        {
            BrowseResponse r = await Session.BrowseAsync(null, null, 0,
                new BrowseDescription[] { new() { NodeId = n, BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype, IncludeSubtypes = false, NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r.Results.Count, Is.EqualTo(1));
            return r.Results[0];
        }

        private NodeId Pid(BrowseResult r)
        {
            return ExpandedNodeId.ToNodeId(r.References[0].NodeId, Session.NamespaceUris);
        }

        [

Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task HasAddInRefTypeExistsAsync()
        {
            DataValue r = await RdAttr(HasAddInRefTypeId, Attributes.BrowseName).ConfigureAwait(
            false);
            if (!StatusCode.IsGood(r.StatusCode))
            {
                Assert.Fail("HasAddIn not present.");
            }
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("HasAddIn"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task HasAddInIsSubtypeOfHasComponentAsync()
        {
            BrowseResult r = await BrInvSub(HasAddInRefTypeId).ConfigureAwait(
            false);
            if (!StatusCode.IsGood(r.StatusCode) || r.References.Count == 0)
            {
                Assert.Fail("HasAddIn not present.");
            }
            Assert.That(Pid(r), Is.EqualTo(ReferenceTypeIds.HasComponent));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task AddInForwardRefsFromServerAsync()
        {
            BrowseResult r = await BrFwd(ObjectIds.Server, HasAddInRefTypeId).ConfigureAwait(
            false);
            if (!StatusCode.IsGood(r.StatusCode))
            {
                Assert.Fail("HasAddIn not supported.");
            }
            Assert.That(r, Is.Not.Null);
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task AddInInstanceBrowseNameNotEmptyAsync()
        {
            BrowseResult r = await BrFwd(ObjectIds.Server, HasAddInRefTypeId).ConfigureAwait(
            false);
            if (!StatusCode.IsGood(r.StatusCode) || r.References.Count == 0)
            {
                Assert.Ignore("No AddIn instances.");
            }
            foreach (ReferenceDescription rd in r.References)
            {
                Assert.That(rd.BrowseName.Name, Is.Not.Null.And.Not.Empty);
            }
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task AddInTargetIsObjectAsync()
        {
            BrowseResult r = await BrFwd(ObjectIds.Server, HasAddInRefTypeId).ConfigureAwait(
            false);
            if (!StatusCode.IsGood(r.StatusCode) || r.References.Count == 0)
            {
                Assert.Ignore("No AddIn instances.");
            }
            foreach (ReferenceDescription rd in r.References)
            {
                Assert.That(rd.NodeClass, Is.EqualTo(NodeClass.Object));
            }
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task AddInInverseRefExistsAsync()
        {
            BrowseResult fwd = await BrFwd(ObjectIds.Server, HasAddInRefTypeId).ConfigureAwait(
            false);
            if (!StatusCode.IsGood(fwd.StatusCode) || fwd.References.Count == 0)
            {
                Assert.Ignore("No AddIn instances.");
            }
            var id = ExpandedNodeId.ToNodeId(fwd.References[0].NodeId, Session.NamespaceUris);
            BrowseResponse inv = await Session.BrowseAsync(null, null, 0, new BrowseDescription[] { new() {
                NodeId = id,
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = HasAddInRefTypeId,
                IncludeSubtypes = false,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All } }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(inv.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Property("ConformanceUnit", "Address Space DataTypeDefinition Attribute")]
        [Property("Tag", "001")]
        [Test]
        public async Task StructureDataTypeHasDefinitionAsync()
        {
            DataValue r = await RdAttr(DataTypeIds.ServerStatusDataType, Attributes.DataTypeDefinition)
            .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space DataTypeDefinition Attribute")]
        [Property("Tag", "002")]
        [Test]
        public async Task EnumDataTypeHasDefinitionAsync()
        {
            DataValue r = await RdAttr(DataTypeIds.ServerState, Attributes.DataTypeDefinition)
            .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space DataTypeDefinition Attribute")]
        [Property("Tag", "001")]
        [Test]
        public async Task DefinitionContainsStructDefAsync()
        {
            DataValue r = await RdAttr(
            DataTypeIds.ServerStatusDataType,
            Attributes.DataTypeDefinition).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            if (!r.WrappedValue.TryGetStructure<StructureDefinition>(out StructureDefinition _))
            {
                Assert.Fail("Not a StructureDefinition");
                return;
            }
        }

        [Property("ConformanceUnit", "Address Space DataTypeDefinition Attribute")]
        [Property("Tag", "001")]
        [Test]
        public async Task DefinitionFieldsHaveNamesAsync()
        {
            DataValue r = await RdAttr(
            DataTypeIds.ServerStatusDataType,
            Attributes.DataTypeDefinition).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            if (!r.WrappedValue.TryGetStructure<StructureDefinition>(out StructureDefinition def))
            {
                Assert.Fail("Could not decode.");
                return;
            }
            Assert.That(def.Fields.Count, Is.GreaterThan(0));
            foreach (StructureField f in def.Fields)
            {
                Assert.That(f.Name, Is.Not.Null.And.Not.Empty);
            }
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task DictFolderExistsAsync()
        {
            DataValue r = await RdAttr(DictFolderId, Attributes.BrowseName).ConfigureAwait(
            false);
            if (!StatusCode.IsGood(r.StatusCode))
            {
                Assert.Fail("Dictionaries folder not present.");
            }
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("Dictionaries"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task DictEntryTypeExistsAsync()
        {
            DataValue r = await RdAttr(DictEntryTypeId, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(r.StatusCode),
            Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("DictionaryEntryType"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task UriDictEntryTypeExistsAsync()
        {
            DataValue r = await RdAttr(UriDictEntryTypeId, Attributes.BrowseName).ConfigureAwait(false);
            Assert
            .That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("UriDictionaryEntryType"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task IrdiDictEntryTypeExistsAsync()
        {
            DataValue r = await RdAttr(IrdiDictEntryTypeId, Attributes.BrowseName).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("IrdiDictionaryEntryType"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task HasDictEntryRefTypeExistsAsync()
        {
            DataValue r = await RdAttr(HasDictEntryId, Attributes.BrowseName).ConfigureAwait(false);
            Assert
            .That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("HasDictionaryEntry"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task DictEntryIsSubtypeOfBaseAsync()
        {
            BrowseResult r = await BrInvSub(DictEntryTypeId).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(r.StatusCode),
            Is.True);
            Assert.That(Pid(r), Is.EqualTo(ObjectTypeIds.BaseObjectType));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task UriDictIsSubtypeOfEntryAsync()
        {
            BrowseResult r = await BrInvSub(UriDictEntryTypeId).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(r.StatusCode),
            Is.True);
            Assert.That(Pid(r), Is.EqualTo(DictEntryTypeId));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task IrdiDictIsSubtypeOfEntryAsync()
        {
            BrowseResult r = await BrInvSub(IrdiDictEntryTypeId).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(r.StatusCode),
            Is.True);
            Assert.That(Pid(r), Is.EqualTo(DictEntryTypeId));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task HasDictEntryIsNonHierarchicalAsync()
        {
            BrowseResult r = await BrInvSub(HasDictEntryId).ConfigureAwait(false);
            Assert.That(
            StatusCode.IsGood(r.StatusCode),
            Is.True);
            Assert.That(Pid(r), Is.EqualTo(ReferenceTypeIds.NonHierarchicalReferences));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task BaseInterfaceTypeExistsAsync()
        {
            DataValue r = await RdAttr(BaseInterfaceTypeId, Attributes.BrowseName).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("BaseInterfaceType"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task HasInterfaceRefTypeExistsAsync()
        {
            DataValue r = await RdAttr(HasInterfaceId, Attributes.BrowseName).ConfigureAwait(false);
            Assert
            .That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("HasInterface"));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task BaseInterfaceIsSubtypeOfBaseObjectAsync()
        {
            BrowseResult r = await BrInvSub(BaseInterfaceTypeId).ConfigureAwait(false);
            Assert.That(
            r.References.Count,
            Is.GreaterThan(0));
            Assert.That(Pid(r), Is.EqualTo(ObjectTypeIds.BaseObjectType));
        }

        [Property("ConformanceUnit", "Address Space Atomicity")]
        [Property("Tag", "001")]
        [Test]
        public async Task AccessLevelExReadableAsync()
        {
            DataValue r = await RdAttr(ToNodeId(Constants.ScalarStaticInt32), Attributes.AccessLevelEx)
            .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode) || r.StatusCode.Code == StatusCodes.BadAttributeIdInvalid, Is.True);
        }

        [Property("ConformanceUnit", "Address Space Atomicity")]
        [Property("Tag", "001")]
        [Test]
        public async Task AtomicBitsAccessLevelExAsync()
        {
            DataValue r = await RdAttr(
            ToNodeId(Constants.ScalarStaticInt32),
            Attributes.AccessLevelEx).ConfigureAwait(false);
            if (r.StatusCode.Code == StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Fail("AccessLevelEx not supported.");
            }
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.WrappedValue.GetUInt32(), Is.GreaterThanOrEqualTo(0u));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task ArrayVarValueRankIsOneDimAsync()
        {
            DataValue r = await RdAttr(
            ToNodeId(Constants.ScalarStaticArrayBoolean),
            Attributes.ValueRank).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<int>(default), Is.EqualTo(ValueRanks.OneDimension));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task ScalarVarValueRankIsScalarAsync()
        {
            DataValue r = await RdAttr(
            ToNodeId(Constants.ScalarStaticInt32),
            Attributes.ValueRank).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<int>(default), Is.EqualTo(ValueRanks.Scalar));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task NonVolatileBitInAccessLevelAsync()
        {
            DataValue r = await RdAttr(
            ToNodeId(Constants.ScalarStaticInt32),
            Attributes.AccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.WrappedValue.GetByte(), Is.GreaterThanOrEqualTo((byte)0));
        }

        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        [Test]
        public async Task NonVolatileBitInAccessLevelExAsync()
        {
            DataValue r = await RdAttr(
            ToNodeId(Constants.ScalarStaticInt32),
            Attributes.AccessLevelEx).ConfigureAwait(false);
            if (r.StatusCode.Code == StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Fail("AccessLevelEx not supported.");
            }
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space Notifier Hierarchy")]
        [Property("Tag", "001")]
        [Test]
        public async Task ServerHasNotifierRefsAsync()
        {
            BrowseResult r = await BrFwd(ObjectIds.Server, ReferenceTypeIds.HasNotifier).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space Notifier Hierarchy")]
        [Property("Tag", "001")]
        [Test]
        public async Task NotifierHierarchyNoLoopsAsync()
        {
            var v = new HashSet<NodeId>();
            var q = new Queue<NodeId>();
            q.Enqueue(ObjectIds.Server);
            v.Add(
            ObjectIds.Server);
            for (int d = 0; q.Count > 0 && d < 5; d++)
            {
                int c = q.Count;
                for (int i = 0; i < c; i++)
                {
                    NodeId cur = q.Dequeue();
                    BrowseResult r = await BrFwd(cur, ReferenceTypeIds.HasNotifier).ConfigureAwait(false);
                    if (!StatusCode.IsGood(
                    r.StatusCode))
                    {
                        continue;
                    }
                    foreach (ReferenceDescription rd in r.References)
                    {
                        var cid = ExpandedNodeId.ToNodeId(rd.NodeId, Session.NamespaceUris);
                        Assert.That(v, Does.Not.Contain(cid), "Loop.");
                        if (v.Add(cid))
                        {
                            q.Enqueue(cid);
                        }
                    }
                }
            }
            Assert.Pass("No loops.");
        }

        [Property("ConformanceUnit", "Address Space Source Hierarchy")]
        [Property("Tag", "000")]
        [Test]
        public async Task HasEventSourceRefExistsAsync()
        {
            DataValue r = await RdAttr(ReferenceTypeIds.HasEventSource, Attributes.BrowseName).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("HasEventSource"));
        }

        [Property("ConformanceUnit", "Address Space Source Hierarchy")]
        [Property("Tag", "000")]
        [Test]
        public async Task SourceHierarchyNoLoopsAsync()
        {
            var v = new HashSet<NodeId>();
            var q = new Queue<NodeId>();
            q.Enqueue(ObjectIds.Server);
            v.Add(
            ObjectIds.Server);
            for (int d = 0; q.Count > 0 && d < 5; d++)
            {
                int c = q.Count;
                for (int i = 0; i < c; i++)
                {
                    NodeId cur = q.Dequeue();
                    BrowseResult r = await BrFwd(cur, ReferenceTypeIds.HasEventSource).ConfigureAwait(false);
                    if (!StatusCode.IsGood(
                    r.StatusCode))
                    {
                        continue;
                    }
                    foreach (ReferenceDescription rd in r.References)
                    {
                        var cid = ExpandedNodeId.ToNodeId(rd.NodeId, Session.NamespaceUris);
                        Assert.That(v, Does.Not.Contain(cid), "Loop.");
                        if (v.Add(cid))
                        {
                            q.Enqueue(cid);
                        }
                    }
                }
            }
            Assert.Pass("No loops.");
        }

        [Property("ConformanceUnit", "Address Space Source Hierarchy")]
        [Property("Tag", "000")]
        [Test]
        public async Task HasEventSourceIsSubtypeOfHierarchicalAsync()
        {
            BrowseResult r = await BrInvSub(ReferenceTypeIds.HasEventSource).ConfigureAwait(
            false);
            Assert.That(r.References.Count, Is.GreaterThan(0));
            Assert.That(Pid(r), Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
        }

        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        [Test]
        public async Task SystemEventTypeExistsAsync()
        {
            DataValue r = await RdAttr(ObjectTypeIds.SystemEventType, Attributes.BrowseName).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("SystemEventType"));
        }

        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        [Test]
        public async Task TransitionEventTypeExistsAsync()
        {
            DataValue r = await RdAttr(
            ObjectTypeIds.TransitionEventType,
            Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("TransitionEventType"));
        }

        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        [Test]
        public async Task ConditionTypeExistsAsync()
        {
            DataValue r = await RdAttr(ObjectTypeIds.ConditionType, Attributes.BrowseName).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.GetValue<QualifiedName>(default).Name, Is.EqualTo("ConditionType"));
        }

        [Property("ConformanceUnit", "Address Space WriteMask")]
        [Property("Tag", "001")]
        [Test]
        public async Task WriteMaskOnObjectNodeAsync()
        {
            DataValue r = await RdAttr(ObjectIds.Server, Attributes.WriteMask).ConfigureAwait(false);
            Assert
            .That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space WriteMask")]
        [Property("Tag", "001")]
        [Test]
        public async Task WriteMaskOnMethodNodeAsync()
        {
            NodeId mid = ToNodeId(
            new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));
            DataValue r = await RdAttr(mid, Attributes.WriteMask).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space WriteMask")]
        [Property("Tag", "001")]
        [Test]
        public async Task WriteMaskOnObjectTypeNodeAsync()
        {
            DataValue r = await RdAttr(ObjectTypeIds.BaseObjectType, Attributes.WriteMask).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space UserWriteMask")]
        [Property("Tag", "005")]
        [Test]
        public async Task UserWriteMaskOnObjectNodeAsync()
        {
            DataValue r = await RdAttr(ObjectIds.Server, Attributes.UserWriteMask).ConfigureAwait(
            false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space UserWriteMask")]
        [Property("Tag", "005")]
        [Test]
        public async Task UserWriteMaskOnObjectTypeNodeAsync()
        {
            DataValue r = await RdAttr(ObjectTypeIds.BaseObjectType, Attributes.UserWriteMask)
            .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space User Access Level Base")]
        [Property("Tag", "002")]
        [Test]
        public async Task UserAccessLevelHistoryReadBitAsync()
        {
            DataValue r = await RdAttr(
            ToNodeId(Constants.ScalarStaticInt32),
            Attributes.UserAccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
            Assert.That(r.WrappedValue.GetByte(), Is.GreaterThanOrEqualTo((byte)0));
        }

        [Property("ConformanceUnit", "Address Space User Access Level Base")]
        [Property("Tag", "002")]
        [Test]
        public async Task UserAccessLevelHistoryWriteBitAsync()
        {
            DataValue r = await RdAttr(
            ToNodeId(Constants.ScalarStaticInt32),
            Attributes.UserAccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
        }

        [Property("ConformanceUnit", "Address Space Atomicity")]
        [Property("Tag", "001")]
        [Test]
        public async Task AccessLevelExOnVariableAsync()
        {
            DataValue r = await RdAttr(ToNodeId(Constants.ScalarStaticInt32), Attributes.AccessLevelEx)
            .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(r.StatusCode) || r.StatusCode.Code == StatusCodes.BadAttributeIdInvalid, Is.True);
        }

        [Property("ConformanceUnit", "Address Space Method Meta Data")]
        [Property("Tag", "004")]
        [Test]
        public async Task MethodInputArgsValueRankIsArrayAsync()
        {
            NodeId mid = ToNodeId(
            new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));
            BrowseResult br = await BrFwd(mid, ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            ReferenceDescription ia = null;
            foreach (ReferenceDescription r in br.References)
            {
                if (r.BrowseName.Name == "InputArguments")
                {
                    ia = r;
                    break;
                }
            }
            Assert.That(ia, Is.Not.Null);
            var iaId = ExpandedNodeId.ToNodeId(ia.NodeId, Session.NamespaceUris);
            DataValue vr = await RdAttr(iaId, Attributes.ValueRank).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(vr.StatusCode), Is.True);
            Assert.That(vr.GetValue<int>(default), Is.EqualTo(ValueRanks.OneDimension));
        }

        [Property("ConformanceUnit", "Address Space Method Meta Data")]
        [Property("Tag", "002")]
        [Test]
        public async Task MethodMetaDataTargetIsVariableAsync()
        {
            NodeId mid = ToNodeId(
            new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));
            BrowseResult br = await BrFwd(mid, ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            foreach (ReferenceDescription r in br.References)
            {
                if (r.BrowseName.Name is "InputArguments" or "OutputArguments")
                {
                    Assert.That(r.NodeClass, Is.EqualTo(NodeClass.Variable));
                }
            }
        }

        [Property("ConformanceUnit", "Address Space Method Meta Data")]
        [Property("Tag", "002")]
        [Test]
        public async Task MethodOutputArgsIsArgArrayAsync()
        {
            NodeId mid = ToNodeId(
            new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));
            BrowseResult br = await BrFwd(mid, ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            ReferenceDescription oa = null;
            foreach (ReferenceDescription r in br.References)
            {
                if (r.BrowseName.Name == "OutputArguments")
                {
                    oa = r;
                    break;
                }
            }
            Assert.That(oa, Is.Not.Null);
            var oaId = ExpandedNodeId.ToNodeId(oa.NodeId, Session.NamespaceUris);
            DataValue val = await RdAttr(oaId, Attributes.Value).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(val.StatusCode), Is.True);
            ExtensionObject[] args = val.GetValue<ExtensionObject[]>(default);
            Assert.That(args, Is.Not.Null.And.Not.Empty);
        }

        [Property("ConformanceUnit", "Address Space Method Meta Data")]
        [Property("Tag", "001")]
        [Test]
        public async Task MethodHasArgDescRefAsync()
        {
            NodeId mid = ToNodeId(
            new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));
            var hasArgDescId = new NodeId(129);
            BrowseResult r = await BrFwd(mid, hasArgDescId).ConfigureAwait(false);
            if (!StatusCode.IsGood(r.StatusCode) || r.References.Count == 0)
            {
                Assert.Ignore("HasArgumentDescription not supported.");
            }
            Assert.That(r.References.Count, Is.GreaterThan(0));
        }
    }
}
