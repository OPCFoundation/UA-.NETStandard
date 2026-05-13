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
using static Opc.Ua.Conformance.Tests.AliasName.AliasNameTestHelpers;

namespace Opc.Ua.Conformance.Tests.AliasName
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
        [Property("ConformanceUnit", "AliasName Category Tags")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "AliasName Category Tags")]
        [Property("Tag", "002")]
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
        [Property("ConformanceUnit", "AliasName Category Tags")]
        [Property("Tag", "003")]
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
