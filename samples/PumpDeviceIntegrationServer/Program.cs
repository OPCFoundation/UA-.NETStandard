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
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Pumps;
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

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "PumpDeviceIntegrationServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:PumpDeviceIntegrationServer";
        o.ProductUri = "uri:opcfoundation.org:PumpDeviceIntegrationServer";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = true;
        o.PkiRoot = Path.Combine(AppContext.BaseDirectory, "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.EndpointUrls.Add($"opc.tcp://{host}:{port}/PumpDeviceIntegrationServer");
    })
    .AddNodeManager<PumpNodeManagerFactory>()
    // Materialise a second pump declaratively at server startup. The
    // runner runs the delegate after the pump address space and
    // fluent wiring are complete.
    .ConfigureDevicesFor<PumpNodeManager>(async ctx =>
    {
        var manager = (PumpNodeManager)ctx.Manager;
        ushort pumpsNamespaceIndex = (ushort)manager.Server.NamespaceUris.GetIndex(
            Opc.Ua.Pumps.Namespaces.Pumps);
        PumpState pumpState = await manager.CreatePumpAsync(
            new QualifiedName("Pump #2", pumpsNamespaceIndex),
            ctx.CancellationToken)
            .ConfigureAwait(false);
        ITopologyElementBuilder<PumpState> pump =
            ctx.TopologyElement<PumpState>(pumpState.NodeId);

        ushort diNamespaceIndex = ctx.Manager.DiNamespaceIndex;
        ushort machineryNamespaceIndex = (ushort)manager.Server.NamespaceUris.GetIndex(
            Opc.Ua.Machinery.Namespaces.Machinery);

        // Nameplate of unit SN-002 as published in DATASHEET.md. The
        // properties are materialised by the node manager; the topology
        // element builder only assigns their values.
        pump.WithIdentificationGroup(id => id.Configure(node =>
            node.WithProperty(
                    new QualifiedName("Manufacturer", diNamespaceIndex),
                    Variant.From(
                        new LocalizedText(PumpDatasheet.Nameplate.Manufacturer)))
                .WithProperty(
                    new QualifiedName("ManufacturerUri", diNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.ManufacturerUri))
                .WithProperty(
                    new QualifiedName("Model", diNamespaceIndex),
                    Variant.From(new LocalizedText(PumpDatasheet.Nameplate.Model)))
                .WithProperty(
                    new QualifiedName("ProductCode", diNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.ProductCode))
                .WithProperty(
                    new QualifiedName("DeviceClass", diNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.DeviceClass))
                .WithProperty(
                    new QualifiedName("HardwareRevision", diNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.HardwareRevision))
                .WithProperty(
                    new QualifiedName("SoftwareRevision", diNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.SoftwareRevision))
                .WithProperty(
                    new QualifiedName("SerialNumber", diNamespaceIndex),
                    Variant.From("SN-002"))
                .WithProperty(
                    new QualifiedName("ProductInstanceUri", diNamespaceIndex),
                    Variant.From(
                        PumpDatasheet.Nameplate.ProductInstanceUriPrefix + "SN-002"))
                .WithProperty(
                    new QualifiedName("AssetId", diNamespaceIndex),
                    Variant.From("PMP-1002"))
                .WithProperty(
                    new QualifiedName("ComponentName", diNamespaceIndex),
                    Variant.From(new LocalizedText("Feed Pump B")))
                .WithProperty(
                    new QualifiedName("Location", machineryNamespaceIndex),
                    Variant.From("Plant 1 / Utility Skid / Bay 4"))
                .WithProperty(
                    new QualifiedName("YearOfConstruction", machineryNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.YearOfConstruction))
                .WithProperty(
                    new QualifiedName("MonthOfConstruction", machineryNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.MonthOfConstruction))
                .WithProperty(
                    new QualifiedName("DayOfConstruction", pumpsNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.DayOfConstruction))
                .WithProperty(
                    new QualifiedName("ArticleNumber", pumpsNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.ArticleNumber))
                .WithProperty(
                    new QualifiedName("OrderProductCode", pumpsNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.OrderProductCode))
                .WithProperty(
                    new QualifiedName("TypeOfProduct", pumpsNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.TypeOfProduct))
                .WithProperty(
                    new QualifiedName("Supplier", pumpsNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.Supplier))
                .WithProperty(
                    new QualifiedName("CountryOfOrigin", pumpsNamespaceIndex),
                    Variant.From(PumpDatasheet.Nameplate.CountryOfOrigin))
                .WithProperty(
                    new QualifiedName("FabricationNumber", pumpsNamespaceIndex),
                    Variant.From("F-2025-0002"))));

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
    });

await builder.Build().RunAsync().ConfigureAwait(false);
