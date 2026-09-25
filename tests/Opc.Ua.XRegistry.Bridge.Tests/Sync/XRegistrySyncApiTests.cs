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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistryReconciliationTests;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistrySyncApiTests
    {
        [Test]
        public async Task DependencyInjectionUsesTheSuppliedEndpointsStoreAndClockAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"native"}""").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"http"}""").ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddSingleton(fixture.Telemetry);
            services.AddSingleton<TimeProvider>(fixture.Clock);
            services.AddXRegistrySynchronization(fixture.Options, fixture.Native, fixture.Http, fixture.Store);
            await using ServiceProvider provider = services.BuildServiceProvider();
            XRegistrySynchronizer engine = provider.GetRequiredService<XRegistrySynchronizer>();
            XRegistrySyncStateManager manager = provider.GetRequiredService<XRegistrySyncStateManager>();
            await engine.RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await manager.ListConflictsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IXRegistrySyncStateStore>(), Is.SameAs(fixture.Store));
                Assert.That(conflicts.Count, Is.EqualTo(1));
                Assert.That(conflicts[0].ObservedAt, Is.EqualTo(fixture.Clock.GetUtcNow()));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
            });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void StableConfiguredIdentitiesAreRequired(string? invalid)
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => new XRegistrySyncOptions(invalid!, "native", "http"));
                Assert.Throws<ArgumentException>(() => new XRegistrySyncOptions("job", invalid!, "http"));
                Assert.Throws<ArgumentException>(() => new XRegistrySyncOptions("job", "native", invalid!));
            });
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public async Task InvalidLimitsAndPolicyAreRejectedBeforeOpeningStateAsync(int partition)
        {
            await using var fixture = new XRegistrySyncFixture();
            XRegistrySyncOptions invalid = partition switch
            {
                0 => fixture.Options with { MaximumEntities = 0 },
                1 => fixture.Options with { MaximumPages = 0 },
                2 => fixture.Options with { MaximumOperations = 0 },
                3 => fixture.Options with { MaximumDocumentBytes = -1 },
                4 => fixture.Options with { MaximumInventoryBytes = 0 },
                5 => fixture.Options with { MaximumStateBytes = 0 },
                6 => fixture.Options with { MaximumJsonDepth = 257 },
                7 => fixture.Options with { RequestTimeout = TimeSpan.Zero },
                _ => fixture.Options with { ConflictPolicy = (XRegistrySyncConflictPolicy)99 }
            };
            Assert.That(() => fixture.Engine(options: invalid), Throws.InstanceOf<ArgumentException>());
            ByteString state = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(state.IsNull, Is.True);
                Assert.That(fixture.Native.Inspections + fixture.Http.Inspections, Is.Zero);
            });
        }

        [Test]
        public async Task ReconnectedSessionIdsDoNotChangeAuthenticatedScopeAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            fixture.Options = fixture.Options with
            {
                OpcUaContext = Writer with { SessionId = "new-native-session" },
                HttpContext = Writer with { SessionId = "new-http-session" }
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
        }

        [Test]
        public async Task OfflineResolutionRejectsUnknownOrInvalidDecisionsWithoutStateChangesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            ByteString before = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await fixture.State.ResolveConflictAsync("missing", XRegistrySyncConflictPolicy.Manual)
                    .ConfigureAwait(false));
            Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await fixture.State.ResolveConflictAsync("missing", XRegistrySyncConflictPolicy.PreferHttp)
                    .ConfigureAwait(false));
            ByteString after = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(after.ToArray(), Is.EqualTo(before.ToArray()));
                Assert.That(fixture.Native.Inspections + fixture.Http.Inspections, Is.Zero);
            });
        }

        [Test]
        public async Task CallerCancellationPreservesTheDurableIntentWithoutRetryAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"new"}""").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            fixture.Http.BeforeExecuteAsync = (request, token) =>
            {
                if (request.IsMutation)
                {
                    fixture.Http.BeforeExecuteAsync = null;
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                return default;
            };
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
                await fixture.Engine().RunOnceAsync(cancellationToken: cancellation.Token).ConfigureAwait(false));
            XRegistrySyncReport restart = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(restart.Pending, Is.EqualTo(1), Details(restart));
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.Zero);
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.Zero);
            });
        }
    }
}
