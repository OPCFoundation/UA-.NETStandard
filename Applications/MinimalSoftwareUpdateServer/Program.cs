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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di.Server.SoftwareUpdate;
using SoftwareUpdate;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62543;

// In-memory store; production deployments switch to
// FileSystemPackageStore over an IFileSystemProvider.
builder.Services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MinimalSoftwareUpdateServer";
        o.ApplicationUri = "urn:localhost:OPCFoundation:MinimalSoftwareUpdateServer";
        o.ProductUri = "uri:opcfoundation.org:MinimalSoftwareUpdateServer";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add($"opc.tcp://localhost:{port}/MinimalSoftwareUpdateServer");
    })
    .AddOpcUaDi()
    .ConfigureDevicesFor<Opc.Ua.Di.Server.DiNodeManager>(async ctx =>
    {
        // Create a single demo device under the DI DeviceSet.
        var device = await ctx.CreateDeviceAsync(
            new QualifiedName("UpdateableDevice #1", ctx.Manager.DiNamespaceIndex))
            .ConfigureAwait(false);

        device.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme Corp");
            id.Model = new LocalizedText("UpdateableDevice X1");
            id.SerialNumber = "SN-SW-1";
            id.DeviceClass = "Controller";
            id.HardwareRevision = "1.0";
            id.SoftwareRevision = "1.0.0";
        });

        // Seed the shared package store with sample firmware payloads
        // exposed through the DI software-update facet.
        ISoftwarePackageStore packageStore =
            ctx.GetRequiredService<ISoftwarePackageStore>();
        await SoftwarePackageSeeder.SeedAsync(packageStore).ConfigureAwait(false);
    });

await builder.Build().RunAsync().ConfigureAwait(false);
