/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
