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

using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
const string applicationName = "MinimalCalcServer";

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62542;

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = applicationName;
        o.ApplicationUri = "urn:localhost:OPCFoundation:MinimalCalcServer";
        o.ProductUri = "uri:opcfoundation.org:MinimalCalcServer";
        o.SubjectName = "CN=MinimalCalcServer, O=OPC Foundation, DC=localhost";
        // Sample convenience only; never auto-accept untrusted certificates in production.
        o.AutoAcceptUntrustedCertificates = true;
        o.PkiRoot = Path.Combine(
            Path.GetTempPath(),
            "OPC Foundation",
            applicationName,
            "pki");
        o.RejectSHA1Certificates = true;
        o.MinCertificateKeySize = 2048;
        o.IncludeSignAndEncryptPolicies = true;
        o.IncludeUnsecurePolicyNone = false;
        o.IncludeEccPolicies = false;
        o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
        {
            TokenType = UserTokenType.Anonymous,
        });
        o.EndpointUrls.Add($"opc.tcp://localhost:{port}/MinimalCalcServer");
    })
    .AddDefaultIdentityAuthenticators(o =>
    {
        o.EnableAnonymous = true;
        o.EnableUserNamePassword = false;
        o.EnableX509 = false;
        o.EnableJwt = false;
    })
    .AddNodeManager<Calc.CalcNodeManagerFactory>();

await builder.Build().RunAsync().ConfigureAwait(false);
