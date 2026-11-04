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
builder.Services.AddSingleton<SampleMeasurementProvider>();
builder.Services.AddSingleton<SampleMeasurements>();
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
    string powerOffNodeId = binding.PowerOff is { } powerOff ? $"{powerOff.NodeId}" : string.Empty;
    await Console.Out.WriteLineAsync(
        $"SIMULATED power only. Standby NodeId: {binding.Standby.NodeId}; WOL NodeId: {powerOffNodeId}")
        .ConfigureAwait(false);
    await Console.Out.WriteLineAsync($"WOL-only NodeId: {standaloneWol.PowerOff.NodeId}").ConfigureAwait(false);
    SampleMeasurementProvider measurementProvider = context.GetRequiredService<SampleMeasurementProvider>();
    AccuracyDomainState accuracyDomain = await manager.CreateAccuracyDomainAsync(
        new QualifiedName("SampleAccuracy", manager.InstanceNamespaceIndex),
        [
            new EnumValueType
            {
                Value = 0,
                DisplayName = new LocalizedText("Class 0"),
                Description = new LocalizedText("Simulated reference accuracy class")
            }
        ],
        context.CancellationToken).ConfigureAwait(false);
    measurementProvider.SetAccuracyDomain(accuracyDomain.NodeId);
    EcmMeasurementBinding measurementBinding = await manager.CreateMeasurementAsync(
        new QualifiedName("SimulatedMeasurement", manager.InstanceNamespaceIndex),
        measurementProvider,
        authorize: static (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new ValueTask<bool>(true);
        },
        cancellationToken: context.CancellationToken).ConfigureAwait(false);
    context.GetRequiredService<SampleMeasurements>().SetBinding(measurementBinding);
    await Console.Out.WriteLineAsync(
        $"SIMULATED measurement NodeId: {measurementBinding.MeasurementPoint.NodeId}")
        .ConfigureAwait(false);
});
builder.Services.AddHostedService(static services => services.GetRequiredService<SamplePower>());
builder.Services.AddHostedService(static services => services.GetRequiredService<SampleMeasurements>());
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
        var status = binding.Standby.StandbyManagementStatus
            ?? throw new InvalidOperationException("The standby graph is missing StandbyManagementStatus.");
        if (status.Value != (byte)EcmStandbyStatus.SleepWol)
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

/// <summary>
/// Supplies only in-memory E0, E1 and D0 samples and persists the sample ApplicationTag in memory.
/// </summary>
internal sealed class SampleMeasurementProvider : IEcmMeasurementProvider
{
    /// <summary>
    /// Publishes the domain created by the server before its first measurement snapshot.
    /// </summary>
    public void SetAccuracyDomain(NodeId accuracyDomain)
    {
        if (accuracyDomain.IsNull)
        {
            throw new ArgumentException("A published accuracy domain is required.", nameof(accuracyDomain));
        }
        lock (m_lock)
        {
            m_accuracyDomain = accuracyDomain;
        }
    }

    /// <inheritdoc/>
    public ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_lock)
        {
            if (m_accuracyDomain.IsNull)
            {
                throw new InvalidOperationException("The sample accuracy domain has not been published.");
            }
            DateTime timestamp = DateTime.UtcNow;
            var phases = new AcPeDataType { L1 = 11.5f, L2 = 11.7f, L3 = 11.6f };
            return new ValueTask<EcmMeasurementSnapshot>(new EcmMeasurementSnapshot(
                m_applicationTag,
                [
                    new(
                        BrowseNames.AcCurrentPe,
                        Variant.FromStructure(phases),
                        DataTypeIds.AcPeDataType,
                        StatusCodes.Good,
                        timestamp,
                        new EUInformation("A", "ampere", "http://www.opcfoundation.org/UA/units/un/cefact")
                        {
                            UnitId = 0x414D50
                        },
                        accuracyClass: 0,
                        accuracyDomain: m_accuracyDomain),
                    new(
                        BrowseNames.AcActivePowerTotal,
                        Variant.From(4.2f),
                        Opc.Ua.DataTypeIds.Float,
                        StatusCodes.Good,
                        timestamp,
                        new EUInformation("kW", "kilowatt", "http://www.opcfoundation.org/UA/units/un/cefact")
                        {
                            UnitId = 0x4B5754
                        },
                        accuracyClass: 0,
                        accuracyDomain: m_accuracyDomain),
                    new(
                        BrowseNames.DcCurrent,
                        Variant.From(5.1f),
                        Opc.Ua.DataTypeIds.Float,
                        StatusCodes.Good,
                        timestamp,
                        new EUInformation("A", "ampere", "http://www.opcfoundation.org/UA/units/un/cefact")
                        {
                            UnitId = 0x414D50
                        },
                        accuracyClass: 0,
                        accuracyDomain: m_accuracyDomain)
                ]));
        }
    }

    /// <inheritdoc/>
    public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(applicationTag, nameof(applicationTag));
        lock (m_lock)
        {
            m_applicationTag = applicationTag;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Protects the in-memory sample state.</summary>
    private readonly System.Threading.Lock m_lock = new();

    /// <summary>The persisted in-memory application tag.</summary>
    private string m_applicationTag = "SimulatedMeter";

    /// <summary>The server-created accuracy domain.</summary>
    private NodeId m_accuracyDomain = NodeId.Null;
}

/// <summary>
/// Refreshes the standalone sample measurement binding.
/// </summary>
internal sealed class SampleMeasurements : BackgroundService
{
    /// <summary>
    /// Supplies the manager-owned binding after startup.
    /// </summary>
    public void SetBinding(EcmMeasurementBinding binding)
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
                EcmMeasurementBinding? binding = Volatile.Read(ref m_binding);
                if (binding != null)
                {
                    await binding.RefreshAsync(stoppingToken).ConfigureAwait(false);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>The borrowed manager-owned binding.</summary>
    private EcmMeasurementBinding? m_binding;
}
