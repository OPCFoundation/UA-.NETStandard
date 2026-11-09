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
