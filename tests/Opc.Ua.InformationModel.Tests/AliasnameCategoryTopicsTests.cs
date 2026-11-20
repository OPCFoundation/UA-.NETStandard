/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using static Opc.Ua.InformationModel.Tests.AliasNameTestHelpers;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for AliasName Category Topics.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AliasName")]
    public class AliasnameCategoryTopicsTests : TestFixture
    {
        [Description("Browse Aliases for the Topics AliasCategory.")]
        [Test]
        public async Task BrowseAliasesForAliasCategoryTopicsAsync()
        {
            (NodeId category, _) = await FindCategoryAsync(
                Session, "Topics").ConfigureAwait(false);

            DataValue dv = await ReadAttributeAsync(
                Session, category, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            Assert.That(dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("Topics"));

            // Verify the category is typed as AliasNameCategoryType (Part 17).
            IList<ReferenceDescription> typeDefs = await BrowseChildrenAsync(
                Session, category, ReferenceTypeIds.HasTypeDefinition)
                .ConfigureAwait(false);
            NodeId[] typeNodeIds = [.. typeDefs.Select(r => ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris))];
            Assert.That(typeNodeIds, Contains.Item(AliasNameCategoryTypeNodeId));
        }

        [Description("Verify that at least one instance of a Topic is included in the category and that the instance is a Dataset.")]
        [Test]
        public async Task TopicsCategoryContainsAliasNameForDatasetAsync()
        {
            (NodeId category, _) = await FindCategoryAsync(
                Session, "Topics").ConfigureAwait(false);

            IList<ReferenceDescription> children = await BrowseChildrenAsync(
                Session, category).ConfigureAwait(false);

            int aliasInstances = 0;
            int aliasForDataSet = 0;
            var browseNames = new List<string>();
            foreach (ReferenceDescription child in children)
            {
                var typeDef = ExpandedNodeId.ToNodeId(
                    child.TypeDefinition, Session.NamespaceUris);
                if (typeDef != AliasNameTypeNodeId)
                {
                    continue;
                }
                aliasInstances++;
                browseNames.Add(child.BrowseName.Name!);

                // Part 17 §9 requires every alias in the Topics hierarchy to
                // point at a PublishedDataSetType instance.
                var aliasId = ExpandedNodeId.ToNodeId(
                    child.NodeId, Session.NamespaceUris);
                IList<ReferenceDescription> targets = await BrowseChildrenAsync(
                    Session, aliasId, AliasForNodeId).ConfigureAwait(false);
                Assert.That(targets, Is.Not.Empty,
                    $"Alias '{child.BrowseName.Name}' should have an AliasFor target.");

                foreach (ReferenceDescription target in targets)
                {
                    Assert.That(
                        await IsPublishedDataSetAsync(Session, target).ConfigureAwait(false),
                        Is.True,
                        $"Topics alias '{child.BrowseName.Name}' should reference a PublishedDataSetType instance.");
                    aliasForDataSet++;
                }
            }

            Assert.That(aliasInstances, Is.GreaterThan(0),
                "Topics should contain at least one AliasName instance.");
            Assert.That(aliasForDataSet, Is.GreaterThan(0),
                "Topics should contain at least one AliasName referencing a dataset.");
            // The datasets are created by the reference server's
            // ReferenceServerConfigurationNodeManager under PublishedDataSets.
            Assert.That(browseNames, Contains.Item("ServerEvents"));
            Assert.That(browseNames, Contains.Item("ReferenceDataSet"));
        }

        [Description("Call the FindAlias method on the Topics object, passing in '%' for the filter.")]
        [Test]
        public async Task FindAliasOnTopicsWithPercentFilterAsync()
        {
            (NodeId category, NodeId method) = await FindCategoryAsync(
                Session, "Topics").ConfigureAwait(false);

            CallMethodResult result = await CallFindAliasAsync(
                Session, category, method, "%Events", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            string[] names = [.. records.Select(r => r.AliasName.Name)!];
            Assert.That(names, Contains.Item("ServerEvents"));
            Assert.That(names, Contains.Item("AuditEvents"));
        }
    }
}
