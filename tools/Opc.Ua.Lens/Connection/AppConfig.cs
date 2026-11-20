/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
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
    public static Task<ApplicationConfiguration> BuildAsync(ITelemetryContext telemetry)
    {
        return BuildAsync(telemetry, CancellationToken.None);
    }

    /// <summary>
    /// Creates an independent configuration/manager. The recipient must await
    /// disposal of its certificate manager after all sessions have closed.
    /// </summary>
    public static Task<ApplicationConfiguration> BuildAsync(ITelemetryContext telemetry, CancellationToken ct)
    {
        return BuildAsync(telemetry, pkiRoot: null, ct);
    }

    /// <summary>
    /// Creates a private manager over the specified PKI root. Probes can use
    /// task-owned stores without changing application-wide certificate paths.
    /// </summary>
    public static async Task<ApplicationConfiguration> BuildAsync(
        ITelemetryContext telemetry,
        string? pkiRoot,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        // The builder facade's only owned resource is the returned certificate
        // manager, whose ownership transfers to the configuration recipient.
        // TODO: use a stack configuration lease when the builder exposes one.
#pragma warning disable CA2000
        var instance = new ApplicationInstance(telemetry)
        {
            ApplicationName = "UaLens",
            ApplicationType = ApplicationType.Client
        };
#pragma warning restore CA2000

        try
        {
            ApplicationConfiguration cfg = await instance
                .Build("urn:localhost:UA:UaLens", "urn:opcfoundation.org:UaLens")
                .AsClient()
                .AddSecurityConfiguration([CreateApplicationCertificate(pkiRoot)], pkiRoot: pkiRoot)
                .SetAutoAcceptUntrustedCertificates(false)
                .SetUseValidatedCertificates(false)
                .CreateAsync(ct)
                .ConfigureAwait(false);
            bool haveCertificate = await instance
                .CheckApplicationInstanceCertificatesAsync(silent: true, ct: ct)
                .ConfigureAwait(false);
            if (!haveCertificate)
            {
                throw new InvalidOperationException("The UaLens application certificate could not be initialized.");
            }
            return cfg;
        }
        catch
        {
            if (instance.ApplicationConfiguration?.CertificateManager is IAsyncDisposable manager)
            {
                await manager.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private static CertificateIdentifier CreateApplicationCertificate(string? pkiRoot)
    {
        // Preserve the legacy builder's single RSA certificate and store
        // resolution. Its default-certificate-list helper also adds ECC
        // certificates, which would change existing stores and policy support.
        string root = pkiRoot is null
            || pkiRoot.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase)
            ? CertificateStoreIdentifier.DefaultPKIRoot
            : pkiRoot.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase)
                ? CertificateStoreIdentifier.CurrentUser
                : pkiRoot;
        string storeType = CertificateStoreIdentifier.DetermineStoreType(root);
        string storePath = root;
        if (storeType.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
        {
            // Match the builder's existing own-suffix handling, including an
            // optional trailing separator, instead of appending own twice.
            int suffix = root.Length - 3;
            if (Path.EndsInDirectorySeparator(root))
            {
                suffix--;
            }
            if (suffix <= 0
                || !root.AsSpan(suffix, 3).Equals("own", StringComparison.OrdinalIgnoreCase))
            {
                storePath = Path.Combine(root, "own");
            }
        }
        else if (storeType.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase))
        {
            storePath = root + (!OperatingSystem.IsWindows()
                && root.StartsWith(CertificateStoreIdentifier.CurrentUser, StringComparison.OrdinalIgnoreCase)
                    ? "My"
                    : "UA_MachineDefault");
        }
        return new CertificateIdentifier
        {
            StoreType = storeType,
            StorePath = storePath,
            SubjectName = "CN=UaLens",
            CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
        };
    }
}
