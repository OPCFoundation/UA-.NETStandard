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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistrySyncRecoveryTests
    {
        [Test]
        public async Task IntentIsPersistedBeforeMutationAndOutcomeBeforeBaselineAsync()
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"new"}""").ConfigureAwait(false);
            int observedPrepared = 0;
            int observedOutcome = 0;
            fixture.Http.BeforeExecuteAsync = async (request, cancellationToken) =>
            {
                XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (request.IsMutation)
                {
                    observedPrepared++;
                    Assert.Multiple(() =>
                    {
                        Assert.That(state.Intents, Is.EqualTo(1));
                        Assert.That(state.Pending, Is.EqualTo(1));
                        Assert.That(state.Outcomes, Is.Zero);
                        Assert.That(state.Verified, Is.Zero);
                    });
                }
                else if (fixture.Http.Committed == 1 && state.Outcomes == 1 && state.Pending == 1)
                {
                    observedOutcome++;
                    Assert.That(state.Verified, Is.Zero);
                }
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus final = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.EqualTo(1), Details(report));
                Assert.That(observedPrepared, Is.EqualTo(1));
                Assert.That(observedOutcome, Is.GreaterThan(0));
                Assert.That(final.Verified, Is.EqualTo(1));
                Assert.That(final.Pending, Is.Zero);
            });
        }

        [Test]
        public async Task LostUpdateResponseUsesGuardedReadbackWithoutInventingAnOutcomeAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"new"}""").ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport restarted = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Pending, Is.Zero, Details(first));
                Assert.That(first.Converged, Is.EqualTo(2), "A recovered entity is counted once, not once per record.");
                Assert.That(restarted.Applied, Is.Zero, Details(restarted));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.EqualTo(1));
                Assert.That(fixture.Http.Acknowledged, Is.Zero);
                Assert.That(fixture.Http.JournalQueries, Is.Zero);
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.Zero, "Readback is not a synthetic server response.");
                Assert.That(state.Verified, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task LostRequestDoesNotCauseABlindPutRetryAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"new"}""").ConfigureAwait(false);
            fixture.Http.FailBeforeMutation = true;
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport second = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Pending, Is.EqualTo(1), Details(first));
                Assert.That(second.Pending, Is.EqualTo(1), Details(second));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.Zero);
                Assert.That(second.ExitCode, Is.Not.Zero);
            });
            await fixture.Http.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"new"}""").ConfigureAwait(false);
            XRegistrySyncReport externallyConverged = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(externallyConverged.Pending, Is.Zero, Details(externallyConverged));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.Zero, "Current equality does not attribute a remote commit.");
                Assert.That(state.Outcomes, Is.Zero);
            });
        }

        [TestCase("io")]
        [TestCase("derived-io")]
        [TestCase("http")]
        [TestCase("service")]
        [TestCase("json")]
        [TestCase("authorization")]
        public async Task TransportFailureClassesRemainPendingWithoutRetryAsync(string failure)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"new"}""").ConfigureAwait(false);
            fixture.Http.BeforeExecuteAsync = (request, _) =>
            {
                if (!request.IsMutation)
                {
                    return default;
                }
                throw failure switch
                {
                    "io" => new IOException("Fixture I/O failure."),
                    "derived-io" => new EndOfStreamException("Fixture truncated response."),
                    "http" => new HttpRequestException("Fixture HTTP failure."),
                    "service" => new ServiceResultException(StatusCodes.BadCommunicationError),
                    "json" => new JsonException("Fixture response decoding failure."),
                    _ => new UnauthorizedAccessException("Fixture authorization loss.")
                };
            };
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport restart = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Pending, Is.EqualTo(1), Details(first));
                Assert.That(restart.Pending, Is.EqualTo(1), Details(restart));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.Zero);
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.Zero);
            });
        }

        [Test]
        public async Task UnknownCreateRemainsPendingAfterRestartAndResolutionRequestAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"created"}""").ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            await fixture.State.ResolveConflictAsync(conflicts[0].Id, XRegistrySyncConflictPolicy.PreferOpcUa)
                .ConfigureAwait(false);
            XRegistrySyncReport restart = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Pending, Is.EqualTo(1), Details(first));
                Assert.That(restart.Pending, Is.EqualTo(1), Details(restart));
                Assert.That(actual.StatusCode, Is.EqualTo(200));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.EqualTo(1));
                Assert.That(fixture.Http.JournalQueries, Is.Zero);
                Assert.That(state.Baselines, Is.EqualTo(1),
                    "A matching created entity alone is not operation evidence.");
                Assert.That(state.Outcomes, Is.Zero);
            });
        }

        [Test]
        public async Task JournalRecoversLostCreateWithoutReplayAsync()
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"created"}""").ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport restart = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            int journal = await fixture.Http.JournalCountAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(first));
                Assert.That(restart.Applied, Is.Zero);
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.EqualTo(1));
                Assert.That(fixture.Http.Acknowledged, Is.Zero);
                Assert.That(fixture.Http.JournalQueries, Is.EqualTo(1));
                Assert.That(journal, Is.EqualTo(1));
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.EqualTo(1));
                Assert.That(state.Verified, Is.EqualTo(1));
                Assert.That(state.Pending, Is.Zero);
            });
        }

        [TestCase(0, 0, 1)]
        [TestCase(1, 1, 0)]
        public async Task RestartAtPersistedIntentOrOutcomeNeverRepeatsTheMutationAsync(
            int failAtIntentState, int expectedWrites, int expectedPending)
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"new"}""").ConfigureAwait(false);
            await using var crash = new SyncCommitFaultStore(fixture.Store, failAtIntentState);
            XRegistrySyncReport failed = await fixture.Engine(crash).RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport recovered = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failed.Status, Is.EqualTo(XRegistrySyncStatus.Failed), Details(failed));
                Assert.That(crash.Failed, Is.True);
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(expectedWrites));
                Assert.That(fixture.Http.Committed, Is.EqualTo(expectedWrites));
                Assert.That(recovered.Pending, Is.EqualTo(expectedPending), Details(recovered));
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.EqualTo(expectedWrites));
                Assert.That(state.Verified, Is.EqualTo(expectedWrites));
            });
        }

        [Test]
        public async Task NewerSourceAfterCommitIsHeldWithoutAdvancingTheBaselineAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"outgoing"}""").ConfigureAwait(false);
            fixture.Http.AfterExecuteAsync = async (request, _, _) =>
            {
                if (request.IsMutation)
                {
                    fixture.Http.AfterExecuteAsync = null;
                    await fixture.Native.ChangeAsync(Group,
                        /*lang=json,strict*/ """{"name":"newer-source"}""").ConfigureAwait(false);
                }
            };
            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus held = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Conflicts, Is.EqualTo(1), Details(first));
                Assert.That(first.Pending, Is.Zero, "A known outcome with a newer edit is conflicted, not replayable.");
                Assert.That(held.Verified, Is.Zero);
                Assert.That(conflicts[0].Reason, Is.EqualTo("verification_changed"));
                Assert.That(conflicts[0].OpcUa!.Metadata.GetProperty("name").GetString(), Is.EqualTo("newer-source"));
                Assert.That(conflicts[0].Http!.Metadata.GetProperty("name").GetString(), Is.EqualTo("outgoing"));
            });
            await fixture.State.ResolveConflictAsync(conflicts[0].Id, XRegistrySyncConflictPolicy.PreferOpcUa)
                .ConfigureAwait(false);
            XRegistrySyncReport resolved = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resolved.Applied, Is.EqualTo(1), Details(resolved));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(2));
                Assert.That(actual.Metadata.GetProperty("name").GetString(), Is.EqualTo("newer-source"));
            });
        }

        [Test]
        public async Task JournalConfirmsLostRejectionWithoutRetryAsync()
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"outgoing"}""").ConfigureAwait(false);
            fixture.Http.BeforeExecuteAsync = async (request, _) =>
            {
                if (request.IsMutation)
                {
                    fixture.Http.BeforeExecuteAsync = null;
                    await fixture.Http.ChangeAsync(Group,
                        /*lang=json,strict*/ """{"name":"concurrent"}""").ConfigureAwait(false);
                    fixture.Http.FailAfterMutation = true;
                }
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Pending, Is.Zero, Details(report));
                Assert.That(report.Conflicts, Is.EqualTo(1));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.Zero);
                Assert.That(fixture.Http.JournalQueries, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.EqualTo(1));
                Assert.That(state.Verified, Is.Zero);
            });
        }

        [Test]
        public async Task LostDeleteResponseConfirmsAbsenceWithoutRetryAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Pending, Is.Zero, Details(report));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.EqualTo(1));
                Assert.That(fixture.Http.Acknowledged, Is.Zero);
                Assert.That(state.Outcomes, Is.Zero);
                Assert.That(state.Tombstones, Is.EqualTo(1));
                Assert.That(state.Verified, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task PollingDeadlineBoundsAnEndpointThatIgnoresCancellationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var never = new TaskCompletionSource<XRegistryEndpointDescription>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Native.InspectOverrideAsync = _ =>
            {
                started.TrySetResult(true);
                return new ValueTask<XRegistryEndpointDescription>(never.Task);
            };
            Task<XRegistrySyncReport> operation = fixture.Engine().RunOnceAsync().AsTask();
            await started.Task.ConfigureAwait(false);
            fixture.Clock.Advance(fixture.Options.RequestTimeout);
            XRegistrySyncReport result = await operation.ConfigureAwait(false);
            never.TrySetResult(new XRegistryEndpointDescription("late"));
            XRegistryResponse survivor = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(XRegistrySyncStatus.Incomplete), Details(result));
                Assert.That(result.InventoryComplete, Is.False);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(survivor.StatusCode, Is.EqualTo(200));
            });
        }
    }

    internal sealed class SyncCommitFaultStore(IXRegistrySyncStateStore inner, int stateToFail)
        : IXRegistrySyncStateStore
    {
        public bool Failed { get; private set; }

        public async ValueTask<IXRegistrySyncStateSession> OpenAsync(
            bool readOnly = false,
            CancellationToken cancellationToken = default)
        {
            IXRegistrySyncStateSession session = await inner.OpenAsync(readOnly, cancellationToken)
                .ConfigureAwait(false);
            return new FaultSession(this, session);
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }

        private readonly int m_stateToFail = stateToFail;

        private sealed class FaultSession(SyncCommitFaultStore owner, IXRegistrySyncStateSession session)
            : IXRegistrySyncStateSession
        {
            public bool IsReadOnly => session.IsReadOnly;

            public ValueTask<ByteString> ReadAsync(CancellationToken cancellationToken = default)
            {
                return session.ReadAsync(cancellationToken);
            }

            public async ValueTask CommitAsync(ByteString state, CancellationToken cancellationToken = default)
            {
                await session.CommitAsync(state, cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(state.Memory);
                if (!owner.Failed &&
                    document.RootElement.GetProperty("payload").GetProperty("intents")
                        .EnumerateArray().Any(intent => intent.GetProperty("state").GetInt32() == owner.m_stateToFail))
                {
                    owner.Failed = true;
                    throw new IOException("Fixture process loss after a durable state commit.");
                }
            }

            public ValueTask DisposeAsync()
            {
                return session.DisposeAsync();
            }
        }
    }
}
