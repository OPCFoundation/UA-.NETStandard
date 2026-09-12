/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    public sealed class PushRollbackCancellationRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RequestCancellationDoesNotCancelCompensationForCommittedOperationsAsync(bool tokenException)
        {
            using var request = new CancellationTokenSource();
            var coordinator = new PushConfigurationTransactionCoordinator(NUnitTelemetryContext.Create());
            var active = new HashSet<int>();
            var reversed = new List<int>();
            int disposed = 0;
            for (int i = 1; i <= 2; i++)
            {
                int operation = i;
                coordinator.Stage(s_sessionId, new PushConfigurationOperation
                {
                    AffectedTrustList = new NodeId((uint)operation, 1),
                    CommitAsync = _ =>
                    {
                        active.Add(operation);
                        return Task.CompletedTask;
                    },
                    RollbackAsync = ct =>
                    {
                        ct.ThrowIfCancellationRequested();
                        active.Remove(operation);
                        reversed.Add(operation);
                        return Task.CompletedTask;
                    },
                    DisposeStaged = () => disposed++
                });
            }
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(3, 1),
                CommitAsync = ct =>
                {
                    request.Cancel();
                    if (tokenException)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "original failure");
                },
                DisposeStaged = () => disposed++
            });
            ServiceResult result = await coordinator.ApplyChangesAsync(s_sessionId, request.Token).ConfigureAwait(false);
            Assert.That(active, Is.Empty);
            Assert.That(reversed, Is.EqualTo(s_expectedReverseOrder));
            Assert.That(result.StatusCode, Is.EqualTo(tokenException ? StatusCodes.Bad : StatusCodes.BadCertificateInvalid));
            PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
            Assert.That(snapshot.Errors, Has.Count.EqualTo(1));
            Assert.That(snapshot.Errors[0].TargetId, Is.EqualTo(new NodeId(3, 1)));
            Assert.That(snapshot.Errors[0].Error, Is.EqualTo(result.StatusCode));
            Assert.That(snapshot.IsActive, Is.False);
            Assert.That(disposed, Is.EqualTo(3));
        }

        [Test]
        public async Task RollbackDeadlineIsIndependentAndDoesNotPreventEarlierCompensationAsync()
        {
            var clock = new FakeTimeProvider();
            var coordinator = new PushConfigurationTransactionCoordinator(NUnitTelemetryContext.Create(), clock);
            var reversed = new List<int>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var safety = new CancellationTokenSource();
            int disposed = 0;
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(1, 1),
                CommitAsync = _ => Task.CompletedTask,
                RollbackAsync = ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    reversed.Add(1);
                    return Task.CompletedTask;
                },
                DisposeStaged = () => disposed++
            });
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(2, 1),
                CommitAsync = _ => Task.CompletedTask,
                RollbackAsync = async ct =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, safety.Token);
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
                },
                DisposeStaged = () => disposed++
            });
            coordinator.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(3, 1),
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadCertificateInvalid),
                DisposeStaged = () => disposed++
            });
            Task<ServiceResult> applying = coordinator.ApplyChangesAsync(s_sessionId, CancellationToken.None).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(29));
                Assert.That(applying.IsCompleted, Is.False);
                Assert.That(disposed, Is.Zero);
                clock.Advance(TimeSpan.FromSeconds(1));
                ServiceResult result = await applying.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
                Assert.That(reversed, Is.EqualTo(s_expectedEarlierRollback));
                PushConfigurationTransactionSnapshot snapshot = coordinator.GetSnapshot();
                Assert.That(snapshot.Errors, Has.Count.EqualTo(2));
                Assert.That(snapshot.Errors[1].TargetId, Is.EqualTo(new NodeId(2, 1)));
                Assert.That(StatusCode.IsBad(snapshot.Errors[1].Error), Is.True);
                Assert.That(disposed, Is.EqualTo(3));
            }
            finally
            {
                safety.Cancel();
                await applying.ConfigureAwait(false);
            }
        }

        private static readonly NodeId s_sessionId = new(100, 1);
        private static readonly int[] s_expectedReverseOrder = [2, 1];
        private static readonly int[] s_expectedEarlierRollback = [1];
    }
}
