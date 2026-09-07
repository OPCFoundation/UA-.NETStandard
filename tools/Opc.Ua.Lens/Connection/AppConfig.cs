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

using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace UaLens.Connection;

/// <summary>
/// Builds an in-memory <see cref="ApplicationConfiguration"/> for the explorer.
/// No XML config file required.
/// </summary>
internal static class AppConfig
{
    public static async Task<ApplicationConfiguration> BuildAsync(ITelemetryContext telemetry)
    {
        // CA2000: ApplicationInstance is a transient fluent-builder facade;
        // the produced ApplicationConfiguration is the only thing the caller
        // needs.  No long-lived resources on the instance itself.
#pragma warning disable CA2000
        var instance = new ApplicationInstance(telemetry)
        {
            ApplicationName = "UaLens",
            ApplicationType = ApplicationType.Client
        };
#pragma warning restore CA2000

        ApplicationConfiguration cfg = await instance
            .Build("urn:localhost:UA:UaLens", "urn:opcfoundation.org:UaLens")
            .AsClient()
            .AddSecurityConfiguration("CN=UaLens")
            .CreateAsync()
            .ConfigureAwait(false);

        // Dev-default trust: auto-accept untrusted peer certificates.  Replaces
        // the legacy `CertificateValidator.CertificateValidation += e => e.Accept = true`
        // hook (gone in the upstream cert-manager refactor).
        if (cfg.SecurityConfiguration is { } sec)
        {
            sec.AutoAcceptUntrustedCertificates = true;
        }
        return cfg;
    }
}
