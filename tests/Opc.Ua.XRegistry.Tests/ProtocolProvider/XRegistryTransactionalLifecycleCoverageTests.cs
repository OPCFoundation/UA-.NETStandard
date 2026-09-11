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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryTransactionalLifecycleCoverageTests
    {
        [TestCase("identity")]
        [TestCase("role")]
        [TestCase("entities")]
        [TestCase("document")]
        [TestCase("state")]
        [TestCase("preparedcount")]
        [TestCase("preparedbytes")]
        [TestCase("model")]
        [TestCase("nullroot")]
        [TestCase("relative")]
        [TestCase("userinfo")]
        [TestCase("query")]
        [TestCase("fragment")]
        public void ConstructorRejectsInvalidOptions(string invalid)
        {
            XRegistryTransactionalOptions options = XRegistryProviderCoverage.Options();
            options = invalid switch
            {
                "identity" => options with { RegistryId = " " },
                "role" => options with { WriteRole = string.Empty },
                "entities" => options with { MaxEntities = 0 },
                "document" => options with { MaxDocumentBytes = -1 },
                "state" => options with { MaxStateBytes = 1023 },
                "preparedcount" => options with { MaxPreparedOperations = 0 },
                "preparedbytes" => options with { MaxPreparedBytes = 0 },
                "model" => options with { Model = default },
                "nullroot" => options with { PublicRoot = null! },
                "relative" => options with { PublicRoot = new Uri("registry", UriKind.Relative) },
                "userinfo" => options with { PublicRoot = new Uri("https://user@registry.example/") },
                "query" => options with { PublicRoot = new Uri("https://registry.example/?q=1") },
                "fragment" => options with { PublicRoot = new Uri("https://registry.example/#fragment") },
                _ => throw new ArgumentOutOfRangeException(nameof(invalid))
            };
            ArgumentException? error = Assert.Throws<ArgumentException>(() =>
            {
                using var endpoint = new XRegistryTransactionalEndpoint(
                    options, new InMemoryXRegistryTransactionStore());
            });
            Assert.That(error?.ParamName, Is.EqualTo("options"));
        }

        [Test]
        public void ConstructorAndRegistrationRejectNullDependencies()
        {
            XRegistryTransactionalOptions options = XRegistryProviderCoverage.Options();
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var endpoint = new XRegistryTransactionalEndpoint(
                    null!, new InMemoryXRegistryTransactionStore());
            });
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var endpoint = new XRegistryTransactionalEndpoint(options, null!);
            });
            Assert.Throws<ArgumentNullException>(() =>
                XRegistryTransactionalServiceCollectionExtensions.AddXRegistryTransactions(null!, options));
            Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddXRegistryTransactions(null!));
        }

        [Test]
        public async Task OptionsOwnJsonSnapshotsAfterTheirDocumentsAreDisposedAsync()
        {
            XRegistryTransactionalOptions options;
            using (JsonDocument model = JsonDocument.Parse("""
                {"attributes":{"site":{"type":"string","required":true}},"groups":{}}
                """))
            using (JsonDocument seed = JsonDocument.Parse("""{"site":"plant"}"""))
            {
                options = XRegistryProviderCoverage.Options() with
                {
                    Model = model.RootElement,
                    InitialMetadata = seed.RootElement
                };
            }
            using var endpoint = new XRegistryTransactionalEndpoint(options, new InMemoryXRegistryTransactionStore());
            XRegistryResponse root = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(options.Model.GetProperty("attributes").GetProperty("site").GetProperty("required")
                    .GetBoolean(), Is.True);
                Assert.That(options.InitialMetadata.GetProperty("site").GetString(), Is.EqualTo("plant"));
                Assert.That(root.Metadata.GetProperty("site").GetString(), Is.EqualTo("plant"));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(options.MaxPreparedOperations, Is.EqualTo(64));
                Assert.That(options.MaxPreparedBytes, Is.EqualTo(256 * 1024 * 1024));
            });
        }

        [Test]
        public async Task RegistrationSharesEndpointAndJournalWithAnInjectedStoreAndClockAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            var clock = new XRegistryProviderCoverageTimeProvider();
            var services = new ServiceCollection();
            services.AddSingleton<IXRegistryTransactionStore>(store);
            services.AddSingleton<TimeProvider>(clock);
            XRegistryTransactionalOptions options = XRegistryProviderCoverage.Options();
            IServiceCollection returned = services.AddXRegistryTransactions(options);
            using ServiceProvider provider = services.BuildServiceProvider();
            XRegistryTransactionalEndpoint endpoint = provider.GetRequiredService<XRegistryTransactionalEndpoint>();
            IXRegistryEndpoint publicEndpoint = provider.GetRequiredService<IXRegistryEndpoint>();
            IXRegistryOperationJournalEndpoint journal =
                provider.GetRequiredService<IXRegistryOperationJournalEndpoint>();
            XRegistryResponse changed = await publicEndpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"registered"}""") with
                {
                    OperationId = "di-operation"
                }).ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await journal.GetOperationOutcomeAsync(
                "di-operation", XRegistryProviderCoverage.Writer).ConfigureAwait(false);
            ByteString persisted = await store.LoadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(services));
                Assert.That(publicEndpoint, Is.SameAs(endpoint));
                Assert.That(journal, Is.SameAs(endpoint));
                Assert.That(endpoint, Is.InstanceOf<IXRegistryPreparedEndpoint>());
                Assert.That(provider.GetRequiredService<IXRegistryPreparedEndpoint>(), Is.SameAs(endpoint));
                Assert.That(provider.GetRequiredService<IXRegistryTransactionStore>(), Is.SameAs(store));
                Assert.That(provider.GetRequiredService<XRegistryTransactionalOptions>(), Is.SameAs(options));
                Assert.That(changed.Metadata.GetProperty("name").GetString(), Is.EqualTo("registered"));
                Assert.That(changed.Metadata.GetProperty("modifiedat").GetString(),
                    Is.EqualTo("2026-01-02T03:04:05.0000000Z"));
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Committed));
                Assert.That(outcome.Response?.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                Assert.That(persisted.IsNull, Is.False);
            });
        }

        [Test]
        public async Task RegistrationSuppliesProcessLocalStoreAndSystemClockByDefaultAsync()
        {
            var services = new ServiceCollection();
            services.AddXRegistryTransactions(XRegistryProviderCoverage.Options());
            using ServiceProvider provider = services.BuildServiceProvider();
            IXRegistryEndpoint endpoint = provider.GetRequiredService<IXRegistryEndpoint>();
            XRegistryEndpointDescription description = await endpoint.InspectAsync(
                XRegistryCallContext.Anonymous).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IXRegistryTransactionStore>(),
                    Is.InstanceOf<InMemoryXRegistryTransactionStore>());
                Assert.That(description.RegistryId, Is.EqualTo("coverage-registry"));
                Assert.That(description.SupportsOperationReplay, Is.False);
                Assert.That(description.SupportsPreparedMutations, Is.True);
            });
        }

        [Test]
        public async Task DeniedPolicyNeverLoadsDataForInspectionExecutionOrOutcomesAsync()
        {
            var store = new Mock<IXRegistryTransactionStore>(MockBehavior.Strict);
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options() with
            {
                AuthorizeAsync = (_, _, _) => new ValueTask<bool>(false)
            }, store.Object);
            XRegistryResponse denied = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await endpoint.InspectAsync(XRegistryProviderCoverage.Writer).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await endpoint.GetOperationOutcomeAsync("denied", XRegistryProviderCoverage.Writer)
                    .ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(403));
                Assert.That(denied.Error?.Code, Is.EqualTo("unauthorized"));
            });
            store.VerifyNoOtherCalls();
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("oversized")]
        public void InvalidOperationIdentityIsRejectedBeforeLoadingStorage(string identity)
        {
            var store = new Mock<IXRegistryTransactionStore>(MockBehavior.Strict);
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store.Object);
            string operationId = identity == "oversized" ? new string('x', 257) : identity;
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await endpoint.GetOperationOutcomeAsync(operationId, XRegistryProviderCoverage.Writer)
                    .ConfigureAwait(false));
            store.VerifyNoOtherCalls();
        }

        [Test]
        public async Task OutcomeIdentitiesSeparateAuthorityAndSubjectWithoutConcatenationCollisionsAsync()
        {
            using var endpoint = XRegistryProviderCoverage.Create();
            var firstCaller = new XRegistryCallContext("bc")
            {
                Authority = "a",
                IsAuthenticated = true,
                Roles = ["xregistry.write"]
            };
            var secondCaller = new XRegistryCallContext("c")
            {
                Authority = "ab",
                IsAuthenticated = true,
                Roles = ["xregistry.write"]
            };
            XRegistryResponse first = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"first"}""") with
                {
                    Context = firstCaller,
                    OperationId = "same"
                }).ConfigureAwait(false);
            XRegistryOperationOutcome absent = await endpoint.GetOperationOutcomeAsync("same", secondCaller)
                .ConfigureAwait(false);
            XRegistryResponse second = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"second"}""") with
                {
                    Context = secondCaller,
                    OperationId = "same"
                }).ConfigureAwait(false);
            XRegistryOperationOutcome retained = await endpoint.GetOperationOutcomeAsync("same", firstCaller)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(200));
                Assert.That(absent.State, Is.EqualTo(XRegistryOperationState.Unknown));
                Assert.That(absent.Response, Is.Null);
                Assert.That(second.Metadata.GetProperty("name").GetString(), Is.EqualTo("second"));
                Assert.That(second.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(2));
                Assert.That(retained.State, Is.EqualTo(XRegistryOperationState.Committed));
                Assert.That(retained.Response?.Metadata.GetProperty("name").GetString(), Is.EqualTo("first"));
                Assert.That(retained.Response?.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task PublicationFailureBeforeWritingBlocksOutstandingCandidatesUntilRecoveryAsync()
        {
            var durable = new InMemoryXRegistryTransactionStore();
            var failing = new Mock<IXRegistryTransactionStore>(MockBehavior.Strict);
            failing.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => durable.LoadAsync(token));
            failing.Setup(store => store.CommitAsync(
                It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Throws(new IOException("Injected publication failure."));
            using (var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options(), failing.Object, new XRegistryProviderCoverageTimeProvider()))
            {
                IXRegistryPreparedOperation first = await endpoint.PrepareAsync(
                    XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"unpublished"}"""))
                    .ConfigureAwait(false);
                await using var firstLifetime = first.ConfigureAwait(false);
                IXRegistryPreparedOperation second = await endpoint.PrepareAsync(
                    XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"also-unpublished"}"""))
                    .ConfigureAwait(false);
                await using var secondLifetime = second.ConfigureAwait(false);
                Assert.ThrowsAsync<IOException>(async () => await first.CommitAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<IOException>(async () => await second.CommitAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<IOException>(async () =>
                    await endpoint.InspectAsync(XRegistryProviderCoverage.Writer).ConfigureAwait(false));
            }
            ByteString saved = await durable.LoadAsync().ConfigureAwait(false);
            using var recovered = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), durable);
            XRegistryResponse root = await recovered.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            XRegistryResponse next = await recovered.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"recovered"}"""))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(saved.IsNull, Is.True);
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(next.Metadata.GetProperty("name").GetString(), Is.EqualTo("recovered"));
                Assert.That(next.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
            failing.Verify(store => store.CommitAsync(
                It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task TransientLoadFailureIsNotTreatedAsPristineStorageAsync()
        {
            var store = new Mock<IXRegistryTransactionStore>(MockBehavior.Strict);
            store.SetupSequence(value => value.LoadAsync(It.IsAny<CancellationToken>()))
                .Throws(new IOException("Injected load failure."))
                .Returns(new ValueTask<ByteString>(default(ByteString)));
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options(), store.Object, new XRegistryProviderCoverageTimeProvider());
            Assert.ThrowsAsync<IOException>(async () => await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false));
            XRegistryResponse retry = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(retry.StatusCode, Is.EqualTo(200));
                Assert.That(retry.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(retry.Metadata.GetProperty("createdat").GetString(),
                    Is.EqualTo("2026-01-02T03:04:05.0000000Z"));
            });
            store.Verify(value => value.LoadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            store.VerifyNoOtherCalls();
        }

        [TestCase(-1, 429)]
        [TestCase(0, 200)]
        public async Task PreparedByteLimitUsesExactEncodedCandidateSizeAsync(int adjustment, int expectedStatus)
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options("""{"groups":{}}""") with
                {
                    MaxPreparedBytes = Encoding.UTF8.GetByteCount(k_candidate) + adjustment
                }, store, new XRegistryProviderCoverageTimeProvider());
            IXRegistryPreparedOperation prepared = await endpoint.PrepareAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", """{"name":"candidate"}"""))
                .ConfigureAwait(false);
            await using var lifetime = prepared.ConfigureAwait(false);
            ByteString before = await store.LoadAsync().ConfigureAwait(false);
            XRegistryResponse committed = await prepared.CommitAsync().ConfigureAwait(false);
            ByteString after = await store.LoadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(prepared.Response.StatusCode, Is.EqualTo(expectedStatus));
                Assert.That(committed.StatusCode, Is.EqualTo(expectedStatus));
                Assert.That(before.IsNull, Is.True);
                if (expectedStatus == 200)
                {
                    Assert.That(Encoding.UTF8.GetString(after.ToArray()), Is.EqualTo(k_candidate));
                    Assert.That(committed.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
                }
                else
                {
                    Assert.That(committed.Error?.Code, Is.EqualTo("too_many_requests"));
                    Assert.That(after.IsNull, Is.True);
                }
            });
        }

        [Test]
        public async Task AggregatePreparedBytesAreReleasedExactlyOnceWhenAbortedAsync()
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options("""{"groups":{}}""") with
                {
                    MaxPreparedBytes = 2 * Encoding.UTF8.GetByteCount(k_candidate)
                }, store, new XRegistryProviderCoverageTimeProvider());
            XRegistryRequest request = XRegistryProviderCoverage.Request(
                XRegistryAction.Merge, "/", """{"name":"candidate"}""");
            IXRegistryPreparedOperation first = await endpoint.PrepareAsync(request).ConfigureAwait(false);
            await using var firstLifetime = first.ConfigureAwait(false);
            IXRegistryPreparedOperation second = await endpoint.PrepareAsync(request).ConfigureAwait(false);
            await using var secondLifetime = second.ConfigureAwait(false);
            IXRegistryPreparedOperation full = await endpoint.PrepareAsync(request).ConfigureAwait(false);
            await using var fullLifetime = full.ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
            IXRegistryPreparedOperation released = await endpoint.PrepareAsync(request).ConfigureAwait(false);
            await using var releasedLifetime = released.ConfigureAwait(false);
            IXRegistryPreparedOperation stillFull = await endpoint.PrepareAsync(request).ConfigureAwait(false);
            await using var stillFullLifetime = stillFull.ConfigureAwait(false);
            ByteString persisted = await store.LoadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Response.StatusCode, Is.EqualTo(200));
                Assert.That(second.Response.StatusCode, Is.EqualTo(200));
                Assert.That(full.Response.StatusCode, Is.EqualTo(429));
                Assert.That(released.Response.StatusCode, Is.EqualTo(200));
                Assert.That(stillFull.Response.StatusCode, Is.EqualTo(429));
                Assert.That(persisted.IsNull, Is.True);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RequestAndRetainedOutcomeSizeLimitsRejectWithoutPublicationAsync(bool oversizedRequest)
        {
            var store = new InMemoryXRegistryTransactionStore();
            using var endpoint = new XRegistryTransactionalEndpoint(
                XRegistryProviderCoverage.Options() with { MaxStateBytes = 1024 },
                store, new XRegistryProviderCoverageTimeProvider());
            string name = new('x', oversizedRequest ? 2048 : 512);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                XRegistryProviderCoverage.Request(XRegistryAction.Merge, "/", $$"""{"name":"{{name}}"}""") with
                {
                    OperationId = "bounded"
                }).ConfigureAwait(false);
            ByteString saved = await store.LoadAsync().ConfigureAwait(false);
            XRegistryOperationOutcome outcome = await endpoint.GetOperationOutcomeAsync(
                "bounded", XRegistryProviderCoverage.Writer).ConfigureAwait(false);
            await XRegistryProviderCoverage.AssertPristineAsync(endpoint, rejected, "bad_request", 413)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(saved.IsNull, Is.True);
                Assert.That(outcome.State, Is.EqualTo(XRegistryOperationState.Unknown));
                if (!oversizedRequest)
                {
                    Assert.That(rejected.Error?.Detail, Does.Contain("retained outcome quota"));
                }
            });
        }

        [TestCase("""{"format":2}""")]
        [TestCase("""{"format":-1}""")]
        [TestCase("""{"registryid":"different-registry"}""")]
        [TestCase("""{"generation":-1}""")]
        [TestCase("""{"operations":[]}""")]
        [TestCase("""{"entries":{}}""")]
        [TestCase("""{"entries":{"/":{"metadata":{"epoch":-1}}}}""")]
        [TestCase("""{"modelsource":{"groups":null}}""")]
        [TestCase("""{"entries":{"/":{"metadata":{"epoch":0},"document":"not base64!"}}}""")]
        public async Task InvalidPersistedGenerationIsRetainedRatherThanReinitializedAsync(string invalid)
        {
            JsonObject snapshot = JsonNode.Parse(k_candidate)!.AsObject();
            foreach (KeyValuePair<string, JsonNode?> property in JsonNode.Parse(invalid)!.AsObject())
            {
                snapshot[property.Key] = property.Value?.DeepClone();
            }
            ByteString fixture = ByteString.From(Encoding.UTF8.GetBytes(snapshot.ToJsonString()));
            var store = new InMemoryXRegistryTransactionStore();
            Assert.That(await store.CommitAsync(default, fixture).ConfigureAwait(false), Is.True);
            using var endpoint = new XRegistryTransactionalEndpoint(XRegistryProviderCoverage.Options(), store);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpoint.InspectAsync(XRegistryProviderCoverage.Writer).ConfigureAwait(false));
            ByteString retained = await store.LoadAsync().ConfigureAwait(false);
            Assert.That(retained.ToArray(), Is.EqualTo(fixture.ToArray()));
        }

        [TestCase("entities")]
        [TestCase("document")]
        [TestCase("state")]
        public async Task ReopeningWithSmallerQuotasCannotOverwriteThePersistedGenerationAsync(string bound)
        {
            var store = new InMemoryXRegistryTransactionStore();
            XRegistryTransactionalOptions options = XRegistryProviderCoverage.Options();
            using (var writer = new XRegistryTransactionalEndpoint(
                options, store, new XRegistryProviderCoverageTimeProvider()))
            {
                string name = bound == "state" ? new string('x', 2048) : "retained";
                XRegistryResponse created = await writer.ExecuteAsync(XRegistryProviderCoverage.Request(
                    XRegistryAction.Replace, "/groups/g/schemas/r",
                    $$"""{"versionid":"v1","name":"{{name}}","schemabase64":"AQID"}""")).ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(201));
            }
            ByteString before = await store.LoadAsync().ConfigureAwait(false);
            options = bound switch
            {
                "entities" => options with { MaxEntities = 3 },
                "document" => options with { MaxDocumentBytes = 2 },
                "state" => options with { MaxStateBytes = 1024 },
                _ => throw new ArgumentOutOfRangeException(nameof(bound))
            };
            using var reopened = new XRegistryTransactionalEndpoint(options, store);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await reopened.InspectAsync(XRegistryProviderCoverage.Writer).ConfigureAwait(false));
            ByteString after = await store.LoadAsync().ConfigureAwait(false);
            Assert.That(after.ToArray(), Is.EqualTo(before.ToArray()));
        }

        [Test]
        public void RejectionConstructorsPreservePublicErrorAndCause()
        {
            var cause = new IOException("storage diagnostic");
            var defaultError = new XRegistryRejectionException();
            var messageError = new XRegistryRejectionException("custom diagnostic");
            var causedError = new XRegistryRejectionException("caused diagnostic", cause);
            var codedError = new XRegistryRejectionException("not_found", "missing", 404);
            Assert.Multiple(() =>
            {
                Assert.That(defaultError.Code, Is.EqualTo("bad_request"));
                Assert.That(defaultError.StatusCode, Is.EqualTo(400));
                Assert.That(messageError.Message, Is.EqualTo("custom diagnostic"));
                Assert.That(messageError.Code, Is.EqualTo("bad_request"));
                Assert.That(causedError.InnerException, Is.SameAs(cause));
                Assert.That(causedError.Message, Is.EqualTo("caused diagnostic"));
                Assert.That(causedError.Code, Is.EqualTo("bad_request"));
                Assert.That(causedError.StatusCode, Is.EqualTo(400));
                Assert.That(codedError.Code, Is.EqualTo("not_found"));
                Assert.That(codedError.StatusCode, Is.EqualTo(404));
            });
        }

        private const string k_candidate =
            "{\"format\":1,\"generation\":1,\"registryid\":\"coverage-registry\","
            + "\"modelsource\":{\"groups\":{}},\"entries\":{\"/\":{\"metadata\":{"
            + "\"registryid\":\"coverage-registry\",\"epoch\":1,\"createdat\":\"2026-01-02T03:04:05.0000000Z\","
            + "\"modifiedat\":\"2026-01-02T03:04:05.0000000Z\",\"name\":\"candidate\"}}},\"operations\":{}}";
    }

    internal static class XRegistryProviderCoverage
    {
        internal static XRegistryCallContext Writer { get; } = new("coverage-writer")
        {
            Authority = "coverage-tests",
            IsAuthenticated = true,
            Roles = ["xregistry.write"]
        };

        internal static XRegistryTransactionalOptions Options(string? modelJson = null)
        {
            return new XRegistryTransactionalOptions
            {
                RegistryId = "coverage-registry",
                PublicRoot = new Uri("https://registry.example/registry/"),
                Model = Parse(modelJson ?? """
                    {"groups":{"groups":{"singular":"group","resources":{"schemas":{"singular":"schema"}}}}}
                    """)
            };
        }

        internal static XRegistryTransactionalEndpoint Create(string? modelJson = null)
        {
            return new XRegistryTransactionalEndpoint(
                Options(modelJson),
                new InMemoryXRegistryTransactionStore(),
                new XRegistryProviderCoverageTimeProvider());
        }

        internal static JsonElement Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        internal static XRegistryRequest Request(XRegistryAction action, string path, string? json = null)
        {
            return new XRegistryRequest(action, path)
            {
                Context = Writer,
                View = XRegistryView.Metadata,
                Metadata = json is null ? default : Parse(json)
            };
        }

        internal static async Task AssertPristineAsync(
            XRegistryTransactionalEndpoint endpoint, XRegistryResponse rejected, string code, int status = 400)
        {
            XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                .ConfigureAwait(false);
            XRegistryResponse groups = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(status));
                Assert.That(rejected.Error?.Code, Is.EqualTo(code));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
                Assert.That(root.Metadata.GetProperty("groupscount").GetInt32(), Is.Zero);
                Assert.That(groups.Metadata.GetRawText(), Is.EqualTo("{}"));
            });
        }
    }

    internal sealed class XRegistryProviderCoverageTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        }
    }
}
