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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistrySyncInventoryTests
    {
        [Test]
        public async Task FullPaginationVisitsEveryIdentityAndConfirmsMembershipAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"same"}""").ConfigureAwait(false);
            await fixture.SeedBothAsync("/schemagroups/second",
                /*lang=json,strict*/ """{"name":"second"}""").ConfigureAwait(false);
            int lastPages = 0;
            fixture.Native.TransformResponse = (request, response) =>
            {
                if (request.Path != "/schemagroups")
                {
                    return response;
                }
                bool last = request.Parameters.Count != 0;
                if (last)
                {
                    lastPages++;
                }
                string id = last ? "second" : "g";
                var page = new JsonObject { [id] = JsonNode.Parse(response.Metadata.GetProperty(id).GetRawText()) };
                return response with
                {
                    Metadata = Json(page.ToJsonString()),
                    Links = last ? [] : [new XRegistryLink("next", "/schemagroups?cursor=second")]
                };
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.True, Details(report));
                Assert.That(state.Baselines, Is.EqualTo(3));
                Assert.That(lastPages, Is.EqualTo(2), "A complete scan must also confirm the full paged membership.");
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [TestCase("/othergroups?cursor=repeat")]
        [TestCase("/othergroups?filter=hidden")]
        [TestCase("/schemagroups?cursor=elsewhere")]
        [TestCase("https://elsewhere.example/othergroups?cursor=external")]
        public async Task UnqualifiedOrCyclicPaginationNeverAuthorizesDeletionAsync(string next)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            fixture.Native.TransformResponse = (request, response) => request.Path != "/othergroups" ? response :
                response with { Links = [new XRegistryLink("next", next)] };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse survivor = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.ExitCode, Is.Not.Zero);
                Assert.That(report.Deleted, Is.Zero);
                Assert.That(survivor.StatusCode, Is.EqualTo(200));
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task MissingDocumentIsNotAnEmptyDocumentAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Resource,
                /*lang=json,strict*/ """{"versionid":"v1","schemabase64":""}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            fixture.Native.TransformResponse = (request, response) =>
                request.Path == VersionPath && request.View == XRegistryView.Default
                    ? response with { Document = default }
                    : response;
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False, Details(report));
                Assert.That(report.Failures, Is.GreaterThan(0));
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task MetadataDocumentRaceMakesTheInventoryIncompleteAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Resource,
                /*lang=json,strict*/ """{"versionid":"v1","name":"old","schemabase64":"AQ=="}""")
                .ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            fixture.Native.AfterExecuteAsync = async (request, _, _) =>
            {
                if (request.Path == VersionPath && request.View == XRegistryView.Default)
                {
                    fixture.Native.AfterExecuteAsync = null;
                    await fixture.Native.ChangeAsync(VersionPath,
                        /*lang=json,strict*/ """{"name":"new","schemabase64":"Ag=="}""")
                        .ConfigureAwait(false);
                }
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse old = await fixture.Http.ReadAsync(VersionPath, XRegistryView.Default)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False, Details(report));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(old.Document.ToArray(), Is.EqualTo(new byte[] { 1 }));
            });
        }

        [Test]
        public async Task ModelChangeDuringInventoryPreventsAbsenceBasedOperationsAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            fixture.Native.BeforeExecuteAsync = (request, _) =>
            {
                if (request.Path == "/othergroups")
                {
                    JsonObject changed = JsonNode.Parse(DefaultModel)!.AsObject();
                    changed["attributes"]!["newattribute"] = new JsonObject { ["type"] = "string" };
                    fixture.Native.ModelOverride = Json(changed.ToJsonString());
                }
                return default;
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse survivor = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False, Details(report));
                Assert.That(report.Deleted, Is.Zero);
                Assert.That(survivor.StatusCode, Is.EqualTo(200));
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task WideEpochsRemainLocalAndAreNotTruncatedAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
            await fixture.Native.SetEpochAsync(Group, "184467440737095516160").ConfigureAwait(false);
            await fixture.Http.SetEpochAsync(Group, "4294967296").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"changed"}""").ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse target = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.EqualTo(1), Details(report));
                Assert.That(fixture.Http.Mutations.Single().Metadata.GetProperty("epoch").GetRawText(),
                    Is.EqualTo("4294967296"));
                Assert.That(target.Metadata.GetProperty("epoch").GetRawText(), Is.EqualTo("4294967297"));
                Assert.That(fixture.Native.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task EscapedIdsArePreservedWithoutSourceIdentityNormalizationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            const string path = "/schemagroups/g%2B%25/schemas/r%2B%25/versions/v%2B%25";
            await fixture.Native.ChangeAsync("/schemagroups/g%2B%25/schemas/r%2B%25",
                                     /*lang=json,strict*/
                                     """{"versionid":"v+%","name":"exact-id","schemabase64":"AQ=="}""").ConfigureAwait(
                                         false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(path).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(actual.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("v+%"));
                Assert.That(actual.Metadata.GetProperty("schemaid").GetString(), Is.EqualTo("r+%"));
                Assert.That(actual.Metadata.GetProperty("name").GetString(), Is.EqualTo("exact-id"));
            });
        }

        [Test]
        public async Task GroupNamedMetaDoesNotBlockUnrelatedGroupsWhenItsCreateIsPendingAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync("/schemagroups/meta",
                /*lang=json,strict*/ """{"name":"held"}""").ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/schemagroups/zother", /*lang=json,strict*/ """{"name":"independent"}""")
                .ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse independent = await fixture.Http.ReadAsync("/schemagroups/zother").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Pending, Is.EqualTo(1), Details(report));
                Assert.That(report.Applied, Is.EqualTo(1));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(2));
                Assert.That(independent.Metadata.GetProperty("name").GetString(), Is.EqualTo("independent"));
            });
        }

        [Test]
        public async Task OperationLimitIsExactAndFurtherPollingMakesProgressAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync("/schemagroups/a", "{}").ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/schemagroups/b", "{}").ConfigureAwait(false);
            fixture.Options = fixture.Options with { MaximumOperations = 1 };
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Applied, Is.EqualTo(1), Details(first));
                Assert.That(first.Status, Is.EqualTo(XRegistrySyncStatus.Incomplete));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
            });
            XRegistrySyncReport second = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport third = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(second.Applied, Is.EqualTo(1), Details(second));
                Assert.That(second.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded));
                Assert.That(third.Applied, Is.Zero);
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public async Task KnownCreationWithUnverifiedAssignedIdentityStaysPendingAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Resource,
                /*lang=json,strict*/ """{"versionid":"v1","schemabase64":"AQ=="}""")
                .ConfigureAwait(false);
            fixture.Http.AfterExecuteAsync = async (request, _, _) =>
            {
                if (request.IsMutation)
                {
                    fixture.Http.AfterExecuteAsync = null;
                    await fixture.Http.DeleteAsync(Resource).ConfigureAwait(false);
                    await fixture.Http.ChangeAsync(Resource,
                        /*lang=json,strict*/ """{"versionid":"assigned","schemabase64":"AQ=="}""")
                        .ConfigureAwait(false);
                }
            };
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport restart = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Pending, Is.EqualTo(1), Details(first));
                Assert.That(restart.Pending, Is.EqualTo(1), Details(restart));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(state.Outcomes, Is.EqualTo(1));
                Assert.That(state.Verified, Is.Zero);
            });
        }
    }
}
