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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for managing the OPC UA PKI (certificate trust lists and rejected certificates).
    /// </summary>
    [McpServerToolType]
    public sealed class PkiTools
    {
        /// <summary>
        /// List certificates in a trust store.
        /// </summary>
        [McpServerTool(Name = "ListCertificates")]
        [Description(
            "List certificates in a PKI store. Use store='Trusted' for trusted peer certificates, 'Issuer' for trusted issuer CAs, 'Rejected' for rejected certificates, or 'Own' for the application's own certificates.")]
        public static async Task<string> ListCertificatesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Certificate store: 'Trusted' (default), 'Issuer', 'Rejected', or 'Own'")] string store = "Trusted",
            CancellationToken ct = default)
        {
            try
            {
                ApplicationConfiguration config = await sessionManager.EnsureConfigurationAsync(ct)
                    .ConfigureAwait(false);

                CertificateStoreIdentifier storeId = GetStoreIdentifier(config, store);

                using ICertificateStore certStore = storeId.OpenStore(sessionManager.Telemetry);
                X509Certificate2Collection certs = await certStore.EnumerateAsync(ct).ConfigureAwait(false);

                var results = certs.Select(c => CertToDict(c)).ToList();

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["store"] = store,
                    ["storePath"] = storeId.StorePath,
                    ["count"] = results.Count,
                    ["certificates"] = results
                });
            }
            catch (Exception ex) when (ex is ServiceResultException or InvalidOperationException)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Trust a rejected certificate by moving it from the rejected store to the trusted peer store.
        /// </summary>
        [McpServerTool(Name = "TrustCertificate")]
        [Description(
            "Trust a previously rejected certificate by moving it from the Rejected store to the Trusted peer store. Use ListCertificates with store='Rejected' to find the thumbprint.")]
        public static async Task<string> TrustCertificateAsync(
            OpcUaSessionManager sessionManager,
            [Description("Thumbprint (SHA-1 hex) of the certificate to trust")] string thumbprint,
            CancellationToken ct = default)
        {
            try
            {
                ApplicationConfiguration config = await sessionManager.EnsureConfigurationAsync(ct)
                    .ConfigureAwait(false);

                CertificateStoreIdentifier rejectedStoreId = GetStoreIdentifier(config, "Rejected");
                CertificateStoreIdentifier trustedStoreId = GetStoreIdentifier(config, "Trusted");

                // Find in rejected store
                X509Certificate2? cert = null;
                using (ICertificateStore rejectedStore = rejectedStoreId.OpenStore(sessionManager.Telemetry))
                {
                    X509Certificate2Collection found = await rejectedStore.FindByThumbprintAsync(
                        thumbprint, ct).ConfigureAwait(false);
                    if (found.Count == 0)
                    {
                        return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                        {
                            ["error"] = true,
                            ["message"] = $"Certificate with thumbprint '{thumbprint}' not found in Rejected store."
                        });
                    }
                    cert = found[0];
                }

                // Add to trusted store
                using (ICertificateStore trustedStore = trustedStoreId.OpenStore(sessionManager.Telemetry))
                {
                    await trustedStore.AddAsync(cert, ct: ct).ConfigureAwait(false);
                }

                // Remove from rejected store
                using (ICertificateStore rejectedStore = rejectedStoreId.OpenStore(sessionManager.Telemetry))
                {
                    await rejectedStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
                }

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = $"Certificate '{cert.Subject}' (thumbprint: {thumbprint}) moved from Rejected to Trusted.",
                    ["certificate"] = CertToDict(cert)
                });
            }
            catch (Exception ex) when (ex is ServiceResultException or InvalidOperationException)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Remove a certificate from a trust store.
        /// </summary>
        [McpServerTool(Name = "RemoveCertificate")]
        [Description("Remove a certificate from a PKI store by thumbprint. Can be used to untrust a certificate or clear a rejected certificate.")]
        public static async Task<string> RemoveCertificateAsync(
            OpcUaSessionManager sessionManager,
            [Description("Thumbprint (SHA-1 hex) of the certificate to remove")] string thumbprint,
            [Description("Certificate store to remove from: 'Trusted' (default), 'Issuer', or 'Rejected'")] string store = "Trusted",
            CancellationToken ct = default)
        {
            try
            {
                ApplicationConfiguration config = await sessionManager.EnsureConfigurationAsync(ct)
                    .ConfigureAwait(false);

                CertificateStoreIdentifier storeId = GetStoreIdentifier(config, store);

                using ICertificateStore certStore = storeId.OpenStore(sessionManager.Telemetry);
                bool deleted = await certStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["success"] = deleted,
                    ["message"] = deleted
                        ? $"Certificate with thumbprint '{thumbprint}' removed from {store} store."
                        : $"Certificate with thumbprint '{thumbprint}' not found in {store} store."
                });
            }
            catch (Exception ex) when (ex is ServiceResultException or InvalidOperationException)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Get the PKI store paths configured for this application.
        /// </summary>
        [McpServerTool(Name = "GetPkiStorePaths")]
        [Description(
            "Get the file system paths for all PKI certificate stores (Trusted, Issuer, Rejected, Own). Useful for understanding where certificates are stored.")]
        public static async Task<string> GetPkiStorePathsAsync(
            OpcUaSessionManager sessionManager,
            CancellationToken ct = default)
        {
            try
            {
                ApplicationConfiguration config = await sessionManager.EnsureConfigurationAsync(ct)
                    .ConfigureAwait(false);

                SecurityConfiguration security = config.SecurityConfiguration;

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["trustedPeerStore"] = new Dictionary<string, object?>
                    {
                        ["storeType"] = security.TrustedPeerCertificates?.StoreType,
                        ["storePath"] = security.TrustedPeerCertificates?.StorePath
                    },
                    ["trustedIssuerStore"] = new Dictionary<string, object?>
                    {
                        ["storeType"] = security.TrustedIssuerCertificates?.StoreType,
                        ["storePath"] = security.TrustedIssuerCertificates?.StorePath
                    },
                    ["rejectedStore"] = new Dictionary<string, object?>
                    {
                        ["storeType"] = security.RejectedCertificateStore?.StoreType,
                        ["storePath"] = security.RejectedCertificateStore?.StorePath
                    },
                    ["applicationCertificates"] = security.ApplicationCertificates
                        .ToArray()!.Select(c => new Dictionary<string, object?>
                        {
                            ["storeType"] = c.StoreType,
                            ["storePath"] = c.StorePath,
                            ["subjectName"] = c.SubjectName
                        }).ToList(),
                    ["autoAcceptUntrustedCertificates"] = security.AutoAcceptUntrustedCertificates,
                    ["rejectSHA1SignedCertificates"] = security.RejectSHA1SignedCertificates,
                    ["minimumCertificateKeySize"] = security.MinimumCertificateKeySize
                });
            }
            catch (Exception ex) when (ex is ServiceResultException or InvalidOperationException)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = ex.Message
                });
            }
        }

        private static CertificateStoreIdentifier GetStoreIdentifier(
            ApplicationConfiguration config, string store)
        {
            SecurityConfiguration security = config.SecurityConfiguration;
            return store.ToUpperInvariant() switch
            {
                "TRUSTED" or "PEER" => security.TrustedPeerCertificates
                    ?? throw new InvalidOperationException("TrustedPeerCertificates store is not configured."),
                "ISSUER" => security.TrustedIssuerCertificates
                    ?? throw new InvalidOperationException("TrustedIssuerCertificates store is not configured."),
                "REJECTED" => security.RejectedCertificateStore
                    ?? throw new InvalidOperationException("RejectedCertificateStore is not configured."),
                "OWN" or "APPLICATION" => GetOwnCertStore(security),
                _ => throw new ArgumentException(
                    $"Unknown store '{store}'. Use 'Trusted', 'Issuer', 'Rejected', or 'Own'.", nameof(store))
            };
        }

        private static CertificateStoreIdentifier GetOwnCertStore(SecurityConfiguration security)
        {
            CertificateIdentifier? certId = security.ApplicationCertificates.ToArray()?.FirstOrDefault();
            if (certId == null)
            {
                throw new InvalidOperationException("No application certificate is configured.");
            }
            return new CertificateStoreIdentifier
            {
                StoreType = certId.StoreType,
                StorePath = certId.StorePath
            };
        }

        private static Dictionary<string, object?> CertToDict(X509Certificate2 cert)
        {
            return new Dictionary<string, object?>
            {
                ["thumbprint"] = cert.Thumbprint,
                ["subject"] = cert.Subject,
                ["issuer"] = cert.Issuer,
                ["notBefore"] = cert.NotBefore.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["notAfter"] = cert.NotAfter.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["serialNumber"] = cert.SerialNumber,
                ["hasPrivateKey"] = cert.HasPrivateKey
            };
        }
    }
}



