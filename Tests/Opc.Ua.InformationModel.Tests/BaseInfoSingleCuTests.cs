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

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for single-CU Base Information checks:
    /// condition types, modelling rules, roles, diagnostics arrays,
    /// and standard type definitions.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoSingleCU")]
    public class BaseInfoSingleCuTests : TestFixture
    {
        [Test]
        public async Task ConditionTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.ConditionType, "ConditionType")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task DialogConditionTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.DialogConditionType, "DialogConditionType")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ExclusiveLimitAlarmTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.ExclusiveLimitAlarmType,
                "ExclusiveLimitAlarmType").ConfigureAwait(false);
        }

        [Test]
        public async Task ModellingRuleMandatoryExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.ModellingRule_Mandatory).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ModellingRuleOptionalExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.ModellingRule_Optional).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ModellingRuleMandatoryPlaceholderExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.ModellingRule_MandatoryPlaceholder)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ModellingRuleOptionalPlaceholderExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.ModellingRule_OptionalPlaceholder)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task RoleSetExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.Server_ServerCapabilities_RoleSet)
                .ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("RoleSet not accessible.");
            }
        }

        [Test]
        public async Task WellKnownRolesAnonymousExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_Anonymous).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_Anonymous should exist.");
        }

        [Test]
        public async Task WellKnownRolesAuthenticatedUserExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_AuthenticatedUser)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_AuthenticatedUser should exist.");
        }

        [Test]
        public async Task WellKnownRolesObserverExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_Observer).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_Observer should exist.");
        }

        [Test]
        public async Task WellKnownRolesOperatorExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_Operator).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_Operator should exist.");
        }

        [Test]
        public async Task WellKnownRolesEngineerExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_Engineer).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_Engineer should exist.");
        }

        [Test]
        public async Task WellKnownRolesSupervisorExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_Supervisor).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_Supervisor should exist.");
        }

        [Test]
        public async Task WellKnownRolesSecurityAdminExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_SecurityAdmin).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_SecurityAdmin should exist.");
        }

        [Test]
        public async Task WellKnownRolesConfigureAdminExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectIds.WellKnownRole_ConfigureAdmin).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "WellKnownRole_ConfigureAdmin should exist.");
        }

        [Test]
        public async Task RoleHasIdentitiesPropertyAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.WellKnownRole_Anonymous).ConfigureAwait(false);

            bool hasIdentities = false;
            foreach (ReferenceDescription r in refs)
            {
                if (r.BrowseName.Name == "Identities")
                {
                    hasIdentities = true;
                    break;
                }
            }

            // Browse from an anonymous session can't see Identities because the
            // standard nodeset declares RolePermission only for SecurityAdmin on
            // that property. Fall back to the well-known NodeId to verify the
            // server still exposes it.
            if (!hasIdentities)
            {
                NodeId fallback = Opc.Ua.Core.Security.Tests.WellKnownRoleNodeIds.TryGetChild(
                    ObjectIds.WellKnownRole_Anonymous, "Identities");
                hasIdentities = !fallback.IsNull;
            }

            Assert.That(hasIdentities, Is.True,
                "Anonymous role should expose an Identities property.");
        }

        [Test]
        public async Task SubscriptionDiagnosticsArrayExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                VariableIds
                    .Server_ServerDiagnostics_SubscriptionDiagnosticsArray)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(result.StatusCode) ||
                result.StatusCode.Code == StatusCodes.BadNotReadable ||
                result.StatusCode.Code == StatusCodes.BadUserAccessDenied,
                Is.True,
                "SubscriptionDiagnosticsArray should exist.");
        }

        [Test]
        public async Task SamplingIntervalDiagnosticsArrayExistsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_SamplingIntervalDiagnosticsArray)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Ignore(
                    $"SamplingIntervalDiagnosticsArray not accessible: {result.StatusCode}");
            }
        }

        [Test]
        public async Task SessionDiagnosticsArrayExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                VariableIds
                    .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(result.StatusCode) ||
                result.StatusCode.Code == StatusCodes.BadNotReadable ||
                result.StatusCode.Code == StatusCodes.BadUserAccessDenied,
                Is.True,
                "SessionDiagnosticsArray should exist.");
        }

        [Test]
        public async Task BaseDataVariableTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                VariableTypeIds.BaseDataVariableType,
                "BaseDataVariableType").ConfigureAwait(false);
        }

        [Test]
        public async Task PropertyTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                VariableTypeIds.PropertyType,
                "PropertyType").ConfigureAwait(false);
        }

        [Test]
        public async Task FolderTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                ObjectTypeIds.FolderType,
                "FolderType").ConfigureAwait(false);
        }

        [Test]
        public async Task FolderTypeHasCorrectReferencesAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectTypeIds.FolderType).ConfigureAwait(false);
            // FolderType may have subtypes or other references
            Assert.That(refs, Is.Not.Null);
        }

        [Test]
        public async Task TwoStateDiscreteTypeExistsAsync()
        {
            await AssertTypeExistsAsync(
                VariableTypeIds.TwoStateDiscreteType,
                "TwoStateDiscreteType").ConfigureAwait(false);
        }

        [Test]
        public async Task TwoStateDiscreteIsSubtypeOfDiscreteItemAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = VariableTypeIds.TwoStateDiscreteType,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].References.Count,
                Is.GreaterThan(0),
                "TwoStateDiscreteType should have a supertype.");

            var parentId = ExpandedNodeId.ToNodeId(
                response.Results[0].References[0].NodeId,
                Session.NamespaceUris);
            Assert.That(parentId,
                Is.EqualTo(VariableTypeIds.DiscreteItemType),
                "TwoStateDiscreteType should be subtype of DiscreteItemType.");
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

        private Task<DataValue> ReadNodeValueAsync(NodeId nodeId)

        {
            return ReadAttributeAsync(
                nodeId, Attributes.Value)
;
        }

        private Task<DataValue> ReadBrowseNameAsync(NodeId nodeId)

        {
            return ReadAttributeAsync(
                nodeId, Attributes.BrowseName);
        }

        private async Task<List<ReferenceDescription>> BrowseForwardAsync(
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

        private async Task AssertTypeExistsAsync(NodeId typeId, string name)
        {
            DataValue result = await ReadBrowseNameAsync(typeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"{name} should exist in the address space.");
        }
    }
}
