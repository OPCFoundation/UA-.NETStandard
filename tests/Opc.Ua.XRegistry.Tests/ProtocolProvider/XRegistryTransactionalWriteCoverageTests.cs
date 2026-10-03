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
        [Test]
        public async Task ModifiedAtWritesDistinguishOmittedNullEqualAndDifferentValuesAsync(
            [Values("/", "/groups/g", "/groups/g/schemas/r/meta", "/groups/g/schemas/r/versions/v1")] string path,
            [Values(XRegistryAction.Replace, XRegistryAction.Merge)] XRegistryAction action,
            [Values("absent", "null", "same", "different")] string presence)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse seeded = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201));
            XRegistryResponse dated = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, path, /*lang=json,strict*/ """{"modifiedat":"2001-02-03T04:05:06Z"}"""))
                .ConfigureAwait(false);
            Assert.That(dated.StatusCode, Is.EqualTo(200), dated.Error?.Detail);
            string json = presence switch
            {
                "absent" => "{}",
                "null" => /*lang=json,strict*/ """{"modifiedat":null}""",
                "same" => /*lang=json,strict*/ """{"modifiedat":"2001-02-03T04:05:06+00:00"}""",
                _ => /*lang=json,strict*/ """{"modifiedat":"2040-05-06T07:08:09+01:00"}"""
            };
            string expected = presence == "different" ? "2040-05-06T06:08:09.0000000Z" : "2026-01-02T03:04:05.0000000Z";
            XRegistryResponse changed = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(action, path, json)).ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, path)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed.StatusCode, Is.EqualTo(200), changed.Error?.Detail);
                Assert.That(changed.Metadata.GetProperty("modifiedat").GetString(), Is.EqualTo(expected));
                Assert.That(read.Metadata.GetProperty("modifiedat").GetString(), Is.EqualTo(expected));
            });
        }

        [Test]
        public async Task ParentModifiedAtTracksMembershipButNotDescendantUpdatesAsync(
            [Values("registry", "group", "meta")] string level,
            [Values("update", "create", "delete")] string change)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            foreach (string version in new[] { "v1", "v2" })
            {
                XRegistryResponse seeded = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                    XRegistryAction.Replace, "/groups/g/schemas/r/versions/" + version, "{}")).ConfigureAwait(false);
                Assert.That(seeded.StatusCode, Is.EqualTo(201));
            }
            string parent = level switch
            {
                "registry" => "/",
                "group" => "/groups/g",
                _ => "/groups/g/schemas/r/meta"
            };
            XRegistryResponse dated = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, parent, /*lang=json,strict*/ """{"modifiedat":"2001-02-03T04:05:06Z"}"""))
                .ConfigureAwait(false);
            Assert.That(dated.StatusCode, Is.EqualTo(200), dated.Error?.Detail);
            string existing = level switch
            {
                "registry" => "/groups/g",
                "group" => "/groups/g/schemas/r",
                _ => "/groups/g/schemas/r/versions/v1"
            };
            string created = level switch
            {
                "registry" => "/groups/next",
                "group" => "/groups/g/schemas/next/versions/v1",
                _ => "/groups/g/schemas/r/versions/v3"
            };
            XRegistryAction action = change switch
            {
                "create" => XRegistryAction.Replace,
                "delete" => XRegistryAction.Delete,
                _ => XRegistryAction.Merge
            };
            XRegistryResponse changed = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                action, change == "create" ? created : existing,
                change == "update" ? /*lang=json,strict*/ """{"name":"child-edit"}""" : "{}"))
                .ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, parent)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed.StatusCode, Is.EqualTo(change switch
                {
                    "create" => 201,
                    "delete" => 204,
                    _ => 200
                }), changed.Error?.Detail);
                Assert.That(read.Metadata.GetProperty("modifiedat").GetString(),
                    Is.EqualTo(change == "update" ? "2001-02-03T04:05:06.0000000Z" : "2026-01-02T03:04:05.0000000Z"));
            });
        }

        [Test]
        public async Task CreatedAtPutDistinguishesOmittedNullAndExplicitValuesAsync(
            [Values("/", "/groups/g", "/groups/g/schemas/r/meta", "/groups/g/schemas/r/versions/v1")] string path,
            [Values("absent", "null", "explicit")] string presence)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse seeded = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201));
            XRegistryResponse dated = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, path, /*lang=json,strict*/ """{"createdat":"2001-02-03T04:05:06Z"}"""))
                .ConfigureAwait(false);
            Assert.That(dated.StatusCode, Is.EqualTo(200), dated.Error?.Detail);
            string json = presence switch
            {
                "absent" => "{}",
                "null" => /*lang=json,strict*/ """{"createdat":null}""",
                _ => /*lang=json,strict*/ """{"createdat":"2040-05-06T07:08:09Z"}"""
            };
            string expected = presence switch
            {
                "absent" => "2001-02-03T04:05:06.0000000Z",
                "null" => "2026-01-02T03:04:05.0000000Z",
                _ => "2040-05-06T07:08:09.0000000Z"
            };
            XRegistryResponse replaced = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, path, json)).ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, path)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replaced.StatusCode, Is.EqualTo(200), replaced.Error?.Detail);
                Assert.That(replaced.Metadata.GetProperty("createdat").GetString(), Is.EqualTo(expected));
                Assert.That(read.Metadata.GetProperty("createdat").GetString(), Is.EqualTo(expected));
                Assert.That(read.Metadata.GetProperty("modifiedat").GetString(),
                    Is.EqualTo("2026-01-02T03:04:05.0000000Z"));
            });
        }

        [TestCase(3, 413)]
        [TestCase(4, 201)]
        public async Task EntityQuotaCountsImplicitGroupMetaAndVersionAtTheExactBoundaryAsync(
            int maximum, int expectedStatus)
        {
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options() with { MaxEntities = maximum },
                new InMemoryXRegistryTransactionStore(), new XRegistryProviderCoverageTimeProvider());
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g/schemas/r", /*lang=json,strict*/ """{"versionid":"v1"}"""))
                    .ConfigureAwait(false);
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

        [TestCase(/*lang=json,strict*/ """{"a":{},"a":{"name":"duplicate"}}""", "bad_request", 400)]
        [TestCase(/*lang=json,strict*/ """{"a":{"name":"valid"},"b":{"groupid":"other"}}""", "mismatched_id", 400)]
        public async Task DuplicateOrMismatchedCollectionIdsCannotPublishAnySiblingAsync(
            string input, string code, int status)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/groups", input)).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, response, code, status)
                .ConfigureAwait(false);
        }

        [TestCase("/groups", null)]
        [TestCase("/groups", "[]")]
        [TestCase("/groups/g/schemas/r", /*lang=json,strict*/ """{"versions":[]}""")]
        [TestCase("/groups/g/schemas/r", /*lang=json,strict*/ """{"versions":null}""")]
        public async Task InvalidCollectionBodyShapesCannotCreateEntitiesAsync(string path, string? input)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Create, path, input)).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "bad_request")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CollectionDeleteSkipsMissingIdsButRollsBackMismatchedIdsAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Create,
                    "/groups", /*lang=json,strict*/ """{"a":{"name":"first"},"b":{"name":"second"}}"""))
                .ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Delete, "/groups",
                                     /*lang=json,strict*/
                                     """{"absent":{},"a":{"groupid":"other"},"b":{}}""")).ConfigureAwait(false);
            XRegistryResponse retained = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups")).ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Delete,
                    "/groups", /*lang=json,strict*/ """{"absent":{},"a":{"groupid":"a","epoch":0}}"""))
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
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", /*lang=json,strict*/ """{"name":"retained"}""")).ConfigureAwait(
                    false);
            XRegistryResponse invalid = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Delete, "/groups/g") with
                {
                    Parameters = [new XRegistryParameter("epoch", "invalid-json")]
                }).ConfigureAwait(false);
            XRegistryResponse retained = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Delete, "/groups/g", /*lang=json,strict*/ """{"epoch":999}""") with
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
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
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
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(
                    XRegistryAction.Merge, path, /*lang=json,strict*/ """{"name":"not-published"}""") with
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
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(action, path, "{}")).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "action_not_supported", 405)
                .ConfigureAwait(false);
            Assert.That(rejected.AllowedActions.ToArray(),
                Is.EqualTo([XRegistryAction.Read, XRegistryAction.Describe]));
        }

        [Test]
        public async Task RootModelSourceMergeRemovesUnusedDefinitionsAndAppliesNewRulesAtomicallyAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","resources":{},
                "attributes":{"obsolete":{"type":"string"}}}}}
                """);
            XRegistryResponse changed = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/",
                                     /*lang=json,strict*/
                                     """
                {"name":"with-model","modelsource":{"groups":{"groups":{"attributes":{
                "obsolete":null,"added":{"type":"integer"}}}}}}
                """)).ConfigureAwait(false);
            XRegistryResponse created = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g", /*lang=json,strict*/ """{"added":7}""")).ConfigureAwait(false);
            XRegistryResponse source = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/modelsource")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/groups/g", /*lang=json,strict*/ """{"obsolete":"not-defined"}"""))
                    .ConfigureAwait(false);
            XRegistryResponse group = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(changed.StatusCode, Is.EqualTo(200));
                Assert.That(changed.Metadata.GetProperty("name").GetString(), Is.EqualTo("with-model"));
                Assert.That(changed.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(source.Metadata.GetProperty("groups").GetProperty("groups").GetProperty("attributes")
                    .GetRawText(), Is.EqualTo(/*lang=json,strict*/ """{"added":{"type":"integer"}}"""));
                Assert.That(rejected.Error?.Code, Is.EqualTo("invalid_attribute"));
                Assert.That(group.Metadata.GetProperty("added").GetInt32(), Is.EqualTo(7));
                Assert.That(group.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        private static readonly string[] s_createdGroups = ["a", "b"];
        private static readonly string[] s_remainingGroups = ["b"];
    }
}
