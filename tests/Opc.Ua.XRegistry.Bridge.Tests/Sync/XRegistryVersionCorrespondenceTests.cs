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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistryVersionCorrespondenceTests
    {
        [Test]
        public async Task DifferentEndpointVersionIdsShareOneBaselineAndKeepTheirActualWriteAddressesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(
                VersionPath, /*lang=json,strict*/ """{"name":"same","schemabase64":"YQ=="}""")
                .ConfigureAwait(false);
            const string remote = Resource + "/versions/http-assigned";
            await fixture.Http.ChangeAsync(remote, /*lang=json,strict*/ """{"name":"same","schemabase64":"YQ=="}""")
                .ConfigureAwait(false);
            fixture.Options = fixture.Options with
            {
                VersionCorrespondences = [new XRegistryVersionCorrespondence(VersionPath, VersionPath, remote)]
            };
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(
                VersionPath, /*lang=json,strict*/ """{"name":"changed"}""", XRegistryAction.Merge)
                .ConfigureAwait(false);
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(remote).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(first));
                Assert.That(fixture.Http.Mutations.Single().Path, Is.EqualTo(remote));
                Assert.That(actual.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("http-assigned"));
                Assert.That(actual.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("http-assigned"));
                Assert.That(actual.Metadata.GetProperty("name").GetString(), Is.EqualTo("changed"));
            });
            fixture.ResetCounts();
            XRegistrySyncReport restarted = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(restarted.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(restarted));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task AssignedDestinationIdentityIsPersistedBeforeCommitAndSurvivesTheNextPassAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            const string model = /*lang=json,strict*/ """
                {"groups":{"schemagroups":{"singular":"schemagroup","resources":{"schemas":{"singular":"schema"}}}}}
                """;
            using var source = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = "source",
                Model = Json(model)
            }, new InMemoryXRegistryTransactionStore(), fixture.Clock);
            using var target = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = "target",
                Model = Json(model.Replace("\"singular\":\"schema\"", "\"singular\":\"schema\",\"setversionid\":false",
                    StringComparison.Ordinal))
            }, new InMemoryXRegistryTransactionStore(), fixture.Clock);
            const string original = Resource + "/versions/source-assigned";
            XRegistryResponse seeded = await source.ExecuteAsync(new XRegistryRequest(XRegistryAction.Replace, original)
            {
                Context = Writer,
                View = XRegistryView.Metadata,
                Metadata = Json(/*lang=json,strict*/ """{"schema":"retained","name":"original"}""")
            }).ConfigureAwait(false);
            Assert.That(seeded.StatusCode, Is.EqualTo(201), seeded.Error?.Detail);
            _ = await source.ExecuteAsync(new XRegistryRequest(XRegistryAction.Merge, "/modelsource")
            {
                Context = Writer,
                Metadata = Json(/*lang=json,strict*/
                    """{"groups":{"schemagroups":{"resources":{"schemas":{"setversionid":false}}}}}""")
            }).ConfigureAwait(false);
            var engine = new XRegistrySynchronizer(
                source, target, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport copied = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual =
                await target.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, Resource + "/versions/1")
                { Context = Writer, View = XRegistryView.Metadata }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(copied.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(copied));
                Assert.That(actual.StatusCode, Is.EqualTo(200), actual.Error?.Detail);
                Assert.That(actual.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("1"));
                Assert.That(actual.Metadata.GetProperty("name").GetString(), Is.EqualTo("original"));
            });
            XRegistrySyncReport next = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(next.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(next));
                Assert.That(next.Applied, Is.Zero);
            });
            _ = await source.ExecuteAsync(new XRegistryRequest(XRegistryAction.Merge, original)
            {
                Context = Writer,
                Metadata = Json(/*lang=json,strict*/ """{"name":"updated"}""")
            }).ConfigureAwait(false);
            XRegistrySyncReport updated = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse retained =
                await target.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, Resource + "/versions/1")
                { Context = Writer, View = XRegistryView.Metadata }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(updated.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(updated));
                Assert.That(retained.Metadata.GetProperty("name").GetString(), Is.EqualTo("updated"));
            });
        }

        [Test]
        public void TypedVersionReferencesAreMappedButOpaqueDomainStringsAreNot()
        {
            const string remote = Resource + "/versions/http-id";
            var mappings = new Dictionary<string, XRegistryVersionCorrespondence>(StringComparer.Ordinal)
            {
                [VersionPath] = new(VersionPath, VersionPath, remote)
            };
            var mapper = new XRegistrySyncCorrespondence(mappings, XRegistrySyncSide.Http);
            mapper.SetDescription(new XRegistryEndpointDescription("http")
            {
                PublicRoot = new Uri("https://http.example/"),
                Model = Json(/*lang=json,strict*/ """
                    {"groups":{"schemagroups":{"singular":"schemagroup","attributes":{
                        "reference":{"type":"xid"},"opaque":{"type":"string"},
                        "settings":{"type":"object","attributes":{"ancestorid":{"type":"xid"}}}
                    },"resources":{"schemas":{"singular":"schema"}}}}}
                    """)
            });
            System.Text.Json.JsonElement value = mapper.Metadata(Group, Json(
                "{\"reference\":\"" +
                remote +
                "\",\"opaque\":\"" +
                remote +
                "\",\"settings\":{\"ancestorid\":\"" +
                remote +
                "\"}}"),
                false);
            Assert.Multiple(() =>
            {
                Assert.That(value.GetProperty("reference").GetString(), Is.EqualTo(VersionPath));
                Assert.That(value.GetProperty("opaque").GetString(), Is.EqualTo(remote));
                Assert.That(
                    value.GetProperty("settings").GetProperty("ancestorid").GetString(), Is.EqualTo(VersionPath));
            });
        }

        [Test]
        public async Task LostAssignedCommitResponseRetainsIdentityEvidenceWithoutRepeatingCreationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            const string model = """{"groups":{"schemagroups":{"singular":"schemagroup","resources":""" +
                """{"schemas":{"singular":"schema","setversionid":false}}}}}""";
            using var source = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            { RegistryId = "source", Model = Json(model) }, new InMemoryXRegistryTransactionStore(), fixture.Clock);
            using var target = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            { RegistryId = "target", Model = Json(model) }, new InMemoryXRegistryTransactionStore(), fixture.Clock);
            _ = await source.ExecuteAsync(new XRegistryRequest(XRegistryAction.Create, Resource)
            {
                Context = Writer,
                View = XRegistryView.Metadata,
                Metadata = Json(/*lang=json,strict*/ """{"schema":"payload"}""")
            }).ConfigureAwait(false);
            var lost = new LostAssignedResponseEndpoint(target);
            var engine = new XRegistrySynchronizer(
                source, lost, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);
            XRegistrySyncReport first = await engine.RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport restarted = await engine.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Pending, Is.EqualTo(1), Details(first));
                Assert.That(restarted.Pending, Is.EqualTo(1), Details(restarted));
                Assert.That(lost.Commits, Is.EqualTo(1));
            });
        }

        [TestCase("/schemagroups/g/schemas/other/versions/v1")]
        [TestCase("/schemagroups/g/schemas/r/meta")]
        [TestCase("/schemagroups/g/other/r/versions/v1")]
        public void CorrespondenceCannotMoveVersionsAcrossTheirResourceOrType(string path)
        {
            Assert.Throws<ArgumentException>(() => new XRegistryVersionCorrespondence(VersionPath, VersionPath, path));
        }

        private sealed class LostAssignedResponseEndpoint(
            XRegistryTransactionalEndpoint endpoint) : IXRegistryPreparedEndpoint
        {
            public int Commits { get; private set; }

            public ValueTask<XRegistryEndpointDescription> InspectAsync(XRegistryCallContext context,
                CancellationToken cancellationToken = default)
            {
                return endpoint.InspectAsync(context, cancellationToken);
            }

            public ValueTask<XRegistryResponse> ExecuteAsync(XRegistryRequest request,
                CancellationToken cancellationToken = default)
            {
                return endpoint.ExecuteAsync(request, cancellationToken);
            }

            public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(XRegistryRequest request,
                CancellationToken cancellationToken = default)
            {
                return new LostResponse(this,
                    await endpoint.PrepareAsync(request, cancellationToken).ConfigureAwait(false));
            }

            private sealed class LostResponse(LostAssignedResponseEndpoint owner, IXRegistryPreparedOperation operation)
                : IXRegistryPreparedOperation
            {
                public XRegistryResponse Response => operation.Response;

                public async ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
                {
                    _ = await operation.CommitAsync(cancellationToken).ConfigureAwait(false);
                    owner.Commits++;
                    throw new TimeoutException("Injected lost response after assigned creation.");
                }

                public ValueTask DisposeAsync()
                {
                    return operation.DisposeAsync();
                }
            }
        }
    }
}
