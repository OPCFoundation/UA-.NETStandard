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

using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryModelBoundaryTests
    {
        [TestCase("""{"absent.color":{"enum":["blue"]}}""")]
        [TestCase("""{"schemas.missing":{"enum":["blue"]}}""")]
        [TestCase("""{"schemas.color":{"enum":["blue"]}}""")]
        [TestCase("""{"schemas.color":{"equals":"absent"}}""")]
        [TestCase("""{"schemas.color":{"equals":"number"}}""")]
        [TestCase("""{"schemas.color":{"unknown":true}}""")]
        [TestCase("\"invalid\"")]
        public void InvalidConstraintsAreRejectedEvenWhenNoInstancesExist(string constraints)
        {
            string model = """
                {"groups":{"groups":{"singular":"group","attributes":{"number":{"type":"integer"}},
                  "constraints":CONSTRAINTS,
                  "resources":{"schemas":{"singular":"schema","attributes":{
                    "color":{"type":"string","enum":["red","blue"],"default":"red","required":true}
                  }}}
                }}}
                """.Replace("CONSTRAINTS", constraints, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() =>
            {
                using XRegistryTransactionalEndpoint endpoint = Create(model);
            });
        }

        [Test]
        public async Task InstanceConstraintsCannotWidenModelLimitsBeforeAnyVersionsExistAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group",
                  "constraints":{"schemas.color":{"enum":["blue"]}},
                  "resources":{"schemas":{"singular":"schema","attributes":{
                    "color":{"type":"string","enum":["red","blue"]}
                  }}}
                }}}
                """);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """{"constraints":{"schemas.color":{"enum":["red","blue"]}}}""")).ConfigureAwait(
                    false);
            Assert.That(response.Error?.Code, Is.EqualTo("constraint_failure"));
            Assert.That((await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups")).ConfigureAwait(false))
                .Metadata.GetRawText(), Is.EqualTo("{}"));
        }

        [Test]
        public async Task ConstraintsCanDefaultImplicitStandardAttributesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group",
                  "constraints":{"schemas.format":{"default":"JSON/1.0"}},
                  "resources":{"schemas":{"singular":"schema"}}
                }}}
                """);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace,
                "/groups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(201), response.Error?.Detail);
            Assert.That(response.Metadata.GetProperty("format").GetString(), Is.EqualTo("JSON/1.0"));
        }

        [Test]
        public async Task MatchingEpochConstraintsAreCheckedAfterFinalCounterUpdatesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{"singular":"schema",
                  "attributes":{"epoch":{"type":"uinteger","readonly":true,"required":true,"matchversions":true}}
                }}}}}
                """);
            XRegistryResponse created =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/r",
                /*lang=json,strict*/ """{"versions":{"v1":{},"v2":{}}}""")).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge,
                "/groups/g/schemas/r/versions/v1", /*lang=json,strict*/ """{"name":"blocked"}""")).ConfigureAwait(
                    false);
            Assert.That(rejected.Error?.Code, Is.EqualTo("mismatched_version_attribute"), rejected.Error?.Detail);
            Assert.That((await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1"))
                .ConfigureAwait(false)).Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
        }

        [Test]
        public async Task VersionCollectionCreationUsesCaseInsensitiveIdentityOrderForImplicitAncestryAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/seed", "{}"))
                .ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Create,
                "/groups/g/schemas/r/versions", /*lang=json,strict*/ """{"b":{},"a":{}}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200), response.Error?.Detail);
                Assert.That(
                    response.Metadata.GetProperty("a").GetProperty("ancestorid").GetString(), Is.EqualTo("seed"));
                Assert.That(response.Metadata.GetProperty("b").GetProperty("ancestorid").GetString(), Is.EqualTo("a"));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AModelAndItsDependentEntitiesAreValidatedAsOneAtomicRootUpdateAsync(bool invalid)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g", "{}")).ConfigureAwait(false);
            string body = """
                {"modelsource":{"groups":{
                  "groups":{"attributes":{"site":{"type":"string","required":true}}},
                  "newgroups":{"singular":"newgroup"}
                }},"groups":{"g":{"site":SITE}},"newgroups":{"one":{}}}
                """.Replace("SITE", invalid ? "false" : "\"west\"", StringComparison.Ordinal);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/", body) with
            { Parameters = [new("inline", "groups,newgroups,model")] }).ConfigureAwait(false);
            XRegistryResponse group = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g"))
                .ConfigureAwait(false);
            XRegistryResponse model = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(invalid ? 400 : 200), response.Error?.Detail);
                Assert.That(group.Metadata.TryGetProperty("site", out _), Is.EqualTo(!invalid));
                Assert.That(
                    model.Metadata.GetProperty("groups").TryGetProperty("newgroups", out _), Is.EqualTo(!invalid));
                Assert.That(group.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(invalid ? 0 : 1));
                if (!invalid)
                {
                    Assert.That(response.Metadata.GetProperty("newgroups").GetProperty("one").GetProperty("newgroupid")
                        .GetString(),
                        Is.EqualTo("one"));
                }
            });
        }

        [Test]
        public async Task PostReturnsTheCreatedVersionWithoutChangingAnExistingStickyDefaultAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"old","meta":{"defaultversionid":"old"}}""")).ConfigureAwait(
                    false);
            XRegistryResponse created =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Create, "/groups/g/schemas/r",
                /*lang=json,strict*/ """{"versionid":"new","name":"created"}""")).ConfigureAwait(false);
            XRegistryResponse meta =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas/r/meta"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(200), created.Error?.Detail);
                Assert.That(created.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("new"));
                Assert.That(created.Metadata.GetProperty("self").GetString(),
                    Is.EqualTo("https://registry.example/registry/groups/g/schemas/r/versions/new"));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("old"));
            });
        }

        [TestCase("/")]
        [TestCase("/groups/g")]
        public async Task OwnerPostsPreserveAttributesAndReturnOnlyProcessedCollectionsAsync(string path)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"registry","groups":{"g":{"name":"group"},"untouched":{}}}"""))
                .ConfigureAwait(false);
            string payload = path == "/"
                ? /*lang=json,strict*/ """{"groups":{"g":{"name":"updated"}}}"""
                : /*lang=json,strict*/ """{"schemas":{"one":{},"two":{}}}""";
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Create, path, payload))
                .ConfigureAwait(false);
            XRegistryResponse owner =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, path)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200), response.Error?.Detail);
                Assert.That(response.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(response.Metadata.TryGetProperty("epoch", out _), Is.False);
                Assert.That(
                    owner.Metadata.GetProperty("name").GetString(), Is.EqualTo(path == "/" ? "registry" : "group"));
                if (path == "/")
                {
                    Assert.That(response.Metadata.GetProperty("groups").TryGetProperty("untouched", out _), Is.False);
                }
                else
                {
                    Assert.That(response.Metadata.GetProperty("schemas").TryGetProperty("one", out _), Is.True);
                    Assert.That(response.Metadata.GetProperty("schemas").TryGetProperty("two", out _), Is.True);
                }
            });
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Create, path,
                /*lang=json,strict*/ """{"name":"cannot-replace-owner"}""")).ConfigureAwait(false);
            Assert.That(rejected.Error?.Code, Is.EqualTo(path == "/" ? "groups_only" : "resources_only"));
        }

        [TestCase(false, 4092, 200)]
        [TestCase(false, 4093, 400)]
        [TestCase(true, 2046, 200)]
        [TestCase(true, 2047, 400)]
        public async Task ScalarAttributeLimitCountsUtf8NameAndValueBytesAsync(bool multibyte, int length, int status)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            string body = new JsonObject { ["name"] = new string(multibyte ? '\u00e9' : 'x', length) }.ToJsonString();
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/", body))
                .ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(status), response.Error?.Detail);
            if (status == 400)
            {
                Assert.That(response.Error?.Code, Is.EqualTo("invalid_attribute"));
                Assert.That((await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false))
                    .Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            }
        }
    }
}
