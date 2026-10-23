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
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistryModelSynchronizationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompatibleModelsArePublishedBeforeDependentMetadataWithoutEchoAsync(bool loseResponse)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource",
                /*lang=json,strict*/ """{"groups":{"schemagroups":{"attributes":{"site":{"type":"string"}}}}}""",
                XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"site":"west"}""").ConfigureAwait(false);
            fixture.Http.FailAfterMutation = loseResponse;
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse group = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(2));
                Assert.That(fixture.Http.Mutations[0].Path, Is.EqualTo("/"));
                Assert.That(fixture.Http.Mutations[0].Metadata.TryGetProperty("modelsource", out _), Is.True);
                Assert.That(fixture.Http.Mutations[1].Path, Is.EqualTo(Group));
                Assert.That(group.Metadata.GetProperty("site").GetString(), Is.EqualTo("west"));
            });
            fixture.ResetCounts();
            XRegistrySyncReport repeated = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(repeated.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(repeated));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task ConcurrentModelChangesRetainBothDefinitionsForManualResolutionAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource",
                /*lang=json,strict*/ """{"groups":{"left":{"singular":"left"}}}""", XRegistryAction.Merge)
                .ConfigureAwait(false);
            await fixture.Http.ChangeAsync("/modelsource",
                /*lang=json,strict*/ """{"groups":{"right":{"singular":"right"}}}""", XRegistryAction.Merge)
                .ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(conflicts.ToList().Single().Path, Is.EqualTo("/modelsource"));
                Assert.That(conflicts[0].OpcUa!.Metadata.GetProperty("groups").TryGetProperty("left", out _), Is.True);
                Assert.That(conflicts[0].Http!.Metadata.GetProperty("groups").TryGetProperty("right", out _), Is.True);
            });
        }

        [Test]
        public async Task RetentionChangingModelsRequireAnExplicitMigrationRatherThanImplicitDataLossAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource",
                /*lang=json,strict*/ """{"groups":{"schemagroups":{"resources":{"schemas":{"maxversions":1}}}}}""",
                XRegistryAction.Merge).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(
                    report.Records.ToList().Any(record => record.Kind == XRegistrySyncRecordKind.Unsupported), Is.True);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }
    }
}
