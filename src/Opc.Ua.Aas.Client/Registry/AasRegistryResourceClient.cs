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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.Aas.Client.Registry
{
    /// <summary>
    /// Base wrapper for one AAS registry resource file.
    /// </summary>
    public abstract class AasRegistryResourceClient
    {
        /// <summary>
        /// Creates a resource wrapper.
        /// </summary>
        protected AasRegistryResourceClient(
            ISession session,
            NodeId groupNodeId,
            NodeId resourceNodeId,
            ResourceTypeClient proxy,
            string sourceIdentityPropertyName,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (groupNodeId.IsNull)
            {
                throw new ArgumentException("A group NodeId is required.", nameof(groupNodeId));
            }
            if (resourceNodeId.IsNull)
            {
                throw new ArgumentException("A resource NodeId is required.", nameof(resourceNodeId));
            }
            if (proxy is null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }
            if (string.IsNullOrEmpty(sourceIdentityPropertyName))
            {
                throw new ArgumentException(
                    "A source identity Property name is required.",
                    nameof(sourceIdentityPropertyName));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            Session = session;
            GroupNodeId = groupNodeId;
            ResourceNodeId = resourceNodeId;
            Proxy = proxy;
            SourceIdentityPropertyName = sourceIdentityPropertyName;
            Telemetry = telemetry;
        }

        /// <summary>
        /// OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Parent group NodeId.
        /// </summary>
        public NodeId GroupNodeId { get; }

        /// <summary>
        /// Resource version Object NodeId.
        /// </summary>
        public NodeId ResourceNodeId { get; }

        /// <summary>
        /// Source-generated resource proxy.
        /// </summary>
        public ResourceTypeClient Proxy { get; }

        /// <summary>
        /// Telemetry context for generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// BrowseName of the source identity Property.
        /// </summary>
        protected string SourceIdentityPropertyName { get; }

        /// <summary>
        /// Reads the source identity defined by AAS clause 6.5.3.
        /// </summary>
        public ValueTask<string> ReadSourceIdentityAsync(CancellationToken ct = default)
        {
            return AasRegistryNodeReader.ReadRequiredStringPropertyAsync(
                Session,
                ResourceNodeId,
                Session.NamespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3),
                SourceIdentityPropertyName,
                ct);
        }

        /// <summary>
        /// Downloads this resource document through the inherited FileType operations.
        /// </summary>
        public ValueTask<ByteString> DownloadAsync(
            int chunkSize = ResourceTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            return Proxy.ReadDocumentAsync(chunkSize, ct);
        }

        /// <summary>
        /// Lists all version files for this resource by browsing the owning group.
        /// </summary>
        /// <example>
        /// <code>
        /// ArrayOf&lt;AasRegistryResourceVersionInfo&gt; versions =
        ///     await submodel.ListVersionsAsync(ct);
        /// </code>
        /// </example>
        public async ValueTask<ArrayOf<AasRegistryResourceVersionInfo>> ListVersionsAsync(
            CancellationToken ct = default)
        {
            ushort xregistryNs = Session.NamespaceUris.GetIndexOrAppend(Opc.Ua.XRegistry.Namespaces.xRegistry);
            string resourceId = await AasRegistryNodeReader.ReadRequiredStringPropertyAsync(
                Session,
                ResourceNodeId,
                xregistryNs,
                "ResourceId",
                ct).ConfigureAwait(false);
            ArrayOf<NodeId> candidates = await AasRegistryNodeReader.BrowseOrganizedObjectsAsync(
                Session,
                GroupNodeId,
                ct).ConfigureAwait(false);
            var versions = new List<AasRegistryResourceVersionInfo>();
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidateResourceId = await AasRegistryNodeReader.ReadOptionalStringPropertyAsync(
                    Session,
                    candidates[i],
                    xregistryNs,
                    "ResourceId",
                    ct).ConfigureAwait(false);
                if (!string.Equals(candidateResourceId, resourceId, StringComparison.Ordinal))
                {
                    continue;
                }
                string versionId = await AasRegistryNodeReader.ReadOptionalStringPropertyAsync(
                    Session,
                    candidates[i],
                    xregistryNs,
                    "VersionId",
                    ct).ConfigureAwait(false);
                DateTime createdAt = await AasRegistryNodeReader.ReadOptionalDateTimePropertyAsync(
                    Session,
                    candidates[i],
                    xregistryNs,
                    "CreatedAt",
                    ct).ConfigureAwait(false);
                DateTime modifiedAt = await AasRegistryNodeReader.ReadOptionalDateTimePropertyAsync(
                    Session,
                    candidates[i],
                    xregistryNs,
                    "ModifiedAt",
                    ct).ConfigureAwait(false);
                versions.Add(new AasRegistryResourceVersionInfo(
                    candidates[i],
                    candidateResourceId,
                    versionId,
                    createdAt,
                    modifiedAt));
            }

            versions.Sort(static (left, right) => left.CreatedAt.CompareTo(right.CreatedAt));
            return versions.ToArrayOf();
        }

        /// <summary>
        /// Resolves the newest version not later than <paramref name="momentUtc"/>.
        /// </summary>
        public async ValueTask<AasRegistryResourceVersionInfo?> ResolveVersionAsOfAsync(
            DateTime momentUtc,
            CancellationToken ct = default)
        {
            ArrayOf<AasRegistryResourceVersionInfo> versions = await ListVersionsAsync(ct).ConfigureAwait(false);
            AasRegistryResourceVersionInfo? selected = null;
            for (int i = 0; i < versions.Count; i++)
            {
                if (versions[i].CreatedAt <= momentUtc &&
                    (selected is null || versions[i].CreatedAt >= selected.CreatedAt))
                {
                    selected = versions[i];
                }
            }
            return selected;
        }
    }

    /// <summary>
    /// Client for an <c>AASSubmodelFileType</c> resource.
    /// </summary>
    public sealed class AasSubmodelFileClient : AasRegistryResourceClient
    {
        /// <summary>
        /// Creates a submodel document client.
        /// </summary>
        public AasSubmodelFileClient(
            ISession session,
            NodeId groupNodeId,
            NodeId resourceNodeId,
            ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                resourceNodeId,
                new AASSubmodelFileTypeClient(session, resourceNodeId, telemetry),
                "SubmodelIdentifier",
                telemetry)
        {
            Proxy = new AASSubmodelFileTypeClient(session, resourceNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS submodel file proxy.
        /// </summary>
        public new AASSubmodelFileTypeClient Proxy { get; }
    }

    /// <summary>
    /// Client for an <c>AASConceptDescriptionFileType</c> resource.
    /// </summary>
    public sealed class AasConceptDescriptionFileClient : AasRegistryResourceClient
    {
        /// <summary>
        /// Creates a concept description document client.
        /// </summary>
        public AasConceptDescriptionFileClient(
            ISession session,
            NodeId groupNodeId,
            NodeId resourceNodeId,
            ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                resourceNodeId,
                new AASConceptDescriptionFileTypeClient(session, resourceNodeId, telemetry),
                "ConceptIdentifier",
                telemetry)
        {
            Proxy = new AASConceptDescriptionFileTypeClient(session, resourceNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS concept description file proxy.
        /// </summary>
        public new AASConceptDescriptionFileTypeClient Proxy { get; }
    }

    /// <summary>
    /// Client for an <c>AASPackageFileType</c> resource.
    /// </summary>
    public sealed class AasPackageFileClient : AasRegistryResourceClient
    {
        /// <summary>
        /// Creates a package document client.
        /// </summary>
        public AasPackageFileClient(
            ISession session,
            NodeId groupNodeId,
            NodeId resourceNodeId,
            ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                resourceNodeId,
                new AASPackageFileTypeClient(session, resourceNodeId, telemetry),
                "PackageIdentifier",
                telemetry)
        {
            Proxy = new AASPackageFileTypeClient(session, resourceNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS package file proxy.
        /// </summary>
        public new AASPackageFileTypeClient Proxy { get; }

        /// <summary>
        /// Downloads the package and verifies its published digest before returning bytes.
        /// </summary>
        /// <example>
        /// <code>
        /// AasVerifiedPackage package = await packageClient.DownloadAsync(ct: ct);
        /// UsePackage(package.Content);
        /// </code>
        /// </example>
        public new async ValueTask<AasVerifiedPackage> DownloadAsync(
            int chunkSize = ResourceTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            ushort aasNs = Session.NamespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            string digest = await AasRegistryNodeReader.ReadRequiredStringPropertyAsync(
                Session,
                ResourceNodeId,
                aasNs,
                "Digest",
                ct).ConfigureAwait(false);
            string digestAlg = await AasRegistryNodeReader.ReadRequiredStringPropertyAsync(
                Session,
                ResourceNodeId,
                aasNs,
                "DigestAlg",
                ct).ConfigureAwait(false);
            ByteString content = await Proxy.ReadDocumentAsync(chunkSize, ct).ConfigureAwait(false);
            AasPackageDigestVerifier.Verify(content, digestAlg, digest);
            return new AasVerifiedPackage(content, digestAlg, digest);
        }
    }

    /// <summary>
    /// Client for an <c>AASEnvironmentFileType</c> resource.
    /// </summary>
    public sealed class AasEnvironmentFileClient : AasRegistryResourceClient
    {
        /// <summary>
        /// Creates an environment document client.
        /// </summary>
        public AasEnvironmentFileClient(
            ISession session,
            NodeId groupNodeId,
            NodeId resourceNodeId,
            ITelemetryContext telemetry)
            : base(
                session,
                groupNodeId,
                resourceNodeId,
                new AASEnvironmentFileTypeClient(session, resourceNodeId, telemetry),
                "EnvironmentIdentifier",
                telemetry)
        {
            Proxy = new AASEnvironmentFileTypeClient(session, resourceNodeId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS environment file proxy.
        /// </summary>
        public new AASEnvironmentFileTypeClient Proxy { get; }
    }

    internal static class AasPackageDigestVerifier
    {
        public static void Verify(ByteString content, string digestAlg, string expectedDigest)
        {
            if (string.IsNullOrEmpty(expectedDigest))
            {
                throw new ServiceResultException(StatusCodes.BadDataEncodingInvalid, "Package Digest is required.");
            }
            if (!AasDigest.IsSupportedAlgorithm(digestAlg))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDataEncodingInvalid,
                    "DigestAlg must be exactly Sha256, Sha384 or Sha512.");
            }
            if (!AasDigest.Matches(content, digestAlg, expectedDigest))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDataEncodingInvalid,
                    "Package digest verification failed.");
            }
        }
    }
}
