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

using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryModelMutationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RootDefaultsAndExplicitInitialMetadataExistBeforeTheFirstWriteAsync(bool supplied)
        {
            using var model = JsonDocument.Parse("""
                {"attributes":{"site":{"type":"string","required":true,"default":"factory"}},"groups":{}}
                """);
            using var initial = JsonDocument.Parse("""{"site":"plant"}""");
            using var endpoint = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                Model = model.RootElement,
                InitialMetadata = supplied ? initial.RootElement : default
            }, new InMemoryXRegistryTransactionStore());
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(root.Metadata.GetProperty("site").GetString(), Is.EqualTo(supplied ? "plant" : "factory"));
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
            });
        }

        [TestCase("2026-09-10T12:30:00+02:00")]
        [TestCase("2026-09-10t10:30:00z")]
        [TestCase("2026-09-10T10:30:00.000Z")]
        public async Task ValidRfc3339TimestampsAreNormalizedToUtcAsync(string timestamp)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                $$"""{"createdat":"{{timestamp}}"}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(201));
                Assert.That(response.Metadata.GetProperty("createdat").GetString(),
                    Is.EqualTo("2026-09-10T10:30:00.0000000Z"));
            });
        }

        [Test]
        public async Task ModelChangeCannotHideAnExistingDomainDocumentAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/r",
                """{"versionid":"v1","schemabase64":"AQID"}""")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/modelsource",
                """{"groups":{"groups":{"resources":{"schemas":{"hasdocument":false}}}}}""")).ConfigureAwait(false);
            XRegistryResponse model = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);
            XRegistryResponse document = await endpoint.ExecuteAsync(Request(XRegistryAction.Read,
                "/groups/g/schemas/r/versions/v1")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(400));
                Assert.That(rejected.Error?.Code, Is.EqualTo("hasdocument_violation"));
                Assert.That(model.Metadata.GetProperty("groups").GetProperty("groups")
                    .GetProperty("resources").GetProperty("schemas").GetProperty("hasdocument").GetBoolean(), Is.True);
                Assert.That(document.Document.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            });
        }

        [Test]
        public async Task ModelChangeCannotInvalidateExistingRequiredAttributesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g", "{}")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/modelsource",
                """{"groups":{"groups":{"attributes":{"requirednew":{"type":"boolean","required":true}}}}}"""))
                .ConfigureAwait(false);
            XRegistryResponse model = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(400));
                Assert.That(model.Metadata.GetProperty("groups").GetProperty("groups")
                    .TryGetProperty("attributes", out _), Is.False);
            });
        }

        [Test]
        public async Task NestedUserEpochIsValidatedAsUserDataRatherThanAGeneratedCounterAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create("""
                {"groups":{"groups":{"singular":"group","resources":{},"attributes":{
                "settings":{"type":"object","attributes":{"epoch":{"type":"uinteger"}}}}}}}
                """);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                """{"settings":{"epoch":"not-a-number"}}""")).ConfigureAwait(false);
            XRegistryResponse missing = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(400));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
            });
        }

        [TestCase("9/10/2026")]
        [TestCase("2026-09-10")]
        [TestCase("2026-09-10T12:30:00")]
        [TestCase("2026-09-10 12:30:00Z")]
        public async Task NonRfc3339TimestampsAreRejectedWithoutCreatingAnEntityAsync(string timestamp)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                $$"""{"createdat":"{{timestamp}}"}""")).ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(400));
                Assert.That(root.Metadata.GetProperty("groupscount").GetInt32(), Is.Zero);
            });
        }

        private static XRegistryTransactionalEndpoint Create(string? json = null)
        {
            using var model = JsonDocument.Parse(json ??
                """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{
                "singular":"schema","hasdocument":true}}}}}
                """);
            return new XRegistryTransactionalEndpoint(
                new XRegistryTransactionalOptions { Model = model.RootElement },
                new InMemoryXRegistryTransactionStore());
        }

        private static XRegistryRequest Request(XRegistryAction action, string path, string? json = null)
        {
            using JsonDocument? body = json is null ? null : JsonDocument.Parse(json);
            return new XRegistryRequest(action, path)
            {
                Metadata = body?.RootElement ?? default,
                Context = new XRegistryCallContext("writer") { IsAuthenticated = true, Roles = ["xregistry.write"] }
            };
        }
    }
}
