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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Nodes
{
    [TestFixture]
    [Category("Nodes")]
    [Parallelizable]
    public sealed class NodeTableSupportTests
    {
        [Test]
        public void ViewTableDefaultViewAcceptsAnyNodeAndReference()
        {
            var table = new ViewTable();
            var defaultView = new ViewDescription();

            Assert.That(table.IsValid(defaultView), Is.True);
            Assert.That(table.IsNodeInView(defaultView, new NodeId(123)), Is.True);
            Assert.That(table.IsReferenceInView(defaultView, new ReferenceDescription()), Is.True);
        }

        [Test]
        public void ViewTableAddValidatesNodeIdAndDuplicates()
        {
            var table = new ViewTable();
            var invalid = new ViewNode { NodeId = NodeId.Null };
            var view = new ViewNode { NodeId = new NodeId(5000) };

            Assert.That(() => table.Add(null), Throws.ArgumentNullException);
            ServiceResultException invalidEx = Assert.Throws<ServiceResultException>(() => table.Add(invalid));
            Assert.That(invalidEx.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));

            table.Add(view);

            Assert.That(table.IsValid(new ViewDescription { ViewId = view.NodeId }), Is.True);
            ServiceResultException duplicateEx = Assert.Throws<ServiceResultException>(() => table.Add(view));
            Assert.That(duplicateEx.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        [Test]
        public void ViewTableRemoveValidatesNullUnknownAndExistingView()
        {
            var table = new ViewTable();
            NodeId viewId = new(6000);
            table.Add(new ViewNode { NodeId = viewId });

            Assert.That(() => table.Remove(NodeId.Null), Throws.ArgumentNullException);
            ServiceResultException unknownEx = Assert.Throws<ServiceResultException>(
                () => table.Remove(new NodeId(7000)));
            Assert.That(unknownEx.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));

            table.Remove(viewId);

            Assert.That(table.IsValid(new ViewDescription { ViewId = viewId }), Is.False);
        }

        [Test]
        public void ViewTableRegisteredViewExcludesNodesAndReferences()
        {
            var table = new ViewTable();
            NodeId viewId = new(8000);
            var description = new ViewDescription { ViewId = viewId };
            table.Add(new ViewNode { NodeId = viewId });

            Assert.That(table.IsNodeInView(description, new NodeId(1)), Is.False);
            Assert.That(table.IsReferenceInView(description, new ReferenceDescription()), Is.False);
        }

        [Test]
        public void ViewTableMissingViewOperationsThrowBadViewIdUnknown()
        {
            var table = new ViewTable();
            var description = new ViewDescription { ViewId = new NodeId(9000) };

            ServiceResultException nodeEx = Assert.Throws<ServiceResultException>(
                () => table.IsNodeInView(description, new NodeId(1)));
            ServiceResultException referenceEx = Assert.Throws<ServiceResultException>(
                () => table.IsReferenceInView(description, new ReferenceDescription()));

            Assert.That(nodeEx.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));
            Assert.That(referenceEx.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));
        }

        [Test]
        public void FilterContextWithoutOperationContextReturnsDefaults()
        {
            NamespaceTable namespaces = new();
            ITypeTable typeTree = new TypeTable(namespaces);
            var preferredLocales = new ArrayOf<string>(s_preferredLocales);

            var context = new FilterContext(
                namespaces,
                typeTree,
                preferredLocales,
                NUnitTelemetryContext.Create());

            Assert.That(context.NamespaceUris, Is.SameAs(namespaces));
            Assert.That(context.TypeTree, Is.SameAs(typeTree));
            Assert.That(context.PreferredLocales, Is.EqualTo(preferredLocales));
            Assert.That(context.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.SymbolicId));
            Assert.That(context.OperationDeadline, Is.EqualTo(DateTime.MaxValue));
            Assert.That(context.OperationStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(context.SessionId, Is.Null);
            Assert.That(context.UserIdentity, Is.Null);
            Assert.That(context.StringTable, Is.Null);
            Assert.That(context.AuditEntryId, Is.Null);
            Assert.That(context.Telemetry, Is.Not.Null);
        }

        [Test]
        public void FilterContextValidatesRequiredTables()
        {
            NamespaceTable namespaces = new();
            ITypeTable typeTree = new TypeTable(namespaces);

            Assert.That(
                () => _ = new FilterContext(null, typeTree, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);
            Assert.That(
                () => _ = new FilterContext(namespaces, null, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AsyncTypeTableAdapterForwardsSynchronousCalls()
        {
            var asyncTable = new RecordingAsyncTypeTable();
            ITypeTable table = asyncTable.AsTypeTable();
            ExpandedNodeId expandedTypeId = ObjectTypeIds.BaseObjectType;
            NodeId typeId = VariableTypeIds.BaseVariableType;
            QualifiedName browseName = new(BrowseNames.HasSubtype);

            Assert.That(table.IsKnown(expandedTypeId), Is.True);
            Assert.That(table.IsKnown(typeId), Is.False);
            Assert.That(table.FindSuperType(expandedTypeId), Is.EqualTo(ObjectTypeIds.BaseObjectType));
            Assert.That(table.FindSuperType(typeId), Is.EqualTo(VariableTypeIds.BaseVariableType));
            Assert.That(table.FindSuperTypeAsync(expandedTypeId).GetAwaiter().GetResult(), Is.EqualTo(ObjectTypeIds.BaseObjectType));
            Assert.That(table.FindSuperTypeAsync(typeId).GetAwaiter().GetResult(), Is.EqualTo(VariableTypeIds.BaseVariableType));
            Assert.That(table.FindSubTypes(expandedTypeId), Is.EqualTo(new ArrayOf<NodeId>(new[] { typeId })));
            Assert.That(table.IsTypeOf(expandedTypeId, ObjectTypeIds.BaseObjectType), Is.True);
            Assert.That(table.IsTypeOf(typeId, ObjectTypeIds.BaseObjectType), Is.False);
            Assert.That(table.FindReferenceTypeName(ReferenceTypeIds.HasSubtype), Is.EqualTo(browseName));
            Assert.That(table.FindReferenceType(browseName), Is.EqualTo(ReferenceTypeIds.HasSubtype));
            Assert.That(table.IsEncodingOf(DataTypeIds.Structure, DataTypeIds.BaseDataType), Is.True);
            Assert.That(table.IsEncodingFor(DataTypeIds.Structure, new ExtensionObject()), Is.True);
            Assert.That(table.IsEncodingFor(DataTypeIds.Structure, new Variant(123)), Is.True);
            Assert.That(table.FindDataTypeId(DataTypeIds.Structure), Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(table.FindDataTypeId((NodeId)DataTypeIds.Structure), Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(asyncTable.CallCount, Is.EqualTo(16));
        }

        [Test]
        public void AsyncNodeTableAdapterForwardsSynchronousCallsAndTables()
        {
            var asyncTypeTable = new RecordingAsyncTypeTable();
            var asyncNodeTable = new RecordingAsyncNodeTable(asyncTypeTable);
            INodeTable table = asyncNodeTable.AsNodeTable();
            ExpandedNodeId sourceId = ObjectIds.Server;

            Assert.That(table.NamespaceUris, Is.SameAs(asyncNodeTable.NamespaceUris));
            Assert.That(table.ServerUris, Is.SameAs(asyncNodeTable.ServerUris));
            Assert.That(table.TypeTree.IsKnown(ObjectTypeIds.BaseObjectType), Is.True);
            Assert.That(table.Exists(sourceId), Is.True);
            Assert.That(table.Find(sourceId), Is.Null);
            Assert.That(
                table.Find(sourceId, ReferenceTypeIds.HasComponent, false, true, new QualifiedName(BrowseNames.Server)),
                Is.Null);
            Assert.That(
                table.Find(sourceId, ReferenceTypeIds.HasComponent, false, true),
                Is.Empty);
            Assert.That(asyncNodeTable.CallCount, Is.EqualTo(4));
        }

        private static readonly string[] s_preferredLocales = ["en-US", "de-DE"];

        private sealed class RecordingAsyncNodeTable : IAsyncNodeTable
        {
            public RecordingAsyncNodeTable(IAsyncTypeTable typeTree)
            {
                TypeTree = typeTree;
            }

            public int CallCount { get; private set; }

            public NamespaceTable NamespaceUris { get; } = new();

            public StringTable ServerUris { get; } = new();

            public IAsyncTypeTable TypeTree { get; }

            public ValueTask<bool> ExistsAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(nodeId == ObjectIds.Server);
            }

            public ValueTask<INode> FindAsync(ExpandedNodeId nodeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<INode>((INode)null);
            }

            public ValueTask<INode> FindAsync(
                ExpandedNodeId sourceId,
                NodeId referenceTypeId,
                bool isInverse,
                bool includeSubtypes,
                QualifiedName browseName,
                CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<INode>((INode)null);
            }

            public ValueTask<ArrayOf<INode>> FindAsync(
                ExpandedNodeId sourceId,
                NodeId referenceTypeId,
                bool isInverse,
                bool includeSubtypes,
                CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<ArrayOf<INode>>(new ArrayOf<INode>(Array.Empty<INode>()));
            }
        }

        private sealed class RecordingAsyncTypeTable : IAsyncTypeTable
        {
            public int CallCount { get; private set; }

            public ValueTask<bool> IsKnownAsync(ExpandedNodeId typeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(typeId == ObjectTypeIds.BaseObjectType);
            }

            public ValueTask<bool> IsKnownAsync(NodeId typeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(typeId == ObjectTypeIds.BaseObjectType);
            }

            public ValueTask<NodeId> FindSuperTypeAsync(ExpandedNodeId typeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<NodeId>(ObjectTypeIds.BaseObjectType);
            }

            public ValueTask<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<NodeId>(VariableTypeIds.BaseVariableType);
            }

            public ValueTask<ArrayOf<NodeId>> FindSubTypesAsync(ExpandedNodeId typeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<ArrayOf<NodeId>>(
                    new ArrayOf<NodeId>(new[] { VariableTypeIds.BaseVariableType }));
            }

            public ValueTask<bool> IsTypeOfAsync(
                ExpandedNodeId subTypeId,
                ExpandedNodeId superTypeId,
                CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(true);
            }

            public ValueTask<bool> IsTypeOfAsync(NodeId subTypeId, NodeId superTypeId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(false);
            }

            public ValueTask<QualifiedName> FindReferenceTypeNameAsync(
                NodeId referenceTypeId,
                CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<QualifiedName>(new QualifiedName(BrowseNames.HasSubtype));
            }

            public ValueTask<NodeId> FindReferenceTypeAsync(QualifiedName browseName, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<NodeId>(ReferenceTypeIds.HasSubtype);
            }

            public ValueTask<bool> IsEncodingOfAsync(
                ExpandedNodeId encodingId,
                ExpandedNodeId datatypeId,
                CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(true);
            }

            public ValueTask<bool> IsEncodingForAsync(
                NodeId expectedTypeId,
                ExtensionObject value,
                CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(true);
            }

            public ValueTask<bool> IsEncodingForAsync(NodeId expectedTypeId, Variant value, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<bool>(true);
            }

            public ValueTask<NodeId> FindDataTypeIdAsync(ExpandedNodeId encodingId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<NodeId>(DataTypeIds.BaseDataType);
            }

            public ValueTask<NodeId> FindDataTypeIdAsync(NodeId encodingId, CancellationToken ct = default)
            {
                CallCount++;
                return new ValueTask<NodeId>(DataTypeIds.BaseDataType);
            }
        }
    }
}
