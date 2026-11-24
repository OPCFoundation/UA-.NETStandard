// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.TestFramework
{
    /// <summary>
    /// Creates the application instance certificates of a test identity once
    /// per test process and seeds later, empty PKI folders with them.
    /// </summary>
    /// <remarks>
    /// Most fixtures start on a fresh PKI folder, so every fixture used to
    /// generate an RSA and four ECC key pairs for the server and again for
    /// the client: about 700 ms of a 1.1 s fixture start on a developer
    /// machine (see plans/server-startup-profile.md). The fixtures still run
    /// <see cref="IApplicationInstance.CheckApplicationInstanceCertificatesAsync"/>
    /// on every start; the cache only adds the certificates of an earlier,
    /// identical identity to the own store first (through
    /// <see cref="DirectoryCertificateStore.AddAsync"/>, so the private key
    /// files get the same permissions as created ones), and the check finds
    /// them instead of creating new ones. Seeded fixtures of the same identity
    /// therefore share one key pair per certificate type within a process;
    /// trust lists stay per PKI folder. Fixtures opt in with
    /// <c>ReuseCachedCertificates</c>; ClientTestFramework and TestFixture do,
    /// ad-hoc fixtures that build several applications of one identity and
    /// expect distinct keys do not. Setting the environment variable
    /// <c>OPCUA_TEST_FRESH_CERTIFICATES=1</c> turns the cache off for the
    /// whole process.
    /// </remarks>
    public static class TestCertificateCache
    {
        /// <summary>
        /// Whether the cache seeds fresh PKI folders. On unless the
        /// <c>OPCUA_TEST_FRESH_CERTIFICATES</c> environment variable is <c>1</c>.
        /// </summary>
        public static bool Enabled { get; set; } = !string.Equals(
            Environment.GetEnvironmentVariable("OPCUA_TEST_FRESH_CERTIFICATES"),
            "1",
            StringComparison.Ordinal);

        /// <summary>
        /// Checks (and if needed creates) the application instance certificates,
        /// seeding an empty own store from an identical identity created earlier
        /// in this process.
        /// </summary>
        /// <param name="application">The application whose certificates are checked.</param>
        /// <param name="lifeTimeInMonths">The lifetime of created certificates.</param>
        /// <param name="reuse">Whether this fixture accepts cached certificates.</param>
        /// <param name="telemetry">The telemetry context for the certificate store.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The result of the certificate check.</returns>
        public static async Task<bool> CheckApplicationInstanceCertificatesAsync(
            IApplicationInstance application,
            ushort? lifeTimeInMonths,
            bool reuse,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            Identity? identity = reuse && Enabled
                ? Identity.TryCreate(application.ApplicationConfiguration, lifeTimeInMonths)
                : null;
            if (identity == null)
            {
                return await application
                    .CheckApplicationInstanceCertificatesAsync(true, lifeTimeInMonths, ct)
                    .ConfigureAwait(false);
            }

            if (s_templates.TryGetValue(identity.Key, out CachedCertificate[]? template))
            {
                return await SeedAndCheckAsync(application, identity, template, lifeTimeInMonths, telemetry, ct)
                    .ConfigureAwait(false);
            }

            // Parallel fixtures of one identity create its certificates once.
            SemaphoreSlim gate = s_creation.GetOrAdd(identity.Key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (s_templates.TryGetValue(identity.Key, out template))
                {
                    return await SeedAndCheckAsync(application, identity, template, lifeTimeInMonths, telemetry, ct)
                        .ConfigureAwait(false);
                }

                bool result = await application
                    .CheckApplicationInstanceCertificatesAsync(true, lifeTimeInMonths, ct)
                    .ConfigureAwait(false);

                if (result && identity.TryCapture() is { } captured)
                {
                    s_templates.TryAdd(identity.Key, captured);
                }
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Seeds the own store and runs the certificate check. When certificates
        /// were seeded the certificate manager is refreshed afterwards, as the
        /// check does after it creates a certificate: configuration validation
        /// loaded the still empty store into the manager, and a check that finds
        /// the certificates instead of creating them leaves the manager as it is.
        /// </summary>
        private static async Task<bool> SeedAndCheckAsync(
            IApplicationInstance application,
            Identity identity,
            CachedCertificate[] template,
            ushort? lifeTimeInMonths,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            bool seeded = await identity.SeedAsync(template, telemetry, ct).ConfigureAwait(false);
            bool result = await application
                .CheckApplicationInstanceCertificatesAsync(true, lifeTimeInMonths, ct)
                .ConfigureAwait(false);
            if (result &&
                seeded &&
                application.ApplicationConfiguration is { CertificateManager: { } manager } configuration)
            {
                await manager
                    .UpdateAsync(configuration.SecurityConfiguration, configuration.ApplicationUri, ct)
                    .ConfigureAwait(false);
            }
            return result;
        }

        /// <summary>
        /// The certificate and private key file contents of one cached certificate.
        /// </summary>
        private sealed class CachedCertificate
        {
            public CachedCertificate(string name, byte[] der, byte[] pfx)
            {
                Name = name;
                Der = der;
                Pfx = pfx;
            }

            /// <summary>The file name without extension, as the store names it.</summary>
            public string Name { get; }

            public byte[] Der { get; }

            public byte[] Pfx { get; }
        }

        /// <summary>
        /// The certificate-shaping settings of one application configuration.
        /// </summary>
        private sealed class Identity
        {
            private Identity(string key, string storePath, string filePrefix, int certificateCount)
            {
                Key = key;
                m_storePath = storePath;
                m_filePrefix = filePrefix;
                m_certificateCount = certificateCount;
            }

            public string Key { get; }

            /// <summary>
            /// Returns the identity of a configuration whose application
            /// certificates all live in one directory store and are found by
            /// subject name; otherwise <c>null</c> and the cache stays out of it.
            /// </summary>
            public static Identity? TryCreate(
                ApplicationConfiguration? configuration,
                ushort? lifeTimeInMonths)
            {
                SecurityConfiguration? security = configuration?.SecurityConfiguration;
                if (configuration == null ||
                    security == null ||
                    security.CertificatePasswordProvider != null ||
                    security.ApplicationCertificates.Count == 0)
                {
                    return null;
                }

                var key = new StringBuilder()
                    .Append(configuration.ApplicationUri).Append('|')
                    .Append(configuration.ApplicationName).Append('|')
                    .Append(lifeTimeInMonths?.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(string.Join(",", configuration.GetServerDomainNames().ToArray() ?? []));

                string? storePath = null;
                string? subjectName = null;
                foreach (CertificateIdentifier id in security.ApplicationCertificates)
                {
                    if (id.StorePath == null ||
                        id.SubjectName == null ||
                        !string.IsNullOrEmpty(id.Thumbprint) ||
                        (id.RawData != null && id.RawData.Length > 0) ||
                        !string.Equals(id.StoreType, CertificateStoreType.Directory, StringComparison.Ordinal))
                    {
                        return null;
                    }

                    string path = Path.GetFullPath(Utils.ReplaceSpecialFolderNames(id.StorePath) ?? id.StorePath);
                    if ((storePath != null && !string.Equals(storePath, path, StringComparison.OrdinalIgnoreCase)) ||
                        (subjectName != null && !string.Equals(subjectName, id.SubjectName, StringComparison.Ordinal)))
                    {
                        return null;
                    }
                    storePath = path;
                    subjectName = id.SubjectName;

                    key.Append('|').Append(id.CertificateType.ToString())
                        .Append(';').Append(id.SubjectName)
                        .Append(';').Append(id.GetMinKeySize(security).ToString(CultureInfo.InvariantCulture));
                }

                string? commonName = X509Utils.ParseDistinguishedName(subjectName!)
                    .FirstOrDefault(name => name.StartsWith("CN=", StringComparison.Ordinal))?[3..].Trim();
                if (string.IsNullOrEmpty(commonName) ||
                    commonName!.Any(ch => kInvalidFileNameChars.Contains(ch, StringComparison.Ordinal)))
                {
                    return null;
                }

                return new Identity(
                    key.ToString(),
                    storePath!,
                    commonName + " [",
                    security.ApplicationCertificates.Count);
            }


            /// <summary>
            /// Seeds the own store with the cached certificates unless it already
            /// holds a certificate of this subject; returns whether it seeded.
            /// </summary>
            /// <remarks>
            /// The first certificate goes through <see cref="DirectoryCertificateStore.AddAsync"/>,
            /// which creates the private key folder with owner-only access (an
            /// inherited Windows ACL, mode 0700 on Unix), exactly as for a created
            /// certificate. The other files are copied into that folder; on Unix
            /// the keys are created with mode 0600. Going through the store for
            /// every certificate would re-encrypt each key and cost ~150 ms per
            /// fixture.
            /// </remarks>
            public async Task<bool> SeedAsync(
                CachedCertificate[] template,
                ITelemetryContext telemetry,
                CancellationToken ct)
            {
                if (FindFiles(kCertsFolder, "*").Length != 0)
                {
                    return false;
                }

                X509KeyStorageFlags flags = X509KeyStorageFlags.Exportable;
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    flags |= X509KeyStorageFlags.EphemeralKeySet;
                }

                using (var store = new DirectoryCertificateStore(telemetry))
                {
                    store.Open(m_storePath);
                    using Certificate first = Certificate.From(
                        X509CertificateLoader.LoadPkcs12(template[0].Pfx, null, flags));
                    await store.AddAsync(first, null, ct).ConfigureAwait(false);
                }

                for (int ii = 1; ii < template.Length; ii++)
                {
                    CachedCertificate cached = template[ii];
                    File.WriteAllBytes(
                        Path.Combine(m_storePath, kCertsFolder, cached.Name + kCertExtension),
                        cached.Der);
                    WritePrivateKey(
                        Path.Combine(m_storePath, kPrivateFolder, cached.Name + kPfxExtension),
                        cached.Pfx);
                }
                return true;
            }

            /// <summary>
            /// Reads the certificate and private key files of this subject from
            /// the own store, or returns <c>null</c> when the store does not hold
            /// exactly one certificate and key per configured certificate.
            /// </summary>
            public CachedCertificate[]? TryCapture()
            {
                string[] keys = FindFiles(kPrivateFolder, "*" + kPfxExtension);
                if (FindFiles(kCertsFolder, "*" + kCertExtension).Length != m_certificateCount ||
                    keys.Length != m_certificateCount ||
                    FindFiles(kCertsFolder, "*").Length != m_certificateCount ||
                    FindFiles(kPrivateFolder, "*").Length != m_certificateCount)
                {
                    return null;
                }

                var certificates = new CachedCertificate[keys.Length];
                for (int ii = 0; ii < keys.Length; ii++)
                {
                    string name = Path.GetFileNameWithoutExtension(keys[ii]);
                    string der = Path.Combine(m_storePath, kCertsFolder, name + kCertExtension);
                    if (!File.Exists(der))
                    {
                        return null;
                    }
                    certificates[ii] = new CachedCertificate(name, File.ReadAllBytes(der), File.ReadAllBytes(keys[ii]));
                }
                return certificates;
            }

            private static void WritePrivateKey(string path, byte[] content)
            {
#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows())
                {
                    using var stream = new FileStream(path, new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                    });
                    stream.Write(content, 0, content.Length);
                    return;
                }
#endif
                // Windows: the file inherits the owner-only ACL of the private folder.
                File.WriteAllBytes(path, content);
            }

            private string[] FindFiles(string folder, string extensionPattern)
            {
                string directory = Path.Combine(m_storePath, folder);
                if (!Directory.Exists(directory))
                {
                    return [];
                }
                // The store names files "<CN> [<qualifier> <thumbprint>].<ext>".
                return Directory.GetFiles(directory, extensionPattern)
                    .Where(path => Path.GetFileName(path).StartsWith(m_filePrefix, StringComparison.Ordinal))
                    .ToArray();
            }

            private const string kCertsFolder = "certs";
            private const string kPrivateFolder = "private";
            private const string kCertExtension = ".der";
            private const string kPfxExtension = ".pfx";
            private readonly string m_storePath;
            private readonly string m_filePrefix;
            private readonly int m_certificateCount;
        }

        private const string kInvalidFileNameChars = "<>:\"/\\|?*[]";
        private static readonly ConcurrentDictionary<string, CachedCertificate[]> s_templates = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_creation = new(StringComparer.Ordinal);
    }
}
