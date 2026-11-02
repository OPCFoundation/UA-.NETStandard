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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.ECM;
using Opc.Ua.ECM.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
int? simulateWakeAfter = null;
string? wakeDelay = builder.Configuration["simulate-wake-after-ms"];
if (wakeDelay != null)
{
    if (!int.TryParse(wakeDelay, NumberStyles.None, CultureInfo.InvariantCulture, out int milliseconds))
    {
        throw new ArgumentException("--simulate-wake-after-ms requires a nonnegative 32-bit millisecond value.");
    }
    simulateWakeAfter = milliseconds;
}
builder.Logging.ClearProviders();
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();
builder.Services.AddSingleton(static _ => new SimulatedEcmPowerProvider(
    ArrayOf.Wrapped<EcmEnergySavingMode>(
    [
        new(1, "ShortPause", 3000, 1000, 1000, 60000, 1000, 2, 0.1f, 0.1f),
        new(2, "DeepPause", 7000, 2000, 2000, 120000, 3000, 1, 0.2f, 0.3f)
    ]),
    ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan())));
builder.Services.AddSingleton(services => new SamplePower(
    services.GetRequiredService<SimulatedEcmPowerProvider>(), simulateWakeAfter));
builder.Services.AddOpcUa().AddServer(options =>
{
    options.ApplicationName = "EcmSimulatedServer";
    options.ApplicationUri = "urn:localhost:OPCFoundation:EcmSimulatedServer";
    options.ProductUri = "uri:opcfoundation.org:EcmSimulatedServer";
    options.EndpointUrls.Add("opc.tcp://localhost:62549/ECM");
})
.AddEcm()
.ConfigureEcm(async (manager, context) =>
{
    SimulatedEcmPowerProvider provider = context.GetRequiredService<SimulatedEcmPowerProvider>();
    EcmPowerBinding binding = await manager.CreatePowerAsync(
        new QualifiedName("SimulatedPower", manager.InstanceNamespaceIndex),
        provider,
        wolProvider: provider,
        authorize: static (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new ValueTask<bool>(true);
        },
        cancellationToken: context.CancellationToken).ConfigureAwait(false);
    context.GetRequiredService<SamplePower>().SetBinding(binding);
    EcmWolBinding standaloneWol = await manager.CreateWolAsync(
        new QualifiedName("SimulatedWolOnly", manager.InstanceNamespaceIndex),
        provider,
        authorize: static (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new ValueTask<bool>(true);
        },
        cancellationToken: context.CancellationToken).ConfigureAwait(false);
    await Console.Out.WriteLineAsync(
        $"SIMULATED power only. Standby NodeId: {binding.Standby.NodeId}; WOL NodeId: {binding.PowerOff?.NodeId}")
        .ConfigureAwait(false);
    await Console.Out.WriteLineAsync($"WOL-only NodeId: {standaloneWol.PowerOff.NodeId}").ConfigureAwait(false);
});
builder.Services.AddHostedService(static services => services.GetRequiredService<SamplePower>());
using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);

/// <summary>
/// Publishes acquisitions and optionally simulates wake without host-power or network operations.
/// </summary>
internal sealed class SamplePower : BackgroundService
{
    /// <summary>
    /// Creates the acquisition service with an optional, explicitly configured simulated wake delay.
    /// </summary>
    public SamplePower(SimulatedEcmPowerProvider provider, int? simulateWakeAfter)
    {
        if (simulateWakeAfter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulateWakeAfter));
        }
        m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        m_simulateWakeAfter = simulateWakeAfter;
    }

    /// <summary>
    /// Supplies the manager-owned binding after startup.
    /// </summary>
    public void SetBinding(EcmPowerBinding binding)
    {
        Volatile.Write(ref m_binding, binding);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                EcmPowerBinding? binding = Volatile.Read(ref m_binding);
                if (binding != null)
                {
                    await binding.RefreshAsync(stoppingToken).ConfigureAwait(false);
                    await WakeIfRequestedAsync(binding, stoppingToken).ConfigureAwait(false);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Simulates receipt of a wake request only after the configured delay in the arrived sleep state.
    /// </summary>
    private async ValueTask WakeIfRequestedAsync(EcmPowerBinding binding, CancellationToken cancellationToken)
    {
        if (!m_simulateWakeAfter.HasValue)
        {
            return;
        }
        if (binding.Standby.StandbyManagementStatus.Value != (byte)EcmStandbyStatus.SleepWol)
        {
            m_sleepStarted = null;
            return;
        }
        long now = Stopwatch.GetTimestamp();
        m_sleepStarted ??= now;
        double elapsed = (now - m_sleepStarted.Value) * 1000d / Stopwatch.Frequency;
        if (elapsed < m_simulateWakeAfter.Value)
        {
            return;
        }
        EcmControlResult result = await m_provider.SimulateWakeAsync(cancellationToken).ConfigureAwait(false);
        if (!StatusCode.IsGood(result.Status))
        {
            throw new ServiceResultException(result.Status);
        }
        m_sleepStarted = null;
        await binding.RefreshAsync(cancellationToken).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
            $"Simulated wake accepted; recovery estimate: {result.CurrentTimeToOperate} ms.").ConfigureAwait(false);
    }

    /// <summary>The borrowed in-memory device provider.</summary>
    private readonly SimulatedEcmPowerProvider m_provider;

    /// <summary>The explicitly configured simulated wake delay in milliseconds.</summary>
    private readonly int? m_simulateWakeAfter;

    /// <summary>The monotonic timestamp when the acquisition service first observed arrived sleep.</summary>
    private long? m_sleepStarted;

    /// <summary>The borrowed manager-owned binding.</summary>
    private EcmPowerBinding? m_binding;
}
