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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;
using UaLens.Connection;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed record OpenUsdWorkflowOrigin(
        string RuleId, string TargetPrimPath, ByteString MetadataDigest, ByteString SecurityDigest);

    internal sealed record OpenUsdWorkflowPeer(
        string Id, string EndpointUrl, string ServerUri, ExpandedNodeId RepresentationId,
        string SecurityPolicyUri, Func<IUserIdentity, bool> AcceptsIdentity,
        Func<CancellationToken, Task<IConnectionSession>> OpenSession);

    /// <summary>
    /// Explicit read-only peer composition. Sessions are supplied by the host's
    /// own identity/trust configuration; primary credentials are never forwarded.
    /// </summary>
    internal sealed class OpenUsdWorkflowFederation
    {
        public OpenUsdWorkflowFederation(ArrayOf<OpenUsdWorkflowPeer> peers)
        {
            if (peers.Count is 0 or > 16)
            {
                throw new ArgumentException("Configure between one and sixteen exact federation peers.", nameof(peers));
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (OpenUsdWorkflowPeer peer in peers)
            {
                ArgumentNullException.ThrowIfNull(peer);
                ConnectionReference.Validate(peer.Id);
                ConnectionReference.ValidateUri(peer.ServerUri);
                if (!ids.Add(peer.Id) ||
                    !Uri.TryCreate(peer.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                    endpoint.UserInfo.Length != 0 ||
                    endpoint.Fragment.Length != 0 ||
                    endpoint.Query.Length != 0 ||
                    string.IsNullOrWhiteSpace(peer.SecurityPolicyUri) ||
                    peer.SecurityPolicyUri.Length > 2048 ||
                    !Uri.TryCreate(peer.SecurityPolicyUri, UriKind.Absolute, out Uri? policy) ||
                    policy.UserInfo.Length != 0 ||
                    policy.Query.Length != 0 ||
                    peer.SecurityPolicyUri == SecurityPolicies.None ||
                    peer.RepresentationId.IsNull ||
                    peer.RepresentationId.ServerIndex != 0 ||
                    (peer.RepresentationId.NamespaceIndex != 0 &&
                        string.IsNullOrEmpty(peer.RepresentationId.NamespaceUri)))
                {
                    throw new ArgumentException(
                        "Invalid, duplicated or non-portable peer configuration.", nameof(peers));
                }
                OpenUsdCompanionExporter.ValidatePrimPath("/" + peer.Id);
                ArgumentNullException.ThrowIfNull(peer.AcceptsIdentity);
                ArgumentNullException.ThrowIfNull(peer.OpenSession);
            }
            m_peers = peers.ConvertAll(static peer => peer);
        }

        public async Task<ArrayOf<OpenUsdWorkflowOrigin>> PrepareAsync(
            CompanionContext context, OpenUsdConnector.RepresentationInfo representation,
            Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> createReader,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(representation);
            ArgumentNullException.ThrowIfNull(createReader);
            cancellationToken.ThrowIfCancellationRequested();
            var origins = new List<OpenUsdWorkflowOrigin>();
            await VisitAsync(context, representation, createReader, origins,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<ISession>(System.Collections.Generic.ReferenceEqualityComparer.Instance)
                {
                    context.Session
                },
                "/Peers", 0, cancellationToken).ConfigureAwait(false);
            if (origins.Count == 0)
            {
                throw new InvalidOperationException("No enabled configured peer components were advertised.");
            }
            return [.. origins];
        }

        public static string Review(ArrayOf<OpenUsdWorkflowOrigin> origins)
        {
            return $"Review {origins.Count} configured peer representation(s). " +
                "Only metadata and telemetry are read. No asset dependencies, remote commands or primary credentials " +
                "are fetched or forwarded; export contains local override values, not a complete federated stage.";
        }

        public async Task<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, OpenUsdWorkflowTaskInput task,
            OpenUsdConnector.RepresentationInfo representation,
            Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> createReader,
            IOpenUsdWorkflowExporter exporter, IProgress<CompanionTaskProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(representation);
            ArgumentNullException.ThrowIfNull(createReader);
            ArgumentNullException.ThrowIfNull(exporter);
            cancellationToken.ThrowIfCancellationRequested();
            task.RequireMatch(context, task.Target, task.OperationId);
            task.RequireMetadata(context, representation);
            ArrayOf<OpenUsdWorkflowOrigin> current = await PrepareAsync(
                context, representation, createReader, cancellationToken).ConfigureAwait(false);
            if (current != task.Origins)
            {
                throw new InvalidOperationException("Configured peer metadata changed. Prepare composition again.");
            }
            var samples = new List<OpenUsdWorkflowSample>();
            for (int index = 0; index < current.Count; index++)
            {
                OpenUsdWorkflowOrigin origin = current[index];
                OpenUsdWorkflowPeer peer = GetPeer(origin.RuleId);
                progress?.Report(new CompanionTaskProgress($"Reading configured peer {peer.Id}."));
                IConnectionSession lease = await peer.OpenSession(cancellationToken).ConfigureAwait(false);
                RequireOwnedSession(lease, [context.Session]);
                await using (lease.ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CompanionContext remote = RequirePeer(context, peer, lease);
                    CompanionOperationDraft evidence = CapturePeer(remote, peer);
                    RequirePeerEvidence(context, peer, lease, remote, evidence, origin.SecurityDigest);
                    IOpenUsdCompanionReader reader = createReader(remote, OpenUsdCompanionProvider.CreateOptions());
                    await using (reader.ConfigureAwait(false))
                    {
                        var target = ExpandedNodeId.ToNodeId(peer.RepresentationId, remote.Session.NamespaceUris);
                        OpenUsdConnector.RepresentationInfo source = await OpenUsdCompanionProvider.FindAsync(
                            reader, remote, target, cancellationToken).ConfigureAwait(false);
                        if (OpenUsdWorkflowMetadata.Digest(remote, source) != origin.MetadataDigest)
                        {
                            throw new InvalidOperationException("The peer changed while acquiring composition.");
                        }
                        ArrayOf<OpenUsdConnector.BindingInfo> bindings =
                            OpenUsdWorkflowTasks.Bindings(source, "read-bindings");
                        for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                        {
                            OpenUsdConnector.BindingInfo binding = bindings[bindingIndex];
                            DataValue value = await reader.ReadValueAsync(binding.SourceNodeId, cancellationToken)
                                .ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            RequirePeerEvidence(context, peer, lease, remote, evidence, origin.SecurityDigest);
                            OpenUsdWorkflowTasks.AddSample(remote, task, samples, binding, value,
                                origin: peer.Id, primPath: origin.TargetPrimPath + binding.PrimPath);
                        }
                        OpenUsdConnector.RepresentationInfo after = await OpenUsdCompanionProvider.FindAsync(
                            reader, remote, target, cancellationToken).ConfigureAwait(false);
                        RequirePeerEvidence(context, peer, lease, remote, evidence, origin.SecurityDigest);
                        if (OpenUsdWorkflowMetadata.Digest(remote, after) != origin.MetadataDigest)
                        {
                            throw new InvalidOperationException("Peer metadata changed during acquisition.");
                        }
                    }
                }
            }
            task.RequireMatch(context, task.Target, task.OperationId);
            IOpenUsdCompanionReader primaryReader = createReader(context, OpenUsdCompanionProvider.CreateOptions());
            await using (primaryReader.ConfigureAwait(false))
            {
                task.RequireMetadata(context, await OpenUsdCompanionProvider.FindAsync(
                    primaryReader, context, task.Target.NodeId, cancellationToken).ConfigureAwait(false));
            }
            cancellationToken.ThrowIfCancellationRequested();
            task.RequireMatch(context, task.Target, task.OperationId);
            if (task.Destination is not null)
            {
                await exporter.WriteAsync(context, task, current, [], [.. samples], cancellationToken)
                    .ConfigureAwait(false);
            }
            return OpenUsdWorkflowTasks.Result(context, task, [.. samples], "Configured peer read completed",
                Review(current));
        }

        private async Task VisitAsync(
            CompanionContext primary, OpenUsdConnector.RepresentationInfo representation,
            Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> createReader,
            List<OpenUsdWorkflowOrigin> origins, HashSet<string> visited, HashSet<ISession> activeSessions,
            string parentPath, int depth,
            CancellationToken cancellationToken)
        {
            foreach (OpenUsdConnector.ComponentInfo component in representation.Components)
            {
                if (!component.Enabled || !OpenUsdCompanionProvider.IsRemote(component))
                {
                    continue;
                }
                if (depth >= 4)
                {
                    throw new InvalidOperationException("Configured composition exceeds its depth budget.");
                }
                OpenUsdWorkflowPeer? peer = null;
                foreach (OpenUsdWorkflowPeer configured in m_peers)
                {
                    if (configured.EndpointUrl == component.ComponentEndpointUrl &&
                        configured.ServerUri == component.ComponentServerUri)
                    {
                        if (peer is not null)
                        {
                            throw new InvalidOperationException("Ambiguous configured federation origin.");
                        }
                        peer = configured;
                    }
                }
                if (peer is null || !visited.Add(peer.Id) || origins.Count >= 16)
                {
                    throw new InvalidOperationException("Unconfigured, cyclic or excessive federation origin.");
                }
                string prim = parentPath + "/" + peer.Id;
                OpenUsdCompanionExporter.ValidatePrimPath(prim);
                cancellationToken.ThrowIfCancellationRequested();
                IConnectionSession lease = await peer.OpenSession(cancellationToken).ConfigureAwait(false);
                ISession ownedSession = RequireOwnedSession(lease, activeSessions);
                await using (lease.ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CompanionContext remote = RequirePeer(primary, peer, lease);
                    var id = ExpandedNodeId.ToNodeId(peer.RepresentationId, remote.Session.NamespaceUris);
                    if (id.IsNull ||
                        (!component.ComponentRepresentation.IsNull && component.ComponentRepresentation != id))
                    {
                        throw new InvalidOperationException(
                            "The advertised peer representation does not match its configured target.");
                    }
                    CompanionOperationDraft evidence = CapturePeer(remote, peer);
                    ByteString securityDigest = SecurityDigest(remote);
                    IOpenUsdCompanionReader reader = createReader(remote, OpenUsdCompanionProvider.CreateOptions());
                    await using (reader.ConfigureAwait(false))
                    {
                        OpenUsdConnector.RepresentationInfo source = await OpenUsdCompanionProvider.FindAsync(
                            reader, remote, id, cancellationToken).ConfigureAwait(false);
                        RequirePeerEvidence(primary, peer, lease, remote, evidence, securityDigest);
                        ByteString metadataDigest = OpenUsdWorkflowMetadata.Digest(remote, source);
                        origins.Add(new OpenUsdWorkflowOrigin(peer.Id, prim,
                            metadataDigest, securityDigest));
                        activeSessions.Add(ownedSession);
                        try
                        {
                            await VisitAsync(primary, source, createReader, origins, visited, activeSessions,
                                prim, depth + 1, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            activeSessions.Remove(ownedSession);
                        }
                        RequirePeerEvidence(primary, peer, lease, remote, evidence, securityDigest);
                        if (metadataDigest != OpenUsdWorkflowMetadata.Digest(remote, source))
                        {
                            throw new InvalidOperationException("Peer metadata changed during discovery.");
                        }
                    }
                }
            }
        }

        private static CompanionContext RequirePeer(
            CompanionContext primary, OpenUsdWorkflowPeer peer, IConnectionSession owner)
        {
            ISession session = owner.Session;
            if (ReferenceEquals(session, primary.Session) ||
                !session.Connected ||
                session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                session.Endpoint.SecurityPolicyUri != peer.SecurityPolicyUri ||
                session.Endpoint.EndpointUrl != peer.EndpointUrl ||
                session.Endpoint.Server?.ApplicationUri != peer.ServerUri ||
                !peer.AcceptsIdentity(session.Identity))
            {
                throw new InvalidOperationException(
                    "The peer session does not match its independently configured identity.");
            }
            return new CompanionContext(session, primary.Telemetry, primary.MaxTargets, primary.MaxFields);
        }

        private static ISession RequireOwnedSession(IConnectionSession owner, IEnumerable<ISession> borrowed)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ISession session = owner.Session ??
                throw new InvalidOperationException("The configured peer factory returned no session.");
            foreach (ISession existing in borrowed)
            {
                if (ReferenceEquals(session, existing))
                {
                    throw new InvalidOperationException(
                        "A peer factory returned a borrowed session; ownership refused.");
                }
            }
            return session;
        }

        private static CompanionOperationDraft CapturePeer(CompanionContext context, OpenUsdWorkflowPeer peer)
        {
            return new CompanionOperationDraft(
                new CompanionTarget("openusd", ExpandedNodeId.ToNodeId(
                    peer.RepresentationId, context.Session.NamespaceUris), peer.Id, "OpenUsdRepresentation"),
                new CompanionOperation("read-peer", "Read configured peer", CompanionOperationSafety.ReadOnly),
                null, context.Session, DateTimeOffset.MaxValue);
        }

        private static ByteString SecurityDigest(CompanionContext context)
        {
            ByteString certificate = context.Session.Endpoint.ServerCertificate;
            if (certificate.IsNull || certificate.Length is 0 or > 65536)
            {
                throw new InvalidOperationException(
                    "The secure peer certificate evidence is unavailable or oversized.");
            }
            return ByteString.From(SHA256.HashData(certificate.Span));
        }

        private static void RequirePeerEvidence(
            CompanionContext primary, OpenUsdWorkflowPeer peer, IConnectionSession owner,
            CompanionContext captured, CompanionOperationDraft evidence, ByteString securityDigest)
        {
            CompanionContext current = RequirePeer(primary, peer, owner);
            if (!ReferenceEquals(current.Session, captured.Session) ||
                !evidence.Matches(current.Session, TimeProvider.System.GetUtcNow()) ||
                SecurityDigest(current) != securityDigest)
            {
                throw new InvalidOperationException("The configured peer session or security identity changed.");
            }
        }

        private OpenUsdWorkflowPeer GetPeer(string id)
        {
            foreach (OpenUsdWorkflowPeer peer in m_peers)
            {
                if (peer.Id == id)
                {
                    return peer;
                }
            }
            throw new InvalidOperationException("The prepared peer is no longer configured.");
        }

        private readonly ArrayOf<OpenUsdWorkflowPeer> m_peers;
    }
}
