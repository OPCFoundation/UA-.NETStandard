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
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using UaLens.Diagnostics;

namespace UaLens.Tests.Workspace;

[TestFixture]
public sealed class ResourceMonitorHostTests
{
    [Test]
    public async Task StartupFailureDisposesTheOwnedHostAndRemainsAFailure()
    {
        var host = new Mock<IHost>(MockBehavior.Strict);
        host.Setup(value => value.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Counters unavailable"));
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        host.Setup(value => value.Dispose());

        await Assert.ThatAsync(
            () => ResourceMonitorHost.StartAsync(host.Object, NullLogger.Instance),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Counters unavailable"))
            .ConfigureAwait(false);

        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task AHostThatFinishesStartupAfterCancellationIsStillDisposed()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new Mock<IHost>(MockBehavior.Strict);
        host.Setup(value => value.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
            });
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        host.Setup(value => value.Dispose());
        Task<ResourceMonitorHost> starting =
            ResourceMonitorHost.StartAsync(
                host.Object, NullLogger.Instance, cancellationToken: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThatAsync(
            () => starting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task ShutdownAwaitsTheHostAndDisposesItExactlyOnce()
    {
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHost> host = CreateHost();
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                stopping.SetResult();
                await release.Task.ConfigureAwait(false);
            });
        ResourceMonitorHost monitor = await ResourceMonitorHost.StartAsync(
            host.Object, NullLogger.Instance, sample: () => (0, 100)).ConfigureAwait(false);
        Task first = monitor.DisposeAsync().AsTask();
        Task second = monitor.DisposeAsync().AsTask();
        try
        {
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(first.IsCompleted, Is.False);
            host.Verify(value => value.Dispose(), Times.Never);
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(first, second).ConfigureAwait(false);

        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task HostAsynchronousDisposalIsAwaitedInsteadOfUsingSynchronousFallback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHost> host = CreateHost();
        Mock<IAsyncDisposable> asynchronous = host.As<IAsyncDisposable>();
        host.Setup(current => current.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        asynchronous.Setup(current => current.DisposeAsync()).Returns(() =>
        {
            entered.SetResult();
            return new ValueTask(release.Task);
        });
        ResourceMonitorHost monitor = await ResourceMonitorHost.StartAsync(
            host.Object, NullLogger.Instance, sample: () => (1, 128)).ConfigureAwait(false);
        Task disposing = monitor.DisposeAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(disposing.IsCompleted, Is.False);
            host.Verify(current => current.Dispose(), Times.Never);
        }
        finally
        {
            release.TrySetResult();
        }
        await disposing.ConfigureAwait(false);
        asynchronous.Verify(current => current.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task ShutdownFailureIsNotSwallowedAndDoesNotSkipHostDisposal()
    {
        Mock<IHost> host = CreateHost();
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stop failed"));
        ResourceMonitorHost monitor = await ResourceMonitorHost.StartAsync(
            host.Object, NullLogger.Instance, sample: () => (0, 100)).ConfigureAwait(false);

        await Assert.ThatAsync(
            () => monitor.DisposeAsync().AsTask(),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Stop failed")).ConfigureAwait(false);
        await Assert.ThatAsync(
            () => monitor.DisposeAsync().AsTask(),
            Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

        host.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(value => value.Dispose(), Times.Once);
    }

    [Test]
    public async Task SamplingFailureDoesNotFabricateCpuOrMemoryMeasurements()
    {
        Mock<IHost> host = CreateHost();
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ResourceMonitorHost monitor = await ResourceMonitorHost.StartAsync(
            host.Object, NullLogger.Instance,
            sample: () => throw new InvalidOperationException("Sample failed")).ConfigureAwait(false);
        await using (monitor.ConfigureAwait(false))
        {
            Assert.That(
                () => monitor.Sample(),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Sample failed"));
            Assert.That(
                () => monitor.SampleNumeric(),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Sample failed"));
            Assert.That(double.IsNaN(monitor.LastSample.Cpu), Is.True);
            Assert.That(double.IsNaN(monitor.LastSample.MemMiB), Is.True);
        }
    }

    [Test]
    public async Task SamplingReturnsMeasuredValuesAndLabelsTheWarmupStateExplicitly()
    {
        Mock<IHost> host = CreateHost();
        host.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        double cpu = double.NaN;
        int reads = 0;
        ResourceMonitorHost monitor = await ResourceMonitorHost.StartAsync(
            host.Object, NullLogger.Instance, sample: () =>
            {
                reads++;
                return (cpu, 128.5);
            }).ConfigureAwait(false);
        await using (monitor.ConfigureAwait(false))
        {
            Assert.That(double.IsNaN(monitor.LastSample.Cpu), Is.True);
            Assert.That(reads, Is.Zero);
            Assert.That(monitor.Sample(), Does.Contain("collecting"));
            cpu = 12.5;
            Assert.That(monitor.SampleNumeric(), Is.EqualTo((12.5, 128.5)));
            Assert.That(monitor.LastSample, Is.EqualTo((12.5, 128.5)));
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(monitor.Sample(), Does.Contain("12.5%").And.Contain("128.5 MiB"));
        }
    }

    [Test]
    public async Task ObservableMetricsAreScopedToTheOwnedHostAndConvertedToPercent()
    {
        const string kMeterName = "Microsoft.Extensions.Diagnostics.ResourceMonitoring";
        using var factory = new TestMeterFactory();
        using var foreign = new TestMeterFactory();
        Meter ownMeter = factory.Create(new MeterOptions(kMeterName));
        Meter otherMeter = foreign.Create(new MeterOptions(kMeterName));
        double value = OperatingSystem.IsWindows() ? 12.5 : 0.125;
        ownMeter.CreateObservableGauge("process.cpu.utilization", () => value);
        otherMeter.CreateObservableGauge("process.cpu.utilization", () => 999.0);
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);
        services.Setup(provider => provider.GetService(typeof(IMeterFactory))).Returns(factory);
        Mock<IHost> host = CreateHost();
        host.SetupGet(current => current.Services).Returns(services.Object);
        host.Setup(current => current.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ResourceMonitorHost monitor = await ResourceMonitorHost.StartAsync(
            host.Object, NullLogger.Instance).ConfigureAwait(false);
        await using (monitor.ConfigureAwait(false))
        {
            (double cpu, double memory) = monitor.SampleNumeric();
            Assert.That(cpu, Is.EqualTo(12.5));
            Assert.That(memory, Is.GreaterThan(0));
            Assert.That(monitor.LastSample.Cpu, Is.EqualTo(12.5));
        }
        host.Verify(current => current.Dispose(), Times.Once);
    }

    private static Mock<IHost> CreateHost()
    {
        var host = new Mock<IHost>(MockBehavior.Strict);
        host.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        host.Setup(value => value.Dispose());
        return host;
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(new MeterOptions(options.Name) { Scope = this });
            m_meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in m_meters)
            {
                meter.Dispose();
            }
            m_meters.Clear();
        }

        private readonly List<Meter> m_meters = [];
    }
}
