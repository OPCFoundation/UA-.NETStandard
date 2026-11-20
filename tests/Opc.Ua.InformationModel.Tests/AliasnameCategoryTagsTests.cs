// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using static Opc.Ua.InformationModel.Tests.AliasNameTestHelpers;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for AliasName Category Tags.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AliasName")]
    public class AliasnameCategoryTagsTests : TestFixture
    {
        [Description("Browse Aliases for AliasCategories.")]
        [Test]
        public async Task BrowseAliasesForAliasCategoryTagsAsync()
        {
            (NodeId category, _) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            DataValue dv = await ReadAttributeAsync(
                Session, category, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            Assert.That(dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("TagVariables"));

            // Verify the category is typed as AliasNameCategoryType (Part 17).
            IList<ReferenceDescription> typeDefs = await BrowseChildrenAsync(
                Session, category, ReferenceTypeIds.HasTypeDefinition)
                .ConfigureAwait(false);
            NodeId[] typeNodeIds = [.. typeDefs.Select(r => ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris))];
            Assert.That(typeNodeIds, Contains.Item(AliasNameCategoryTypeNodeId));
        }

        [Description("Verify that at least one instance of a AliasName is included in the category and that the instance is an AliasName for a Variable.")]
        [Test]
        public async Task TagsCategoryContainsAliasNameForVariableAsync()
        {
            (NodeId category, _) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            IList<ReferenceDescription> children = await BrowseChildrenAsync(
                Session, category).ConfigureAwait(false);

            int aliasInstances = 0;
            int aliasForVariable = 0;
            foreach (ReferenceDescription child in children)
            {
                var typeDef = ExpandedNodeId.ToNodeId(
                    child.TypeDefinition, Session.NamespaceUris);
                if (typeDef != AliasNameTypeNodeId)
                {
                    continue;
                }
                aliasInstances++;

                var aliasId = ExpandedNodeId.ToNodeId(
                    child.NodeId, Session.NamespaceUris);
                IList<ReferenceDescription> aliasTargets = await BrowseChildrenAsync(
                    Session, aliasId, AliasForNodeId).ConfigureAwait(false);
                foreach (ReferenceDescription target in aliasTargets)
                {
                    if (target.NodeClass == NodeClass.Variable)
                    {
                        aliasForVariable++;
                    }
                }
            }

            Assert.That(aliasInstances, Is.GreaterThan(0),
                "TagVariables should contain at least one AliasName instance.");
            Assert.That(aliasForVariable, Is.GreaterThan(0),
                "TagVariables should contain at least one AliasName referencing a Variable.");
        }

        [Description("Call the FindAlias method on the TagVariables object, passing in '%' for the filter.")]
        [Test]
        public async Task FindAliasOnTagVariablesWithPercentFilterAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "%", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records, Is.Not.Empty,
                "FindAlias('%') on TagVariables should return at least one alias.");
            Assert.That(records.Select(r => r.AliasName.Name),
                Has.Some.EqualTo("TIC101_Setpoint"));
        }
    }
}
