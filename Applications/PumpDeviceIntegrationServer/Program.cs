/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server.Fluent;
using Pumps;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62542;

// Bind host for the OPC UA endpoint. Defaults to 0.0.0.0 so the server is
// reachable from outside a container; override with --host / host env var
// (e.g. "localhost" for local-only development).
string host = builder.Configuration["host"] is { Length: > 0 } h ? h : "0.0.0.0";

// In-memory store backing the OPC 10000-100 software-update facet
// attached to Pump #2 below. Production deployments swap this for
// FileSystemPackageStore over an IFileSystemProvider.
builder.Services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "PumpDeviceIntegrationServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:PumpDeviceIntegrationServer";
        o.ProductUri = "uri:opcfoundation.org:PumpDeviceIntegrationServer";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/PumpDeviceIntegrationServer");
    })
    .AddNodeManager<PumpNodeManagerFactory>()
    // Materialise a second pump declaratively at server startup. The
    // runner runs the delegate after the pump address space and
    // fluent wiring are complete.
    .ConfigureDevicesFor<PumpNodeManager>(async ctx =>
    {
        IDeviceBuilder<DeviceState> pump = await ctx.CreateDeviceAsync(
            new QualifiedName("Pump #2", ctx.Manager.DiNamespaceIndex))
            .ConfigureAwait(false);

        pump.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme Pumps Inc.");
            id.Model = new LocalizedText("PumpX-2000 (declarative)");
            id.SerialNumber = "SN-DI-2";
            id.DeviceClass = "Pump";
            id.HardwareRevision = "1.0";
            id.SoftwareRevision = "2.5.3";
        });

        // Materialise the optional DI DeviceHealth child on Pump #2
        // (PumpType itself does not carry DeviceHealth — it inherits
        // from TopologyElementType, not DeviceType — so we attach the
        // health variable to the declarative DeviceState device
        // instead, then register it with the manager so the
        // simulation tick can toggle NAMUR NE 107 states based on
        // the simulated supervision flags shared across both pumps).
        pump.Device.AddDeviceHealth(ctx.Manager.SystemContext);
        pump.WithDeviceHealth(DeviceHealthEnumeration.NORMAL);
        ((PumpNodeManager)ctx.Manager)
            .RegisterSupervisedDeviceHealth(pump.Device.DeviceHealth);

        // Seed the shared package store with sample firmware payloads
        // exposed through the DI software-update facet.
        ISoftwarePackageStore packageStore =
            ctx.GetRequiredService<ISoftwarePackageStore>();
        await SoftwarePackageSeeder.SeedAsync(packageStore).ConfigureAwait(false);

        // Demonstrate the non-typed WithFunctionalGroup(QualifiedName)
        // builder for ad-hoc groups not covered by the 8 well-known
        // DI typed extensions (WithMaintenanceGroup, WithOperationalGroup,
        // ...). Pump #2 exposes a custom "Diagnostics" group that
        // surfaces a few operational signals as plain properties so
        // clients get a single browsable folder without having to chase
        // the supervision alarm tree.
        //
        // WithProperty creates each property on the freshly built group
        // (read-only by default); LastError is made writable via the
        // fluent Writable() helper.
        pump.WithFunctionalGroup(
            new QualifiedName("Diagnostics", ctx.Manager.DiNamespaceIndex),
            fg => fg.Configure(node =>
                node.WithProperty("LastError", Variant.From(string.Empty), p => p.Writable())
                    .WithProperty("ErrorCount", 0)
                    .WithProperty("LastSelfTest", (DateTimeUtc)DateTime.UtcNow)));

        // Materialise the OPC 10000-100 §10.3 SoftwareUpdateType facet
        // under Pump #2. The default PackageLoading + library-supplied
        // "succeed immediately" callbacks give clients a fully browsable
        // SU subtree (Loading / PrepareForUpdate / Installation /
        // PowerCycle / Confirmation) with no per-application code.
        pump.WithSoftwareUpdate(packageStore, su => su.UsePackageLoading());
    });

await builder.Build().RunAsync().ConfigureAwait(false);
