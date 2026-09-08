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
        [TestCase(23470u, 24054u, 24057u, 24060u)]       // Aliases
        [TestCase(23479u, 24063u, 24066u, 24069u)]       // TagVariables
        [TestCase(23488u, 24072u, 24075u, 24078u)]       // Topics
        public async Task StandardCategoryExposesOptionalMethodsAsync(
            uint categoryId, uint verboseId, uint addId, uint deleteId)
        {
            var category = new NodeId(categoryId);

            // One browse serves all three assertions — the returned
            // ReferenceDescriptions already carry NodeId and BrowseName.
            IList<ReferenceDescription> children = await BrowseChildrenAsync(
                Session, category, ReferenceTypeIds.HasComponent)
                .ConfigureAwait(false);

            // Part 17 §9 allocates a NodeId for each optional child of the
            // three well-known categories. The ModelDesign does not declare
            // these children, so the source generator cannot assign the ids
            // — DiagnosticsNodeManager.ReservedChildIds supplies them
            // explicitly when the children are created.
            (string Name, uint Id)[] expected =
            [
                ("FindAliasVerbose", verboseId),
                ("AddAliasesToCategory", addId),
                ("DeleteAliasesFromCategory", deleteId)
            ];

            foreach ((string name, uint id) in expected)
            {
                ReferenceDescription method = children.FirstOrDefault(
                    c => c.BrowseName.Name == name);
                Assert.That(method, Is.Not.Null,
                    $"{category} should expose the optional {name} method.");
                Assert.That(
                    ExpandedNodeId.ToNodeId(method.NodeId, Session.NamespaceUris),
                    Is.EqualTo(new NodeId(id)),
                    $"{category}.{name} should use the NodeId Part 17 reserves for it.");
            }
        }

        [Description("Verify the optional LastChange property is exposed on the well-known categories at its reserved NodeId.")]
        [Test]
        [TestCase(23470u, 32852u)]       // Aliases
        [TestCase(23479u, 32854u)]       // TagVariables
        [TestCase(23488u, 32856u)]       // Topics
        public async Task StandardCategoryExposesLastChangeAsync(
            uint categoryId, uint lastChangeId)
        {
            var category = new NodeId(categoryId);

            IList<ReferenceDescription> children = await BrowseChildrenAsync(
                Session, category, ReferenceTypeIds.HasProperty).ConfigureAwait(false);

            ReferenceDescription lastChange = children.FirstOrDefault(
                c => c.BrowseName.Name == BrowseNames.LastChange);
            Assert.That(lastChange, Is.Not.Null,
                $"{category} should expose the LastChange property.");
            Assert.That(
                ExpandedNodeId.ToNodeId(lastChange.NodeId, Session.NamespaceUris),
                Is.EqualTo(new NodeId(lastChangeId)),
                $"{category}.LastChange should use the NodeId Part 17 reserves for it.");

            // A VersionTime the client can actually read.
            DataValue value = await ReadAttributeAsync(
                Session, new NodeId(lastChangeId), Attributes.Value).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True,
                $"{category}.LastChange should be readable: {value.StatusCode}");
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
                new Variant(s_stringValues1.ToArrayOf()),
                new Variant(new ExpandedNodeId[] { new(ObjectIds.Server) }.ToArrayOf()),
                new Variant(new string[] { string.Empty }.ToArrayOf()),
                new Variant(AliasForNodeId)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "AddAliasesToCategory requires SecurityAdmin on a SignAndEncrypt channel.");
        }

        [Description("Verify a secure channel alone is not enough — the SecurityAdmin role is also required.")]
        [Test]
        public async Task AddAliasesWithoutAdminRoleOnSecureChannelIsDeniedAsync()
        {
            // The fixture Session runs over SecurityPolicies.None, so the
            // anonymous test above only exercises the channel half of the
            // gate. This one connects anonymously over SignAndEncrypt so
            // the denial can only come from the missing role.
            ISession secure;
            try
            {
                secure = await OpenAuxSessionAsync(SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                Assert.Ignore("Server exposes no Basic256Sha256 endpoint.");
                return;
            }

            try
            {
                if (secure.ConfiguredEndpoint.Description.SecurityMode !=
                    MessageSecurityMode.SignAndEncrypt)
                {
                    Assert.Ignore("Server exposes no SignAndEncrypt endpoint.");
                }

                (NodeId category, _) = await FindCategoryAsync(secure, "TagVariables")
                    .ConfigureAwait(false);
                NodeId addAliases = await RequireMethodAsync(
                    secure, category, "AddAliasesToCategory").ConfigureAwait(false);

                CallMethodResult result = await CallMethodAsync(
                    secure, category, addAliases,
                    new Variant(s_stringValues2.ToArrayOf()),
                    new Variant(new ExpandedNodeId[] { new(ObjectIds.Server) }.ToArrayOf()),
                    new Variant(new string[] { string.Empty }.ToArrayOf()),
                    new Variant(AliasForNodeId)).ConfigureAwait(false);

                Assert.That(
                    result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "A caller without the SecurityAdmin role must be denied even on a secure channel.");
            }
            finally
            {
                await secure.CloseAsync(default).ConfigureAwait(false);
                secure.Dispose();
            }
        }

        /// <summary>
        /// Connects as the seeded SecurityAdmin user and skips the test when
        /// the server cannot satisfy the mutation gate's preconditions —
        /// no username endpoint at all, or none with SignAndEncrypt (the
        /// fixture helper falls back to Sign and None, which the gate
        /// rejects, so the test would fail instead of skip).
        /// </summary>
        private async Task<ISession> ConnectSecureAdminOrIgnoreAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Ignore("Server exposes no username endpoint for the SecurityAdmin user.");
            }

            if (admin.ConfiguredEndpoint.Description.SecurityMode !=
                MessageSecurityMode.SignAndEncrypt)
            {
                await admin.CloseAsync(default).ConfigureAwait(false);
                admin.Dispose();
                Assert.Ignore(
                    "The SecurityAdmin mutation gate requires a SignAndEncrypt endpoint, " +
                    "which this server does not expose.");
            }

            return admin;
        }

        [Description("Add an alias as SecurityAdmin, find it, then delete it again.")]
        [Test]
        public async Task AddAndDeleteAliasRoundTripAsync()
        {
            ISession admin = await ConnectSecureAdminOrIgnoreAsync().ConfigureAwait(false);

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
                // A Good method-level status does not mean the entry was
                // added — per-entry failures travel in the ErrorCodes
                // output (e.g. a duplicate add), and a false pass here
                // would let the test delete an alias it did not create.
                AssertSingleErrorCodeGood(added, "AddAliasesToCategory");

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

                // Only once the delete actually succeeded — method-level
                // status AND the per-entry ErrorCodes value — is the alias
                // gone and the finally-block cleanup unnecessary.
                deleteRequired = !IsSingleErrorCodeGood(deleted);

                Assert.That(StatusCode.IsGood(deleted.StatusCode), Is.True,
                    $"DeleteAliasesFromCategory should succeed: {deleted.StatusCode}");
                AssertSingleErrorCodeGood(deleted, "DeleteAliasesFromCategory");

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
        /// Decodes the per-entry <c>ErrorCodes</c> output argument of an
        /// <c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>
        /// call (Part 17 §6.3.4/§6.3.5).
        /// </summary>
        private static ArrayOf<StatusCode> DecodeErrorCodes(CallMethodResult result)
        {
            if (result.OutputArguments.Count == 0 ||
                !result.OutputArguments[0].TryGetValue(out ArrayOf<StatusCode> codes))
            {
                return [];
            }
            return codes;
        }

        /// <summary>
        /// True when the method-level status and the single per-entry
        /// <c>ErrorCodes</c> value both report success.
        /// </summary>
        private static bool IsSingleErrorCodeGood(CallMethodResult result)
        {
            ArrayOf<StatusCode> codes = DecodeErrorCodes(result);
            return StatusCode.IsGood(result.StatusCode) &&
                codes.Count == 1 &&
                StatusCode.IsGood(codes[0]);
        }

        /// <summary>
        /// Asserts the mutation's per-entry <c>ErrorCodes</c> output holds
        /// exactly one Good entry — a Good method-level status alone does
        /// not mean the entry was applied.
        /// </summary>
        private static void AssertSingleErrorCodeGood(CallMethodResult result, string method)
        {
            ArrayOf<StatusCode> codes = DecodeErrorCodes(result);
            Assert.That(codes.Count, Is.EqualTo(1),
                $"{method} should return one ErrorCodes entry per input entry.");
            Assert.That(StatusCode.IsGood(codes[0]), Is.True,
                $"{method} per-entry status should be Good: {codes[0]}");
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
            ISession admin = await ConnectSecureAdminOrIgnoreAsync().ConfigureAwait(false);

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
                AssertSingleErrorCodeGood(added, "AddAliasesToCategory");

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

        private static readonly string[] s_stringValues1 = ["AnonymousShouldNotAdd"];
        private static readonly string[] s_stringValues2 = ["NonAdminShouldNotAdd"];
    }
}
