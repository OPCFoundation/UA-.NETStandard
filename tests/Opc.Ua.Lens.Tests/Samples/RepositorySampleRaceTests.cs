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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    public sealed class RepositorySampleRaceTests
    {
        [Test]
        public async Task StopDuringStartupDrainsBeforeNewStart()
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
                Task<RepositorySampleSnapshot> starting =
                    context.Service.StartAsync(RepositorySampleTestContext.Options);
                await context.StartEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Task<RepositorySampleSnapshot> stopping = context.Service.StopAsync();
                try
                {
                    await context.StartupCanceled.Task.WaitAsync(RepositorySampleTestContext.Bound)
                        .ConfigureAwait(false);
                    Assert.That(starting.IsCompleted, Is.False);
                    Assert.That(stopping.IsCompleted, Is.False);
                    await Assert.ThatAsync(
                        () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                        Throws.InvalidOperationException).ConfigureAwait(false);
                    await Assert.ThatAsync(
                        () => context.Service.ConfigureAsync(
                            RepositorySampleId.ConsoleReferenceServer, context.Source),
                        Throws.InvalidOperationException).ConfigureAwait(false);
                    Assert.That(context.Process.DisposeCount, Is.Zero);
                }
                finally
                {
                    context.Process.Complete(0);
                    release.TrySetResult();
                }
                await Assert.ThatAsync(
                    () => starting.WaitAsync(RepositorySampleTestContext.Bound),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                RepositorySampleSnapshot stopped = await stopping.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);
                Assert.That(stopped.OwnsResources, Is.False);
                Assert.That(stopped.Phase, Is.EqualTo(RepositorySamplePhase.Stopped));
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                await context.ConfigureAsync(RepositorySampleId.PumpSoftwareUpdateSimulator).ConfigureAwait(false);
                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Configured));
                context.Runtime.Verify(value => value.StartAsync(
                    It.IsAny<RepositorySampleLaunch>(),
                    It.IsAny<RepositorySampleOutput>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Test]
        public async Task PreCanceledStopDoesNotCancelStartup()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                TaskCompletionSource release = RepositorySampleTestContext.NewSignal();
                context.StartHandler = async token =>
                {
                    await release.Task.WaitAsync(RepositorySampleTestContext.Bound, token).ConfigureAwait(false);
                    return context.Process;
                };
                await context.ConfigureAsync().ConfigureAwait(false);
                Task<RepositorySampleSnapshot> starting =
                    context.Service.StartAsync(RepositorySampleTestContext.Options);
                await context.StartEntered.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                try
                {
                    await Assert.ThatAsync(
                        () => context.Service.StopAsync(new CancellationToken(canceled: true)),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(context.StartupCanceled.Task.IsCompleted, Is.False);
                    Assert.That(starting.IsCompleted, Is.False);
                    Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.Starting));
                }
                finally
                {
                    release.TrySetResult();
                }
                RepositorySampleSnapshot ready = await starting.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);
                Assert.That(ready.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
            }
        }

        [Test]
        public async Task RepeatedStopSharesCleanup()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                TaskCompletionSource release = RepositorySampleTestContext.NewSignal();
                context.Process.DisposeHandler = async () =>
                    await release.Task.WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                context.Process.Complete(0);
                await context.Process.DisposeEntered.Task.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);
                Task<RepositorySampleSnapshot> first = context.Service.StopAsync();
                Task<RepositorySampleSnapshot> second = context.Service.StopAsync();
                try
                {
                    Assert.That(second, Is.SameAs(first));
                    Assert.That(first.IsCompleted, Is.False);
                    Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                    Assert.That(context.Port.DisposeCount, Is.Zero);
                    Assert.That(Directory.Exists(context.Launch!.Files.Root), Is.True);
                }
                finally
                {
                    release.TrySetResult();
                }
                await Task.WhenAll(first, second).WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                await context.Service.StopAsync().ConfigureAwait(false);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
                Assert.That(Directory.Exists(context.Launch!.Files.Root), Is.False);
            }
        }

        [Test]
        public async Task CancellationAfterReadinessDoesNotStopAnAcceptedRun()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                using var cancellation = new CancellationTokenSource();
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options, cancellation.Token)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);

                Assert.That(context.Service.Snapshot.Phase, Is.EqualTo(RepositorySamplePhase.AdvertisedReady));
                Assert.That(context.Service.Completion.IsCompleted, Is.False);
                Assert.That(context.Process.TerminationCount, Is.Zero);
                Assert.That(context.Process.DisposeCount, Is.Zero);
                context.Process.Complete(0);
                RepositorySampleSnapshot result = await context.Service.Completion
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Assert.That(result.Failure, Is.EqualTo(RepositorySampleFailure.None));
            }
        }

        [Test]
        public async Task AcceptedStopCannotBeAbandonedByLaterCallerCancellation()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                using var cancellation = new CancellationTokenSource();
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Task<RepositorySampleSnapshot> stopping = context.Service.StopAsync(cancellation.Token);
                await cancellation.CancelAsync().ConfigureAwait(false);
                Assert.That(stopping.IsCompleted, Is.False);
                context.Process.Complete(0);
                RepositorySampleSnapshot result = await stopping.WaitAsync(RepositorySampleTestContext.Bound)
                    .ConfigureAwait(false);

                Assert.That(result.Phase, Is.EqualTo(RepositorySamplePhase.Stopped));
                Assert.That(result.OwnsResources, Is.False);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ChangedOwnershipMarkerRetainsFilesAndReservationForRetry()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                RepositorySampleRunFiles files = context.Launch!.Files;
                string marker = Path.Combine(files.Root, RepositorySampleRunFiles.MarkerName);
                await File.WriteAllTextAsync(marker, Guid.NewGuid().ToString("N")).ConfigureAwait(false);
                context.Process.Complete(0);
                RepositorySampleSnapshot failed = await context.Service.Completion
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);

                Assert.That(failed.Phase, Is.EqualTo(RepositorySamplePhase.CleanupRequired));
                Assert.That(failed.OwnsResources, Is.True);
                Assert.That(Directory.Exists(files.Root), Is.True);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.Zero);
                await File.WriteAllTextAsync(marker, files.RunId.ToString("N")).ConfigureAwait(false);
                RepositorySampleSnapshot retried = await context.Service.StopAsync()
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Assert.That(retried.OwnsResources, Is.False);
                Assert.That(Directory.Exists(files.Root), Is.False);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ConcurrentDisposalJoinsTheOwnedRunAndPreventsNewCommands()
        {
            RepositorySampleTestContext context = await RepositorySampleTestContext.CreateAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                await context.ConfigureAsync().ConfigureAwait(false);
                await context.Service.StartAsync(RepositorySampleTestContext.Options)
                    .WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Task first = context.Service.DisposeAsync().AsTask();
                Task second = context.Service.DisposeAsync().AsTask();

                Assert.That(second, Is.SameAs(first));
                Assert.That(first.IsCompleted, Is.False);
                await Assert.ThatAsync(
                    () => context.Service.StartAsync(RepositorySampleTestContext.Options),
                    Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => context.Service.RestoreSelectionAsync(RepositorySampleId.ConsoleReferenceServer),
                    Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
                context.Process.Complete(0);
                await Task.WhenAll(first, second).WaitAsync(RepositorySampleTestContext.Bound).ConfigureAwait(false);
                Assert.That(context.Process.DisposeCount, Is.EqualTo(1));
                Assert.That(context.Port.DisposeCount, Is.EqualTo(1));
            }
        }
    }
}
