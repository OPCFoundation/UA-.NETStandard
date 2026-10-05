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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Verifies the method preconditions and transitions of the
    /// PrepareForUpdate (OPC 10000-100 §8.4.8) and Installation (§8.4.9)
    /// state machines of a device's software update.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    [Category("SoftwareUpdate")]
    public sealed class SoftwareUpdateMethodTransitionTests
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);
        private DiServerFixture m_fixture = null!;
        private ISoftwarePackageStore m_store = null!;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_store = new MemoryPackageStore();
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task PrepareIsRejectedUnlessIdleAsync()
        {
            int prepareCalls = 0;
            SoftwareUpdateNodes nodes = await CreateAsync("PrepareTwice", su => su.OnPrepare((_, _) =>
            {
                Interlocked.Increment(ref prepareCalls);
                return default;
            })).ConfigureAwait(false);

            ServiceResult first = await CallAsync(nodes.Prepare.Prepare, nodes).ConfigureAwait(false);
            ServiceResult second = await CallAsync(nodes.Prepare.Prepare, nodes).ConfigureAwait(false);

            Assert.That(first, Is.EqualTo(ServiceResult.Good));
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Volatile.Read(ref prepareCalls), Is.EqualTo(1));
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("PreparedForUpdate"));
        }

        [Test]
        public async Task AbortCancelsTheRunningPreparationAsync()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int cancelled = 0;
            var phases = new ConcurrentQueue<SoftwareUpdatePhase>();
            SoftwareUpdateNodes nodes = await CreateAsync("PrepareAbort", su => su
                .OnPrepare(async (_, ct) =>
                {
                    started.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Exchange(ref cancelled, 1);
                        throw;
                    }
                })
                .OnPrepareStateChanged((_, change) =>
                {
                    phases.Enqueue(change.Phase);
                    return default;
                })).ConfigureAwait(false);

            Task<ServiceResult> prepare = CallAsync(nodes.Prepare.Prepare, nodes);
            Assert.That(await CompletesAsync(started.Task).ConfigureAwait(false), Is.True);
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("Preparing"));

            ServiceResult abort = await CallAsync(nodes.Prepare.Abort, nodes).ConfigureAwait(false);

            Assert.That(abort, Is.EqualTo(ServiceResult.Good));
            Assert.That(await CompletesAsync(prepare).ConfigureAwait(false), Is.True);
            Assert.That(Volatile.Read(ref cancelled), Is.EqualTo(1));
            Assert.That((await prepare.ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("Idle"));
            Assert.That(nodes.Prepare.LastTransition!.Value.Text, Is.EqualTo("PreparingToIdle"));
            Assert.That(phases, Is.EqualTo(new[] { SoftwareUpdatePhase.Started, SoftwareUpdatePhase.Failed }));
        }

        [Test]
        public async Task AHandlerStillRunningAfterAbortCanUseItsTokenAsync()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool cancellationSeen = false;
            Exception? tokenError = null;
            SoftwareUpdateNodes nodes = await CreateAsync("PrepareAbortLate", su => su
                .OnPrepare(async (_, ct) =>
                {
                    started.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    try
                    {
                        cancellationSeen = ct.WaitHandle.WaitOne(0);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        tokenError = ex;
                    }
                })).ConfigureAwait(false);

            Task<ServiceResult> prepare = CallAsync(nodes.Prepare.Prepare, nodes);
            Assert.That(await CompletesAsync(started.Task).ConfigureAwait(false), Is.True);
            ServiceResult abort;
            try
            {
                abort = await CallAsync(nodes.Prepare.Abort, nodes).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.That(await CompletesAsync(prepare).ConfigureAwait(false), Is.True);
            ServiceResult prepared = await prepare.ConfigureAwait(false);

            Assert.That(abort, Is.EqualTo(ServiceResult.Good));
            Assert.That(tokenError, Is.Null, "The token source must outlive the running handler.");
            Assert.That(cancellationSeen, Is.True);
            Assert.That(prepared.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("Idle"));
        }

        [Test]
        public async Task AbortIsRejectedWhenNothingIsPreparingAsync()
        {
            SoftwareUpdateNodes nodes = await CreateAsync("AbortIdle").ConfigureAwait(false);

            ServiceResult abort = await CallAsync(nodes.Prepare.Abort, nodes).ConfigureAwait(false);

            Assert.That(abort.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("Idle"));
        }

        [Test]
        public async Task ResumeReturnsAPreparedDeviceToIdleAsync()
        {
            int prepareCalls = 0;
            var phases = new ConcurrentQueue<SoftwareUpdatePhase>();
            SoftwareUpdateNodes nodes = await CreateAsync("PrepareResume", su => su
                .OnPrepare((_, _) =>
                {
                    Interlocked.Increment(ref prepareCalls);
                    return default;
                })
                .OnPrepareStateChanged((_, change) =>
                {
                    phases.Enqueue(change.Phase);
                    return default;
                })).ConfigureAwait(false);

            Assert.That(
                await CallAsync(nodes.Prepare.Prepare, nodes).ConfigureAwait(false),
                Is.EqualTo(ServiceResult.Good));
            ServiceResult resume = await CallAsync(nodes.Prepare.Resume, nodes).ConfigureAwait(false);

            Assert.That(resume, Is.EqualTo(ServiceResult.Good));
            Assert.That(Volatile.Read(ref prepareCalls), Is.EqualTo(1), "Resume must not prepare again.");
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("Idle"));
            Assert.That(nodes.Prepare.LastTransition!.Value.Text, Is.EqualTo("ResumingToIdle"));
            Assert.That(phases, Is.EqualTo(new[]
            {
                SoftwareUpdatePhase.Started,
                SoftwareUpdatePhase.Completed,
                SoftwareUpdatePhase.Started,
                SoftwareUpdatePhase.Completed
            }));
        }

        [Test]
        public async Task ResumeIsRejectedUnlessPreparedForUpdateAsync()
        {
            int prepareCalls = 0;
            SoftwareUpdateNodes nodes = await CreateAsync("ResumeIdle", su => su.OnPrepare((_, _) =>
            {
                Interlocked.Increment(ref prepareCalls);
                return default;
            })).ConfigureAwait(false);

            ServiceResult resume = await CallAsync(nodes.Prepare.Resume, nodes).ConfigureAwait(false);

            Assert.That(resume.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(Volatile.Read(ref prepareCalls), Is.Zero);
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("Idle"));
        }

        [Test]
        public async Task ResumeIsRejectedWhileInstallingAsync()
        {
            var installing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SoftwareUpdateNodes nodes = await CreateAsync("ResumeInstalling", su => su
                .OnInstall(async (_, _, _) =>
                {
                    installing.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                })).ConfigureAwait(false);

            Assert.That(
                await CallAsync(nodes.Prepare.Prepare, nodes).ConfigureAwait(false),
                Is.EqualTo(ServiceResult.Good));
            Task<ServiceResult> install = CallAsync(
                nodes.Installation.InstallSoftwarePackage, nodes, InstallInputs());
            Assert.That(await CompletesAsync(installing.Task).ConfigureAwait(false), Is.True);

            ServiceResult resume;
            try
            {
                resume = await CallAsync(nodes.Prepare.Resume, nodes).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }

            Assert.That(resume.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(nodes.Prepare.CurrentState!.Value.Text, Is.EqualTo("PreparedForUpdate"));
            Assert.That(await CompletesAsync(install).ConfigureAwait(false), Is.True);
            Assert.That(await install.ConfigureAwait(false), Is.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task AnInstallationCannotStartWhileResumeIsMovingThePreparationAsync()
        {
            var installing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SoftwareUpdateNodes nodes = await CreateAsync("ResumeRacesInstall", su => su
                .OnInstall((_, _, _) =>
                {
                    installing.TrySetResult(true);
                    return default;
                })).ConfigureAwait(false);
            Assert.That(
                await CallAsync(nodes.Prepare.Prepare, nodes).ConfigureAwait(false),
                Is.EqualTo(ServiceResult.Good));

            // Resume has passed its "no installation running" check when it
            // writes the move to Resuming. Start an installation from that
            // very moment and see whether it gets to run before Resume is done.
            Task<ServiceResult>? install = null;
            bool installRanDuringResume = false;
            int started = 0;
            nodes.Prepare.CurrentState!.OnStateChanged = (_, _, _) =>
            {
                if (Interlocked.Exchange(ref started, 1) == 0)
                {
                    install = Task.Run(() => CallAsync(
                        nodes.Installation.InstallSoftwarePackage, nodes, InstallInputs()));
                    installRanDuringResume = installing.Task.Wait(TimeSpan.FromMilliseconds(500));
                }
            };

            ServiceResult resume = await CallAsync(nodes.Prepare.Resume, nodes).ConfigureAwait(false);
            nodes.Prepare.CurrentState.OnStateChanged = null;

            Assert.That(resume, Is.EqualTo(ServiceResult.Good));
            Assert.That(install, Is.Not.Null);
            Assert.That(
                installRanDuringResume, Is.False,
                "An installation must not start between the check of Resume and its move.");
            Assert.That(await CompletesAsync(install!).ConfigureAwait(false), Is.True);
            Assert.That(await install!.ConfigureAwait(false), Is.EqualTo(ServiceResult.Good));
            Assert.That(nodes.Installation.CurrentState!.Value.Text, Is.EqualTo("Idle"));
        }

        [Test]
        public async Task AnInstallationIsRejectedUnlessIdleAsync()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var installing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int installCalls = 0;
            SoftwareUpdateNodes nodes = await CreateAsync("InstallTwice", su => su
                .OnInstall(async (_, _, _) =>
                {
                    Interlocked.Increment(ref installCalls);
                    installing.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                })).ConfigureAwait(false);

            Task<ServiceResult> first = CallAsync(
                nodes.Installation.InstallSoftwarePackage, nodes, InstallInputs());
            Assert.That(await CompletesAsync(installing.Task).ConfigureAwait(false), Is.True);
            Task<ServiceResult> secondCall = CallAsync(
                nodes.Installation.InstallSoftwarePackage, nodes, InstallInputs());
            bool secondRejected;
            try
            {
                secondRejected = await Task.WhenAny(secondCall, Task.Delay(TimeSpan.FromSeconds(5)))
                    .ConfigureAwait(false) == secondCall;
            }
            finally
            {
                release.TrySetResult(true);
            }

            Assert.That(secondRejected, Is.True, "The second installation must be rejected, not run.");
            ServiceResult second = await secondCall.ConfigureAwait(false);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await first.ConfigureAwait(false), Is.EqualTo(ServiceResult.Good));
            Assert.That(Volatile.Read(ref installCalls), Is.EqualTo(1));
            Assert.That(nodes.Installation.CurrentState!.Value.Text, Is.EqualTo("Idle"));
        }

        [Test]
        public async Task InstallationResumeLeavesErrorForIdleAsync()
        {
            SoftwareUpdateNodes nodes = await CreateAsync("InstallResume", su => su
                .OnInstall((_, _, _) => throw new InvalidOperationException("flash failed")))
                .ConfigureAwait(false);

            ServiceResult install = await CallAsync(
                nodes.Installation.InstallSoftwarePackage, nodes, InstallInputs()).ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(install), Is.True);
            Assert.That(nodes.Installation.CurrentState!.Value.Text, Is.EqualTo("Error"));

            ServiceResult resume = await CallAsync(nodes.Installation.Resume, nodes).ConfigureAwait(false);

            Assert.That(resume, Is.EqualTo(ServiceResult.Good));
            Assert.That(nodes.Installation.CurrentState!.Value.Text, Is.EqualTo("Idle"));
            Assert.That(nodes.Installation.LastTransition!.Value.Text, Is.EqualTo("ErrorToIdle"));
        }

        [Test]
        public async Task InstallationResumeIsRejectedOutsideErrorAsync()
        {
            SoftwareUpdateNodes nodes = await CreateAsync("InstallResumeIdle").ConfigureAwait(false);

            ServiceResult resume = await CallAsync(nodes.Installation.Resume, nodes).ConfigureAwait(false);

            Assert.That(resume.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(nodes.Installation.CurrentState!.Value.Text, Is.EqualTo("Idle"));
        }

        private async Task<SoftwareUpdateNodes> CreateAsync(
            string deviceName,
            Action<ISoftwareUpdateBuilder>? configure = null)
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(deviceName, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);
            builder.WithSoftwareUpdate(m_store, configure);

            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext context = m_fixture.Manager.SystemContext;
            NodeState softwareUpdate = builder.Device.FindChild(
                context, new QualifiedName("SoftwareUpdate", diNs))!;
            return new SoftwareUpdateNodes(
                softwareUpdate.NodeId,
                (PrepareForUpdateStateMachineState)softwareUpdate.FindChild(
                    context, new QualifiedName("PrepareForUpdate", diNs))!,
                (InstallationStateMachineState)softwareUpdate.FindChild(
                    context, new QualifiedName("Installation", diNs))!);
        }

        private static ArrayOf<Variant> InstallInputs()
        {
            return new ArrayOf<Variant>(new[]
            {
                new Variant("urn:acme:firmware"),
                new Variant("9.9.9"),
                new Variant(Array.Empty<string>()),
                new Variant(Array.Empty<byte>())
            });
        }

        private static async Task<bool> CompletesAsync(Task task)
        {
            return await Task.WhenAny(task, Task.Delay(s_timeout)).ConfigureAwait(false) == task;
        }

        private static async Task<ServiceResult> CallAsync(
            MethodState? method,
            SoftwareUpdateNodes nodes,
            ArrayOf<Variant> inputs = default)
        {
            Assert.That(method, Is.Not.Null, "the method is materialised");
            var outputs = new List<Variant>();
            if (method!.OnCallMethod2Async is not null)
            {
                return await method.OnCallMethod2Async(
                    null!, method, nodes.SoftwareUpdateId, inputs, outputs,
                    CancellationToken.None).ConfigureAwait(false);
            }
            if (method.OnCallMethod2 is not null)
            {
                return method.OnCallMethod2(null!, method, nodes.SoftwareUpdateId, inputs, outputs);
            }
            return new ServiceResult(StatusCodes.BadNotImplemented);
        }

        private sealed record SoftwareUpdateNodes(
            NodeId SoftwareUpdateId,
            PrepareForUpdateStateMachineState Prepare,
            InstallationStateMachineState Installation);
    }
}
