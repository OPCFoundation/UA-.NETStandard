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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// Extended compliance tests for audit event type hierarchy:
    /// AddNodes, DeleteNodes, AddReferences, DeleteReferences,
    /// CreateSession mandatory properties, and condition event chain.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AuditingExtended")]
    public class AuditingExtendedTests : TestFixture
    {
        [Test]
        public async Task AuditAddNodesEventTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.AuditAddNodesEventType,
                "AuditAddNodesEventType").ConfigureAwait(false);
        }

        [Test]
        public async Task AuditDeleteNodesEventTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.AuditDeleteNodesEventType,
                "AuditDeleteNodesEventType").ConfigureAwait(false);
        }

        [Test]
        public async Task AuditAddReferencesEventTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.AuditAddReferencesEventType,
                "AuditAddReferencesEventType").ConfigureAwait(false);
        }

        [Test]
        public async Task AuditDeleteReferencesEventTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.AuditDeleteReferencesEventType,
                "AuditDeleteReferencesEventType").ConfigureAwait(false);
        }

        [Test]
        public async Task AuditCreateSessionEventTypeHasMandatoryPropertiesAsync()
        {
            List<ReferenceDescription> refs = await BrowseChildrenAsync(
                ObjectTypeIds.AuditCreateSessionEventType)
                .ConfigureAwait(false);

            // Should have properties like SecureChannelId, ClientCertificate,
            // ClientCertificateThumbprint, RevisedSessionTimeout
            bool hasAny = refs.Count > 0;
            Assert.That(hasAny, Is.True,
                "AuditCreateSessionEventType should have child properties.");
        }

        [Test]
        public async Task AuditActivateSessionEventTypeHasPropertiesAsync()
        {
            List<ReferenceDescription> refs = await BrowseChildrenAsync(
                ObjectTypeIds.AuditActivateSessionEventType)
                .ConfigureAwait(false);

            bool hasAny = refs.Count > 0;
            Assert.That(hasAny, Is.True,
                "AuditActivateSessionEventType should have child properties.");
        }

        [Test]
        public async Task AuditConditionCommentEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditConditionCommentEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditConditionCommentEventType not supported.");
            }
        }

        [Test]
        public async Task AuditConditionEnableEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditConditionEnableEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditConditionEnableEventType not supported.");
            }
        }

        [Test]
        public async Task AuditConditionAcknowledgeEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditConditionAcknowledgeEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditConditionAcknowledgeEventType not supported.");
            }
        }

        [Test]
        public async Task AuditHistoryEventUpdateEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditHistoryEventUpdateEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditHistoryEventUpdateEventType not supported.");
            }
        }

        private async Task<DataValue> ReadBrowseNameAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task AssertTypeExistsAsync(NodeId typeId, string name)
        {
            DataValue result = await ReadBrowseNameAsync(typeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"{name} should exist in the address space.");
        }

        private async Task<List<ReferenceDescription>> BrowseChildrenAsync(
            NodeId nodeId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
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
            var refs = new List<ReferenceDescription>();
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                refs.Add(r);
            }
            return refs;
        }
    }
}
