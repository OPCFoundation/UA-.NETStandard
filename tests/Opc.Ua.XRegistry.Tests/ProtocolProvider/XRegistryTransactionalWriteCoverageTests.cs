/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryTransactionalWriteCoverageTests
    {
        [TestCase(3, 413)]
        [TestCase(4, 201)]
        public async Task EntityQuotaCountsImplicitGroupMetaAndVersionAtTheExactBoundaryAsync(
            int maximum, int expectedStatus)
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options() with { MaxEntities = maximum },
                new InMemoryXRegistryTransactionStore(), new XRegistryProviderCoverageTimeProvider());
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g/schemas/r", """{"versionid":"v1"}""")).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
            if (maximum == 3)
            {
                await XRegistryProviderCoverage.AssertPristineAsync(endpoint, response, "bad_request", 413)
                    .ConfigureAwait(false);
            }
            else
            {
                XRegistryResponse root = await endpoint.ExecuteAsync(
                    XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(response.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("v1"));
                    Assert.That(response.Metadata.GetProperty("versionscount").GetInt32(), Is.EqualTo(1));
                    Assert.That(root.Metadata.GetProperty("groupscount").GetInt32(), Is.EqualTo(1));
                    Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                });
            }
        }

        [TestCase("""{"a":{},"a":{"name":"duplicate"}}""", "bad_request", 400)]
        [TestCase("""{"a":{"name":"valid"},"b":{"groupid":"other"}}""", "mismatched_id", 400)]
        public async Task DuplicateOrMismatchedCollectionIdsCannotPublishAnySiblingAsync(
            string input, string code, int status)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/groups", input)).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, response, code, status)
                .ConfigureAwait(false);
        }

        [TestCase("/groups", null)]
        [TestCase("/groups", "[]")]
        [TestCase("/groups/g/schemas/r", """{"versions":[]}""")]
        [TestCase("/groups/g/schemas/r", """{"versions":null}""")]
        public async Task InvalidCollectionBodyShapesCannotCreateEntitiesAsync(string path, string? input)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Create, path, input)).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "bad_request")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CollectionDeleteSkipsMissingIdsButRollsBackMismatchedIdsAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Create, "/groups", """{"a":{"name":"first"},"b":{"name":"second"}}"""))
                .ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Delete, "/groups",
                """{"absent":{},"a":{"groupid":"other"},"b":{}}""")).ConfigureAwait(false);
            XRegistryResponse retained = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups")).ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Delete, "/groups", """{"absent":{},"a":{"groupid":"a","epoch":0}}"""))
                .ConfigureAwait(false);
            XRegistryResponse remaining = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups")).ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(200));
                Assert.That(rejected.Error?.Code, Is.EqualTo("mismatched_id"));
                Assert.That(retained.Metadata.EnumerateObject().Select(value => value.Name),
                    Is.EqualTo(s_createdGroups));
                Assert.That(retained.Metadata.GetProperty("a").GetProperty("name").GetString(), Is.EqualTo("first"));
                Assert.That(retained.Metadata.GetProperty("b").GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(deleted.StatusCode, Is.EqualTo(204));
                Assert.That(remaining.Metadata.EnumerateObject().Select(value => value.Name),
                    Is.EqualTo(s_remainingGroups));
                Assert.That(remaining.Metadata.GetProperty("b").GetProperty("name").GetString(), Is.EqualTo("second"));
                Assert.That(root.Metadata.GetProperty("groupscount").GetInt32(), Is.EqualTo(1));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task DeleteEpochFlagOverridesBodyAndMalformedFlagCannotMutateAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", """{"name":"retained"}""")).ConfigureAwait(false);
            XRegistryResponse invalid = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Delete, "/groups/g") with
                {
                    Parameters = [new XRegistryParameter("epoch", "invalid-json")]
                }).ConfigureAwait(false);
            XRegistryResponse retained = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Delete, "/groups/g", """{"epoch":999}""") with
                {
                    Parameters = [new XRegistryParameter("epoch", "0")]
                }).ConfigureAwait(false);
            XRegistryResponse missing = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(invalid.Error?.Code, Is.EqualTo("bad_request"));
                Assert.That(retained.Metadata.GetProperty("name").GetString(), Is.EqualTo("retained"));
                Assert.That(retained.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(deleted.StatusCode, Is.EqualTo(204));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
            });
        }

        [Test]
        public async Task DeletingLastVersionRemovesResourceMetaAndTouchesOnlyItsGroupAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Delete, "/groups/g/schemas/r/versions/v1")).ConfigureAwait(false);
            XRegistryResponse meta = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g/schemas/r/meta"))
                .ConfigureAwait(false);
            XRegistryResponse group = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(deleted.StatusCode, Is.EqualTo(204));
                Assert.That(meta.StatusCode, Is.EqualTo(404));
                Assert.That(group.Metadata.GetProperty("schemascount").GetInt32(), Is.Zero);
                Assert.That(group.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(root.Metadata.GetProperty("groupscount").GetInt32(), Is.EqualTo(1));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [TestCase("/")]
        [TestCase("/groups/g")]
        public async Task RawDocumentCannotMutateNonDocumentEntitiesAsync(string path)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, path, """{"name":"not-published"}""") with
                {
                    Document = ByteString.Empty
                }).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "bad_request")
                .ConfigureAwait(false);
        }

        [TestCase("/", XRegistryAction.Delete)]
        [TestCase("/groups/g/schemas/r/meta", XRegistryAction.Delete)]
        [TestCase("/model", XRegistryAction.Merge)]
        [TestCase("/modelsource", XRegistryAction.Delete)]
        [TestCase("/capabilities", XRegistryAction.Replace)]
        [TestCase("/export", XRegistryAction.Merge)]
        [TestCase("/groups", XRegistryAction.Replace)]
        [TestCase("/groups/g/schemas/r/versions", XRegistryAction.Replace)]
        public async Task ProtectedTargetsRejectUnsupportedActionsWithoutMutationAsync(
            string path, XRegistryAction action)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(action, path, "{}")).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "action_not_supported", 405)
                .ConfigureAwait(false);
            Assert.That(rejected.AllowedActions.ToArray(),
                Is.EqualTo(new[] { XRegistryAction.Read, XRegistryAction.Describe }));
        }

        [Test]
        public async Task RootModelSourceMergeRemovesUnusedDefinitionsAndAppliesNewRulesAtomicallyAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create("""
                {"groups":{"groups":{"singular":"group","resources":{},
                "attributes":{"obsolete":{"type":"string"}}}}}
                """);
            XRegistryResponse changed = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/",
                """
                {"name":"with-model","modelsource":{"groups":{"groups":{"attributes":{
                "obsolete":null,"added":{"type":"integer"}}}}}}
                """)).ConfigureAwait(false);
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", """{"added":7}""")).ConfigureAwait(false);
            XRegistryResponse source = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/modelsource")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/groups/g", """{"obsolete":"not-defined"}""")).ConfigureAwait(false);
            XRegistryResponse group = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed.StatusCode, Is.EqualTo(200));
                Assert.That(changed.Metadata.GetProperty("name").GetString(), Is.EqualTo("with-model"));
                Assert.That(changed.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(source.Metadata.GetProperty("groups").GetProperty("groups").GetProperty("attributes")
                    .GetRawText(), Is.EqualTo("""{"added":{"type":"integer"}}"""));
                Assert.That(rejected.Error?.Code, Is.EqualTo("invalid_attribute"));
                Assert.That(group.Metadata.GetProperty("added").GetInt32(), Is.EqualTo(7));
                Assert.That(group.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        private static readonly string[] s_createdGroups = ["a", "b"];
        private static readonly string[] s_remainingGroups = ["b"];
    }
}
