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
using System.Collections.Generic;
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
using BrowseNames = Opc.Ua.ECM.BrowseNames;
using DataTypeIds = Opc.Ua.ECM.DataTypeIds;

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
builder.Services.AddSingleton(static _ => new SampleMeasurementProvider());
builder.Services.AddSingleton(static _ => new SampleEnergyProvider());
builder.Services.AddSingleton(static _ => new SampleMeasurements());
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
    context.GetRequiredService<SampleMeasurements>().AddBinding(measurementBinding);
    await Console.Out.WriteLineAsync(
        $"SIMULATED measurement NodeId: {measurementBinding.MeasurementPoint.NodeId}")
        .ConfigureAwait(false);
    SampleEnergyProvider energyProvider = context.GetRequiredService<SampleEnergyProvider>();
    energyProvider.SetAccuracyDomain(accuracyDomain.NodeId);
    EcmMeasurementBinding energyBinding = await manager.CreateMeasurementAsync(
        new QualifiedName("SimulatedEnergy", manager.InstanceNamespaceIndex),
        energyProvider,
        authorize: static (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new ValueTask<bool>(true);
        },
        cancellationToken: context.CancellationToken).ConfigureAwait(false);
    context.GetRequiredService<SampleMeasurements>().AddBinding(energyBinding);
    await Console.Out.WriteLineAsync(
        $"SIMULATED E2/D1 statistics measurement NodeId: {energyBinding.MeasurementPoint.NodeId}")
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
            $"Simulated wake accepted; recovery estimate: {result.CurrentTimeToOperate} ms.".AsMemory(), cancellationToken).ConfigureAwait(false);
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
                        AccuracyClass: 0,
                        AccuracyDomain: m_accuracyDomain),
                    new(
                        BrowseNames.AcActivePowerTotal,
                        Variant.From(4200f),
                        Opc.Ua.DataTypeIds.Float,
                        StatusCodes.Good,
                        timestamp,
                        new EUInformation("W", "watt", "http://www.opcfoundation.org/UA/units/un/cefact")
                        {
                            UnitId = 5_723_220
                        },
                        AccuracyClass: 0,
                        AccuracyDomain: m_accuracyDomain),
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
                        AccuracyClass: 0,
                        AccuracyDomain: m_accuracyDomain)
                ]));
        }
    }

    /// <inheritdoc/>
    public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        applicationTag.ThrowIfNull(nameof(applicationTag));
        lock (m_lock)
        {
            m_applicationTag = applicationTag;
        }
        return default;
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
    /// Adds a manager-owned binding after startup.
    /// </summary>
    public void AddBinding(EcmMeasurementBinding binding)
    {
        lock (m_lock)
        {
            m_bindings = [.. m_bindings, binding];
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                EcmMeasurementBinding[] bindings;
                lock (m_lock)
                {
                    bindings = m_bindings;
                }
                foreach (EcmMeasurementBinding binding in bindings)
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

    /// <summary>Protects the binding list.</summary>
    private readonly System.Threading.Lock m_lock = new();

    /// <summary>The borrowed manager-owned bindings.</summary>
    private EcmMeasurementBinding[] m_bindings = [];
}

/// <summary>
/// Supplies simulated E2 and D1 measurements with a grouped statistics reset.
/// </summary>
internal sealed class SampleEnergyProvider : IEcmStatisticsProvider
{
    /// <summary>
    /// Publishes the domain created by the server before the first snapshot.
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
            m_energy += 0.01f;
            return new ValueTask<EcmMeasurementSnapshot>(new EcmMeasurementSnapshot(
                m_applicationTag,
                [
                    .. Readings(DateTime.UtcNow, includeStatistics: true)
                ])
            {
                StatisticsStartTime = m_startTime
            });
        }
    }

    /// <inheritdoc/>
    public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        applicationTag.ThrowIfNull(nameof(applicationTag));
        lock (m_lock)
        {
            m_applicationTag = applicationTag;
        }
        return default;
    }

    /// <inheritdoc/>
    public ValueTask<ServiceResult> AuthorizeStatisticsResetAsync(
        ISystemContext requestContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ServiceResult>(ServiceResult.Good);
    }

    /// <inheritdoc/>
    public ValueTask<EcmStatisticsSnapshot> ResetStatisticsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_lock)
        {
            m_energy = 0;
            m_startTime = DateTimeUtc.Now;
            return new ValueTask<EcmStatisticsSnapshot>(new EcmStatisticsSnapshot(
                m_startTime,
                [
                    .. Readings(DateTime.UtcNow, includeStatistics: true, statisticsOnly: true)
                ]));
        }
    }

    /// <summary>
    /// Creates every E2 and D1 reading from the in-memory accumulators.
    /// </summary>
    private EcmMeasurementReading[] Readings(DateTime timestamp, bool includeStatistics, bool statisticsOnly = false)
    {
        var readings = new List<EcmMeasurementReading>();
        if (!statisticsOnly)
        {
            readings.Add(Float(BrowseNames.AcActivePowerTotal, 4200f, "W", 5_723_220, timestamp, true));
            readings.Add(Float(BrowseNames.DcCurrent, 5.1f, "A", 0x414D50, timestamp, true));
            readings.Add(Float(BrowseNames.DcActivePower, 120f, "W", 5_723_220, timestamp, false));
            readings.Add(Float(BrowseNames.DcVoltage, 24f, "V", 5_655_636, timestamp, false));
        }
        if (includeStatistics)
        {
            readings.Add(Float(
                BrowseNames.AcActiveEnergyTotalExportLp,
                m_energy * 1000,
                "W·h",
                5_720_146,
                timestamp,
                true));
            readings.Add(Float(
                BrowseNames.AcActiveEnergyTotalImportLp,
                m_energy * 500,
                "W·h",
                5_720_146,
                timestamp,
                true));
            readings.Add(Float(
                BrowseNames.DcElectricalCharge,
                m_energy * (1000f / 24f),
                "A·h",
                4_279_624,
                timestamp,
                false));
            readings.Add(Float(
                BrowseNames.DcEnergyTotalExportLp,
                m_energy * 1000,
                "W·h",
                5_720_146,
                timestamp,
                false));
            readings.Add(Float(
                BrowseNames.DcEnergyTotalImportLp,
                m_energy * 500,
                "W·h",
                5_720_146,
                timestamp,
                false));
            readings.Add(Float(BrowseNames.DcRelativeCharge, 80f, "%", 20_529, timestamp, false));
        }
        return [.. readings];
    }

    /// <summary>
    /// Creates one simulated Float reading with engineering units and accuracy metadata.
    /// </summary>
    private EcmMeasurementReading Float(
        string name,
        float value,
        string unit,
        int unitId,
        DateTime timestamp,
        bool range)
    {
        return new EcmMeasurementReading(
            name,
            Variant.From(value),
            Opc.Ua.DataTypeIds.Float,
            StatusCodes.Good,
            timestamp,
            new EUInformation(unit, unit, "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                UnitId = unitId
            },
            AccuracyClass: 0,
            AccuracyDomain: m_accuracyDomain,
            AccuracyRange: range ? 1f : null);
    }

    /// <summary>Protects the in-memory sample state.</summary>
    private readonly System.Threading.Lock m_lock = new();

    /// <summary>The persisted in-memory application tag.</summary>
    private string m_applicationTag = "SimulatedEnergy";

    /// <summary>The server-created accuracy domain.</summary>
    private NodeId m_accuracyDomain = NodeId.Null;

    /// <summary>The simulated accumulated energy.</summary>
    private float m_energy;

    /// <summary>The start of the current statistics collection.</summary>
    private DateTimeUtc m_startTime = DateTimeUtc.Now;
}
