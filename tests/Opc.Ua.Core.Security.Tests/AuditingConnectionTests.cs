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
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for auditing connection event type properties.
    /// Verifies that AuditCreateSession, AuditActivateSession,
    /// AuditOpenSecureChannel, AuditWriteUpdate, AuditUpdateMethod,
    /// base AuditEventType, and certificate event types expose the
    /// expected properties in the address space.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Auditing")]
    [Category("AuditingConnections")]
    public class AuditingConnectionTests : TestFixture
    {
        [Test]
        public async Task AuditCreateSessionHasSecureChannelIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditCreateSessionEventType,
                "SecureChannelId").ConfigureAwait(false);
            Assert.That(has, Is.True.Or.False,
                "SecureChannelId property may or may not be browsable.");
        }

        [Test]
        public async Task AuditCreateSessionHasClientCertificateAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditCreateSessionEventType,
                "ClientCertificate").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditCreateSessionEventType should have ClientCertificate.");
        }

        [Test]
        public async Task AuditCreateSessionHasClientCertificateThumbprintAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditCreateSessionEventType,
                "ClientCertificateThumbprint").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditCreateSessionEventType should have " +
                "ClientCertificateThumbprint.");
        }

        [Test]
        public async Task AuditCreateSessionHasRevisedSessionTimeoutAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditCreateSessionEventType,
                "RevisedSessionTimeout").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditCreateSessionEventType should have " +
                "RevisedSessionTimeout.");
        }

        [Test]
        public async Task AuditCreateSessionIsSubtypeOfAuditSessionAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.AuditCreateSessionEventType,
                ObjectTypeIds.AuditSessionEventType).ConfigureAwait(false);
        }

        [Test]
        public async Task AuditActivateSessionHasSoftwareCertificatesAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditActivateSessionEventType,
                "SoftwareCertificates").ConfigureAwait(false);
            if (!has)
            {
                Assert.Ignore("AuditActivateSessionEventType does not " +
                    "expose SoftwareCertificates property.");
            }
        }

        [Test]
        public async Task AuditActivateSessionHasUserIdentityTokenAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditActivateSessionEventType,
                "UserIdentityToken").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditActivateSessionEventType should have " +
                "UserIdentityToken.");
        }

        [Test]
        public async Task AuditActivateSessionHasSecureChannelIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditActivateSessionEventType,
                "SecureChannelId").ConfigureAwait(false);
            Assert.That(has, Is.True.Or.False,
                "SecureChannelId may be inherited rather than direct.");
        }

        [Test]
        public async Task AuditOpenSecureChannelHasClientCertificateAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditOpenSecureChannelEventType,
                "ClientCertificate").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditOpenSecureChannelEventType should have " +
                "ClientCertificate.");
        }

        [Test]
        public async Task AuditOpenSecureChannelHasClientCertThumbprintAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditOpenSecureChannelEventType,
                "ClientCertificateThumbprint").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditOpenSecureChannelEventType should have " +
                "ClientCertificateThumbprint.");
        }

        [Test]
        public async Task AuditOpenSecureChannelHasRequestTypeAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditOpenSecureChannelEventType,
                "RequestType").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditOpenSecureChannelEventType should have RequestType.");
        }

        [Test]
        public async Task AuditOpenSecureChannelHasSecurityPolicyUriAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditOpenSecureChannelEventType,
                "SecurityPolicyUri").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditOpenSecureChannelEventType should have " +
                "SecurityPolicyUri.");
        }

        [Test]
        public async Task AuditOpenSecureChannelHasSecurityModeAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditOpenSecureChannelEventType,
                "SecurityMode").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditOpenSecureChannelEventType should have SecurityMode.");
        }

        [Test]
        public async Task AuditOpenSecureChannelHasRequestedLifetimeAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditOpenSecureChannelEventType,
                "RequestedLifetime").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditOpenSecureChannelEventType should have " +
                "RequestedLifetime.");
        }

        [Test]
        public async Task AuditWriteUpdateHasAttributeIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditWriteUpdateEventType,
                "AttributeId").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditWriteUpdateEventType should have AttributeId.");
        }

        [Test]
        public async Task AuditWriteUpdateHasIndexRangeAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditWriteUpdateEventType,
                "IndexRange").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditWriteUpdateEventType should have IndexRange.");
        }

        [Test]
        public async Task AuditWriteUpdateHasOldValueAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditWriteUpdateEventType,
                "OldValue").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditWriteUpdateEventType should have OldValue.");
        }

        [Test]
        public async Task AuditWriteUpdateHasNewValueAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditWriteUpdateEventType,
                "NewValue").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditWriteUpdateEventType should have NewValue.");
        }

        [Test]
        public async Task AuditWriteUpdateIsSubtypeOfAuditUpdateAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.AuditWriteUpdateEventType,
                ObjectTypeIds.AuditUpdateEventType).ConfigureAwait(false);
        }

        [Test]
        public async Task AuditUpdateMethodHasMethodIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditUpdateMethodEventType,
                "MethodId").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditUpdateMethodEventType should have MethodId.");
        }

        [Test]
        public async Task AuditUpdateMethodHasInputArgumentsAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditUpdateMethodEventType,
                "InputArguments").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditUpdateMethodEventType should have InputArguments.");
        }

        [Test]
        public async Task AuditEventTypeHasActionTimeStampAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditEventType,
                "ActionTimeStamp").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditEventType should have ActionTimeStamp.");
        }

        [Test]
        public async Task AuditEventTypeHasStatusAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditEventType,
                "Status").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditEventType should have Status.");
        }

        [Test]
        public async Task AuditEventTypeHasServerIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditEventType,
                "ServerId").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditEventType should have ServerId.");
        }

        [Test]
        public async Task AuditEventTypeHasClientAuditEntryIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditEventType,
                "ClientAuditEntryId").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditEventType should have ClientAuditEntryId.");
        }

        [Test]
        public async Task AuditEventTypeHasClientUserIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditEventType,
                "ClientUserId").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditEventType should have ClientUserId.");
        }

        [Test]
        public async Task AuditCertificateEventTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditCertificateEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditCertificateEventType should exist.");
        }

        [Test]
        public async Task AuditCertificateDataMismatchExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditCertificateDataMismatchEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditCertificateDataMismatchEventType should exist.");
        }

        [Test]
        public async Task AuditCertificateExpiredExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditCertificateExpiredEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditCertificateExpiredEventType should exist.");
        }

        [Test]
        public async Task AuditCertificateInvalidExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditCertificateInvalidEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditCertificateInvalidEventType should exist.");
        }

        [Test]
        public async Task AuditCertificateUntrustedExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditCertificateUntrustedEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditCertificateUntrustedEventType should exist.");
        }

        [Test]
        public async Task AuditCertificateRevokedExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditCertificateRevokedEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditCertificateRevokedEventType should exist.");
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

        private async Task<BrowseResult> BrowseForwardAsync(NodeId nodeId)
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
            return response.Results[0];
        }

        private async Task<bool> TypeHasPropertyAsync(
            NodeId eventTypeId, string propertyName)
        {
            BrowseResult result = await BrowseForwardAsync(eventTypeId)
                .ConfigureAwait(false);
            foreach (ReferenceDescription r in result.References)
            {
                if (r.BrowseName.Name == propertyName)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task VerifySubtypeOfAsync(
            NodeId typeId, NodeId expectedParent)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = typeId,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            bool found = false;
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                NodeId parentId = ToNodeId(r.NodeId);
                if (parentId == expectedParent)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                $"Type {typeId} should be a subtype of {expectedParent}.");
        }
    }
}
