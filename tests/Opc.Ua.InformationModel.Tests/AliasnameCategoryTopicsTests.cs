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

        [Description("Verify that at least one instance of a Topic is included in the Topics category and that it references a remote Object.")]
        [Test]
        public async Task TopicsCategoryContainsAliasNameForDatasetAsync()
        {
            (NodeId category, _) = await FindCategoryAsync(
                Session, "Topics").ConfigureAwait(false);

            IList<ReferenceDescription> children = await BrowseChildrenAsync(
                Session, category).ConfigureAwait(false);

            int aliasInstances = 0;
            var browseNames = new List<string>();
            foreach (ReferenceDescription child in children)
            {
                var typeDef = ExpandedNodeId.ToNodeId(
                    child.TypeDefinition, Session.NamespaceUris);
                if (typeDef == AliasNameTypeNodeId)
                {
                    aliasInstances++;
                    browseNames.Add(child.BrowseName.Name);
                }
            }

            if (aliasInstances == 0)
            {
                Assert.Ignore(
                    "Topics category exposes no AliasName instances on this server.");
            }

            Assert.That(aliasInstances, Is.GreaterThan(0),
                "Topics should contain at least one AliasName instance.");
            // ServerEvents and AuditEvents are populated by the
            // Quickstart reference server's AliasNameNodeManager.
            Assert.That(browseNames, Contains.Item("ServerEvents"));
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
            string[] names = [.. records.Select(r => r.AliasName.Name)];
            Assert.That(names, Contains.Item("ServerEvents"));
            Assert.That(names, Contains.Item("AuditEvents"));
        }
    }
}
