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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryTransactionalReadCoverageTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task InspectReportsQualifiedCapabilitiesAndInjectedStoreDurabilityAsync(bool durable)
        {
            var store = new Mock<IXRegistryTransactionStore>(MockBehavior.Strict);
            store.SetupGet(value => value.SupportsDurableReplay).Returns(durable);
            store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ByteString>(default(ByteString)));
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store.Object);
            XRegistryEndpointDescription description = await endpoint.InspectAsync(XRegistryCallContext.Anonymous)
                .ConfigureAwait(false);
            JsonElement schema = description.Model.GetProperty("groups").GetProperty("groups")
                .GetProperty("resources").GetProperty("schemas");
            Assert.Multiple(() =>
            {
                Assert.That(description.RegistryId, Is.EqualTo("coverage-registry"));
                Assert.That(description.Profile, Is.EqualTo("experimental-transactional-v1"));
                Assert.That(description.SupportsAtomicMutations, Is.True);
                Assert.That(description.SupportsConditionalMutations, Is.True);
                Assert.That(description.SupportsWriteTouch, Is.True);
                Assert.That(description.SupportsPreparedMutations, Is.True);
                Assert.That(description.SupportsOperationReplay, Is.EqualTo(durable));
                Assert.That(description.Capabilities.GetProperty("versionmodes").GetRawText(),
                    Is.EqualTo("""["manual","createdat","modifiedat"]"""));
                Assert.That(description.Capabilities.GetProperty("pagination").GetBoolean(), Is.False);
                Assert.That(description.Capabilities.GetProperty("available").GetProperty("modelsource")
                    .GetProperty("mutable").GetBoolean(), Is.True);
                Assert.That(schema.GetProperty("maxversions").GetInt32(), Is.Zero);
                Assert.That(schema.GetProperty("versionmode").GetString(), Is.EqualTo("manual"));
                Assert.That(schema.GetProperty("hasdocument").GetBoolean(), Is.True);
                Assert.That(schema.GetProperty("setversionid").GetBoolean(), Is.True);
                Assert.That(schema.GetProperty("singleversionroot").GetBoolean(), Is.False);
            });
        }

        [Test]
        public async Task ModelSourceRetainsOriginalShapeWhileModelNormalizesMissingCollectionsAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create("""{"groups":{"groups":{"singular":"group"}}}""");
            XRegistryResponse source = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/modelsource")).ConfigureAwait(false);
            XRegistryResponse model = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/model")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(source.StatusCode, Is.EqualTo(200));
                Assert.That(source.Metadata.GetRawText(),
                    Is.EqualTo("""{"groups":{"groups":{"singular":"group"}}}"""));
                Assert.That(model.Metadata.GetProperty("groups").GetProperty("groups").GetProperty("plural")
                    .GetString(), Is.EqualTo("groups"));
                Assert.That(model.Metadata.GetProperty("groups").GetProperty("groups").GetProperty("resources")
                    .GetRawText(), Is.EqualTo("{}"));
            });
        }

        [Test]
        public async Task ExportContainsWholeTreeExactDocumentsAndIndependentRootRepresentationsAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse exported = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/export")).ConfigureAwait(false);
            JsonElement root = exported.Metadata;
            JsonElement group = root.GetProperty("groups").GetProperty("g");
            JsonElement resource = group.GetProperty("schemas").GetProperty("r");
            JsonElement versions = resource.GetProperty("versions");
            XRegistryResponse after = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exported.StatusCode, Is.EqualTo(200));
                Assert.That(exported.Document.IsNull, Is.True);
                Assert.That(root.GetProperty("self").GetString(), Is.EqualTo("https://registry.example/registry"));
                Assert.That(root.GetProperty("xid").GetString(), Is.EqualTo("/"));
                Assert.That(root.GetProperty("specversion").GetString(), Is.EqualTo("1.0-rc4"));
                Assert.That(root.GetProperty("groupscount").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("model").GetProperty("groups").GetProperty("groups")
                    .GetProperty("plural").GetString(), Is.EqualTo("groups"));
                Assert.That(root.GetProperty("modelsource").GetProperty("groups").GetProperty("groups")
                    .TryGetProperty("plural", out _), Is.False);
                Assert.That(root.GetProperty("capabilities").GetProperty("shortself").GetBoolean(), Is.False);
                Assert.That(group.GetProperty("schemascount").GetInt32(), Is.EqualTo(1));
                Assert.That(resource.GetProperty("versionid").GetString(), Is.EqualTo("a"));
                Assert.That(resource.GetProperty("versionscount").GetInt32(), Is.EqualTo(2));
                Assert.That(resource.GetProperty("meta").GetProperty("defaultversionid").GetString(), Is.EqualTo("a"));
                Assert.That(resource.GetProperty("meta").GetProperty("readonly").GetBoolean(), Is.False);
                Assert.That(versions.GetProperty("a").GetProperty("schemabase64").GetString(), Is.EqualTo("AAEC"));
                Assert.That(versions.GetProperty("a").GetProperty("isdefault").GetBoolean(), Is.True);
                Assert.That(versions.GetProperty("b").GetProperty("schemabase64").GetString(), Is.EqualTo("/w=="));
                Assert.That(versions.GetProperty("b").GetProperty("isdefault").GetBoolean(), Is.False);
                Assert.That(after.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task InlineSelectorsExpandRequestedTreeAndLeaveUnrequestedDocumentsAbsentAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/") with
                {
                    Parameters =
                    [
                        new XRegistryParameter("unknown", "ignored"),
                        new XRegistryParameter("inline", "model,modelsource,capabilities"),
                        new XRegistryParameter("inline", "groups.schemas.meta,groups.schemas.versions")
                    ]
                }).ConfigureAwait(false);
            JsonElement resource = response.Metadata.GetProperty("groups").GetProperty("g")
                .GetProperty("schemas").GetProperty("r");
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Metadata.GetProperty("model").ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(response.Metadata.GetProperty("modelsource").GetProperty("groups").GetProperty("groups")
                    .TryGetProperty("plural", out _), Is.False);
                Assert.That(response.Metadata.GetProperty("capabilities").GetProperty("specversions").GetRawText(),
                    Is.EqualTo("""["1.0-rc4"]"""));
                Assert.That(resource.GetProperty("meta").GetProperty("defaultversionid").GetString(), Is.EqualTo("a"));
                Assert.That(resource.GetProperty("versions").EnumerateObject().Select(value => value.Name),
                    Is.EqualTo(s_versionIds));
                Assert.That(resource.TryGetProperty("schemabase64", out _), Is.False);
                Assert.That(resource.GetProperty("versions").GetProperty("a").TryGetProperty("schemabase64", out _),
                    Is.False);
            });
        }

        [TestCase("inline", "*")]
        [TestCase("inline", "schema")]
        [TestCase("doc", null)]
        public async Task DocumentSelectionEmbedsExactBase64WithoutChangingTheDocumentViewAsync(
            string flag, string? selection)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/b") with
                {
                    Parameters = [new XRegistryParameter(flag, selection)]
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Document.IsNull, Is.True);
                Assert.That(response.Metadata.GetProperty("schemabase64").GetString(), Is.EqualTo("/w=="));
                Assert.That(response.Metadata.GetProperty("isdefault").GetBoolean(), Is.False);
                Assert.That(response.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [TestCase("/", "groups", "g")]
        [TestCase("/groups/g", "schemas", "r")]
        public async Task CollectionsFlagReturnsOnlyNamedCollectionMapsAsync(
            string path, string collection, string id)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, path) with
                {
                    Parameters = [new XRegistryParameter("collections", null)]
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Metadata.EnumerateObject().Select(value => value.Name),
                    Is.EqualTo(new[] { collection }));
                Assert.That(response.Metadata.GetProperty(collection).EnumerateObject().Select(value => value.Name),
                    Is.EqualTo(new[] { id }));
            });
        }

        [TestCase("/groups/g/schemas/r", "a", "AAEC", "first")]
        [TestCase("/groups/g/schemas/r/versions/b", "b", "/w==", "second")]
        public async Task DefaultDocumentReadsReturnExactBytesAndVersionLocationAsync(
            string path, string version, string base64, string name)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, path) with { View = XRegistryView.Default })
                .ConfigureAwait(false);
            byte[] expected = base64 == "AAEC" ? [0, 1, 2] : [255];
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Document.ToArray(), Is.EqualTo(expected));
                Assert.That(response.ContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(response.ContentLocation,
                    Is.EqualTo("https://registry.example/registry/groups/g/schemas/r/versions/" + version));
                Assert.That(response.Metadata.GetProperty("self").GetString(),
                    Is.EqualTo("https://registry.example/registry" + path));
                Assert.That(response.Metadata.GetProperty("name").GetString(), Is.EqualTo(name));
            });
        }

        [TestCase("/model", "Read,Describe")]
        [TestCase("/capabilities", "Read,Describe")]
        [TestCase("/groups", "Read,Create,Merge,Delete,Describe")]
        [TestCase("/groups/g", "Read,Replace,Merge,Delete,Describe")]
        public async Task DescribeReportsExactActionsWithoutCreatingMissingEntitiesAsync(string path, string actions)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Describe, path)).ConfigureAwait(false);
            XRegistryAction[] allowed = response.AllowedActions.ToArray() ??
                throw new AssertionException("The endpoint omitted its declared actions.");
            XRegistryResponse root = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(204));
                Assert.That(allowed.Select(action => action.ToString()),
                    Is.EqualTo(actions.Split(',')));
                Assert.That(response.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(root.Metadata.GetProperty("groupscount").GetInt32(), Is.Zero);
            });
        }

        [TestCase("/missing")]
        [TestCase("/groups/g/missing")]
        [TestCase("/groups/g/schemas/r/invalid")]
        [TestCase("/groups/g/schemas/r/versions/a/extra")]
        [TestCase("/groups/absent/schemas")]
        [TestCase("/groups/g/schemas/absent/versions")]
        public async Task UnknownOrAbsentAddressesReturnNotFoundAsync(string path)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, path)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(404));
                Assert.That(response.Error?.Code, Is.EqualTo("not_found"));
                Assert.That(response.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
                Assert.That(response.Document.IsNull, Is.True);
            });
        }

        [Test]
        public async Task UnknownFlagsAreIgnoredButUnsupportedSpecVersionCannotMutateAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse read = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/") with
                {
                    Parameters =
                    [
                        new XRegistryParameter("future-extension", "value"),
                        new XRegistryParameter("inline", null),
                        new XRegistryParameter("specversion", "1.0-rc4")
                    ]
                }).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"not-published"}""") with
                {
                    Parameters = [new XRegistryParameter("specversion", "unsupported")]
                }).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(200));
            Assert.That(read.Metadata.TryGetProperty("groups", out _), Is.False);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "bad_flag").ConfigureAwait(false);
        }

        [TestCase("/capabilities")]
        [TestCase("/capabilitiesoffered")]
        public async Task CapabilityDocumentsExposeOnlyQualifiedFlagsAndMutableAspectsAsync(string path)
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, path)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Metadata.GetProperty("flags").GetRawText(),
                    Is.EqualTo("""["inline","doc","binary","collections","epoch","specversion"]"""));
                Assert.That(response.Metadata.GetProperty("available").GetProperty("model")
                    .GetProperty("mutable").GetBoolean(), Is.False);
                Assert.That(response.Metadata.GetProperty("available").GetProperty("entities")
                    .GetProperty("mutable").GetBoolean(), Is.True);
                Assert.That(response.Metadata.GetProperty("formats").GetRawText(), Is.EqualTo("[]"));
                Assert.That(response.Metadata.GetProperty("compatibilities").GetRawText(), Is.EqualTo("{}"));
            });
        }

        private static async Task SeedAsync(XRegistryTransactionalEndpoint endpoint)
        {
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, "/groups/g",
                """
                {"name":"group","schemas":{"r":{"versions":{
                "a":{"name":"first","schemabase64":"AAEC","contenttype":"application/octet-stream"},
                "b":{"name":"second","schemabase64":"/w==","contenttype":"application/octet-stream"}},
                "meta":{"defaultversionid":"a"}}}}
                """)).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(201));
        }

        private static readonly string[] s_versionIds = ["a", "b"];
    }
}
