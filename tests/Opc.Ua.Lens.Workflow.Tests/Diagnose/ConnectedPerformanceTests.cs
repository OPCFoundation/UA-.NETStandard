// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Platform("Win,Linux")]
[Category("LensDesktopWorkflow")]
[NonParallelizable]
public sealed class ConnectedPerformanceTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public Task StoppingDrainsIssuedOperationsAndRetainsExactPartialEvidence(bool call, bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var targets = new ConcurrentQueue<NodeId>();
            context.Write = async (values, _) =>
            {
                Assert.That(values.Count, Is.EqualTo(1));
                Assert.That(values[0].Value.WrappedValue.TryGetValue(out int _), Is.True);
                targets.Enqueue(values[0].NodeId);
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new WriteResponse { Results = [reject ? StatusCodes.BadNotWritable : StatusCodes.Good] };
            };
            context.Call = async (values, _) =>
            {
                Assert.That(values.Count, Is.EqualTo(1));
                Assert.That(values[0].ObjectId, Is.EqualTo(s_parent));
                Assert.That(values[0].InputArguments.IsEmpty, Is.True);
                targets.Enqueue(values[0].MethodId);
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new CallResponse
                {
                    Results = [new CallMethodResult
                    {
                        StatusCode = reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                    }]
                };
            };
            await using var plugin = new PerformancePlugin(context.Host)
            {
                Mode = call ? BenchmarkMode.Call : BenchmarkMode.Write,
                Generator = ValueGenerator.Sequential,
                TargetRate = 1,
                UnboundedBurst = false,
                DurationUnit = DurationUnit.Hours,
                DurationSeconds = 1,
                Target = new BenchmarkTarget(call ? BenchmarkMode.Call : BenchmarkMode.Write,
                    s_target, call ? s_parent : NodeId.Null, BuiltInType.Int32, ValueRanks.Scalar, [], "Fixture")
            };
            try
            {
                await plugin.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Assert.That(plugin.IsRunning, Is.True);
                Assert.That(plugin.ConfigurationEditable, Is.False);
                Assert.That(plugin.RunHistory, Is.Empty);
                Task stopping = plugin.StopCommand.ExecuteAsync(null);
                Assert.That(stopping.IsCompleted, Is.False, "The server call has not yet completed.");
                release.SetResult();
                await stopping.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Assert.That(plugin.IsRunning, Is.False);
                Assert.That(plugin.ConfigurationEditable, Is.True);
                Assert.That(plugin.RunHistory, Has.Count.EqualTo(1));
                BenchmarkRun run = plugin.RunHistory[0].Run;
                Assert.That(targets, Is.Not.Empty.And.All.EqualTo(s_target));
                Assert.That(run.TotalOps, Is.EqualTo(targets.Count));
                Assert.That(run.ErrorCount, Is.EqualTo(reject ? targets.Count : 0));
                Assert.That(run.Completion, Is.EqualTo(BenchmarkCompletion.Stopped));
                Assert.That(run.ElapsedSeconds, Is.GreaterThan(0));
                Assert.That(run.AchievedRate, Is.EqualTo(run.TotalOps / run.ElapsedSeconds!.Value).Within(0.000001));
                Assert.That(run.Configuration!.TargetNodeId, Is.EqualTo("nsu=urn:fixture:application;s=Target"));
                Assert.That(run.Configuration.DurationSeconds, Is.EqualTo(3600));
                Assert.That(run.Configuration.Generator, Is.EqualTo(ValueGenerator.Sequential));
                Assert.That(run.Distribution, Is.Not.Null);
                Assert.That(plugin.Status, Does.Contain("issued operations drained"));
                plugin.ResetCommand.Execute(null);
                Assert.That(plugin.TotalOpsText, Is.EqualTo("0"));
                Assert.That(plugin.RunHistory, Has.Count.EqualTo(1), "Resetting live counters must not erase history.");
            }
            finally
            {
                release.TrySetResult();
            }
        });
    }

    private static readonly NodeId s_target = new("Target", 2);
    private static readonly NodeId s_parent = new("Parent", 2);
}
