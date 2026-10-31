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

using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryGenerationTests
    {
        [Test]
        public async Task InspectionAndEveryReadRepresentationShareOneGenerationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse seeded = await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryEndpointDescription description = await endpoint.InspectAsync(XRegistryProviderCoverage.Writer)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(description.SupportsGenerationGuards, Is.True);
                Assert.That(description.Generation, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    seeded.Generation, Is.Null, "A mutation reply must not promise a reusable read generation.");
            });
            foreach (string path in s_readPaths)
            {
                foreach (XRegistryView view in new[] { XRegistryView.Metadata, XRegistryView.Default })
                {
                    XRegistryResponse read = await endpoint.ExecuteAsync(
                        XRegistryProviderCoverage.Request(XRegistryAction.Read, path) with
                        {
                            View = view,
                            ExpectedGeneration = description.Generation
                        }).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(read.StatusCode, Is.EqualTo(200), path);
                        Assert.That(read.Generation, Is.EqualTo(description.Generation), path);
                    });
                }
            }
            XRegistryResponse methods = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Describe, k_version) with
                {
                    ExpectedGeneration = description.Generation
                }).ConfigureAwait(false);
            XRegistryEndpointDescription repeated = await endpoint.InspectAsync(XRegistryProviderCoverage.Writer)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(methods.StatusCode, Is.EqualTo(204));
                Assert.That(methods.Generation, Is.EqualTo(description.Generation));
                Assert.That(repeated.Generation, Is.EqualTo(description.Generation));
            });
        }

        [Test]
        public async Task StaleGenerationRejectsEveryActionEvenWhenRootEpochIsUnchangedAsync(
            [Values] XRegistryAction action)
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse before = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            XRegistryResponse edit = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_version, /*lang=json,strict*/ """{"name":"external"}"""))
                .ConfigureAwait(false);
            Assert.That(edit.StatusCode, Is.EqualTo(200));
            ByteString committed = await store.LoadAsync().ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(action, k_version, "{}") with
                {
                    ExpectedGeneration = before.Generation
                }).ConfigureAwait(false);
            XRegistryResponse after = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(409));
                Assert.That(rejected.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(rejected.Generation, Is.Null);
                Assert.That(after.Generation, Is.Not.EqualTo(before.Generation));
                Assert.That(after.Metadata.GetProperty("epoch").GetInt32(),
                    Is.EqualTo(before.Metadata.GetProperty("epoch").GetInt32()));
            });
            Assert.That(await store.LoadAsync().ConfigureAwait(false), Is.EqualTo(committed));
        }

        [Test]
        public async Task AbortedPreparationAndUnjournaledRejectionDoNotChangeGenerationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryResponse before = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            IXRegistryPreparedOperation prepared = await endpoint.PrepareAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", "{}") with
                {
                    ExpectedGeneration = before.Generation
                }).ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"epoch":999}""")).ConfigureAwait(false);
            XRegistryResponse after = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/") with
                {
                    ExpectedGeneration = before.Generation
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Response.StatusCode, Is.EqualTo(200));
                Assert.That(rejected.Error?.Code, Is.EqualTo("mismatched_epoch"));
                Assert.That(after.StatusCode, Is.EqualTo(200));
                Assert.That(after.Generation, Is.EqualTo(before.Generation));
                Assert.That(after.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalStoreChangesAndRestartExpireObservedGenerationsAsync(bool restart)
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse before = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            using var other = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            if (!restart)
            {
                XRegistryResponse edited = await other.ExecuteAsync(XRegistryProviderCoverage.Request(
                    XRegistryAction.Merge, k_version, /*lang=json,strict*/ """{"name":"external"}"""))
                    .ConfigureAwait(false);
                Assert.That(edited.StatusCode, Is.EqualTo(200));
            }
            XRegistryTransactionalEndpoint reader = restart ? other : endpoint;
            XRegistryResponse rejected = await reader.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version) with
                {
                    ExpectedGeneration = before.Generation
                }).ConfigureAwait(false);
            XRegistryResponse current = await reader.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(current.Generation, Is.Not.EqualTo(before.Generation));
                Assert.That(current.Metadata.GetProperty("name").GetString(),
                    Is.EqualTo(restart ? "original" : "external"));
            });
        }

        [Test]
        public async Task GuardedPreparationStillUsesGlobalCasAtPublicationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryEndpointDescription observed = await endpoint.InspectAsync(XRegistryProviderCoverage.Writer)
                .ConfigureAwait(false);
            IXRegistryPreparedOperation prepared = await endpoint.PrepareAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_version, /*lang=json,strict*/ """{"name":"prepared"}""") with
            {
                ExpectedGeneration = observed.Generation
            }).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable lifetime = prepared.ConfigureAwait(false);
            XRegistryResponse intervening = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", "{}")).ConfigureAwait(false);
            Assert.That(intervening.StatusCode, Is.EqualTo(200));
            XRegistryResponse rejected = await prepared.CommitAsync().ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Response.Metadata.GetProperty("name").GetString(), Is.EqualTo("prepared"));
                Assert.That(rejected.Error?.Code, Is.EqualTo("concurrent_change"));
                Assert.That(current.Metadata.GetProperty("name").GetString(), Is.EqualTo("original"));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [Test]
        public async Task ReplayingAnOldGuardedOperationPreservesTheOutcomeWithoutTouchingAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            XRegistryEndpointDescription observed = await endpoint.InspectAsync(XRegistryProviderCoverage.Writer)
                .ConfigureAwait(false);
            XRegistryRequest request = XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"name":"once"}""") with
            {
                ExpectedGeneration = observed.Generation,
                OperationId = "generation-once"
            };
            XRegistryResponse first = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryEndpointDescription committed = await endpoint.InspectAsync(XRegistryProviderCoverage.Writer)
                .ConfigureAwait(false);
            XRegistryResponse replay = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse collision = await endpoint.ExecuteAsync(request with
            {
                ExpectedGeneration = committed.Generation
            }).ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(replay.Metadata.GetRawText(), Is.EqualTo(first.Metadata.GetRawText()));
                Assert.That(collision.StatusCode, Is.EqualTo(400));
                Assert.That(current.Generation, Is.EqualTo(committed.Generation));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public async Task GenerationAndIncarnationGuardsAreBothRequiredAsync(bool generation, bool incarnation)
        {
            using XRegistryTransactionalEndpoint endpoint = XRegistryProviderCoverage.Create();
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse observed = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, k_version, /*lang=json,strict*/ """{"epoch":0,"name":"guarded"}""") with
            {
                ExpectedGeneration = generation ? observed.Generation : "stale-generation",
                ExpectedVersionIncarnation = incarnation ? observed.VersionIncarnation : "replaced-version"
            }).ConfigureAwait(false);
            XRegistryResponse current = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, k_version)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(generation && incarnation ? 200 : 409));
                Assert.That(response.Error?.Code, Is.EqualTo(!generation ? "concurrent_change" :
                    !incarnation ? "version_incarnation_changed" : null));
                Assert.That(current.Metadata.GetProperty("name").GetString(),
                    Is.EqualTo(generation && incarnation ? "guarded" : "original"));
                Assert.That(current.Metadata.GetProperty("epoch").GetInt32(),
                    Is.EqualTo(generation && incarnation ? 1 : 0));
            });
        }

        private static async Task<XRegistryResponse> SeedAsync(XRegistryTransactionalEndpoint endpoint)
        {
            XRegistryResponse response = await endpoint.ExecuteAsync(XRegistryProviderCoverage.Request(
                XRegistryAction.Replace, k_version, /*lang=json,strict*/ """{"name":"original"}"""))
                .ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(201));
            return response;
        }

        private const string k_version = "/groups/g/schemas/r/versions/v1";

        private static readonly string[] s_readPaths =
        [
            "/", "/groups", "/groups/g", "/groups/g/schemas", "/groups/g/schemas/r",
            "/groups/g/schemas/r/meta", "/groups/g/schemas/r/versions", k_version,
            "/model", "/modelsource", "/capabilities", "/export"
        ];
    }
}
