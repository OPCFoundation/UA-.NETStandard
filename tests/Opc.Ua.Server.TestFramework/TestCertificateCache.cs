// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Configuration;

namespace Opc.Ua.Server.TestFramework
{
    /// <summary>
    /// Creates the application instance certificates of a test identity once
    /// per test process and seeds later, empty PKI folders with copies.
    /// </summary>
    /// <remarks>
    /// Most fixtures start on a fresh PKI folder, so every fixture used to
    /// generate an RSA and four ECC key pairs for the server and again for
    /// the client: about 700 ms of a 1.1 s fixture start on a developer
    /// machine (see plans/server-startup-profile.md). The fixtures still run
    /// <see cref="IApplicationInstance.CheckApplicationInstanceCertificatesAsync"/>
    /// on every start; the cache only puts the files of an earlier, identical
    /// identity into the own store first, so the check finds them instead of
    /// creating new ones. Seeded fixtures of the same identity therefore share
    /// one key pair per certificate type within a process; trust lists stay
    /// per PKI folder. Fixtures opt in with <c>ReuseCachedCertificates</c>;
    /// ClientTestFramework and TestFixture do, ad-hoc fixtures that build
    /// several applications of one identity and expect distinct keys do not.
    /// Setting the environment variable <c>OPCUA_TEST_FRESH_CERTIFICATES=1</c>
    /// turns the cache off for the whole process.
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
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The result of the certificate check.</returns>
        public static async Task<bool> CheckApplicationInstanceCertificatesAsync(
            IApplicationInstance application,
            ushort? lifeTimeInMonths,
            bool reuse,
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

            if (s_templates.TryGetValue(identity.Key, out CachedFile[]? template))
            {
                return await SeedAndCheckAsync(application, identity, template, lifeTimeInMonths, ct)
                    .ConfigureAwait(false);
            }

            // Parallel fixtures of one identity create its certificates once.
            SemaphoreSlim gate = s_creation.GetOrAdd(identity.Key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (s_templates.TryGetValue(identity.Key, out template))
                {
                    return await SeedAndCheckAsync(application, identity, template, lifeTimeInMonths, ct)
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
        /// Seeds the own store and runs the certificate check. When files were
        /// seeded the certificate manager is refreshed afterwards, as the check
        /// does after it creates a certificate: configuration validation loaded
        /// the still empty store into the manager, and a check that finds the
        /// certificates instead of creating them leaves the manager as it is.
        /// </summary>
        private static async Task<bool> SeedAndCheckAsync(
            IApplicationInstance application,
            Identity identity,
            CachedFile[] template,
            ushort? lifeTimeInMonths,
            CancellationToken ct)
        {
            bool seeded = identity.Seed(template);
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
        /// A file of a cached own store, relative to the store folder.
        /// </summary>
        private sealed class CachedFile
        {
            public CachedFile(string relativePath, byte[] content)
            {
                RelativePath = relativePath;
                Content = content;
            }

            public string RelativePath { get; }

            public byte[] Content { get; }
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
                if (string.IsNullOrEmpty(commonName) || commonName!.Any(ch => kInvalidFileNameChars.Contains(ch, StringComparison.Ordinal)))
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
            /// Copies the cached files into the own store unless it already
            /// holds a certificate of this subject; returns whether it copied.
            /// </summary>
            public bool Seed(CachedFile[] template)
            {
                if (FindFiles(kCertsFolder, "*").Length != 0)
                {
                    return false;
                }

                foreach (CachedFile file in template)
                {
                    string target = Path.Combine(m_storePath, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, file.Content);
                }
                return true;
            }

            /// <summary>
            /// Reads the certificate and private key files of this subject from
            /// the own store, or returns <c>null</c> when they are not exactly
            /// one certificate and key per configured certificate.
            /// </summary>
            public CachedFile[]? TryCapture()
            {
                string[] certificates = FindFiles(kCertsFolder, "*.der");
                string[] keys = FindFiles(kPrivateFolder, "*.pfx");
                if (certificates.Length != m_certificateCount ||
                    keys.Length != m_certificateCount ||
                    FindFiles(kCertsFolder, "*").Length != m_certificateCount ||
                    FindFiles(kPrivateFolder, "*").Length != m_certificateCount)
                {
                    return null;
                }

                var files = new List<CachedFile>(certificates.Length + keys.Length);
                foreach (string path in certificates)
                {
                    files.Add(new CachedFile(
                        Path.Combine(kCertsFolder, Path.GetFileName(path)),
                        File.ReadAllBytes(path)));
                }
                foreach (string path in keys)
                {
                    files.Add(new CachedFile(
                        Path.Combine(kPrivateFolder, Path.GetFileName(path)),
                        File.ReadAllBytes(path)));
                }
                return [.. files];
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
            private readonly string m_storePath;
            private readonly string m_filePrefix;
            private readonly int m_certificateCount;
        }

        private const string kInvalidFileNameChars = "<>:\"/\\|?*[]";
        private static readonly ConcurrentDictionary<string, CachedFile[]> s_templates = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_creation = new(StringComparer.Ordinal);
    }
}
