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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.ECM;
using Opc.Ua.ECM.Client;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.EnableOpcUaRedaction();
builder.Logging.AddConsole();
string endpoint = builder.Configuration["endpoint"] ?? "opc.tcp://localhost:62549/ECM";
string pki = builder.Configuration["pki"] ?? Path.Combine(Environment.CurrentDirectory, "pki", "EcmClient");
string? pause = builder.Configuration["pause-ms"];
bool sleep = ReadFlag("sleep");
bool endPause = ReadFlag("end-pause");
bool wolOnly = ReadFlag("wol-only");
if (wolOnly && (pause != null || endPause))
{
    throw new ArgumentException("--wol-only supports --sleep or read-only access, not standby commands.");
}
if ((pause == null ? 0 : 1) + (sleep ? 1 : 0) + (endPause ? 1 : 0) > 1)
{
    throw new ArgumentException("Select only one of --pause-ms, --sleep or --end-pause.");
}
double? pauseTime = null;
if (pause != null)
{
    if (!double.TryParse(pause, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
        double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0)
    {
        throw new ArgumentException("--pause-ms requires a finite nonnegative millisecond value.");
    }
    pauseTime = parsed;
}

builder.Services.AddOpcUa().AddClient(options =>
{
    options.ApplicationName = "EcmClient";
    options.ApplicationUri = "urn:localhost:OPCFoundation:EcmClient";
    options.PkiRoot = pki;
    options.AutoAcceptUntrustedCertificates = false;
    options.Session = new ManagedSessionOptions { SessionName = "EcmClient" };
}).AddDiscoveryAndConnect(options =>
{
    options.DiscoveryUrl = endpoint;
    options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
    options.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
}).AddEcmClient();

using IHost host = builder.Build();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
CancellationToken ct = timeout.Token;
await host.StartAsync(ct).ConfigureAwait(false);
try
{
    ManagedSession session = await host.Services
        .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>()(ct).ConfigureAwait(false);
    ITelemetryContext telemetry = host.Services.GetRequiredService<ITelemetryContext>();
    NodeId deviceSet = ExpandedNodeId.ToNodeId(Opc.Ua.Di.ObjectIds.DeviceSet, session.NamespaceUris);
    bool found = false;
    if (wolOnly)
    {
        await foreach (EcmWolClient discovered in EcmWolClient.DiscoverAsync(session, deviceSet, telemetry, ct)
            .ConfigureAwait(false))
        {
            found = true;
            EcmWolClient client = await host.Services.GetRequiredService<EcmClientFactory>()
                .CreateWolAsync(discovered.PowerOff.ObjectId, ct).ConfigureAwait(false);
            await PrintWolMetadataAsync(client.PowerOff).ConfigureAwait(false);
            if (sleep)
            {
                EcmControlResult result = await client.SwitchOffWolAsync(ct).ConfigureAwait(false);
                Console.WriteLine(
                    $"Call={result.Status}, ReturnCode={result.ReturnCode}, ModeID={result.ModeId}, " +
                    $"Destination={result.CurrentTimeToDestination} ms");
                if (!StatusCode.IsGood(result.Status))
                {
                    throw new ServiceResultException(result.Status);
                }
            }
            break;
        }
    }
    else
    {
        await foreach (EcmClient discovered in EcmClient.DiscoverAsync(session, deviceSet, telemetry, ct)
            .ConfigureAwait(false))
        {
            found = true;
            EcmClient client = await host.Services.GetRequiredService<EcmClientFactory>()
                .CreateAsync(discovered.Standby.ObjectId, discovered.PowerOff?.ObjectId ?? NodeId.Null, ct)
                .ConfigureAwait(false);
            var initial = await client.Standby.StandbyManagementStatusVariable().ReadAsync(ct).ConfigureAwait(false);
            Console.WriteLine(
                $"Standby {client.Standby.ObjectId}: hasValue={initial.HasValue}, " +
                $"status={initial.DataValue.StatusCode}");
            if (client.PowerOff != null)
            {
                await PrintWolMetadataAsync(client.PowerOff).ConfigureAwait(false);
            }
            EcmControlResult? result = null;
            if (pauseTime.HasValue)
            {
                StatusCode writeStatus = await client.Standby.PauseTimeVariable()
                    .WriteWithStatusAsync(pauseTime.Value, ct).ConfigureAwait(false);
                Console.WriteLine($"PauseTime write: {writeStatus}, requested={pauseTime.Value} ms.");
                if (!StatusCode.IsGood(writeStatus))
                {
                    throw new ServiceResultException(writeStatus);
                }
            }
            else if (endPause)
            {
                result = await client.EndPauseAsync(ct).ConfigureAwait(false);
            }
            else if (sleep)
            {
                result = await client.SwitchOffWolAsync(ct).ConfigureAwait(false);
            }
            if (result != null)
            {
                Console.WriteLine(
                    $"Call={result.Status}, ReturnCode={result.ReturnCode}, ModeID={result.ModeId}, " +
                    $"Destination={result.CurrentTimeToDestination} ms, Operate={result.CurrentTimeToOperate} ms");
                if (!StatusCode.IsGood(result.Status))
                {
                    throw new ServiceResultException(result.Status);
                }
            }
            using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
            watch.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await foreach (var value in client.Standby.StandbyManagementStatusVariable()
                    .ObserveAsync(cancellationToken: watch.Token).ConfigureAwait(false))
                {
                    string state = value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "<no value>";
                    Console.WriteLine(
                        $"State={state}, quality={value.DataValue.StatusCode}, " +
                        $"source={value.DataValue.SourceTimestamp:O}");
                }
            }
            catch (OperationCanceledException) when (watch.IsCancellationRequested && !ct.IsCancellationRequested)
            {
            }
            break;
        }
    }
    if (!found)
    {
        throw new InvalidOperationException(wolOnly ?
            "The DI DeviceSet has no ECM WOL object." : "The DI DeviceSet has no ECM standby object.");
    }
}
finally
{
    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
}

bool ReadFlag(string name)
{
    string? value = builder.Configuration[name];
    if (value == null)
    {
        return false;
    }
    return bool.TryParse(value, out bool parsed) ? parsed :
        throw new ArgumentException($"--{name} requires true or false.");
}

async ValueTask PrintWolMetadataAsync(EnergyDevicePowerOffTypeClient powerOff)
{
    var mac = await powerOff.WOLMagicPacketVariable().ReadAsync(ct).ConfigureAwait(false);
    string address = mac.HasValue ? mac.Value.ToHexString() : "<no value>";
    Console.WriteLine($"WOL {powerOff.ObjectId}: MAC={address}, quality={mac.DataValue.StatusCode}");
    var recovery = await powerOff.RegularTimeToOperateVariable().ReadAsync(ct).ConfigureAwait(false);
    string duration = recovery.HasValue ?
        recovery.Value.ToString(CultureInfo.InvariantCulture) : "<no value>";
    Console.WriteLine($"WOL recovery={duration} ms, quality={recovery.DataValue.StatusCode}");
    var minimum = await powerOff.TimeMinPauseVariable().ReadAsync(ct).ConfigureAwait(false);
    duration = minimum.HasValue ? minimum.Value.ToString(CultureInfo.InvariantCulture) : "<no value>";
    Console.WriteLine($"WOL minimum pause={duration} ms, quality={minimum.DataValue.StatusCode}");
    var power = await powerOff.ModePowerConsumptionVariable().ReadAsync(ct).ConfigureAwait(false);
    string consumption = power.HasValue ? power.Value.ToString(CultureInfo.InvariantCulture) : "<no value>";
    Console.WriteLine($"WOL power={consumption} kW, quality={power.DataValue.StatusCode}");
}
