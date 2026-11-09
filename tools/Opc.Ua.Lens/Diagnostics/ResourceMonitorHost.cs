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
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;

namespace UaLens.Diagnostics;

/// <summary>
/// Owns the optional resource-monitor host and its scoped observable metrics listener.
/// CPU comes from resource-monitor metrics; memory is the process's resident working
/// set. Startup, sampling and shutdown failures are never replaced with invented data.
/// </summary>
internal sealed class ResourceMonitorHost : IAsyncDisposable
{
    private ResourceMonitorHost(
        IHost host,
        ILogger log,
        Func<(double Cpu, double MemMiB)> sample,
        ObservableResourceMeasurements? measurements)
    {
        m_host = host;
        m_log = log;
        m_sample = sample;
        m_measurements = measurements;
    }

    /// <summary>
    /// Latest completed sample for renderers. Reading this never performs I/O or
    /// invokes observable instruments. Missing and failed measurements are NaN.
    /// </summary>
    public (double Cpu, double MemMiB) LastSample
    {
        get
        {
            lock (m_lifecycle)
            {
                return m_lastSample;
            }
        }
    }

    public static Task<ResourceMonitorHost> StartAsync(CancellationToken ct = default)
        => StartDefaultHostAsync(NullLogger.Instance, ct);

    public static Task<ResourceMonitorHost> StartAsync(ITelemetryContext telemetry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        return StartDefaultHostAsync(telemetry.CreateLogger("ResourceMonitor"), ct);
    }

    /// <summary>
    /// Takes ownership of the supplied host, including cleanup when startup fails.
    /// This is also the direct-construction seam for deterministic lifecycle tests.
    /// </summary>
    public static async Task<ResourceMonitorHost> StartAsync(
        IHost host,
        ILogger logger,
        Func<(double Cpu, double MemMiB)>? sample = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);
        bool transferred = false;
        ObservableResourceMeasurements? measurements = null;
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (sample is null)
            {
                measurements = new ObservableResourceMeasurements(host.Services.GetRequiredService<IMeterFactory>());
                sample = measurements.Sample;
            }
            var result = new ResourceMonitorHost(host, logger, sample, measurements);
            transferred = true;
            ResourceMonitorHostLog.Started(logger);
            return result;
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception
            or IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException)
        {
            ResourceMonitorHostLog.StartupFailed(logger, error);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                try
                {
                    await StopAndDisposeHostAsync(host, logger).ConfigureAwait(false);
                }
                finally
                {
                    measurements?.Dispose();
                }
            }
        }
    }

    public string Sample()
    {
        (double cpu, double memory) = SampleNumeric();
        if (double.IsNaN(cpu))
        {
            return string.Format(CultureInfo.InvariantCulture,
                "CPU collecting…   Mem {0,7:0.0} MiB", memory);
        }
        return string.Format(CultureInfo.InvariantCulture,
            "CPU {0,5:0.0}%   Mem {1,7:0.0} MiB", cpu, memory);
    }

    public (double Cpu, double MemMiB) SampleNumeric()
    {
        ObjectDisposedException.ThrowIf(m_disposal is not null, this);
        try
        {
            (double cpu, double memory) = m_sample();
            lock (m_lifecycle)
            {
                m_lastSample = (cpu, memory);
            }
            return (cpu, memory);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception
            or NotSupportedException or AggregateException)
        {
            lock (m_lifecycle)
            {
                m_lastSample = (double.NaN, double.NaN);
            }
            ResourceMonitorHostLog.SampleFailed(m_log, error);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (m_lifecycle)
        {
            if (m_disposal is not null)
            {
                return new ValueTask(m_disposal);
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            m_disposal = completion.Task;
            return new ValueTask(CompleteDisposalAsync(completion));
        }
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
            await completion.Task.ConfigureAwait(false);
            throw;
        }
    }

    private static IHost CreateHost()
    {
        return new HostBuilder()
            .ConfigureServices((_, services) => services.AddResourceMonitoring())
            .Build();
    }

    private static async Task<ResourceMonitorHost> StartDefaultHostAsync(ILogger logger, CancellationToken ct)
    {
        IHost? host = CreateHost();
        try
        {
            Task<ResourceMonitorHost> startup = StartAsync(host, logger, cancellationToken: ct);
            host = null;
            return await startup.ConfigureAwait(false);
        }
        finally
        {
            if (host is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host?.Dispose();
            }
        }
    }

    private static async ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asynchronous)
        {
            await asynchronous.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            host.Dispose();
        }
    }

    private static async Task StopAndDisposeHostAsync(IHost host, ILogger logger)
    {
        try
        {
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception
            or IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException)
        {
            ResourceMonitorHostLog.ShutdownFailed(logger, error);
            throw;
        }
        finally
        {
            await DisposeHostAsync(host).ConfigureAwait(false);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await StopAndDisposeHostAsync(m_host, m_log).ConfigureAwait(false);
        }
        finally
        {
            m_measurements?.Dispose();
        }
    }

    private sealed class ObservableResourceMeasurements : IDisposable
    {
        public ObservableResourceMeasurements(IMeterFactory factory)
        {
            m_listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == kResourceMeter
                        && ReferenceEquals(instrument.Meter.Scope, factory)
                        && instrument.Name is kProcessCpu or kContainerCpu)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            m_listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
            {
                lock (m_sampling)
                {
                    bool isProcess = instrument.Name == kProcessCpu;
                    if (isProcess || !m_hasProcessCpu)
                    {
                        m_cpu = measurement;
                        m_hasCpu = true;
                        m_hasProcessCpu |= isProcess;
                    }
                }
            });
            m_listener.Start();
        }

        public (double Cpu, double MemMiB) Sample()
        {
            lock (m_sampling)
            {
                m_hasCpu = false;
                m_hasProcessCpu = false;
                m_listener.RecordObservableInstruments();
                if (!m_hasCpu)
                {
                    throw new InvalidOperationException("The resource monitor did not publish a CPU measurement.");
                }

                // ResourceMonitoring 10.5 defaults publish percent on Windows and
                // fractions on Linux. No experimental option or value heuristic is used.
                double cpu = OperatingSystem.IsWindows() ? m_cpu : m_cpu * 100;
                return (cpu, Environment.WorkingSet / 1024.0 / 1024.0);
            }
        }

        public void Dispose() => m_listener.Dispose();

        private const string kResourceMeter = "Microsoft.Extensions.Diagnostics.ResourceMonitoring";
        private const string kProcessCpu = "process.cpu.utilization";
        private const string kContainerCpu = "container.cpu.limit.utilization";
        private readonly MeterListener m_listener;
        private readonly System.Threading.Lock m_sampling = new();
        private double m_cpu = double.NaN;
        private bool m_hasCpu;
        private bool m_hasProcessCpu;
    }

    private readonly IHost m_host;
    private readonly ILogger m_log;
    private readonly Func<(double Cpu, double MemMiB)> m_sample;
    private readonly ObservableResourceMeasurements? m_measurements;
    private readonly System.Threading.Lock m_lifecycle = new();
    private (double Cpu, double MemMiB) m_lastSample = (double.NaN, double.NaN);
    private Task? m_disposal;
}

internal static partial class ResourceMonitorHostLog
{
    [LoggerMessage(
        EventId = UaLensEventIds.ResourceMonitorStarted,
        Level = LogLevel.Information,
        Message = "Resource monitoring started.")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(
        EventId = UaLensEventIds.ResourceMonitorStartupFailed,
        Level = LogLevel.Error,
        Message = "Resource monitoring could not start.")]
    public static partial void StartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.ResourceMonitorShutdownFailed,
        Level = LogLevel.Error,
        Message = "Resource monitoring could not stop cleanly.")]
    public static partial void ShutdownFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.ResourceMonitorSampleFailed,
        Level = LogLevel.Error,
        Message = "Resource utilization sampling failed.")]
    public static partial void SampleFailed(ILogger logger, Exception exception);
}
