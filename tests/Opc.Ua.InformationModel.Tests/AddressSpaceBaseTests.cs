// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for the server address space structure (OPC UA Part 5).
    /// Verifies that mandatory nodes, folders, and type hierarchies exist.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpace")]
    public class AddressSpaceBaseTests : TestFixture
    {
        [Test]
        public async Task RootFolderExistsAsync()
        {
            DataValue result = await ReadValueAsync(ObjectIds.RootFolder, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("Root"));
        }

        [Test]
        public async Task ObjectsFolderExistsAsync()
        {
            DataValue result = await ReadValueAsync(ObjectIds.ObjectsFolder, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("Objects"));
        }

        [Test]
        public async Task TypesFolderExistsAsync()
        {
            DataValue result = await ReadValueAsync(ObjectIds.TypesFolder, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("Types"));
        }

        [Test]
        public async Task ViewsFolderExistsAsync()
        {
            DataValue result = await ReadValueAsync(ObjectIds.ViewsFolder, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("Views"));
        }

        [Test]
        public async Task ServerObjectExistsAsync()
        {
            DataValue result = await ReadValueAsync(ObjectIds.Server, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("Server"));
        }

        [Test]
        public async Task ServerObjectHasRequiredChildrenAsync()
        {
            BrowseResult browseResult = await BrowseForwardAsync(ObjectIds.Server).ConfigureAwait(false);
            var childNames = new List<string>();
            foreach (ReferenceDescription r in browseResult.References)
            {
                childNames.Add(r.BrowseName.Name!);
            }

            Assert.That(childNames, Does.Contain("ServerCapabilities"));
            Assert.That(childNames, Does.Contain("ServerDiagnostics"));
            Assert.That(childNames, Does.Contain("ServerStatus"));
            Assert.That(childNames, Does.Contain("NamespaceArray"));
            Assert.That(childNames, Does.Contain("ServerArray"));
        }

        [Test]
        public async Task ServerCapabilitiesExistsAsync()
        {
            DataValue result = await ReadValueAsync(
                ObjectIds.Server_ServerCapabilities, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ServerStatusExistsAsync()
        {
            DataValue result = await ReadValueAsync(
                VariableIds.Server_ServerStatus, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ServerStatusHasRequiredVariablesAsync()
        {
            BrowseResult browseResult = await BrowseForwardAsync(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            var childNames = new List<string>();
            foreach (ReferenceDescription r in browseResult.References)
            {
                childNames.Add(r.BrowseName.Name!);
            }

            Assert.That(childNames, Does.Contain("State"));
            Assert.That(childNames, Does.Contain("CurrentTime"));
            Assert.That(childNames, Does.Contain("StartTime"));
            Assert.That(childNames, Does.Contain("BuildInfo"));
        }

        [Test]
        public async Task TypesFolderContainsSubfoldersAsync()
        {
            BrowseResult browseResult = await BrowseForwardAsync(ObjectIds.TypesFolder).ConfigureAwait(false);
            var childNames = new List<string>();
            foreach (ReferenceDescription r in browseResult.References)
            {
                childNames.Add(r.BrowseName.Name!);
            }

            Assert.That(childNames, Does.Contain("ObjectTypes"));
            Assert.That(childNames, Does.Contain("VariableTypes"));
            Assert.That(childNames, Does.Contain("DataTypes"));
            Assert.That(childNames, Does.Contain("ReferenceTypes"));
        }

        [Test]
        public async Task BaseObjectTypeExistsAsync()
        {
            DataValue result = await ReadValueAsync(ObjectTypeIds.BaseObjectType, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("BaseObjectType"));
        }

        [Test]
        public async Task BaseVariableTypeExistsAsync()
        {
            DataValue result = await ReadValueAsync(VariableTypeIds.BaseVariableType, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            QualifiedName browseName = result.GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("BaseVariableType"));
        }

        [Test]
        public async Task DataTypeHierarchyNumberToInt32Async()
        {
            // Verify Int32 → Integer → Number hierarchy via inverse browse
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = DataTypeIds.Int32,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));

            var parentId = ExpandedNodeId.ToNodeId(
                response.Results[0].References[0].NodeId, Session.NamespaceUris);
            Assert.That(parentId, Is.EqualTo(DataTypeIds.Integer));
        }

        [Test]
        public async Task ReferenceTypeHierarchyExistsAsync()
        {
            // HierarchicalReferences should be a subtype of References
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ReferenceTypeIds.HierarchicalReferences,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));

            var parentId = ExpandedNodeId.ToNodeId(
                response.Results[0].References[0].NodeId, Session.NamespaceUris);
            Assert.That(parentId, Is.EqualTo(ReferenceTypeIds.References));
        }

        [Test]
        public async Task VariableNodeHasDataTypeAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadValueAsync(nodeId, Attributes.DataType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out NodeId _), Is.True);
        }

        [Test]
        public async Task VariableNodeHasValueRankAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadValueAsync(nodeId, Attributes.ValueRank).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        private async Task<BrowseResult> BrowseForwardAsync(NodeId nodeId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
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
            return response.Results[0];
        }

        private async Task<DataValue> ReadValueAsync(NodeId nodeId, uint attributeId = Attributes.Value)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
