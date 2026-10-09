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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Server.AliasNames
{
    /// <summary>
    /// Reads the AliasNames of a registered Server over a client Session:
    /// browses the <c>AliasNameCategoryType</c> hierarchy below the
    /// well-known <c>Aliases</c> object and the <c>AliasFor</c> references
    /// of every <c>AliasNameType</c> instance in it (OPC 10000-17 §6,
    /// Annex C.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reader browses rather than calling <c>FindAlias</c> because the
    /// category hierarchy, which the GDS has to aggregate as well, is only
    /// visible in the address space, and because a Server whose aliases
    /// come from a NodeSet may expose <c>FindAlias</c> without implementing
    /// it.
    /// </para>
    /// <para>
    /// It connects anonymously, to the most secure endpoint first; when that
    /// fails (typically because the source does not trust the GDS
    /// certificate) it falls back to an endpoint without message security,
    /// if the source offers one.
    /// </para>
    /// </remarks>
    public sealed class SessionAliasNameSourceReader : IAliasNameSourceReader
    {
        /// <summary>
        /// Creates a reader.
        /// </summary>
        /// <param name="configuration">The configuration of the GDS; its
        /// application instance certificate and trust lists are used for
        /// the client connections.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="sessionFactory">Optional session factory.</param>
        public SessionAliasNameSourceReader(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            ISessionFactory? sessionFactory = null)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<SessionAliasNameSourceReader>();
            // A server configuration has no client section, which the
            // Session requires; the copy shares certificates and trust lists.
            m_configuration = new ApplicationConfiguration(configuration)
            {
                ClientConfiguration = configuration.ClientConfiguration ?? new ClientConfiguration()
            };
            m_sessionFactory = sessionFactory ?? new DefaultSessionFactory(telemetry);
        }

        /// <summary>
        /// Timeout of the discovery and service calls, in milliseconds.
        /// </summary>
        public int OperationTimeout { get; set; } = 10000;

        /// <summary>
        /// Maximum depth of nested categories followed below <c>Aliases</c>.
        /// </summary>
        public int MaxCategoryDepth { get; set; } = 16;

        /// <summary>
        /// Maximum number of nodes browsed per source.
        /// </summary>
        public int MaxNodes { get; set; } = 100000;

        /// <inheritdoc/>
        public async ValueTask<AliasNameSourceSnapshot> ReadAsync(
            ApplicationRecordDataType application,
            CancellationToken ct = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (string.IsNullOrEmpty(application.ApplicationUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The application has no ApplicationUri.");
            }

            Exception? lastError = null;
            foreach (string discoveryUrl in OrderDiscoveryUrls(application.DiscoveryUrls))
            {
                // One GetEndpoints per URL: an unreachable source then costs
                // a single failed connect.
                Uri url;
                ArrayOf<EndpointDescription> endpoints;
                try
                {
                    (url, endpoints) = await GetEndpointsAsync(discoveryUrl, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    m_logger.AliasNameSourceConnectFailed(
                        application.ApplicationUri, discoveryUrl, true, ex.Message);
                    lastError = ex;
                    continue;
                }

                EndpointDescription? secureEndpoint = null;
                foreach (bool useSecurity in s_securityPreference)
                {
                    EndpointDescription? endpoint = CoreClientUtils.SelectEndpoint(
                        m_configuration, url, endpoints, useSecurity, m_telemetry);
                    if (endpoint == null ||
                        (!useSecurity && endpoint.SecurityMode != MessageSecurityMode.None) ||
                        ReferenceEquals(endpoint, secureEndpoint))
                    {
                        continue;
                    }
                    if (useSecurity)
                    {
                        secureEndpoint = endpoint;
                    }
                    try
                    {
                        return await ReadFromEndpointAsync(application, WithHost(endpoint, url), ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        m_logger.AliasNameSourceConnectFailed(
                            application.ApplicationUri, discoveryUrl, useSecurity, ex.Message);
                        lastError = ex;
                    }
                }
            }

            string message = "Could not read the AliasNames of " + application.ApplicationUri + ".";
            throw lastError != null
                ? new ServiceResultException(StatusCodes.BadCommunicationError, message, lastError)
                : new ServiceResultException(StatusCodes.BadCommunicationError, message);
        }

        private async ValueTask<(Uri Url, ArrayOf<EndpointDescription> Endpoints)> GetEndpointsAsync(
            string discoveryUrl,
            CancellationToken ct)
        {
            Uri uri = CoreClientUtils.GetDiscoveryUrl(discoveryUrl);
            var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            endpointConfiguration.OperationTimeout = OperationTimeout;
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                m_configuration,
                uri,
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);
            return (uri, endpoints);
        }

        /// <summary>
        /// Connects to the host and port the source was reached at, as
        /// <see cref="CoreClientUtils.SelectEndpointAsync(ApplicationConfiguration, string, bool, ITelemetryContext, CancellationToken)"/>
        /// does: a Server often advertises a host name the GDS cannot resolve.
        /// </summary>
        private static EndpointDescription WithHost(EndpointDescription endpoint, Uri discoveryUrl)
        {
            Uri? endpointUrl = Utils.ParseUri(endpoint.EndpointUrl);
            if (endpointUrl == null || endpointUrl.Scheme != discoveryUrl.Scheme)
            {
                return endpoint;
            }
            var copy = (EndpointDescription)endpoint.Clone();
            copy.EndpointUrl = new UriBuilder(endpointUrl)
            {
                Host = discoveryUrl.IdnHost,
                Port = discoveryUrl.Port
            }.ToString();
            return copy;
        }

        private async ValueTask<AliasNameSourceSnapshot> ReadFromEndpointAsync(
            ApplicationRecordDataType application,
            EndpointDescription endpoint,
            CancellationToken ct)
        {
            var configuredEndpoint = new ConfiguredEndpoint(
                null,
                endpoint,
                EndpointConfiguration.Create(m_configuration));

            ISession session = await m_sessionFactory.CreateAsync(
                m_configuration,
                configuredEndpoint,
                false,
                false,
                "GDS AliasName aggregation",
                (uint)Math.Max(OperationTimeout * 6, 60000),
                new UserIdentity(),
                default,
                ct).ConfigureAwait(false);
            try
            {
                var walker = new Walker(this, session, application.ApplicationUri!, ct);
                await walker.WalkAsync().ConfigureAwait(false);
                return new AliasNameSourceSnapshot(
                    application.ApplicationUri!,
                    walker.Categories,
                    walker.Aliases);
            }
            finally
            {
                try
                {
                    await session.CloseAsync(OperationTimeout, true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.AliasNameSourceCloseFailed(application.ApplicationUri, ex.Message);
                }
                session.Dispose();
            }
        }

        private static IEnumerable<string> OrderDiscoveryUrls(ArrayOf<string> discoveryUrls)
        {
            // opc.tcp first: it is the transport every Server implements.
            return (discoveryUrls.ToArray() ?? [])
                .Where(url => !string.IsNullOrEmpty(url))
                .OrderBy(url => url.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        }

        /// <summary>
        /// One browse pass over the alias hierarchy of a source.
        /// </summary>
        private sealed class Walker
        {
            public Walker(
                SessionAliasNameSourceReader reader,
                ISession session,
                string serverUri,
                CancellationToken ct)
            {
                m_reader = reader;
                m_session = session;
                m_serverUri = serverUri;
                m_ct = ct;
                m_browser = new Browser(reader.m_telemetry, new BrowserOptions
                {
                    BrowseDirection = BrowseDirection.Forward,
                    ResultMask = (uint)BrowseResultMask.All
                })
                {
                    Session = session
                };
            }

            public List<AliasNameSourceCategory> Categories { get; } = [];

            public List<AliasNameSourceAlias> Aliases { get; } = [];

            public async ValueTask WalkAsync()
            {
                await VisitCategoryAsync(Ua.ObjectIds.Aliases, [], [Ua.ObjectIds.Aliases])
                    .ConfigureAwait(false);
            }

            private async ValueTask VisitCategoryAsync(
                NodeId categoryId,
                List<string> path,
                HashSet<NodeId> ancestors)
            {
                ArrayOf<ReferenceDescription> children = await BrowseAsync(
                    categoryId,
                    ReferenceTypeIds.HierarchicalReferences,
                    (uint)NodeClass.Object).ConfigureAwait(false);
                foreach (ReferenceDescription reference in children.ToArray() ?? [])
                {
                    NodeId childId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                    NodeId typeDefinition = ExpandedNodeId.ToNodeId(
                        reference.TypeDefinition, m_session.NamespaceUris);
                    if (childId.IsNull || typeDefinition.IsNull || string.IsNullOrEmpty(reference.BrowseName.Name))
                    {
                        continue;
                    }

                    if (await IsTypeOfAsync(typeDefinition, Ua.ObjectTypeIds.AliasNameCategoryType)
                        .ConfigureAwait(false))
                    {
                        if (ancestors.Contains(childId) || path.Count >= m_reader.MaxCategoryDepth)
                        {
                            continue;
                        }
                        List<string> childPath = [.. path, CategoryName(childId, reference.BrowseName.Name!)];
                        Categories.Add(new AliasNameSourceCategory(childPath));
                        ancestors.Add(childId);
                        await VisitCategoryAsync(childId, childPath, ancestors).ConfigureAwait(false);
                        ancestors.Remove(childId);
                    }
                    else if (await IsTypeOfAsync(typeDefinition, Ua.ObjectTypeIds.AliasNameType)
                        .ConfigureAwait(false))
                    {
                        await VisitAliasAsync(childId, reference.BrowseName.Name!, path)
                            .ConfigureAwait(false);
                    }
                }
            }

            private async ValueTask VisitAliasAsync(NodeId aliasId, string name, List<string> path)
            {
                ArrayOf<ReferenceDescription> targets = await BrowseAsync(
                    aliasId,
                    ReferenceTypeIds.AliasFor,
                    0).ConfigureAwait(false);
                foreach (ReferenceDescription reference in targets.ToArray() ?? [])
                {
                    (ExpandedNodeId target, string serverUri) = ToAbsolute(reference.NodeId);
                    if (target.IsNull)
                    {
                        continue;
                    }
                    Aliases.Add(new AliasNameSourceAlias(
                        path,
                        name,
                        reference.ReferenceTypeId,
                        target,
                        serverUri));
                }
            }

            /// <summary>
            /// Converts a target into the namespace URI form, and a target
            /// that the source itself references on another Server into
            /// that Server's URI.
            /// </summary>
            private (ExpandedNodeId Target, string ServerUri) ToAbsolute(ExpandedNodeId target)
            {
                if (target.IsNull)
                {
                    return (ExpandedNodeId.Null, m_serverUri);
                }

                string serverUri = m_serverUri;
                if (target.ServerIndex != 0)
                {
                    string? remoteUri = m_session.ServerUris.GetString(target.ServerIndex);
                    if (string.IsNullOrEmpty(remoteUri))
                    {
                        return (ExpandedNodeId.Null, m_serverUri);
                    }
                    serverUri = remoteUri!;
                    target = target.WithServerIndex(0);
                }
                if (string.IsNullOrEmpty(target.NamespaceUri) && target.NamespaceIndex != 0)
                {
                    string? namespaceUri = m_session.NamespaceUris.GetString(target.NamespaceIndex);
                    if (string.IsNullOrEmpty(namespaceUri))
                    {
                        return (ExpandedNodeId.Null, m_serverUri);
                    }
                    target = target.WithNamespaceUri(namespaceUri);
                }
                return (target, serverUri);
            }

            private static string CategoryName(NodeId categoryId, string browseName)
            {
                if (categoryId == Ua.ObjectIds.TagVariables)
                {
                    return Ua.BrowseNames.TagVariables;
                }
                if (categoryId == Ua.ObjectIds.Topics)
                {
                    return Ua.BrowseNames.Topics;
                }
                return browseName;
            }

            private ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
                NodeId nodeId,
                NodeId referenceTypeId,
                uint nodeClassMask)
            {
                if (++m_browsed > m_reader.MaxNodes)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        "The AliasName hierarchy of " + m_serverUri + " exceeds the browse limit.");
                }
                m_ct.ThrowIfCancellationRequested();
                m_browser.ReferenceTypeId = referenceTypeId;
                m_browser.IncludeSubtypes = true;
                m_browser.NodeClassMask = nodeClassMask;
                return m_browser.BrowseAsync(nodeId, m_ct);
            }

            /// <summary>
            /// Returns <c>true</c> if <paramref name="typeId"/> is
            /// <paramref name="baseTypeId"/> or a subtype, following the
            /// source's HasSubtype references.
            /// </summary>
            private async ValueTask<bool> IsTypeOfAsync(NodeId typeId, NodeId baseTypeId)
            {
                for (int depth = 0; depth < 16 && !typeId.IsNull; depth++)
                {
                    if (typeId == baseTypeId)
                    {
                        return true;
                    }
                    if (!m_supertypes.TryGetValue(typeId, out NodeId supertype))
                    {
                        m_browser.BrowseDirection = BrowseDirection.Inverse;
                        try
                        {
                            ArrayOf<ReferenceDescription> references = await BrowseAsync(
                                typeId,
                                ReferenceTypeIds.HasSubtype,
                                (uint)NodeClass.ObjectType).ConfigureAwait(false);
                            supertype = references.Count > 0
                                ? ExpandedNodeId.ToNodeId(references[0].NodeId, m_session.NamespaceUris)
                                : NodeId.Null;
                        }
                        finally
                        {
                            m_browser.BrowseDirection = BrowseDirection.Forward;
                        }
                        m_supertypes[typeId] = supertype;
                    }
                    typeId = supertype;
                }
                return false;
            }

            private readonly SessionAliasNameSourceReader m_reader;
            private readonly ISession m_session;
            private readonly string m_serverUri;
            private readonly CancellationToken m_ct;
            private readonly Browser m_browser;
            private readonly Dictionary<NodeId, NodeId> m_supertypes = [];
            private int m_browsed;
        }

        private static readonly bool[] s_securityPreference = [true, false];
        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly ISessionFactory m_sessionFactory;
    }

    /// <summary>
    /// Source-generated log messages for SessionAliasNameSourceReader.
    /// </summary>
    internal static partial class SessionAliasNameSourceReaderLog
    {
        [LoggerMessage(EventId = GdsServerCommonEventIds.AliasNameAggregation + 0, Level = LogLevel.Warning,
            Message = "AliasName source {ApplicationUri}: reading via {DiscoveryUrl} (secure: {Secure}) failed: {Error}")]
        public static partial void AliasNameSourceConnectFailed(
            this ILogger logger, string? applicationUri, string discoveryUrl, bool secure, string error);

        [LoggerMessage(EventId = GdsServerCommonEventIds.AliasNameAggregation + 1, Level = LogLevel.Debug,
            Message = "AliasName source {ApplicationUri}: closing the session failed: {Error}")]
        public static partial void AliasNameSourceCloseFailed(
            this ILogger logger, string? applicationUri, string error);
    }
}
