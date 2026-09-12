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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Opc.Ua;

namespace UaLens.Samples
{
    /// <summary>
    /// Prepares only the two managed repository samples. The parent directory is
    /// injectable for task-owned storage; construction performs no filesystem I/O.
    /// </summary>
    internal sealed class RepositorySampleFiles
    {
        public RepositorySampleFiles()
            : this(Path.Combine(Path.GetTempPath(), "UaLens-repository-samples"), LocalFileSystem.Instance)
        {
        }

        public RepositorySampleFiles(string runParent, IFileSystem fileSystem)
        {
            m_runParent = RepositorySamplePaths.ValidateRoot(runParent);
            m_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public RepositorySamplePrerequisite Inspect(RepositorySampleSource? source, RepositorySampleId sample)
        {
            RepositorySampleDescriptor descriptor = RepositorySampleCatalog.Get(sample);
            if (source is null)
            {
                return new(false, "Requires configuration: select a trusted local source root and a managed build.");
            }
            string root = RepositorySamplePaths.ValidateRoot(source.Root);
            string build = RepositorySampleCatalog.GetBuildDirectory(source, sample);
            RepositorySamplePaths.EnsureNoLinks(root, build);
            string project = RepositorySamplePaths.ResolveChild(
                root, Path.Combine(descriptor.ProjectDirectory, descriptor.AssemblyName + ".csproj"));
            RepositorySamplePaths.EnsureNoLinks(root, project);
            string solution = RepositorySamplePaths.ResolveChild(root, "UA.slnx");
            RepositorySamplePaths.EnsureNoLinks(root, solution);
            if (!m_fileSystem.Exists(root, isDirectory: true) ||
                !m_fileSystem.Exists(solution) ||
                !m_fileSystem.Exists(project))
            {
                return new(false, "Requires configuration: the selected root does not contain this sample.");
            }
            ArrayOf<string> artifacts =
            [
                descriptor.AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty),
                descriptor.AssemblyName + ".dll",
                descriptor.AssemblyName + ".deps.json",
                descriptor.AssemblyName + ".runtimeconfig.json"
            ];
            foreach (string name in artifacts)
            {
                string artifact = RepositorySamplePaths.ResolveChild(build, name);
                RepositorySamplePaths.EnsureNoLinks(root, artifact);
                if (!m_fileSystem.Exists(artifact))
                {
                    return new(false, "Requires configuration: the selected managed build is missing " + name + ".");
                }
            }
            if (sample == RepositorySampleId.ConsoleReferenceServer)
            {
                string template = GetReferenceTemplate(root);
                RepositorySamplePaths.EnsureNoLinks(root, template);
                if (!m_fileSystem.Exists(template))
                {
                    return new(false, "Requires configuration: the reference configuration template is missing.");
                }
            }
            return new(true, "Configured. Launch is explicit; secure connection still requires separate trust.");
        }

        public RepositorySampleRunFiles AllocateRun()
        {
            var runId = Guid.NewGuid();
            string root = RepositorySamplePaths.ResolveChild(m_runParent, runId.ToString("N"));
            return new RepositorySampleRunFiles(m_runParent, root, runId, m_fileSystem);
        }

        public async Task<RepositorySampleLaunch> PrepareAsync(
            RepositorySampleSource source,
            RepositorySampleId sample,
            int port,
            RepositorySampleRunOptions options,
            RepositorySampleRunFiles files,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(files);
            await files.VerifyOwnershipAsync(cancellationToken).ConfigureAwait(false);
            options.Validate();
            ArgumentOutOfRangeException.ThrowIfLessThan(port, 1024);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
            cancellationToken.ThrowIfCancellationRequested();
            RepositorySamplePrerequisite prerequisite = Inspect(source, sample);
            if (!prerequisite.CanLaunch)
            {
                throw new RepositorySampleException(RepositorySampleFailure.Prerequisite, prerequisite.Message);
            }

            RepositorySampleDescriptor descriptor = RepositorySampleCatalog.Get(sample);
            string build = RepositorySampleCatalog.GetBuildDirectory(source, sample);
            string executable = Path.Combine(
                build, descriptor.AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            var endpoint = new Uri(
                "opc.tcp://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + descriptor.EndpointPath);
            string runSeconds = options.RunSeconds.ToString(CultureInfo.InvariantCulture);
            ArrayOf<string> arguments;
            ArrayOf<string> applicationUris;
            if (sample == RepositorySampleId.ConsoleReferenceServer)
            {
                string applicationUri = "urn:ualens-repository-sample:" + files.RunId.ToString("N");
                await WriteReferenceConfigurationAsync(
                    source.Root, files, descriptor, endpoint, applicationUri, cancellationToken)
                    .ConfigureAwait(false);
                arguments = ["--timeout", runSeconds, "--console", "--log"];
                applicationUris = [applicationUri];
            }
            else
            {
                arguments =
                [
                    "--host", "127.0.0.1",
                    "--port", port.ToString(CultureInfo.InvariantCulture),
                    "--pumps", "2",
                    "--pki-root", files.PkiRoot,
                    "--autoaccept", "false",
                    "--software-update-demo", "true",
                    "--run-seconds", runSeconds
                ];
                applicationUris =
                [
                    "urn:localhost:OPCFoundation:PumpDeviceIntegrationServer",
                    "urn:" + Utils.GetHostName() + ":OPCFoundation:PumpDeviceIntegrationServer"
                ];
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new RepositorySampleLaunch(
                descriptor, source, executable, endpoint, applicationUris, arguments, files);
        }

        private async Task WriteReferenceConfigurationAsync(
            string sourceRoot,
            RepositorySampleRunFiles files,
            RepositorySampleDescriptor descriptor,
            Uri endpoint,
            string applicationUri,
            CancellationToken cancellationToken)
        {
            string template = GetReferenceTemplate(sourceRoot);
            RepositorySamplePaths.EnsureNoLinks(sourceRoot, template);
            using Stream input = m_fileSystem.OpenRead(template);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            });
            XDocument original = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken)
                .ConfigureAwait(false);
            XNamespace ns = "http://opcfoundation.org/UA/SDK/Configuration.xsd";
            XNamespace ua = "http://opcfoundation.org/UA/2008/02/Types.xsd";
            XElement root = original.Root ?? throw new XmlException("The reference configuration is empty.");
            if (root.Name != ns + "ApplicationConfiguration")
            {
                throw new XmlException("The reference configuration has an unsupported root.");
            }
            var server = new XElement(root.Element(ns + "ServerConfiguration") ??
                throw new XmlException("The reference server configuration is missing."));
            server.Element(ns + "BaseAddresses")?.Remove();
            server.AddFirst(new XElement(ns + "BaseAddresses", new XElement(ua + "String", endpoint.AbsoluteUri)));
            server.Element(ns + "AlternateBaseAddresses")?.Remove();
            server.Element(ns + "ReverseConnect")?.Remove();
            server.Element(ns + "RegistrationEndpoint")?.Remove();
            XElement policies = server.Element(ns + "SecurityPolicies") ??
                throw new XmlException("The reference security policies are missing.");
            policies.ReplaceWith(
                new XElement(ns + "SecurityPolicies",
                    new XElement(ns + "ServerSecurityPolicy",
                        new XElement(ns + "SecurityMode", "SignAndEncrypt_3"),
                        new XElement(ns + "SecurityPolicyUri", SecurityPolicies.Basic256Sha256))));
            XElement tokens = server.Element(ns + "UserTokenPolicies") ??
                throw new XmlException("The reference user token policies are missing.");
            tokens.ReplaceWith(
                new XElement(ns + "UserTokenPolicies",
                    new XElement(ua + "UserTokenPolicy", new XElement(ua + "TokenType", "Anonymous_0"))));
            server.SetElementValue(ns + "MaxRegistrationInterval", 0);
            server.SetElementValue(ns + "MultiCastDnsEnabled", false);
            server.SetElementValue(ns + "DurableSubscriptionsEnabled", false);
            server.SetElementValue(ns + "NodeManagerSaveFile", Path.Combine(files.Root, "server.nodes.xml"));
            server.Element(ns + "ServerCapabilities")?.ReplaceWith(
                new XElement(ns + "ServerCapabilities",
                    new XElement(ua + "String", "DA"),
                    new XElement(ua + "String", "HD"),
                    new XElement(ua + "String", "AC")));

            XElement Store(string element, string child)
            {
                return new XElement(ns + element,
                    new XElement(ns + "StoreType", "Directory"),
                    new XElement(ns + "StorePath", Path.Combine(files.PkiRoot, child)));
            }

            XElement certificate = Store("CertificateIdentifier", "own");
            certificate.Add(
                new XElement(ns + "SubjectName", "CN=UaLens Repository Reference Server, DC=localhost"),
                new XElement(ns + "CertificateTypeString", "RsaSha256"));
            var configuration = new XDocument(new XElement(ns + "ApplicationConfiguration",
                new XAttribute(XNamespace.Xmlns + "ua", ua),
                new XElement(ns + "ApplicationName", descriptor.ApplicationName),
                new XElement(ns + "ApplicationUri", applicationUri),
                new XElement(ns + "ProductUri", descriptor.ProductUri),
                new XElement(ns + "ApplicationType", "Server_0"),
                new XElement(ns + "SecurityConfiguration",
                    new XElement(ns + "ApplicationCertificates", certificate),
                    Store("TrustedIssuerCertificates", "issuer"),
                    Store("TrustedPeerCertificates", "trusted"),
                    Store("RejectedCertificateStore", "rejected"),
                    new XElement(ns + "MaxRejectedCertificates", 5),
                    new XElement(ns + "AutoAcceptUntrustedCertificates", false),
                    new XElement(ns + "RejectSHA1SignedCertificates", true),
                    new XElement(ns + "MinimumCertificateKeySize", 2048),
                    new XElement(ns + "AddAppCertToTrustedStore", false),
                    Store("UserIssuerCertificates", "issuerUser"),
                    Store("TrustedUserCertificates", "trustedUser")),
                new XElement(ns + "TransportConfigurations"),
                new XElement(root.Element(ns + "TransportQuotas") ??
                    throw new XmlException("The reference transport quotas are missing.")),
                server,
                new XElement(ns + "TraceConfiguration",
                    new XElement(ns + "OutputFilePath", Path.Combine(files.Root, "logs", "reference.log")),
                    new XElement(ns + "DeleteOnLoad", false),
                    new XElement(ns + "TraceMasks", 1))));

            using Stream output = m_fileSystem.OpenWrite(Path.Combine(files.Root, ReferenceConfigurationName));
            await configuration.SaveAsync(output, SaveOptions.None, cancellationToken).ConfigureAwait(false);
        }

        private static string GetReferenceTemplate(string root)
        {
            return RepositorySamplePaths.ResolveChild(
                root,
                Path.Combine(
                    RepositorySampleCatalog.Get(RepositorySampleId.ConsoleReferenceServer).ProjectDirectory,
                    ReferenceConfigurationName));
        }

        internal const string ReferenceConfigurationName = "Quickstarts.ReferenceServer.Config.xml";
        private readonly string m_runParent;
        private readonly IFileSystem m_fileSystem;
    }

    /// <summary>
    /// An immutable launch assembled from the allowlist, never deserialized from a document.
    /// </summary>
    internal sealed class RepositorySampleLaunch
    {
        internal RepositorySampleLaunch(
            RepositorySampleDescriptor descriptor,
            RepositorySampleSource source,
            string executable,
            Uri endpoint,
            ArrayOf<string> applicationUris,
            ArrayOf<string> arguments,
            RepositorySampleRunFiles files)
        {
            Descriptor = descriptor;
            Source = source;
            Executable = executable;
            Endpoint = endpoint;
            ApplicationUris = applicationUris;
            Arguments = arguments;
            Files = files;
        }

        public RepositorySampleDescriptor Descriptor { get; }

        public RepositorySampleSource Source { get; }

        public string SourceRoot => Source.Root;

        public string Executable { get; }

        public Uri Endpoint { get; }

        public ArrayOf<string> ApplicationUris { get; }

        public ArrayOf<string> Arguments { get; }

        public RepositorySampleRunFiles Files { get; }
    }

    /// <summary>
    /// A generated run directory with an exact ownership marker. Only the service may
    /// delete it, after the owned process has exited and its output readers have drained.
    /// </summary>
    internal sealed class RepositorySampleRunFiles
    {
        internal RepositorySampleRunFiles(string parent, string root, Guid runId, IFileSystem fileSystem)
        {
            m_parent = parent;
            Root = root;
            RunId = runId;
            m_fileSystem = fileSystem;
        }

        public string Root { get; }

        public Guid RunId { get; }

        public string PkiRoot => Path.Combine(Root, "pki");

        public void EnsureInitialized()
        {
            if (!m_initialized || m_deleted)
            {
                throw new InvalidOperationException("The sample run directory is not initialized or was cleaned.");
            }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_directoryCreated || m_deleted)
            {
                throw new InvalidOperationException("An owned sample directory can only be initialized once.");
            }
            RepositorySamplePaths.EnsureNoLinks(m_parent, m_parent);
            CreatePrivateDirectory(m_parent);
            if (Directory.Exists(Root) || File.Exists(Root))
            {
                throw new IOException("The new sample run directory is already occupied.");
            }
            CreatePrivateDirectory(Root);
            m_directoryCreated = true;
            RepositorySamplePaths.EnsureNoLinks(m_parent, Root);
            using (var stream = new FileStream(
                Path.Combine(Root, MarkerName),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                m_markerCreated = true;
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes(RunId.ToString("N")), cancellationToken).ConfigureAwait(false);
            }
            m_initialized = true;
            foreach (string child in (ArrayOf<string>)["pki", "temp", "profile", "logs"])
            {
                CreatePrivateDirectory(Path.Combine(Root, child));
            }
        }

        public async Task<bool> ContainsPublicCertificateAsync(
            ByteString certificate,
            RepositorySampleId sample,
            CancellationToken cancellationToken)
        {
            EnsureInitialized();
            if (certificate.IsNull || certificate.Length is 0 or > 65536)
            {
                return false;
            }
            string directory = sample switch
            {
                RepositorySampleId.ConsoleReferenceServer => Path.Combine(PkiRoot, "own", "certs"),
                RepositorySampleId.PumpSoftwareUpdateSimulator => Path.Combine(PkiRoot, "certs"),
                _ => throw new ArgumentOutOfRangeException(nameof(sample))
            };
            RepositorySamplePaths.EnsureNoLinks(m_parent, directory);
            if (!Directory.Exists(directory))
            {
                return false;
            }
            int count = 0;
            foreach (string path in Directory.EnumerateFiles(directory, "*.der", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > 32)
                {
                    throw new IOException("The sample public-certificate directory exceeded its bounded size.");
                }
                RepositorySamplePaths.EnsureNoLinks(m_parent, path);
                using Stream stream = m_fileSystem.OpenRead(path);
                if (stream.Length != certificate.Length)
                {
                    continue;
                }
                byte[] bytes = new byte[certificate.Length];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (certificate.Span.SequenceEqual(bytes))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task CleanupAsync(CancellationToken cancellationToken)
        {
            if (m_deleted)
            {
                return;
            }
            if (!m_directoryCreated)
            {
                m_deleted = true;
                return;
            }
            RepositorySamplePaths.EnsureNoLinks(m_parent, Root);
            string marker = Path.Combine(Root, MarkerName);
            RepositorySamplePaths.EnsureNoLinks(m_parent, marker);
            if (!m_initialized)
            {
                if (m_markerCreated)
                {
                    m_fileSystem.Delete(marker);
                }
                Directory.Delete(Root, recursive: false);
                m_deleted = true;
                return;
            }
            await VerifyOwnershipAsync(cancellationToken).ConfigureAwait(false);
            var pending = new Stack<(string Path, int Depth)>();
            var files = new List<string>();
            var directories = new List<string>();
            pending.Push((Root, 0));
            while (pending.TryPop(out (string Path, int Depth) entry))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Depth > 16)
                {
                    throw new IOException("The sample directory exceeded its bounded cleanup depth.");
                }
                foreach (string child in Directory.EnumerateFileSystemEntries(entry.Path))
                {
                    RepositorySamplePaths.EnsureNoLinks(m_parent, child);
                    if (directories.Count + files.Count >= 4096)
                    {
                        throw new IOException("The sample directory exceeded its bounded cleanup entry count.");
                    }
                    if (Directory.Exists(child))
                    {
                        directories.Add(child);
                        pending.Push((child, entry.Depth + 1));
                    }
                    else if (!string.Equals(child, marker, StringComparison.Ordinal))
                    {
                        files.Add(child);
                    }
                }
            }
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RepositorySamplePaths.EnsureNoLinks(m_parent, file);
                m_fileSystem.Delete(file);
            }
            for (int index = directories.Count - 1; index >= 0; index--)
            {
                RepositorySamplePaths.EnsureNoLinks(m_parent, directories[index]);
                Directory.Delete(directories[index], recursive: false);
            }
            RepositorySamplePaths.EnsureNoLinks(m_parent, Root);
            m_fileSystem.Delete(marker);
            Directory.Delete(Root, recursive: false);
            m_deleted = true;
        }

        public async Task VerifyOwnershipAsync(CancellationToken cancellationToken)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            RepositorySamplePaths.EnsureNoLinks(m_parent, Root);
            string marker = Path.Combine(Root, MarkerName);
            RepositorySamplePaths.EnsureNoLinks(m_parent, marker);
            using Stream stream = m_fileSystem.OpenRead(marker);
            if (stream.Length != 32)
            {
                throw new IOException("The sample run ownership marker changed; the operation was refused.");
            }
            using var reader = new StreamReader(stream, Encoding.ASCII);
            string token = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(token, RunId.ToString("N"), StringComparison.Ordinal))
            {
                throw new IOException("The sample run ownership marker changed; the operation was refused.");
            }
        }

        private static void CreatePrivateDirectory(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(
                    path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        internal const string MarkerName = ".ualens-owned-run";
        private readonly string m_parent;
        private readonly IFileSystem m_fileSystem;
        private bool m_directoryCreated;
        private bool m_markerCreated;
        private bool m_initialized;
        private bool m_deleted;
    }
}
