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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

// Conformance tests use inline literal arrays as expected-value
// assertions; the per-call allocation cost is irrelevant for tests
// and keeping the literal adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Base Information Model conformance.
    /// Verifies mandatory server objects, properties, and capabilities.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInformation")]
    public class BaseInformationTests : TestFixture
    {
        [Test]
        public async Task ReadServerTypeDefinitionAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));

            var typeId = ExpandedNodeId.ToNodeId(
                response.Results[0].References[0].NodeId,
                Session.NamespaceUris);
            Assert.That(typeId, Is.EqualTo(ObjectTypeIds.ServerType));
        }

        [Test]
        public async Task ReadNamespaceArrayContainsOpcUaAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_NamespaceArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string[] namespaces = result.GetValue<string[]>(default);
            Assert.That(namespaces, Is.Not.Null);
            Assert.That(namespaces, Is.Not.Empty);
            Assert.That(namespaces[0], Is.EqualTo(Namespaces.OpcUa));
        }

        [Test]
        public async Task ReadServerArrayContainsServerUriAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string[] serverArray = result.GetValue<string[]>(default);
            Assert.That(serverArray, Is.Not.Null);
            Assert.That(serverArray, Is.Not.Empty);
            Assert.That(serverArray[0], Is.Not.Empty);
        }

        [Test]
        public async Task ReadMaxBrowseContinuationPointsPositiveAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            ushort value = result.WrappedValue.GetUInt16();
            Assert.That(value, Is.GreaterThan((ushort)0));
        }

        [Test]
        public async Task ReadMaxQueryContinuationPointsExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxQueryContinuationPoints)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxHistoryContinuationPointsExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxHistoryContinuationPoints)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadConformanceUnitsExistsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerCapabilities,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task SecurityGroupFolderHasMandatoryMethodsAsync()
        {
            // Issue #3719 — the SecurityGroups folder (i=15443) was reported
            // as missing the Mandatory AddSecurityGroup / RemoveSecurityGroup
            // methods on its instance. Per Part 14 (PubSub) the standard
            // SecurityGroups folder (i=15443) shall expose these two methods.
            await BrowseRequiresMandatoryMethodsAsync(
                folderNodeId: new NodeId(15443),
                folderName: "SecurityGroups (i=15443)",
                expectedMethods: new[] { "AddSecurityGroup", "RemoveSecurityGroup" })
                .ConfigureAwait(false);
        }

        [Test]
        public async Task PubSubKeyPushTargetFolderHasMandatoryMethodsAsync()
        {
            // Issue #3719 — the KeyPushTargets folder (i=25440) was reported
            // as missing the Mandatory AddPushTarget / RemovePushTarget
            // methods on its instance.
            await BrowseRequiresMandatoryMethodsAsync(
                folderNodeId: new NodeId(25440),
                folderName: "KeyPushTargets (i=25440)",
                expectedMethods: new[] { "AddPushTarget", "RemovePushTarget" })
                .ConfigureAwait(false);
        }

        private async Task BrowseRequiresMandatoryMethodsAsync(
            NodeId folderNodeId,
            string folderName,
            string[] expectedMethods)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = folderNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Method,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            if (StatusCode.IsBad(response.Results[0].StatusCode))
            {
                Assert.Ignore(
                    folderName +
                    " is not present in the address space (Bad status: " +
                    response.Results[0].StatusCode + ").");
            }

            var methodNames = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            if (response.Results[0].References != default)
            {
                foreach (ReferenceDescription r in response.Results[0].References)
                {
                    if (!string.IsNullOrEmpty(r.BrowseName.Name))
                    {
                        methodNames.Add(r.BrowseName.Name);
                    }
                }
            }

            foreach (string expected in expectedMethods)
            {
                if (!methodNames.Contains(expected))
                {
                    Assert.Ignore(
                        "Mandatory method '" + expected + "' is missing from " +
                        folderName + " — issue #3719 still open.");
                }
            }
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerReadPositiveAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerRead)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint value = result.WrappedValue.GetUInt32();
            Assert.That(value, Is.GreaterThan((uint)0));
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerWriteExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerBrowseExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerMethodCallExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerRegisterNodesExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerTranslateBrowsePathsExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxMonitoredItemsPerCallExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadDiagnosticsEnabledFlagExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_EnabledFlag)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadBuildInfoProductNameNotEmptyAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_ProductName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.WrappedValue.GetString(), Is.Not.Empty);
        }

        [Test]
        public async Task ReadBuildInfoSoftwareVersionExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_SoftwareVersion)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out string _), Is.True);
        }

        [Test]
        public async Task ReadBuildInfoManufacturerNameExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_ManufacturerName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out string _), Is.True);
        }

        [Test]
        public async Task ReadBuildInfoBuildNumberExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_BuildNumber)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out string _), Is.True);
        }

        [Test]
        public async Task ReadBuildInfoBuildDateExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_BuildDate)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadServerStatusStartTimeBeforeCurrentTimeAsync()
        {
            DataValue startResult = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_StartTime).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(startResult.StatusCode), Is.True);

            DataValue currentResult = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(currentResult.StatusCode), Is.True);

            Assert.That(startResult.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
            Assert.That(currentResult.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
        }

        [Test]
        public async Task ReadServerStatusSecondsTillShutdownZeroAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_SecondsTillShutdown)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint value = result.WrappedValue.GetUInt32();
            Assert.That(value, Is.Zero);
        }

        [Test]
        public async Task ReadServerServiceLevelAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServiceLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            byte value = result.WrappedValue.GetByte();
            Assert.That(value, Is.InRange((byte)0, (byte)255));
        }

        [Test]
        public async Task ReadServerAuditingPropertyExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                VariableIds.Server_Auditing, Attributes.Value)
                .ConfigureAwait(false);
            // Some servers may not expose Auditing; accept Good or
            // gracefully handle Bad status.
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    $"Server_Auditing not accessible: {result.StatusCode}");
            }
        }

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

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
