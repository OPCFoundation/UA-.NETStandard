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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using static Opc.Ua.InformationModel.Tests.AliasNameTestHelpers;

// Conformance tests use inline literal arrays as expected-value
// assertions; the per-call allocation cost is irrelevant for tests
// and keeping the literal adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// Compliance tests for the optional Part 17 methods on the well-known
    /// alias categories: <c>FindAliasVerbose</c> (§6.3.3),
    /// <c>AddAliasesToCategory</c> (§6.3.4) and
    /// <c>DeleteAliasesFromCategory</c> (§6.3.5). The standard NodeSet does
    /// not instantiate these, so the server adds them through the generated
    /// optional-child helpers, which place them at the NodeIds Part 17 §9
    /// reserves.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AliasName")]
    public class AliasnameOptionalMethodsTests : TestFixture
    {
        [Description("Verify the optional Part 17 methods are exposed on the well-known categories at their reserved NodeIds.")]
        [Test]
        [TestCase("Aliases", 24054u, 24057u, 24060u)]
        [TestCase("TagVariables", 24063u, 24066u, 24069u)]
        [TestCase("Topics", 24072u, 24075u, 24078u)]
        public async Task StandardCategoryExposesOptionalMethodsAsync(
            string categoryName, uint verboseId, uint addId, uint deleteId)
        {
            NodeId category = await ResolveCategoryAsync(Session, categoryName)
                .ConfigureAwait(false);

            // Part 17 §9 allocates a NodeId for each optional child of the
            // three well-known categories, and the source generator assigns
            // them when the child is added to one of those parents.
            (string Name, uint Id)[] expected =
            [
                ("FindAliasVerbose", verboseId),
                ("AddAliasesToCategory", addId),
                ("DeleteAliasesFromCategory", deleteId)
            ];

            foreach ((string name, uint id) in expected)
            {
                NodeId methodId = await FindMethodAsync(Session, category, name)
                    .ConfigureAwait(false);
                Assert.That(methodId.IsNull, Is.False,
                    $"{categoryName} should expose the optional {name} method.");
                Assert.That(methodId, Is.EqualTo(new NodeId(id)),
                    $"{categoryName}.{name} should use the NodeId Part 17 reserves for it.");

                DataValue browseName = await ReadAttributeAsync(
                    Session, methodId, Attributes.BrowseName).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(browseName.StatusCode), Is.True,
                    $"{categoryName}.{name} should be readable at {methodId}.");
                Assert.That(browseName.GetValue<QualifiedName>(default).Name,
                    Is.EqualTo(name));
            }
        }

        [Description("Verify the optional LastChange property is exposed on the well-known categories at its reserved NodeId.")]
        [Test]
        [TestCase("Aliases", 32852u)]
        [TestCase("TagVariables", 32854u)]
        [TestCase("Topics", 32856u)]
        public async Task StandardCategoryExposesLastChangeAsync(
            string categoryName, uint lastChangeId)
        {
            NodeId category = await ResolveCategoryAsync(Session, categoryName)
                .ConfigureAwait(false);

            IList<ReferenceDescription> children = await BrowseChildrenAsync(
                Session, category, ReferenceTypeIds.HasProperty).ConfigureAwait(false);

            ReferenceDescription lastChange = children.FirstOrDefault(
                c => c.BrowseName.Name == BrowseNames.LastChange);
            Assert.That(lastChange, Is.Not.Null,
                $"{categoryName} should expose the LastChange property.");
            Assert.That(
                ExpandedNodeId.ToNodeId(lastChange.NodeId, Session.NamespaceUris),
                Is.EqualTo(new NodeId(lastChangeId)),
                $"{categoryName}.LastChange should use the NodeId Part 17 reserves for it.");

            // A VersionTime the client can actually read.
            DataValue value = await ReadAttributeAsync(
                Session, new NodeId(lastChangeId), Attributes.Value).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True,
                $"{categoryName}.LastChange should be readable: {value.StatusCode}");
        }

        /// <summary>
        /// Resolves a well-known category by name. Aliases is the root of
        /// the hierarchy, so it is not a child of itself.
        /// </summary>
        private static async Task<NodeId> ResolveCategoryAsync(
            ISession session, string categoryName)
        {
            if (categoryName == "Aliases")
            {
                return AliasesNodeId;
            }
            (NodeId category, _) = await FindCategoryAsync(session, categoryName)
                .ConfigureAwait(false);
            return category;
        }

        [Description("Call FindAliasVerbose on TagVariables and verify the verbose fields are populated.")]
        [Test]
        public async Task FindAliasVerboseReturnsCategoryAndServerUrisAsync()
        {
            (NodeId category, _) = await FindCategoryAsync(Session, "TagVariables")
                .ConfigureAwait(false);

            NodeId findAliasVerbose = await RequireMethodAsync(
                Session, category, "FindAliasVerbose").ConfigureAwait(false);

            CallMethodResult result = await CallMethodAsync(
                Session, category, findAliasVerbose,
                new Variant("%"), new Variant(AliasForNodeId)).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"FindAliasVerbose should succeed: {result.StatusCode}");

            IList<AliasNameVerboseDataType> records =
                DecodeVerboseAliasResults(Session, result);
            Assert.That(records, Is.Not.Empty,
                "FindAliasVerbose('%') should return the TagVariables aliases.");

            foreach (AliasNameVerboseDataType record in records)
            {
                Assert.That(record.ReferencedNodes, Is.Not.Empty);
                Assert.That(record.ServerUris.Count, Is.EqualTo(record.ReferencedNodes.Count),
                    "ServerUris is parallel to ReferencedNodes (Part 17 §7.3).");
                Assert.That(record.AliasNameCategoryId.IsNull, Is.False,
                    "Each record names the category the alias lives in.");
            }

            // The sub-tree search reports the nested category as the home of
            // the aliases that live there.
            var categoryIds = records
                .Select(r => r.AliasNameCategoryId)
                .Distinct()
                .ToList();
            Assert.That(categoryIds, Has.Count.GreaterThan(1),
                "TagVariables aggregates aliases from itself and its nested category.");
        }

        [Description("Verify an anonymous session cannot mutate a category.")]
        [Test]
        public async Task AddAliasesFromAnonymousSessionIsDeniedAsync()
        {
            (NodeId category, _) = await FindCategoryAsync(Session, "TagVariables")
                .ConfigureAwait(false);

            NodeId addAliases = await RequireMethodAsync(
                Session, category, "AddAliasesToCategory").ConfigureAwait(false);

            CallMethodResult result = await CallMethodAsync(
                Session, category, addAliases,
                new Variant(new string[] { "AnonymousShouldNotAdd" }.ToArrayOf()),
                new Variant(new ExpandedNodeId[] { new(ObjectIds.Server) }.ToArrayOf()),
                new Variant(new string[] { string.Empty }.ToArrayOf()),
                new Variant(AliasForNodeId)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "AddAliasesToCategory requires SecurityAdmin on a SignAndEncrypt channel.");
        }

        [Description("Add an alias as SecurityAdmin, find it, then delete it again.")]
        [Test]
        public async Task AddAndDeleteAliasRoundTripAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Ignore("Server exposes no username endpoint for the SecurityAdmin user.");
            }

            const string aliasName = "RoundTripAlias";
            var target = new ExpandedNodeId(ObjectIds.Server);
            bool deleteRequired = true;
            NodeId deleteAliases = NodeId.Null;
            NodeId categoryForCleanup = NodeId.Null;

            try
            {
                (NodeId category, NodeId findAlias) = await FindCategoryAsync(
                    admin, "TagVariables").ConfigureAwait(false);
                categoryForCleanup = category;
                NodeId addAliases = await RequireMethodAsync(
                    admin, category, "AddAliasesToCategory").ConfigureAwait(false);
                deleteAliases = await RequireMethodAsync(
                    admin, category, "DeleteAliasesFromCategory").ConfigureAwait(false);

                CallMethodResult added = await CallMethodAsync(
                    admin, category, addAliases,
                    new Variant(new string[] { aliasName }.ToArrayOf()),
                    new Variant(new ExpandedNodeId[] { target }.ToArrayOf()),
                    new Variant(new string[] { string.Empty }.ToArrayOf()),
                    new Variant(AliasForNodeId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(added.StatusCode), Is.True,
                    $"AddAliasesToCategory should succeed for SecurityAdmin: {added.StatusCode}");

                CallMethodResult found = await CallFindAliasAsync(
                    admin, category, findAlias, aliasName, AliasForNodeId)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(found.StatusCode), Is.True);
                Assert.That(
                    DecodeAliasResults(admin, found).Select(r => r.AliasName.Name),
                    Has.Some.EqualTo(aliasName),
                    "FindAlias should return the alias that was just added.");

                CallMethodResult deleted = await CallMethodAsync(
                    admin, category, deleteAliases,
                    new Variant(new string[] { aliasName }.ToArrayOf()),
                    new Variant(new ExpandedNodeId[] { target }.ToArrayOf()))
                    .ConfigureAwait(false);

                // Only once the delete actually succeeded is the alias gone
                // and the finally-block cleanup unnecessary.
                deleteRequired = !StatusCode.IsGood(deleted.StatusCode);

                Assert.That(StatusCode.IsGood(deleted.StatusCode), Is.True,
                    $"DeleteAliasesFromCategory should succeed: {deleted.StatusCode}");

                CallMethodResult gone = await CallFindAliasAsync(
                    admin, category, findAlias, aliasName, AliasForNodeId)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(gone.StatusCode), Is.True);
                Assert.That(DecodeAliasResults(admin, gone), Is.Empty,
                    "FindAlias should no longer return the deleted alias.");
            }
            finally
            {
                // A failed assertion must not leave the probe alias behind:
                // the fixture's server is shared with the other alias tests,
                // several of which assert on exact alias sets.
                if (deleteRequired && !deleteAliases.IsNull)
                {
                    await RemoveAliasAsync(
                        admin, categoryForCleanup, deleteAliases, aliasName, target)
                        .ConfigureAwait(false);
                }
                await admin.CloseAsync(default).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        /// <summary>
        /// Best-effort removal of a probe alias. This runs while another
        /// failure may already be unwinding, so it calls the session
        /// directly rather than through <c>CallMethodAsync</c> — that helper
        /// asserts on the response shape, and an assertion raised here would
        /// replace the original failure with a cleanup error.
        /// </summary>
        private static async Task RemoveAliasAsync(
            ISession session,
            NodeId category,
            NodeId deleteMethod,
            string aliasName,
            ExpandedNodeId target)
        {
            try
            {
                await session.CallAsync(
                    null,
                    new CallMethodRequest[]
                    {
                        new() {
                            ObjectId = category,
                            MethodId = deleteMethod,
                            InputArguments = new Variant[]
                            {
                                new(new string[] { aliasName }.ToArrayOf()),
                                new(new ExpandedNodeId[] { target }.ToArrayOf())
                            }.ToArrayOf()
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // The alias was never added, or the session is already gone.
            }
            catch (ObjectDisposedException)
            {
                // The session was torn down before cleanup could run.
            }
        }

        [Description("Verify a mutation advances the LastChange property of the Aliases object.")]
        [Test]
        public async Task MutationAdvancesLastChangeAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Ignore("Server exposes no username endpoint for the SecurityAdmin user.");
            }

            const string aliasName = "LastChangeProbeAlias";
            var target = new ExpandedNodeId(ObjectIds.Server);

            // Aliases.LastChange (i=32852) is the one optional child the
            // standard NodeSet does ship, so it has a generated constant.
            NodeId lastChangeNodeId = VariableIds.Aliases_LastChange;
            NodeId deleteAliases = NodeId.Null;

            try
            {
                NodeId addAliases = await RequireMethodAsync(
                    admin, AliasesNodeId, "AddAliasesToCategory").ConfigureAwait(false);
                deleteAliases = await RequireMethodAsync(
                    admin, AliasesNodeId, "DeleteAliasesFromCategory").ConfigureAwait(false);

                DataValue before = await ReadAttributeAsync(
                    admin, lastChangeNodeId, Attributes.Value).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(before.StatusCode), Is.True,
                    "Aliases.LastChange should be readable.");

                CallMethodResult added = await CallMethodAsync(
                    admin, AliasesNodeId, addAliases,
                    new Variant(new string[] { aliasName }.ToArrayOf()),
                    new Variant(new ExpandedNodeId[] { target }.ToArrayOf()),
                    new Variant(new string[] { string.Empty }.ToArrayOf()),
                    new Variant(AliasForNodeId)).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(added.StatusCode), Is.True,
                    $"AddAliasesToCategory on Aliases should succeed: {added.StatusCode}");

                DataValue after = await ReadAttributeAsync(
                    admin, lastChangeNodeId, Attributes.Value).ConfigureAwait(false);
                Assert.That(after.GetValue<uint>(0), Is.Not.EqualTo(before.GetValue<uint>(0)),
                    "LastChange must advance after a successful mutation (Part 17 §6.3.1).");
            }
            finally
            {
                // Leave the address space as it was found even when an
                // assertion above failed — the server is shared with the
                // other alias tests.
                if (!deleteAliases.IsNull)
                {
                    await RemoveAliasAsync(
                        admin, AliasesNodeId, deleteAliases, aliasName, target)
                        .ConfigureAwait(false);
                }
                await admin.CloseAsync(default).ConfigureAwait(false);
                admin.Dispose();
            }
        }
    }
}
