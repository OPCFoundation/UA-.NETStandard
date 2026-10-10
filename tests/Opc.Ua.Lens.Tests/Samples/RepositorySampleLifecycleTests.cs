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
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    public sealed class RepositorySampleLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PermanentDiscoveryFailureIsNotRetriedAsStartupDelay(bool socketDenied)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.ProbeHandler = (_, _) =>
                {
                    context.Process.Complete(0);
                    return Task.FromException<ArrayOf<EndpointDescription>>(
                        socketDenied ? new SocketException((int)SocketError.AccessDenied) :
                        new ServiceResultException(StatusCodes.BadUserAccessDenied));
                };
                await context.ConfigureAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options)
                        .WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.Readiness));
                Assert.That(context.Service.Snapshot.Message,
                    Does.StartWith(socketDenied ? "The sample operation failed" : "OPC UA discovery failed"));
                Assert.That(context.Service.Snapshot.OwnsResources, Is.False);
                context.Probe.Verify(value => value.DiscoverAsync(
                    It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Test]
        public async Task RuntimeStartFailureReleasesResourcesWithoutTouchingAnUnownedProcess()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.StartHandler = _ =>
                    Task.FromException<IRepositorySampleProcess>(new Win32Exception("Finite apphost start failure."));
                await context.ConfigureAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options)
                        .WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.Startup));
                Assert.That(context.Service.Snapshot.ProcessId, Is.Null);
                Assert.That(context.Service.Snapshot.OwnsResources, Is.False);
                Assert.That(context.Process.TerminationCount, Is.Zero);
                Assert.That(context.Process.DisposeCount, Is.Zero);
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
                Assert.That(Directory.Exists(context.Launch!.Files.Root), Is.False);
                context.Probe.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task UnrelatedProbeCancellationDoesNotInventAStartupTimeout()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.ProbeHandler = (_, _) =>
                {
                    context.Process.Complete(0);
                    return Task.FromCanceled<ArrayOf<EndpointDescription>>(new CancellationToken(canceled: true));
                };
                await context.ConfigureAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options)
                        .WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.Readiness));
                Assert.That(context.Service.Snapshot.OwnsResources, Is.False);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ProcessStartAndOutputAloneNeverDeclareReadiness()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var response = new TaskCompletionSource<ArrayOf<EndpointDescription>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                context.ProbeHandler = (_, _) =>
                    response.Task.WaitAsync(RepositorySampleTestContext.Bound, CancellationToken.None);
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting =
                    context.Service.StartAsync(RepositorySampleTestContext.Options);
                await context.ProbeEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                context.Output!.Append(RepositorySampleOutputStream.StandardOutput, "Server started. Ready!\n");
                try
                {
                    Assert.That(context.Service.Snapshot.ProcessId, Is.EqualTo(73101));
                    Assert.That(context.Service.Snapshot.Output.Count, Is.EqualTo(2));
                    Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Starting));
                    Assert.That(context.Service.Snapshot.Evidence, Is.Null);
                    Assert.That(context.Service.Snapshot.SecureConnectAuthorized, Is.False);
                    Assert.That(starting.IsCompleted, Is.False);
                }
                finally
                {
                    response.TrySetResult(context.Endpoints());
                }
                RepositorySampleSnapshot ready = await starting.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);
                Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
            }
        }

        [Test]
        public async Task CaptureFailureIsObservedAndStopsOwnedRun()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                context.Output!.ReportCaptureFailure(RepositorySampleOutputStream.StandardError);
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.GracefulBudget)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.GracefulBudget);
                RepositorySampleSnapshot failed = await context.Service.Completion
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);

                Assert.That(failed.Phase, Is.EqualTo(RepositorySamplePhase.Failed));
                Assert.That(failed.Failure, Is.EqualTo(RepositorySampleFailure.OutputCapture));
                Assert.That(failed.OwnsResources, Is.False);
                Assert.That(context.Process.TerminationCount, Is.EqualTo(1));
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task RunLifetimeWatchdogDoesNotDependOnCallerStopping()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.GracefulBudget)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.GracefulBudget);
                RepositorySampleSnapshot finished = await context.Service.Completion
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);

                Assert.That(finished.Phase, Is.EqualTo(RepositorySamplePhase.Failed));
                Assert.That(finished.Message, Does.Contain("bounded lifetime"));
                Assert.That(finished.ForcedTermination, Is.True);
                Assert.That(finished.OwnsResources, Is.False);
                Assert.That(context.Process.TerminationCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task VerifiedSampleReadinessNeverAuthorizesSecureConnect(bool pump)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync(pump
                    ? RepositorySampleId.PumpSoftwareUpdateSimulator
                    : RepositorySampleId.ConsoleReferenceServer).ConfigureAwait(false);
                RepositorySampleSnapshot ready = await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);

                Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
                Assert.That(ready.ProcessId, Is.EqualTo(73101));
                Assert.That(ready.Endpoint!.Port, Is.EqualTo(58123));
                Assert.That(ready.Evidence, Is.Not.Null);
                Assert.That(ready.Evidence!.ApplicationName,
                    Is.EqualTo(pump ? "PumpDeviceIntegrationServer" : "ConsoleReferenceServer"));
                Assert.That(ready.Evidence.CertificateSha256,
                    Is.EqualTo(ByteString.From(SHA256.HashData(RepositorySampleTestContext.PublicCertificate.Span))));
                Assert.That(ready.PrivatePkiRoot, Is.EqualTo(context.Launch!.Files.PkiRoot));
                Assert.That(ready.SecureConnectAuthorized, Is.False);
                Assert.That(ready.OwnsResources, Is.True);
                Assert.That(context.Port.ReleaseCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.Zero);
                Assert.That(context.Service.Completion.IsCompleted, Is.False);
            }
        }

        [Test]
        public async Task UnavailablePortDoesNotLaunch()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.Ports.Setup(value => value.ReserveAsync(It.IsAny<CancellationToken>()))
                    .Returns(() => ValueTask.FromException<IRepositorySamplePortLease>(
                        new RepositorySampleException(
                            RepositorySampleFailure.ResourceUnavailable, "The selected sample port is occupied.")));
                await context.ConfigureAsync().ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options)
                        .WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.ResourceUnavailable));
                Assert.That(context.Service.Snapshot.OwnsResources, Is.False);
                Assert.That(Directory.Exists(context.RunParent), Is.False);
                context.Runtime.VerifyNoOtherCalls();
                context.Probe.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task ConcurrentStartsAdmitOnlyOneProcess()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                TaskCompletionSource release = RepositorySampleTestContext.NewSignal();
                context.StartHandler = async _ =>
                {
                    await release.Task.WaitAsync(RepositorySampleTestContext.Bound, CancellationToken.None)
                        .ConfigureAwait(false);
                    return context.Process;
                };
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> first = context.Service.StartAsync(RepositorySampleTestContext.Options);
                await context.StartEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                try
                {
                    await Assert.ThatAsync(
                        () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                        Throws.InvalidOperationException).ConfigureAwait(false);
                    await Assert.ThatAsync(
                        () => context.Service.RestoreSelectionAsync(RepositorySampleId.PumpSoftwareUpdateSimulator),
                        Throws.InvalidOperationException).ConfigureAwait(false);
                    Assert.That(first.IsCompleted, Is.False);
                    context.Runtime.Verify(value => value.StartAsync(
                        It.IsAny<RepositorySampleLaunch>(),
                        It.IsAny<RepositorySampleOutput>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                }
                finally
                {
                    release.TrySetResult();
                }
                Assert.That((await first.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false)).Phase,
                    Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
            }
        }

        [Test]
        public async Task PreCanceledStartHasNoSideEffects()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(
                        RepositorySampleTestContext.Options, new CancellationToken(canceled: true)),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Configured));
                Assert.That(Directory.Exists(context.RunParent), Is.False);
                context.Runtime.VerifyNoOtherCalls();
                context.Ports.VerifyNoOtherCalls();
                context.Probe.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task CancellationBeforeProcessCreationReleasesLatePortLease()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var binding = new TaskCompletionSource<IRepositorySamplePortLease>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource entered = RepositorySampleTestContext.NewSignal();
                context.Ports.Setup(value => value.ReserveAsync(It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        entered.TrySetResult();
                        return new ValueTask<IRepositorySamplePortLease>(
                            binding.Task.WaitAsync(RepositorySampleTestContext.Bound));
                    });
                using var cancellation = new CancellationTokenSource();
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting = context.Service.StartAsync(
                    RepositorySampleTestContext.Options, cancellation.Token);
                await entered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                try
                {
                    Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Starting));
                    Assert.That(context.Service.Snapshot.OwnsResources, Is.True,
                        "The admitted run must remain cancelable before its port reservation completes.");
                    Assert.That(context.Service.Snapshot.ProcessId, Is.Null);
                }
                finally
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    binding.TrySetResult(context.Port);
                }

                await Assert.ThatAsync(
                    () => starting.WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.ReleaseCount, Is.Zero);
                Assert.That(Directory.Exists(context.RunParent), Is.False);
                context.Runtime.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task LateProcessAfterCancellationIsStillOwnedAndStopped()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                TaskCompletionSource release = RepositorySampleTestContext.NewSignal();
                context.StartHandler = async _ =>
                {
                    await release.Task.WaitAsync(RepositorySampleTestContext.Bound, CancellationToken.None)
                        .ConfigureAwait(false);
                    return context.Process;
                };
                using var cancellation = new CancellationTokenSource();
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting = context.Service.StartAsync(
                    RepositorySampleTestContext.Options, cancellation.Token);
                await context.StartEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.That(starting.IsCompleted, Is.False);
                release.TrySetResult();
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.GracefulBudget)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.GracefulBudget);

                await Assert.ThatAsync(
                    () => starting.WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.Canceled));
                Assert.That(context.Service.Snapshot.Evidence, Is.Null);
                Assert.That(context.Process.TerminationCount, Is.EqualTo(1));
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
                Assert.That(Directory.Exists(context.Launch!.Files.Root), Is.False);
                context.Probe.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task LateReadinessAfterCancellationIsNotPublished()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var response = new TaskCompletionSource<ArrayOf<EndpointDescription>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                context.ProbeHandler = (_, _) =>
                    response.Task.WaitAsync(RepositorySampleTestContext.Bound, CancellationToken.None);
                using var cancellation = new CancellationTokenSource();
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting = context.Service.StartAsync(
                    RepositorySampleTestContext.Options, cancellation.Token);
                await context.ProbeEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.That(starting.IsCompleted, Is.False);
                context.Process.Complete(0);
                response.TrySetResult(context.Endpoints());

                await Assert.ThatAsync(
                    () => starting.WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Evidence, Is.Null);
                Assert.That(context.Service.Snapshot.SecureConnectAuthorized, Is.False);
                Assert.That(context.Service.Snapshot.OwnsResources, Is.False);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task LateReadyAfterStartupDeadlineIsRejected()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var response = new TaskCompletionSource<ArrayOf<EndpointDescription>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                context.ProbeHandler = (_, _) =>
                    response.Task.WaitAsync(RepositorySampleTestContext.Bound, CancellationToken.None);
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting =
                    context.Service.StartAsync(RepositorySampleTestContext.Options);
                await context.ProbeEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.Options.StartupTimeout);
                context.Process.Complete(0);
                response.TrySetResult(context.Endpoints());

                await Assert.ThatAsync(
                    () => starting.WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.ReadinessTimeout));
                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Failed));
                Assert.That(context.Service.Snapshot.Evidence, Is.Null);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
            }
        }

        [TestCase("empty")]
        [TestCase("service")]
        [TestCase("halted")]
        [TestCase("request-timeout")]
        [TestCase("server-busy")]
        [TestCase("refused")]
        [TestCase("reset")]
        [TestCase("timeout")]
        public async Task EmptyOrTransientDiscoveryDoesNotImplyReadiness(string transportFailure)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                int calls = 0;
                context.ProbeHandler = (_, _) =>
                {
                    if (++calls == 1)
                    {
                        return transportFailure switch
                        {
                            "service" => Task.FromException<ArrayOf<EndpointDescription>>(
                                new ServiceResultException(StatusCodes.BadCommunicationError)),
                            "halted" => Task.FromException<ArrayOf<EndpointDescription>>(
                                new ServiceResultException(StatusCodes.BadServerHalted)),
                            "request-timeout" => Task.FromException<ArrayOf<EndpointDescription>>(
                                new ServiceResultException(StatusCodes.BadRequestTimeout)),
                            "server-busy" => Task.FromException<ArrayOf<EndpointDescription>>(
                                new ServiceResultException(StatusCodes.BadServerTooBusy)),
                            "refused" => Task.FromException<ArrayOf<EndpointDescription>>(
                                new SocketException((int)SocketError.ConnectionRefused)),
                            "reset" => Task.FromException<ArrayOf<EndpointDescription>>(
                                new SocketException((int)SocketError.ConnectionReset)),
                            "timeout" => Task.FromException<ArrayOf<EndpointDescription>>(
                                new SocketException((int)SocketError.TimedOut)),
                            _ => Task.FromResult(ArrayOf.Empty<EndpointDescription>())
                        };
                    }
                    return Task.FromResult(context.Endpoints());
                };
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting =
                    context.Service.StartAsync(RepositorySampleTestContext.Options);
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.Options.ProbeInterval)
                    .ConfigureAwait(false);

                Assert.That(starting.IsCompleted, Is.False);
                Assert.That(context.Service.Snapshot.Evidence, Is.Null);
                context.Clock.Advance(RepositorySampleTestContext.Options.ProbeInterval);
                RepositorySampleSnapshot ready = await starting.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);
                Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
                Assert.That(calls, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task ProbeFailureStopsOwnedProcess()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.ProbeHandler = (_, _) =>
                    Task.FromException<ArrayOf<EndpointDescription>>(new IOException("Finite fake probe failure."));
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting =
                    context.Service.StartAsync(RepositorySampleTestContext.Options);
                await context.ProbeEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.GracefulBudget)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.GracefulBudget);

                await Assert.ThatAsync(
                    () => starting.WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Failed));
                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.Readiness));
                Assert.That(context.Service.Snapshot.Evidence, Is.Null);
                Assert.That(context.Process.TerminationCount, Is.EqualTo(1));
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(27)]
        public async Task ExitBeforeDiscoveryIsNeverReadyEvenWithZeroCode(int exitCode)
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.Process.Complete(exitCode);
                await context.ConfigureAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options)
                        .WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.TypeOf<RepositorySampleException>()).ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Failure, Is.EqualTo(RepositorySampleFailure.ExitedBeforeReady));
                Assert.That(context.Service.Snapshot.ExitCode, Is.EqualTo(exitCode));
                Assert.That(context.Service.Snapshot.Evidence, Is.Null);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                context.Probe.VerifyNoOtherCalls();
            }
        }

        [Test]
        public async Task NormalExitCleansOnlyOwnedRun()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                string hostPki = Path.Combine(context.Root, "host-pki");
                Directory.CreateDirectory(hostPki);
                string sentinel = Path.Combine(hostPki, "do-not-touch.txt");
                await File.WriteAllTextAsync(sentinel, "host PKI sentinel").ConfigureAwait(false);
                string sourceBefore = await File.ReadAllTextAsync(context.ReferenceTemplatePath())
                    .ConfigureAwait(false);
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                string run = context.Launch!.Files.Root;
                string sibling = Path.Combine(context.RunParent, "unowned-run");
                Directory.CreateDirectory(sibling);
                await File.WriteAllTextAsync(Path.Combine(sibling, "keep.txt"), "unowned").ConfigureAwait(false);

                context.Process.Complete(0);
                RepositorySampleSnapshot finished = await context.Service.Completion
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);

                Assert.That(finished.Phase, Is.EqualTo(RepositorySamplePhase.Stopped));
                Assert.That(finished.ExitCode, Is.Zero);
                Assert.That(finished.Failure, Is.EqualTo(RepositorySampleFailure.None));
                Assert.That(finished.OwnsResources, Is.False);
                Assert.That(finished.Evidence, Is.Null);
                Assert.That(Directory.Exists(run), Is.False);
                Assert.That(await File.ReadAllTextAsync(sentinel).ConfigureAwait(false),
                    Is.EqualTo("host PKI sentinel"));
                Assert.That(await File.ReadAllTextAsync(Path.Combine(sibling, "keep.txt")).ConfigureAwait(false),
                    Is.EqualTo("unowned"));
                Assert.That(await File.ReadAllTextAsync(context.ReferenceTemplatePath()).ConfigureAwait(false),
                    Is.EqualTo(sourceBefore));
                Assert.That(context.Process.TerminationCount, Is.Zero);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task NonzeroExitIsExplicit()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                context.Process.Complete(19);
                RepositorySampleSnapshot finished = await context.Service.Completion
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);

                Assert.That(finished.Phase, Is.EqualTo(RepositorySamplePhase.Failed));
                Assert.That(finished.Failure, Is.EqualTo(RepositorySampleFailure.NonzeroExit));
                Assert.That(finished.ExitCode, Is.EqualTo(19));
                Assert.That(finished.Message, Does.Contain("nonzero"));
                Assert.That(finished.ForcedTermination, Is.False);
                Assert.That(finished.OwnsResources, Is.False);
            }
        }

        [Test]
        public async Task StopWaitsForGracefulDeadlineBeforeExactTermination()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var unowned = new SampleTestProcess(73102);
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Task<RepositorySampleSnapshot> stopping = context.Service.StopAsync();
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.GracefulBudget)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.GracefulBudget - TimeSpan.FromMilliseconds(1));
                Assert.That(context.Process.TerminationCount, Is.Zero);
                Assert.That(stopping.IsCompleted, Is.False);
                Assert.That(Directory.Exists(context.Launch!.Files.Root), Is.True);
                context.Clock.Advance(TimeSpan.FromMilliseconds(1));
                RepositorySampleSnapshot stopped = await stopping.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);

                Assert.That(stopped.Phase, Is.EqualTo(RepositorySamplePhase.Stopped));
                Assert.That(stopped.ForcedTermination, Is.True);
                Assert.That(stopped.ExitCode, Is.EqualTo(-1));
                Assert.That(stopped.ProcessId, Is.EqualTo(context.Process.Id));
                Assert.That(context.Process.TerminationCount, Is.EqualTo(1));
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(unowned.TerminationCount, Is.Zero);
                Assert.That(unowned.DisposeCount, Is.Zero);
                Assert.That(unowned.Exit.IsCompleted, Is.False);
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task NormalGracefulStopDoesNotTerminate()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Task<RepositorySampleSnapshot> stopping = context.Service.StopAsync();
                context.Process.Complete(0);
                RepositorySampleSnapshot result = await stopping.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);

                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(result.ForcedTermination, Is.False);
                Assert.That(context.Process.TerminationCount, Is.Zero);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task FailedTerminationRetainsOwnershipForStopRetry()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                context.Process.TerminateHandler = _ =>
                    ValueTask.FromException(new Win32Exception("Fake access denial."));
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Task<RepositorySampleSnapshot> stopping = context.Service.StopAsync();
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.GracefulBudget)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.GracefulBudget);
                RepositorySampleSnapshot failed = await stopping.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);

                Assert.That(failed.Phase, Is.EqualTo(RepositorySamplePhase.CleanupRequired));
                Assert.That(failed.OwnsResources, Is.True);
                Assert.That(failed.ProcessId, Is.EqualTo(context.Process.Id));
                Assert.That(Directory.Exists(context.Launch!.Files.Root), Is.True);
                Assert.That(context.Process.DisposeCount, Is.Zero);
                Assert.That(context.Port.DisposeCount, Is.Zero);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                    Throws.InvalidOperationException).ConfigureAwait(false);

                context.Process.TerminateHandler = null;
                RepositorySampleSnapshot retried = await context.Service.StopAsync()
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Assert.That(retried.Phase, Is.EqualTo(RepositorySamplePhase.Stopped));
                Assert.That(retried.OwnsResources, Is.False);
                Assert.That(context.Process.TerminationCount, Is.EqualTo(2));
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task UnresponsiveTerminationIsBoundedAndRetainsOwnership()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                TaskCompletionSource neverExits = RepositorySampleTestContext.NewSignal();
                context.Process.TerminateHandler = async token =>
                    await neverExits.Task.WaitAsync(RepositorySampleTestContext.Bound, token).ConfigureAwait(false);
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Task<RepositorySampleSnapshot> stopping = context.Service.StopAsync();
                await context.Clock.WaitForTimerAsync(RepositorySampleTestContext.GracefulBudget)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.GracefulBudget);
                await context.Process.TerminationEntered.Task.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);
                context.Clock.Advance(RepositorySampleTestContext.Options.TerminationTimeout);
                RepositorySampleSnapshot failed = await stopping.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);

                Assert.That(failed.Phase, Is.EqualTo(RepositorySamplePhase.CleanupRequired));
                Assert.That(failed.OwnsResources, Is.True);
                Assert.That(context.Process.DisposeCount, Is.Zero);
                Assert.That(context.Port.DisposeCount, Is.Zero);
                context.Process.TerminateHandler = null;
                await context.Service.StopAsync().WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Assert.That(context.Process.TerminationCount, Is.EqualTo(2));
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
            }
        }
    }
}
