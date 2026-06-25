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
using System.Collections.Generic;
using HighAvailability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server.Distributed;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62543;
string nodeId = builder.Configuration["HA_NODE_ID"] ?? Guid.NewGuid().ToString("N");
string endpointUrl = $"opc.tcp://localhost:{port}/HighAvailabilityServer";
string applicationUri = $"urn:localhost:OPCFoundation:HighAvailabilityServer:{nodeId}";

builder.Services.AddSingleton(new HaSampleReplicaInfo(nodeId));

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "HighAvailabilityServer";
        o.ApplicationUri = applicationUri;
        o.ProductUri = "uri:opcfoundation.org:HighAvailabilityServer";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add(endpointUrl);
    })
    .AddNodeManager<HaSampleNodeManagerFactory>()
    .UseDistributedAddressSpace(d =>
    {
        d.UseLeaderElection = true;
        d.NodeId = nodeId;
    })
    .AddServerRedundancy(r =>
    {
        r.Mode = RedundancySupport.Hot;
        foreach (string peerServerUri in ReadList(builder.Configuration, "peerServerUris"))
        {
            r.PeerServerUris.Add(peerServerUri);
        }
    });

await builder.Build().RunAsync().ConfigureAwait(false);

static IEnumerable<string> ReadList(IConfiguration configuration, string key)
{
    string? value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        yield break;
    }

    foreach (string item in value.Split(
        [',', ';'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        yield return item;
    }
}
