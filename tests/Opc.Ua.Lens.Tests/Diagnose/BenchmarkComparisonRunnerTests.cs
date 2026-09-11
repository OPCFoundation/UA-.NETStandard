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

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class BenchmarkComparisonRunnerTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task StoppingDrainsEveryIssuedOperationBeforeFinalResultAsync(bool call, bool rejected)
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        int samples = 0;
        int successes = 0;
        int completions = 0;
        var histogram = new LatencyHistogram();
        var target = new BenchmarkTarget(
            call ? BenchmarkMode.Call : BenchmarkMode.Write,
            new NodeId(100u), call ? new NodeId(200u) : NodeId.Null,
            BuiltInType.Int32, ValueRanks.Scalar, [], "Bounded workload");
        if (call)
        {
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken _) =>
                {
                    Assert.That(calls.Count, Is.EqualTo(1));
                    Assert.That(calls[0].ObjectId, Is.EqualTo(target.ObjectId));
                    Assert.That(calls[0].MethodId, Is.EqualTo(target.NodeId));
                    Assert.That(calls[0].InputArguments.IsEmpty, Is.True);
                    if (Interlocked.Increment(ref requests) == BenchmarkRunner.MaxConcurrencyCap)
                    {
                        entered.TrySetResult();
                    }
                    await release.Task.ConfigureAwait(false);
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            ServiceResult = rejected ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                        },
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    };
                });
        }
        else
        {
            session.Setup(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader? _, ArrayOf<WriteValue> writes, CancellationToken _) =>
                {
                    Assert.That(writes.Count, Is.EqualTo(1));
                    Assert.That(writes[0].NodeId, Is.EqualTo(target.NodeId));
                    Assert.That(writes[0].AttributeId, Is.EqualTo(Attributes.Value));
                    Assert.That(writes[0].Value.WrappedValue.TryGetValue(out int _), Is.True);
                    if (Interlocked.Increment(ref requests) == BenchmarkRunner.MaxConcurrencyCap)
                    {
                        entered.TrySetResult();
                    }
                    await release.Task.ConfigureAwait(false);
                    return new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            ServiceResult = rejected ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                        },
                        Results = [StatusCodes.Good]
                    };
                });
        }
        var runner = new BenchmarkRunner(
            session.Object, target, ValueGenerator.Fixed, 0, true, TimeSpan.FromHours(1));
        await using (runner.ConfigureAwait(false))
        {
            runner.OnSample += sample =>
            {
                histogram.Record(sample.LatencyMs);
                Interlocked.Increment(ref samples);
                if (sample.Success)
                {
                    Interlocked.Increment(ref successes);
                }
                Assert.That(finished.Task.IsCompleted, Is.False);
            };
            runner.OnFinished += error =>
            {
                Assert.That(error, Is.Null);
                Assert.That(samples, Is.EqualTo(BenchmarkRunner.MaxConcurrencyCap));
                Interlocked.Increment(ref completions);
                finished.TrySetResult();
            };
            runner.Start();
            Task stop;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                stop = runner.StopAsync();
                Assert.That(stop.IsCompleted, Is.False);
                Assert.That(runner.IsRunning, Is.True);
                Assert.That(finished.Task.IsCompleted, Is.False);
                Assert.That(samples, Is.Zero);
            }
            finally
            {
                release.TrySetResult();
            }
            await stop.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(runner.IsRunning, Is.False);
            Assert.That(runner.Completion, Is.EqualTo(BenchmarkCompletion.Stopped));
            Assert.That(runner.Elapsed, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(requests, Is.EqualTo(BenchmarkRunner.MaxConcurrencyCap));
            Assert.That(successes, Is.EqualTo(rejected ? 0 : BenchmarkRunner.MaxConcurrencyCap));
            Assert.That(histogram.Capture().SampleCount, Is.EqualTo(BenchmarkRunner.MaxConcurrencyCap));
            session.Verify(value => value.Dispose(), Times.Never);
        }
    }
}
