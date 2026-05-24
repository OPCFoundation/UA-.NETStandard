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
