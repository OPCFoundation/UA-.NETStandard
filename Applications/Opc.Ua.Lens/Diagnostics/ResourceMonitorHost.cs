/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ResourceMonitoring;
using Microsoft.Extensions.Hosting;

namespace UaLens.Diagnostics;

/// <summary>
/// Wires up <see cref="IResourceMonitor"/> from
/// <c>Microsoft.Extensions.Diagnostics.ResourceMonitoring</c> as a long-lived
/// background tracker, and exposes a single <see cref="Sample"/> entry point
/// that returns a friendly "CPU 12.3%  Mem 187 MiB" string.
/// </summary>
/// <remarks>
/// The library samples on its own <see cref="IHostedService"/> so we run a
/// minimal <see cref="IHost"/> next to Avalonia: the host is started in
/// <see cref="StartAsync"/> and stopped on Avalonia exit.  When the OS does
/// not expose resource counters (e.g. a sandboxed test environment), falls
/// back to <see cref="Process.WorkingSet64"/> with a "—" CPU value.
/// </remarks>
internal sealed class ResourceMonitorHost : IAsyncDisposable
{
    private readonly IHost m_host;
    private readonly IResourceMonitor m_monitor;
    private readonly Process m_process = Process.GetCurrentProcess();
    private readonly TimeSpan m_window = TimeSpan.FromSeconds(5);

    public IResourceMonitor Monitor => m_monitor;

    private ResourceMonitorHost(IHost host)
    {
        m_host = host;
        m_monitor = host.Services.GetRequiredService<IResourceMonitor>();
    }

    /// <summary>Build the host and start its hosted services.</summary>
    public static async Task<ResourceMonitorHost> StartAsync(CancellationToken ct = default)
    {
        IHost host = new HostBuilder()
            .ConfigureServices((_, services) => services.AddResourceMonitoring())
            .Build();
        await host.StartAsync(ct).ConfigureAwait(false);
        return new ResourceMonitorHost(host);
    }

    public string Sample()
    {
        try
        {
            ResourceUtilization u = m_monitor.GetUtilization(m_window);
            double cpu = u.CpuUsedPercentage;
            ulong mem = u.MemoryUsedInBytes;
            return string.Format(CultureInfo.InvariantCulture,
                "CPU {0,5:0.0}%   Mem {1,7:0.0} MiB",
                cpu,
                mem / 1024.0 / 1024.0);
        }
        catch
        {
            m_process.Refresh();
            return string.Format(CultureInfo.InvariantCulture,
                "CPU   --     Mem {0,7:0.0} MiB",
                m_process.WorkingSet64 / 1024.0 / 1024.0);
        }
    }

    /// <summary>
    /// Numeric snapshot for graphing.  Returns (cpuPercent, memoryMiB).
    /// On any failure falls back to (NaN, current working set).
    /// </summary>
    public (double Cpu, double MemMiB) SampleNumeric()
    {
        try
        {
            ResourceUtilization u = m_monitor.GetUtilization(m_window);
            return (u.CpuUsedPercentage, u.MemoryUsedInBytes / 1024.0 / 1024.0);
        }
        catch
        {
            m_process.Refresh();
            return (double.NaN, m_process.WorkingSet64 / 1024.0 / 1024.0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await m_host.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch { }
        m_host.Dispose();
        m_process.Dispose();
    }
}
