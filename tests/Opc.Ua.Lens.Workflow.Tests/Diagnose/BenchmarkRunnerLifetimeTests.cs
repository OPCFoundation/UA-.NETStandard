// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    [NonParallelizable]
    public Task OlderRunCallbackCannotChangeSuccessorMeasurements()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Write = async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new WriteResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    Results = [StatusCodes.Good]
                };
            };
            await using var plugin = new PerformancePlugin(context.Host)
            {
                Target = Target(BenchmarkMode.Write),
                Generator = ValueGenerator.Fixed,
                TargetRate = 1,
                DurationUnit = DurationUnit.Hours,
                DurationSeconds = 1
            };
            try
            {
                await plugin.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Task stopping = plugin.StopCommand.ExecuteAsync(null);
                release.TrySetResult();
                await stopping.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                Assert.That(plugin.IsRunning, Is.False);
                Assert.That(plugin.SnapshotCurrentRun().Configuration, Is.Not.Null);

                Guid oldRun = Guid.NewGuid();
                Guid successor = Guid.NewGuid();
                plugin.StartMeasurements(oldRun);
                plugin.HandleSample(new BenchmarkSample(oldRun, 1, 10, true));
                plugin.StartMeasurements(successor);
                plugin.HandleSample(new BenchmarkSample(oldRun, 2, 1000, false));
                Assert.That(plugin.GetHistogramSnapshot().ToArray(), Is.All.Zero);
                Assert.That(plugin.SnapshotCurrentRun().TotalOps, Is.Zero);
                Assert.That(plugin.SnapshotCurrentRun().ErrorCount, Is.Zero);
                plugin.HandleSample(new BenchmarkSample(successor, 3, 5, true));
                Assert.That(plugin.GetHistogramSnapshot().ToArray(), Has.Some.GreaterThan(0));
                Assert.That(plugin.SnapshotCurrentRun().TotalOps, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult();
            }
        });
    }
}
