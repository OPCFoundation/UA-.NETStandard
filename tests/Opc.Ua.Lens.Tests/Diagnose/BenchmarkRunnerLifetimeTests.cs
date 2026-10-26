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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Performance;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed partial class BenchmarkRunnerLifetimeTests
{
    [Test]
    public void ArgumentValueGenerationUsesOnlyStandardNumericTypeIds()
    {
        Assert.That(ValueFactory.BuiltInForArgument(
            new Argument { DataType = new NodeId((uint)BuiltInType.Double, 0) }), Is.EqualTo(BuiltInType.Double));
        Assert.That(ValueFactory.BuiltInForArgument(
            new Argument { DataType = new NodeId((uint)BuiltInType.Double, 2) }), Is.EqualTo(BuiltInType.Int32));
        Assert.That(ValueFactory.BuiltInForArgument(
            new Argument { DataType = new NodeId("Double", 0) }), Is.EqualTo(BuiltInType.Int32));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task StopJoinsEveryDeferredOperationAndReportsCleanupTimeout(bool call, bool timeout)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int issued = 0;
        int samples = 0;
        var completed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ISession> session = Session(async () =>
        {
            if (Interlocked.Increment(ref issued) == BenchmarkRunner.MaxConcurrencyCap)
            {
                entered.TrySetResult();
            }
            await release.Task.ConfigureAwait(false);
        });
        await using var runner = new BenchmarkRunner(
            session.Object, Target(call ? BenchmarkMode.Call : BenchmarkMode.Write),
            ValueGenerator.Fixed, 1000, true, TimeSpan.FromHours(1),
            timeout ? TimeSpan.Zero : TimeSpan.FromMinutes(1));
        runner.OnSample += sample =>
        {
            Assert.That(sample.RunId, Is.EqualTo(runner.RunId));
            Interlocked.Increment(ref samples);
        };
        runner.OnFinished += (_, error) => completed.SetResult(error);
        runner.OnDrainTimedOut += _ => timedOut.SetResult();
        runner.Start();
        await entered.Task.ConfigureAwait(false);

        Task stop = runner.StopAsync();
        if (timeout)
        {
            await timedOut.Task.ConfigureAwait(false);
            Assert.That(runner.DrainTimedOut, Is.True);
        }
        Assert.That(stop.IsCompleted, Is.False);
        Assert.That(completed.Task.IsCompleted, Is.False);
        Assert.That(runner.IsRunning, Is.True);
        release.SetResult();
        await stop.ConfigureAwait(false);
        string? error = await completed.Task.ConfigureAwait(false);
        Assert.That(samples, Is.EqualTo(issued));
        Assert.That(samples, Is.EqualTo(BenchmarkRunner.MaxConcurrencyCap));
        if (timeout)
        {
            Assert.That(error, Does.Contain("timed out"));
        }
        else
        {
            Assert.That(error, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DurationCompletionWaitsForTheFinalSample(bool call)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new Mock<TimeProvider>();
        clock.Setup(c => c.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object? state, TimeSpan _, TimeSpan _) =>
            {
                deadline.TrySetResult(() => callback(state));
                return Mock.Of<ITimer>();
            });
        int issued = 0;
        int samples = 0;
        Mock<ISession> session = Session(async () =>
        {
            Interlocked.Increment(ref issued);
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
        });
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runner = new BenchmarkRunner(
            session.Object, Target(call ? BenchmarkMode.Call : BenchmarkMode.Write),
            ValueGenerator.Fixed, 1000, true, TimeSpan.FromHours(1),
            TimeSpan.FromHours(1), clock.Object);
        runner.OnSample += _ => Interlocked.Increment(ref samples);
        runner.OnFinished += (_, _) => finished.SetResult();
        runner.Start();
        await entered.Task.ConfigureAwait(false);
        Action expire = await deadline.Task.ConfigureAwait(false);
        expire();
        Assert.That(finished.Task.IsCompleted, Is.False);
        release.SetResult();
        await finished.Task.ConfigureAwait(false);
        Assert.That(samples, Is.EqualTo(issued));
    }
}
