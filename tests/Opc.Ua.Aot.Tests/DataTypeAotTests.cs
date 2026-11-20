// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for DataType operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class DataTypeAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task ReadDataTypeDefinitionAsync()
        {
            Node node = await fixture.Session.NodeCache
                .FetchNodeAsync(
                    DataTypeIds.ServerStatusDataType,
                    CancellationToken.None)
                .ConfigureAwait(false) ??
                throw new InvalidOperationException("The data type node was not found.");

            await Assert.That(node).IsNotNull();
            await Assert.That(node.NodeClass)
                .IsEqualTo(NodeClass.DataType);
            await Assert.That(node.BrowseName.Name)
                .IsEqualTo("ServerStatusDataType");
        }

        [Test]
        public async Task ReadEnumStringsAsync()
        {
            var browser = new Browser(fixture.Session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                IncludeSubtypes = false,
                NodeClassMask = (int)NodeClass.Variable
            };

            ArrayOf<ReferenceDescription> refs =
                await browser.BrowseAsync(
                    DataTypeIds.ServerState,
                    CancellationToken.None).ConfigureAwait(false);

            ReferenceDescription enumRef = refs.ToList().FirstOrDefault(
                r => r.BrowseName.Name is "EnumStrings" or "EnumValues") ??
                throw new InvalidOperationException("The enum values node was not found.");

            await Assert.That(enumRef).IsNotNull();

            var enumNodeId = ExpandedNodeId.ToNodeId(
                enumRef.NodeId, fixture.Session.NamespaceUris);

            DataValue enumValues = await fixture.Session.ReadValueAsync(
                enumNodeId, CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(enumValues.StatusCode))
                .IsTrue();
            await Assert.That(enumValues.WrappedValue.IsNull).IsFalse();
        }

        [Test]
        public async Task ReadBuildInfoAsync()
        {
            DataValue buildInfo = await fixture.Session.ReadValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(buildInfo.StatusCode))
                .IsTrue();
            await Assert.That(buildInfo.WrappedValue.AsBoxedObject())
                .IsNotNull();

            DataValue productName = await fixture.Session.ReadValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_ProductName,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(productName.StatusCode))
                .IsTrue();
            await Assert.That(productName.WrappedValue.AsBoxedObject())
                .IsNotNull();
        }
    }
}
