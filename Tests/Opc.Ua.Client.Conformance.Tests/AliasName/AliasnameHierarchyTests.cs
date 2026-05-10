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
using System.Threading.Tasks;
using NUnit.Framework;
using static Opc.Ua.Client.Conformance.Tests.AliasNameTestHelpers;

namespace Opc.Ua.Client.Conformance.Tests
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
        [Property("ConformanceUnit", "AliasName Hierarchy")]
        [Property("Tag", "001")]
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

        [Description("Call the FindAlias method on an instance of AliasNameCategoryType (under Aliases), passing in a '%' for the filter. Pass in the AliasFor for the Reference type.")]
        [Test]
        [Property("ConformanceUnit", "AliasName Hierarchy")]
        [Property("Tag", "002")]
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
    }
}
