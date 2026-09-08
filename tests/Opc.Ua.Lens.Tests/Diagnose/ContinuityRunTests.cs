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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Diagnostics;
using UaLens.Plugins.Continuity;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class ContinuityRunTests
{
    [Test]
    public async Task MissingPrerequisiteDoesNotClaimAStartedScenario()
    {
        var backend = new ContinuityTestBackend
        {
            Setup = new(ContinuityAvailability.RequiresConfiguration, "Configure same-user auxiliary sessions.")
        };
        var run = new ContinuityRun(backend, new ContinuityTimeline());
        await using (run.ConfigureAwait(false))
        {
            await run.StartAsync(Configuration(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(run.Phase, Is.EqualTo(ContinuityRunPhase.RequiresSetup));
            Assert.That(backend.Starts, Is.Zero);
            Assert.That(run.Timeline.Snapshot().Entries.ToList().Any(row => row.Kind == ContinuityEvidenceKind.Started),
                Is.False);
            Assert.That(run.Status, Does.Contain("Configure"));
        }
    }

    [Test]
    public async Task StopCancelsAndJoinsAnInFlightStartBeforeReleasingResources()
    {
        var entered = NewSignal();
        var canceled = NewSignal();
        var release = NewSignal();
        var backend = new ContinuityTestBackend
        {
            Start = async ct =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                finally
                {
                    canceled.TrySetResult();
                    await release.Task.ConfigureAwait(false);
                }
            }
        };
        var run = new ContinuityRun(backend, new ContinuityTimeline());
        Task starting = run.StartAsync(Configuration(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Task stopping = run.StopAsync();
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.That(backend.Stops, Is.Zero, "Stop must join Start before resource cleanup.");
        Assert.That(stopping.IsCompleted, Is.False);
        Assert.That(run.StopAsync(), Is.SameAs(stopping));
        release.TrySetResult();
        await Assert.ThatAsync(() => starting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        await stopping.ConfigureAwait(false);
        await run.DisposeAsync().ConfigureAwait(false);
        await run.DisposeAsync().ConfigureAwait(false);

        Assert.That(backend.Stops, Is.EqualTo(1));
        Assert.That(backend.Disposals, Is.EqualTo(1));
        Assert.That(run.Phase, Is.EqualTo(ContinuityRunPhase.Stopped));
        Assert.That(run.Timeline.Snapshot().Entries.ToList().Any(row => row.Kind == ContinuityEvidenceKind.Started),
            Is.False);
    }

    [Test]
    public async Task StopCancelsAScenarioStepAndDoesNotRepeatIt()
    {
        var entered = NewSignal();
        var backend = new ContinuityTestBackend
        {
            Step = async ct =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return new(false, "Unreachable");
            }
        };
        var run = new ContinuityRun(backend, new ContinuityTimeline());
        await using (run.ConfigureAwait(false))
        {
            await run.StartAsync(Configuration(), CancellationToken.None).ConfigureAwait(false);
            Task step = run.StepAsync(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await run.StopAsync().ConfigureAwait(false);
            await Assert.ThatAsync(() => step, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(backend.Steps, Is.EqualTo(1));
            Assert.That(backend.Stops, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ACompletedExperimentKeepsObservingButCannotAllocateRepeatedRetainedSources()
    {
        var backend = new ContinuityTestBackend();
        var run = new ContinuityRun(backend, new ContinuityTimeline());
        await using (run.ConfigureAwait(false))
        {
            await run.StartAsync(Configuration(), CancellationToken.None).ConfigureAwait(false);
            await run.StepAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(run.Phase, Is.EqualTo(ContinuityRunPhase.ObservingAfterStep));
            await Assert.ThatAsync(() => run.StepAsync(CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(backend.Steps, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task CleanupFailureRemainsVisibleAndDisposalStillReleasesTheBackend()
    {
        var backend = new ContinuityTestBackend
        {
            Stop = _ => Task.FromException(new ServiceResultException(StatusCodes.BadUserAccessDenied))
        };
        var run = new ContinuityRun(backend, new ContinuityTimeline());
        await run.StartAsync(Configuration(), CancellationToken.None).ConfigureAwait(false);

        await Assert.ThatAsync(() => run.DisposeAsync().AsTask(),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(backend.Disposals, Is.EqualTo(1));
        Assert.That(run.Phase, Is.EqualTo(ContinuityRunPhase.Failed));
        Assert.That(run.Status, Does.Contain("BadUserAccessDenied"));
        Assert.That(run.Timeline.Snapshot().Entries[^1].Kind, Is.EqualTo(ContinuityEvidenceKind.CleanupUncertain));
    }

    [Test]
    public async Task DesktopStopIsAvailableWhileStartIsStillPending()
    {
        var entered = NewSignal();
        var backend = new ContinuityTestBackend
        {
            Start = async ct =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
        };
        var plugin = new ContinuityPlugin(backend, () => null, () => null);
        await using (plugin.ConfigureAwait(false))
        {
            var availability = new List<bool>();
            plugin.StopCommand.CanExecuteChanged += (_, _) => availability.Add(plugin.StopCommand.CanExecute(null));
            Task start = plugin.StartCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(plugin.StopCommand.CanExecute(null), Is.True);
            Assert.That(availability, Does.Contain(true), "The desktop must be notified while Start is pending.");
            await plugin.StopCommand.ExecuteAsync(null).ConfigureAwait(false);
            await start.ConfigureAwait(false);
            Assert.That(backend.Stops, Is.EqualTo(1));
        }
    }

    private static ContinuityConfiguration Configuration()
    {
        return ContinuityState.Validate(new ContinuityStateDto
        {
            Scenario = (int)ContinuityScenario.RecreateOwnedSubscription
        });
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ContinuityTestBackend : IContinuityBackend, IContinuityBackendFactory
{
    public ContinuitySetup Setup { get; set; } = new(ContinuityAvailability.Supported, "Test setup.");

    public Func<CancellationToken, Task> Start { get; set; } = _ => Task.CompletedTask;

    public Func<CancellationToken, Task<ContinuityStepResult>> Step { get; set; } =
        _ => Task.FromResult(new ContinuityStepResult(false, "Owned step completed."));

    public Func<CancellationToken, Task> Stop { get; set; } = _ => Task.CompletedTask;

    public int Starts { get; private set; }

    public int Steps { get; private set; }

    public int Stops { get; private set; }

    public int Disposals { get; private set; }

    public IContinuityBackend Create(ContinuityTimeline timeline) => this;

    public ContinuitySetup CheckSetup(ContinuityConfiguration configuration) => Setup;

    public Task StartAsync(ContinuityConfiguration configuration, CancellationToken ct)
    {
        Starts++;
        return Start(ct);
    }

    public Task<ContinuityStepResult> StepAsync(CancellationToken ct)
    {
        Steps++;
        return Step(ct);
    }

    public Task StopAsync(CancellationToken ct)
    {
        Stops++;
        return Stop(ct);
    }

    public ArrayOf<DiagnosticMetric> CaptureDiagnostics() => default;

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return ValueTask.CompletedTask;
    }
}
