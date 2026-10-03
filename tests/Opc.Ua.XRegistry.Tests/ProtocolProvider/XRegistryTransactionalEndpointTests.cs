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

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryTransactionalEndpointTests
    {
        [Test]
        public async Task ExplicitZeroGuardsTheInitialRootAndCannotBeReusedAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryResponse first = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Merge, "/", """{"epoch":0,"name":"first"}""")).ConfigureAwait(false);
            XRegistryResponse stale = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Merge, "/", """{"epoch":0,"name":"stale"}""")).ConfigureAwait(false);
            XRegistryResponse actual = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(first.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
                Assert.That(stale.StatusCode, Is.EqualTo(400));
                Assert.That(stale.Error?.Code, Is.EqualTo("mismatched_epoch"));
                Assert.That(actual.Metadata.GetProperty("name").GetString(), Is.EqualTo("first"));
                Assert.That(actual.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task InvalidNestedEntityLeavesEveryEntityAndParentEpochUnchangedAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                """{"name":"uncommitted","schemagroups":{"valid":{"name":"one"},"invalid":null}}"""))
                .ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            XRegistryResponse group = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/valid"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(400));
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(group.StatusCode, Is.EqualTo(404));
            });
        }

        [Test]
        public async Task IdenticalAndEmptySuccessfulWritesTouchOnlyTheTargetAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", """{"name":"same"}""")).ConfigureAwait(false);
            XRegistryResponse replaced = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", """{"name":"same"}""")).ConfigureAwait(false);
            XRegistryResponse touched = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Merge, "/schemagroups/g", "{}")).ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(replaced.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
                Assert.That(touched.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(2));
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1),
                    "An existing child's update must not be treated as a recursive root watermark.");
            });
        }

        [Test]
        public async Task VersionDocumentTouchDoesNotChangeResourceMetaEpochAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g/schemas/r",
                    """{"versionid":"v1","schemabase64":"AP8BAg==","contenttype":"application/octet-stream"}"""))
                .ConfigureAwait(false);
            XRegistryResponse touched = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g/schemas/r/versions/v1", """{"epoch":0}""") with
                {
                    Document = ByteString.From(new byte[] { 0, 255, 1, 2 })
                }).ConfigureAwait(false);
            XRegistryResponse meta = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/meta")).ConfigureAwait(false);
            XRegistryResponse bytes = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
                {
                    View = XRegistryView.Default
                }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(touched.StatusCode, Is.EqualTo(200));
                Assert.That(touched.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
                Assert.That(meta.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(bytes.Document.ToArray(), Is.EqualTo(new byte[] { 0, 255, 1, 2 }));
                Assert.That(bytes.ContentType, Is.Null,
                    "A document request without Content-Type erases that attribute.");
            });
        }

        [Test]
        public async Task ReplayingACommittedOperationDoesNotTouchAgainAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", """{"name":"once"}""") with
            {
                OperationId = "stable-operation"
            };
            XRegistryResponse first = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse replay = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await endpoint.GetOperationOutcomeAsync(
                request.OperationId, request.Context).ConfigureAwait(false);
            using var alternate = JsonDocument.Parse("""{"name":"different"}""");
            XRegistryResponse collision = await endpoint.ExecuteAsync(request with { Metadata = alternate.RootElement })
                .ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replay.Metadata.GetRawText(), Is.EqualTo(first.Metadata.GetRawText()));
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
                Assert.That(root.Metadata.GetProperty("name").GetString(), Is.EqualTo("once"));
                Assert.That(collision.StatusCode, Is.EqualTo(400));
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Committed));
            });
        }

        [Test]
        public async Task RejectedDeleteReplayCannotDeleteALaterIncarnationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryRequest request = Request(XRegistryAction.Delete, "/schemagroups/g") with
            {
                OperationId = "missing"
            };
            XRegistryResponse missing = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g", "{}"))
                .ConfigureAwait(false);
            XRegistryResponse replay = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
            XRegistryResponse existing = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                .ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await endpoint.GetOperationOutcomeAsync("missing", request.Context)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(missing.StatusCode, Is.EqualTo(404));
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(replay.StatusCode, Is.EqualTo(404));
                Assert.That(existing.StatusCode, Is.EqualTo(200));
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Rejected));
            });
        }

        [TestCase("""{"epoch":4294967296}""", 400)]
        [TestCase("""{"epoch":184467440737095516160}""", 400)]
        [TestCase("""{"epoch":-1}""", 400)]
        [TestCase("""{"epoch":null}""", 200)]
        [TestCase("{}", 200)]
        public async Task EpochWidthAndPresenceAreNeverNarrowedAsync(string body, int expectedStatus)
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/", body))
                .ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(expectedStatus == 200 ? 1 : 0));
            });
        }

        [Test]
        public async Task ResourceDeleteUsesMetaEpochRatherThanVersionEpochAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/g/schemas/r",
                """{"versionid":"v1"}""")).ConfigureAwait(false);
            await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/schemagroups/g/schemas/r/versions/v1",
                """{"name":"touched"}""")).ConfigureAwait(false);
            XRegistryResponse wrong = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Delete, "/schemagroups/g/schemas", """{"r":{"epoch":1}}"""))
                .ConfigureAwait(false);
            XRegistryResponse stale = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Delete, "/schemagroups/g/schemas", """{"r":{"meta":{"epoch":1}}}"""))
                .ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Delete, "/schemagroups/g/schemas", """{"r":{"meta":{"epoch":0}}}"""))
                .ConfigureAwait(false);
            XRegistryResponse missing = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/schemagroups/g/schemas/r"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(wrong.Error?.Code, Is.EqualTo("misplaced_epoch"));
                Assert.That(stale.Error?.Code, Is.EqualTo("mismatched_epoch"));
                Assert.That(deleted.StatusCode, Is.EqualTo(204));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
            });
        }

        [Test]
        public async Task InvalidResponseFlagCannotCommitAValidMutationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/schemagroups/g/schemas/r", "{}") with
                {
                    Parameters = [new XRegistryParameter("collections", null)]
                }).ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Error?.Code, Is.EqualTo("bad_flag"));
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(root.Metadata.GetProperty("schemagroupscount").GetInt32(), Is.Zero);
            });
        }

        [Test]
        public async Task SharedStoreReadersObserveAnotherEndpointsCommittedGenerationAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using XRegistryTransactionalEndpoint reader = CreateEndpoint(store);
            using XRegistryTransactionalEndpoint writer = CreateEndpoint(store);
            await reader.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            await writer.ExecuteAsync(Request(XRegistryAction.Merge, "/", """{"name":"new"}""")).ConfigureAwait(false);
            XRegistryResponse observed = await reader.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.That(observed.Metadata.GetProperty("name").GetString(), Is.EqualTo("new"));
        }

        [Test]
        public async Task DurableStoreRestoresDocumentsAndOutcomesAndRejectsSecondWriterAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), "xreg-provider-" + Guid.NewGuid().ToString("N"));
            try
            {
                XRegistryRequest request = Request(XRegistryAction.Replace, "/schemagroups/g/schemas/r",
                    """{"versionid":"v1","schemabase64":"AQID"}""") with
                {
                    OperationId = "durable"
                };
                using (var store = new FileXRegistryTransactionStore(directory))
                using (XRegistryTransactionalEndpoint endpoint = CreateEndpoint(store))
                {
                    Assert.Throws<IOException>(() =>
                    {
                        using var competing = new FileXRegistryTransactionStore(directory);
                    });
                    XRegistryResponse created = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                    Assert.That(created.StatusCode, Is.EqualTo(201));
                }
                using (var store = new FileXRegistryTransactionStore(directory))
                using (XRegistryTransactionalEndpoint endpoint = CreateEndpoint(store))
                {
                    XRegistryOperationOutcome outcome = await endpoint.GetOperationOutcomeAsync(
                        "durable", request.Context).ConfigureAwait(false);
                    XRegistryResponse replay = await endpoint.ExecuteAsync(request).ConfigureAwait(false);
                    XRegistryResponse actual = await endpoint.ExecuteAsync(
                        Request(XRegistryAction.Read, "/schemagroups/g/schemas/r/versions/v1") with
                        {
                            View = XRegistryView.Default
                        }).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Committed));
                        Assert.That(replay.StatusCode, Is.EqualTo(201));
                        Assert.That(actual.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                        Assert.That(actual.Document.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                    });
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public async Task ModelRequiredDefaultsTypedMetadataAndReadonlyFieldsAreEnforcedAtomicallyAsync()
        {
            const string modelJson = """
                {"groups":{"groups":{"singular":"group","attributes":{
                "settings":{"type":"object","required":true,"attributes":{
                "retries":{"type":"uinteger","required":true,"default":3},
                "enabled":{"type":"boolean","required":true},
                "server":{"type":"integer","readonly":true}}}
                },"resources":{}}}}
                """;
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint(modelJson: modelJson);
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                """{"settings":{"enabled":false,"server":"ignore invalid readonly value"}}""")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/groups/g",
                """{"settings":{"enabled":"not-a-boolean"}}""")).ConfigureAwait(false);
            XRegistryResponse unknown = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/groups/g",
                """{"settings":{"unknown":true}}""")).ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(created.Metadata.GetProperty("settings").GetProperty("retries").GetInt32(), Is.EqualTo(3));
                Assert.That(created.Metadata.GetProperty("settings").TryGetProperty("server", out _), Is.False);
                Assert.That(rejected.Error?.Code, Is.EqualTo("invalid_attribute"));
                Assert.That(unknown.Error?.Code, Is.EqualTo("invalid_attribute"));
                Assert.That(read.Metadata.GetProperty("settings").GetProperty("enabled").GetBoolean(), Is.False);
                Assert.That(read.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
            });
        }

        [Test]
        public async Task CollectionMutationRespondsOnlyWithRequestedEntriesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/old", "{}"))
                .ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Create, "/schemagroups",
                """{"new":{"name":"inserted"}}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(200));
                Assert.That(response.Metadata.TryGetProperty("old", out _), Is.False);
                Assert.That(response.Metadata.GetProperty("new").GetProperty("name").GetString(),
                    Is.EqualTo("inserted"));
            });
        }

        [Test]
        public async Task EmptyDeleteMapIsANoopAndAbsentMapDeletesTheCollectionAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/schemagroups/g", "{}"))
                .ConfigureAwait(false);
            XRegistryResponse noop = await endpoint.ExecuteAsync(Request(XRegistryAction.Delete, "/schemagroups", "{}"))
                .ConfigureAwait(false);
            XRegistryResponse stillThere = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                .ConfigureAwait(false);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(Request(XRegistryAction.Delete, "/schemagroups"))
                .ConfigureAwait(false);
            XRegistryResponse missing = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/schemagroups/g"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(noop.StatusCode, Is.EqualTo(204));
                Assert.That(stillThere.StatusCode, Is.EqualTo(200));
                Assert.That(deleted.StatusCode, Is.EqualTo(204));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
            });
        }

        [Test]
        public async Task StickyDefaultAndRetentionPreserveTheSelectedVersionAsync()
        {
            const string modelJson = """
                {"groups":{"groups":{"singular":"group","resources":{"resources":{
                "singular":"resource","hasdocument":false,"maxversions":2}}}}}
                """;
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint(modelJson: modelJson);
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/resources/r",
                    """{"versions":{"a":{},"b":{}},"meta":{"defaultversionid":"b"}}"""))
                .ConfigureAwait(false);
            XRegistryResponse next = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace,
                "/groups/g/resources/r/versions/c", "{}")).ConfigureAwait(false);
            XRegistryResponse oldest = await endpoint.ExecuteAsync(Request(XRegistryAction.Read,
                "/groups/g/resources/r/versions/a")).ConfigureAwait(false);
            XRegistryResponse meta = await endpoint.ExecuteAsync(Request(XRegistryAction.Read,
                "/groups/g/resources/r/meta")).ConfigureAwait(false);
            XRegistryResponse deletion = await endpoint.ExecuteAsync(Request(XRegistryAction.Delete,
                "/groups/g/resources/r/versions/b")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201));
                Assert.That(next.StatusCode, Is.EqualTo(201));
                Assert.That(oldest.StatusCode, Is.EqualTo(404));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("b"));
                Assert.That(meta.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.True);
                Assert.That(deletion.Error?.Code, Is.EqualTo("setdefaultversionsticky_false"));
            });
        }

        [Test]
        public async Task ResponseLossAfterStoreCommitRequiresRecoveryAndDoesNotReplayTheMutationAsync()
        {
            var durable = new InMemoryXRegistryTransactionStore();
            var failed = new ResponseLossStore(durable);
            XRegistryRequest request = Request(XRegistryAction.Merge, "/", """{"name":"committed"}""") with
            {
                OperationId = "recoverable"
            };
            using (XRegistryTransactionalEndpoint first = CreateEndpoint(failed))
            {
                Assert.ThrowsAsync<IOException>(async () => await first.ExecuteAsync(request).ConfigureAwait(false));
                Assert.ThrowsAsync<IOException>(async () =>
                    await first.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false));
            }
            using XRegistryTransactionalEndpoint recovered = CreateEndpoint(durable);
            XRegistryResponse replay = await recovered.ExecuteAsync(request).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replay.StatusCode, Is.EqualTo(200));
                Assert.That(replay.Metadata.GetProperty("name").GetString(), Is.EqualTo("committed"));
                Assert.That(replay.Metadata.GetProperty("epoch").GetUInt64(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task UnauthenticatedMutationNeverTouchesTheStateStoreAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint();
            XRegistryResponse response = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Merge, "/", """{"name":"unauthorized"}""") with
                {
                    Context = XRegistryCallContext.Anonymous
                }).ConfigureAwait(false);
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(403));
                Assert.That(root.Metadata.GetProperty("epoch").GetUInt64(), Is.Zero);
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
            });
        }

        [Test]
        public async Task CorruptPersistedStateFailsClosedWithoutPublishingANewRegistryAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            var corrupt = ByteString.From(Encoding.UTF8.GetBytes("""{"format":1,"registryid":"test-registry"}"""));
            await store.CommitAsync(default, corrupt).ConfigureAwait(false);
            using XRegistryTransactionalEndpoint endpoint = CreateEndpoint(store);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false));
            ByteString retained = await store.LoadAsync().ConfigureAwait(false);
            Assert.That(retained.ToArray(), Is.EqualTo(corrupt.ToArray()));
        }

        private static XRegistryTransactionalEndpoint CreateEndpoint(
            IXRegistryTransactionStore? store = null, string? modelJson = null)
        {
            using var model = JsonDocument.Parse(modelJson ??
                """
                {"groups":{"schemagroups":{"plural":"schemagroups","singular":"schemagroup",
                "resources":{"schemas":{"plural":"schemas","singular":"schema","hasdocument":true,
                "attributes":{"*":{"type":"any"}}}}}}}
                """);
            return new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = "test-registry",
                Model = model.RootElement,
                PublicRoot = new Uri("https://registry.example/")
            }, store ?? new InMemoryXRegistryTransactionStore());
        }

        private sealed class ResponseLossStore(IXRegistryTransactionStore inner) : IXRegistryTransactionStore
        {
            public bool SupportsDurableReplay => false;

            public ValueTask<ByteString> LoadAsync(CancellationToken cancellationToken = default)
            {
                return inner.LoadAsync(cancellationToken);
            }

            public async ValueTask<bool> CommitAsync(
                ByteString expected, ByteString replacement, CancellationToken cancellationToken = default)
            {
                bool committed = await inner.CommitAsync(expected, replacement, cancellationToken)
                    .ConfigureAwait(false);
                if (committed)
                {
                    throw new IOException("Simulated acknowledgement loss.");
                }
                return false;
            }
        }

        private static XRegistryRequest Request(XRegistryAction action, string path, string? json = null)
        {
            using JsonDocument? document = json is null ? null : JsonDocument.Parse(json);
            return new XRegistryRequest(action, path)
            {
                View = XRegistryView.Metadata,
                Context = new XRegistryCallContext("writer")
                {
                    IsAuthenticated = true,
                    Roles = ["xregistry.write"]
                },
                Metadata = document?.RootElement ?? default
            };
        }
    }
}
