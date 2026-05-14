/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
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
