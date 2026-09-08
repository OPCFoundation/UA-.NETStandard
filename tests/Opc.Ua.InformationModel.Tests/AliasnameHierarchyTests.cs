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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using static Opc.Ua.InformationModel.Tests.AliasNameTestHelpers;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for AliasName Hierarchy.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AliasName")]
    public class AliasnameHierarchyTests : TestFixture
    {
        [Description("Verify that the AliasNameCategories can be nested.")]
        [Test]
        public async Task AliasNameCategoriesCanBeNestedAsync()
        {
            // Walk Aliases → categories and verify each category is an
            // instance of AliasNameCategoryType, and that the standard
            // categories surface AliasName instances of AliasNameType.
            IList<ReferenceDescription> categories =
                await BrowseChildrenAsync(Session, AliasesNodeId).ConfigureAwait(false);

            var categoryNames = new List<string>();
            int aliasNamesFound = 0;

            foreach (ReferenceDescription category in categories)
            {
                var categoryTypeDef = ExpandedNodeId.ToNodeId(
                    category.TypeDefinition, Session.NamespaceUris);
                if (categoryTypeDef != AliasNameCategoryTypeNodeId)
                {
                    continue;
                }
                categoryNames.Add(category.BrowseName.Name);

                var categoryId = ExpandedNodeId.ToNodeId(
                    category.NodeId, Session.NamespaceUris);
                IList<ReferenceDescription> aliasChildren =
                    await BrowseChildrenAsync(Session, categoryId)
                        .ConfigureAwait(false);

                foreach (ReferenceDescription child in aliasChildren)
                {
                    var childTypeDef = ExpandedNodeId.ToNodeId(
                        child.TypeDefinition, Session.NamespaceUris);
                    if (childTypeDef == AliasNameTypeNodeId)
                    {
                        aliasNamesFound++;
                    }
                }
            }

            Assert.That(categoryNames, Has.Count.GreaterThanOrEqualTo(2),
                "Expected at least two nested categories under Aliases (TagVariables and Topics).");
            Assert.That(categoryNames, Contains.Item("TagVariables"));
            Assert.That(categoryNames, Contains.Item("Topics"));
            Assert.That(aliasNamesFound, Is.GreaterThan(0),
                "Nested categories should expose AliasName instances.");
        }

        [Description("Verify that an AliasNameCategoryType instance exists under another AliasNameCategoryType instance.")]
        [Test]
        public async Task AliasNameCategoryNestedUnderCategoryAsync()
        {
            (NodeId tagVariables, _) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            ReferenceDescription nested = await FindNestedCategoryAsync(
                Session, tagVariables).ConfigureAwait(false);

            Assert.That(nested, Is.Not.Null,
                "TagVariables should organize a nested AliasNameCategoryType instance.");
            Assert.That(nested.BrowseName.Name, Is.EqualTo("Devices"));

            // The nested category must carry the mandatory FindAlias method.
            var nestedId = ExpandedNodeId.ToNodeId(
                nested.NodeId, Session.NamespaceUris);
            NodeId methodId = await FindMethodAsync(
                Session, nestedId, "FindAlias").ConfigureAwait(false);
            Assert.That(methodId.IsNull, Is.False,
                "The nested category should expose a FindAlias method.");
        }

        [Description("Verify FindAlias on a nested category is restricted to that category's hierarchical structure.")]
        [Test]
        public async Task FindAliasOnNestedCategoryIsRestrictedToItsSubtreeAsync()
        {
            (NodeId tagVariables, NodeId parentMethod) = await FindCategoryAsync(
                Session, "TagVariables").ConfigureAwait(false);

            ReferenceDescription nested = await FindNestedCategoryAsync(
                Session, tagVariables).ConfigureAwait(false);
            Assert.That(nested, Is.Not.Null);

            var nestedId = ExpandedNodeId.ToNodeId(
                nested.NodeId, Session.NamespaceUris);
            NodeId nestedMethod = await FindMethodAsync(
                Session, nestedId, "FindAlias").ConfigureAwait(false);
            Assert.That(nestedMethod.IsNull, Is.False);

            CallMethodResult nestedResult = await CallFindAliasAsync(
                Session, nestedId, nestedMethod, "%", AliasForNodeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(nestedResult.StatusCode), Is.True,
                $"FindAlias on the nested category should succeed: {nestedResult.StatusCode}");
            string[] nestedNames =
                [.. DecodeAliasResults(Session, nestedResult).Select(r => r.AliasName.Name)];

            Assert.That(nestedNames, Is.EquivalentTo(s_stringValues1),
                "FindAlias on the nested category should return only its own aliases.");

            // Per Part 17 §6.3.2 the parent search covers the sub-tree too.
            CallMethodResult parentResult = await CallFindAliasAsync(
                Session, tagVariables, parentMethod, "%", AliasForNodeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(parentResult.StatusCode), Is.True);
            string[] parentNames =
                [.. DecodeAliasResults(Session, parentResult).Select(r => r.AliasName.Name)];

            Assert.That(parentNames, Is.SupersetOf(nestedNames),
                "FindAlias on the parent category should also return the nested aliases.");
            Assert.That(parentNames, Has.Some.EqualTo("TIC101_Setpoint"),
                "FindAlias on the parent should still return its own aliases.");
        }

        /// <summary>
        /// Returns the first AliasNameCategoryType instance organized under
        /// <paramref name="categoryId"/>, or null when there is none.
        /// </summary>
        private static async Task<ReferenceDescription> FindNestedCategoryAsync(
            Opc.Ua.Client.ISession session, NodeId categoryId)
        {
            IList<ReferenceDescription> children =
                await BrowseChildrenAsync(session, categoryId).ConfigureAwait(false);

            foreach (ReferenceDescription child in children)
            {
                if (ExpandedNodeId.ToNodeId(child.TypeDefinition, session.NamespaceUris) ==
                    AliasNameCategoryTypeNodeId)
                {
                    return child;
                }
            }
            return null;
        }

        [Description("Call the FindAlias method on an instance of AliasNameCategoryType (under Aliases), passing in a '%' for the filter. Pass in the AliasFor for the Reference type.")]
        [Test]
        public async Task FindAliasOnNestedAliasCategoryWithPercentFilterAsync()
        {
            // Pick the first AliasNameCategory under Aliases — the test
            // does not assume a specific category ordering.
            IList<ReferenceDescription> categories =
                await BrowseChildrenAsync(Session, AliasesNodeId).ConfigureAwait(false);

            // Prefer a category whose NodeId is NOT in namespace 0 (the
            // standard NodeSet exposes empty placeholder TagVariables /
            // Topics objects in namespace 0 that have no working FindAlias
            // implementation).
            ReferenceDescription target = null;
            ReferenceDescription fallback = null;
            foreach (ReferenceDescription c in categories)
            {
                if (ExpandedNodeId.ToNodeId(c.TypeDefinition, Session.NamespaceUris) !=
                    AliasNameCategoryTypeNodeId)
                {
                    continue;
                }
                var resolved = ExpandedNodeId.ToNodeId(
                    c.NodeId, Session.NamespaceUris);
                if (resolved.NamespaceIndex != 0)
                {
                    target = c;
                    break;
                }
                fallback ??= c;
            }
            target ??= fallback;
            if (target == null)
            {
                Assert.Ignore("No AliasNameCategory exposed under Aliases.");
            }

            var categoryId = ExpandedNodeId.ToNodeId(
                target.NodeId, Session.NamespaceUris);
            NodeId methodId = await FindMethodAsync(
                Session, categoryId, "FindAlias").ConfigureAwait(false);
            if (methodId.IsNull)
            {
                Assert.Ignore(
                    $"Category '{target.BrowseName.Name}' does not expose a FindAlias method.");
            }

            CallMethodResult result = await CallFindAliasAsync(
                Session, categoryId, methodId, "%", AliasForNodeId)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"FindAlias on '{target.BrowseName.Name}' should succeed.");
            IList<AliasRecord> records = DecodeAliasResults(Session, result);
            Assert.That(records, Is.Not.Empty,
                $"FindAlias('%') on '{target.BrowseName.Name}' should return at least one alias.");
            foreach (AliasRecord record in records)
            {
                Assert.That(record.AliasName, Is.Not.Null);
                Assert.That(record.ReferencedNodes, Is.Not.Empty);
            }
        }

        private static readonly string[] s_stringValues1 = ["Pump1_Status", "Heater_Power"];
    }
}
