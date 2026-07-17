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

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for the A and C CertificateExpiration
    /// conformance unit. Verifies that CertificateExpirationAlarmType
    /// exists and has the expected properties.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsCertificateExpirationTests
        : TestFixture
    {
        [Test]
        public async Task CertificateExpirationAlarmTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.CertificateExpirationAlarmType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "CertificateExpirationAlarmType should exist.");
        }

        [Test]
        public async Task CertificateExpirationIsSubtypeOfSystemOffNormalAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.CertificateExpirationAlarmType,
                ObjectTypeIds.SystemOffNormalAlarmType)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CertificateExpirationHasExpirationDateAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.CertificateExpirationAlarmType,
                "ExpirationDate").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "CertificateExpirationAlarmType should have " +
                "ExpirationDate property.");
        }

        [Test]
        public async Task CertificateExpirationHasExpirationLimitAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.CertificateExpirationAlarmType,
                "ExpirationLimit").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "CertificateExpirationAlarmType should have " +
                "ExpirationLimit property.");
        }

        [Test]
        public async Task CertificateExpirationHasCertificateTypeAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.CertificateExpirationAlarmType,
                "CertificateType").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "CertificateExpirationAlarmType should have " +
                "CertificateType property.");
        }

        [Test]
        public async Task CertificateGroupExposesDynamicAlarmInstancesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new()
                    {
                        NodeId =
                            ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            ReferenceDescription? certificateExpired = null;
            ReferenceDescription? trustListOutOfDate = null;
            int certificateExpiredCount = 0;
            int trustListOutOfDateCount = 0;
            foreach (ReferenceDescription reference in response.Results[0].References)
            {
                if (reference.BrowseName.Name == BrowseNames.CertificateExpired)
                {
                    certificateExpired = reference;
                    certificateExpiredCount++;
                }
                else if (reference.BrowseName.Name == BrowseNames.TrustListOutOfDate)
                {
                    trustListOutOfDate = reference;
                    trustListOutOfDateCount++;
                }
            }

            Assert.That(certificateExpired, Is.Not.Null);
            Assert.That(trustListOutOfDate, Is.Not.Null);
            BrowseResult certificateChildren = await BrowseForwardAsync(
                ToNodeId(certificateExpired!.NodeId)).ConfigureAwait(false);
            BrowseResult trustListChildren = await BrowseForwardAsync(
                ToNodeId(trustListOutOfDate!.NodeId)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(certificateExpiredCount, Is.EqualTo(1));
                Assert.That(trustListOutOfDateCount, Is.EqualTo(1));
                Assert.That(
                    ToNodeId(certificateExpired!.TypeDefinition),
                    Is.EqualTo(ObjectTypeIds.CertificateExpirationAlarmType));
                Assert.That(certificateExpired.NodeId.NamespaceIndex, Is.GreaterThan(0));
                Assert.That(
                    ToNodeId(trustListOutOfDate!.TypeDefinition),
                    Is.EqualTo(ObjectTypeIds.TrustListOutOfDateAlarmType));
                Assert.That(trustListOutOfDate.NodeId.NamespaceIndex, Is.GreaterThan(0));
                foreach (ReferenceDescription reference in certificateChildren.References)
                {
                    Assert.That(
                        reference.NodeId.NamespaceIndex,
                        Is.GreaterThan(0),
                        $"CertificateExpired child {reference.BrowseName} retained " +
                        $"declaration NodeId {reference.NodeId}.");
                }
                foreach (ReferenceDescription reference in trustListChildren.References)
                {
                    Assert.That(
                        reference.NodeId.NamespaceIndex,
                        Is.GreaterThan(0),
                        $"TrustListOutOfDate child {reference.BrowseName} retained " +
                        $"declaration NodeId {reference.NodeId}.");
                }
            });
        }

        [Test]
        public async Task StandardCertificateAlarmDeclarationsHaveMandatoryModellingRulesAsync()
        {
            NodeId[] declarationIds =
            [
                VariableIds.CertificateExpirationAlarmType_ExpirationDate,
                VariableIds.CertificateExpirationAlarmType_CertificateType,
                VariableIds.CertificateExpirationAlarmType_Certificate,
                VariableIds.TrustListOutOfDateAlarmType_TrustListId,
                VariableIds.TrustListOutOfDateAlarmType_LastUpdateTime,
                VariableIds.TrustListOutOfDateAlarmType_UpdateFrequency
            ];
            var descriptions = new BrowseDescription[declarationIds.Length];
            for (int ii = 0; ii < declarationIds.Length; ii++)
            {
                descriptions[ii] = new BrowseDescription
                {
                    NodeId = declarationIds[ii],
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasModellingRule,
                    IncludeSubtypes = false,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                };
            }

            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                descriptions.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(declarationIds.Length));
            for (int ii = 0; ii < response.Results.Count; ii++)
            {
                Assert.That(
                    StatusCode.IsGood(response.Results[ii].StatusCode),
                    Is.True,
                    $"Browse failed for declaration {declarationIds[ii]}.");
                Assert.That(
                    response.Results[ii].References.Count,
                    Is.EqualTo(1),
                    $"Declaration {declarationIds[ii]} must expose one HasModellingRule reference.");
                Assert.That(
                    ToNodeId(response.Results[ii].References[0].NodeId),
                    Is.EqualTo(ObjectIds.ModellingRule_Mandatory));
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
            NodeId typeId, string propertyName)
        {
            BrowseResult result = await BrowseForwardAsync(typeId)
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
