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

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Base Information capabilities, modelling rules,
    /// operation limits, redundancy, and optional server features.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoCapabilities")]
    public class BaseInfoCapabilitiesTests : TestFixture
    {
        [Test]
        public async Task ReadMaxSubscriptionsPerSessionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxSubscriptionsPerSession)
                .ConfigureAwait(false);

            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("MaxSubscriptionsPerSession not supported by server.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxMonitoredItemsPerSubscriptionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription)
                .ConfigureAwait(false);

            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("MaxMonitoredItemsPerSubscription not supported by server.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyModellingRuleMandatoryExistsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.ModellingRule_Mandatory,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            QualifiedName browseName = response.Results[0].GetValue<QualifiedName>(default);
            Assert.That(browseName, Is.Not.Null);
            Assert.That(browseName.Name, Is.EqualTo("Mandatory"));
        }

        [Test]
        public async Task VerifyModellingRuleOptionalExistsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.ModellingRule_Optional,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            QualifiedName browseName = response.Results[0].GetValue<QualifiedName>(default);
            Assert.That(browseName, Is.Not.Null);
            Assert.That(browseName.Name, Is.EqualTo("Optional"));
        }

        [Test]
        public async Task ReadServerNamespacesFolderAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_Namespaces,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(
                response.Results[0].References.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task VerifyServerRedundancyExistsAsync()
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
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            bool found = false;
            foreach (ReferenceDescription reference in response.Results[0].References)
            {
                if (reference.BrowseName == new QualifiedName("ServerRedundancy"))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "ServerRedundancy child not found under Server.");
        }

        [Test]
        public async Task ReadRedundancySupportAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerRedundancy_RedundancySupport)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            int value = result.WrappedValue.GetInt32();
            Assert.That(value, Is.GreaterThanOrEqualTo(0));
            Assert.That(value, Is.LessThanOrEqualTo(5));
        }

        [Test]
        public async Task BrowseServerCapabilitiesOperationLimitsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerCapabilities_OperationLimits,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(
                response.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerHistoryReadDataAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerHistoryReadEventsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerHistoryUpdateDataAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerHistoryUpdateEventsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerNodeManagementAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyRolesFolderExistsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerCapabilities_RoleSet,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            if (StatusCode.IsBad(response.Results[0].StatusCode))
            {
                Assert.Fail("RoleSet folder not supported by server.");
            }

            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxMonitoredItemsQueueSizeAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxMonitoredItemsQueueSize)
                .ConfigureAwait(false);

            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Fail("MaxMonitoredItemsQueueSize not supported by server.");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
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
    }
}
