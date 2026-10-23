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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Secret-safe standard SKS workflows. Raw GetSecurityKeys outputs never cross the MCP serialization boundary.
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubSecurityTools
    {
        /// <summary>
        /// Creates tools over existing sessions, protected stores and explicit target authority.
        /// </summary>
        public PubSubSecurityTools(
            OpcUaSessionManager sessions, McpPubSubOptions options, McpSecretReferences secrets,
            McpPubSubKeyReferences references, McpNetworkPolicy network, TimeProvider time)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_references = references ?? throw new ArgumentNullException(nameof(references));
            m_network = network ?? throw new ArgumentNullException(nameof(network));
            m_time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <summary>
        /// Reads only public SecurityGroup metadata and real optional properties.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_inspect", ReadOnly = true)]
        [Description("Return public SecurityGroup metadata, policy/lifetime and supported optional state. Requires " +
            "explicit SKS host/caller/session/target authority. Never calls GetSecurityKeys or returns key bytes.")]
        public Task<CallToolResult> InspectAsync(
            [Description(PubSubMcpFilters.SecurityGroupNodeIdDescription)]
            string groupNodeId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_inspect", groupNodeId, ObjectTypeIds.SecurityGroupType,
                (info, node, token) => McpPubSubRemote.PropertiesAsync(info.Session, node,
                    [BrowseNames.SecurityGroupId, BrowseNames.SecurityPolicyUri, BrowseNames.KeyLifetime,
                        BrowseNames.MaxFutureKeyCount, BrowseNames.MaxPastKeyCount], token),
                sessionName, ct);
        }

        /// <summary>
        /// Creates exactly one remote security group.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_add_group", Destructive = true)]
        [Description("Create one remote SecurityGroup with explicit AES policy, key lifetime and future/past counts. " +
            "No keys are pulled, exported or deployed automatically; returns the real group id and node id.")]
        public Task<CallToolResult> AddGroupAsync(
            [Description("Exact parent SecurityGroupFolder NodeId on the selected SKS, e.g. ns=2;s=SecurityGroups.")]
            string folderId,
            [Description("Name of the new SecurityGroup, 1..64 characters, e.g. Line1; not an existing group NodeId.")]
            string name,
            [Description("Exact policy URI: http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR " +
                "or http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes256-CTR.")]
            string policyUri,
            [Description("Finite key lifetime in milliseconds, 1..2592000000 (30 days), e.g. 60000.")]
            double keyLifetimeMilliseconds,
            [Description("Maximum future keys retained by the server, 0..16; default 1. Does not pull keys locally.")]
            uint maxFutureKeyCount = 1,
            [Description("Maximum past keys retained by the server, 0..16; default 1. " +
                "Server key validity remains authoritative.")]
            uint maxPastKeyCount = 1,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_add_group", folderId, ObjectTypeIds.SecurityGroupFolderType,
                async (info, node, token) =>
                {
                    PubSubInputValidation.Text(name, 64);
                    Policy(policyUri);
                    Duration(keyLifetimeMilliseconds);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFutureKeyCount, 16u);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPastKeyCount, 16u);
                    (string group, NodeId added) = await new SecurityGroupFolderTypeClient(
                        info.Session, node, info.Session.MessageContext.Telemetry)
                        .AddSecurityGroupAsync(name, keyLifetimeMilliseconds, policyUri,
                            maxFutureKeyCount, maxPastKeyCount, token).ConfigureAwait(false);
                    JsonObject result = McpPubSubRemote.Completed();
                    result["securityGroupId"] = group;
                    result["nodeId"] = added.ToString();
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes an explicit remote group and invalidates tracked local uses of its keys.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_remove_group", Destructive = true)]
        [Description("Remove one remote SecurityGroup through its explicit folder. On acknowledged removal, closes " +
            "local runtimes using tracked keys from that exact server group; no other group's keys are touched. " +
            "Returns method completion and the number of local references invalidated.")]
        public Task<CallToolResult> RemoveGroupAsync(
            [Description("Exact parent SecurityGroupFolder NodeId on the selected SKS, e.g. ns=2;s=SecurityGroups.")]
            string folderId,
            [Description(PubSubMcpFilters.SecurityGroupNodeIdDescription)]
            string groupNodeId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_remove_group", folderId, ObjectTypeIds.SecurityGroupFolderType,
                async (info, node, token) =>
                {
                    NodeId group = await McpPubSubRemote.TargetAsync(
                        info.Session, groupNodeId, ObjectTypeIds.SecurityGroupType, token).ConfigureAwait(false);
                    McpPubSubRemote.RequireTarget(info, group, "pubsub_security_remove_group", m_options);
                    string resource = McpSecretReferences.GetResourceBinding(info, group);
                    _ = m_references.Epoch(resource);
                    await new SecurityGroupFolderTypeClient(info.Session, node, info.Session.MessageContext.Telemetry)
                        .RemoveSecurityGroupAsync(group, token).ConfigureAwait(false);
                    JsonObject result = McpPubSubRemote.Completed();
                    result["localReferencesInvalidated"] = m_references.Invalidate(resource);
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Requests one remote key rotation without automatically pulling or deploying the new key.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_rotate", Destructive = true)]
        [Description("Force one remote SecurityGroup rotation. Does not automatically pull/deploy keys or pretend " +
            "old tokens were invalidated; their validity remains the server's past-key/lifetime policy. " +
            "Returns method completion, never key material.")]
        public Task<CallToolResult> RotateAsync(
            [Description(PubSubMcpFilters.SecurityGroupNodeIdDescription)]
            string groupNodeId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_rotate", groupNodeId, ObjectTypeIds.SecurityGroupType,
                async (info, node, token) =>
                {
                    await new SecurityGroupTypeClient(info.Session, node, info.Session.MessageContext.Telemetry)
                        .ForceKeyRotationAsync(token).ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Invalidates remote keys and local runtime dependencies for the exact group.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_invalidate", Destructive = true)]
        [Description("Invalidate one remote group's keys. On acknowledgement, invalidates local tracked references " +
            "and closes dependent runtimes across callers. Returns method completion and the number of local " +
            "references invalidated; does not reveal or delete unrelated host secrets.")]
        public Task<CallToolResult> InvalidateAsync(
            [Description(PubSubMcpFilters.SecurityGroupNodeIdDescription)]
            string groupNodeId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_invalidate", groupNodeId, ObjectTypeIds.SecurityGroupType,
                async (info, node, token) =>
                {
                    string resource = McpSecretReferences.GetResourceBinding(info, node);
                    _ = m_references.Epoch(resource);
                    await new SecurityGroupTypeClient(info.Session, node, info.Session.MessageContext.Telemetry)
                        .InvalidateKeysAsync(token).ConfigureAwait(false);
                    JsonObject result = McpPubSubRemote.Completed();
                    result["localReferencesInvalidated"] = m_references.Invalidate(resource);
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Pulls current/future keys once and persists each before publishing a protected reference.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_pull_keys")]
        [Description("Pull current/future SKS keys once into the host's protected writable store. Returns opaque " +
            "caller/group/token references and real lifetimes, never raw keys. Partial storage failures are explicit.")]
        public Task<CallToolResult> PullKeysAsync(
            [Description("Exact SKS SecurityGroupId string, 1..64 characters, returned by add_group or inspect; " +
                "not the group's NodeId or a locally chosen alias.")]
            string securityGroupId,
            [Description("Maximum current/future keys to request and reserve storage for, 1..16; default 1.")]
            uint requestedKeyCount = 1,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_pull_keys", ObjectIds.PublishSubscribe.ToString(),
                ObjectTypeIds.PubSubKeyServiceType, async (info, node, token) =>
                {
                    PubSubInputValidation.Text(securityGroupId, 64);
                    ArgumentOutOfRangeException.ThrowIfLessThan(requestedKeyCount, 1u);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(requestedKeyCount, 16u);
                    var proxy = new PubSubKeyServiceTypeClient(
                        info.Session, node, info.Session.MessageContext.Telemetry);
                    NodeId group = await proxy.GetSecurityGroupAsync(securityGroupId, token).ConfigureAwait(false);
                    McpPubSubRemote.RequireTarget(info, group, "pubsub_security_pull_keys", m_options);
                    string resource = McpSecretReferences.GetResourceBinding(info, group);
                    long epoch = m_references.Epoch(resource);
                    using McpSecretReservation reservation = m_secrets.ReserveIssuance(
                        McpCapability.PubSubSecurity, (int)requestedKeyCount);
                    DateTimeOffset requestStarted = m_time.GetUtcNow();
                    (string policy, uint first, ArrayOf<ByteString> keys, double toNext, double lifetime) =
                        await proxy.GetSecurityKeysAsync(securityGroupId, 0, requestedKeyCount, token)
                            .ConfigureAwait(false);
                    try
                    {
                        Policy(policy);
                        Duration(lifetime);
                        if (!double.IsFinite(toNext) ||
                            toNext < 0 ||
                            toNext > lifetime ||
                            keys.Count > requestedKeyCount)
                        {
                            throw new ServiceResultException(StatusCodes.BadDecodingError,
                                "The SKS returned invalid key counts or lifetime metadata.");
                        }
                        int expected = policy == PubSubSecurityPolicyUri.PubSubAes128Ctr ? 52 : 68;
                        if (keys.FindIndex(key => key.Length != expected) >= 0)
                        {
                            throw new ServiceResultException(StatusCodes.BadDecodingError,
                                "The SKS returned a malformed key.");
                        }
                        var references = new JsonArray();
                        JsonObject result = McpPubSubRemote.Completed();
                        result["securityGroupId"] = securityGroupId;
                        result["policyUri"] = policy;
                        result["keyLifetimeMilliseconds"] = lifetime;
                        result["timeToNextKeyMilliseconds"] = toNext;
                        result["keys"] = references;
                        result["rawKeysIncluded"] = false;
                        using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_time);
                        for (int index = 0; index < keys.Count; index++)
                        {
                            uint tokenId = unchecked(first + (uint)index);
                            double remaining = (toNext > 0 ? toNext : lifetime) + (index * lifetime);
                            DateTimeOffset expiry = requestStarted.AddMilliseconds(remaining);
                            try
                            {
                                McpSecretReference reference = await reservation.StoreForIssuerAsync(
                                    keys[index], "pubsub-key:" + securityGroupId,
                                    resource, info.Endpoint.EndpointUrl!, expiry,
                                    tokenId.ToString(CultureInfo.InvariantCulture), persistence.Token)
                                    .ConfigureAwait(false);
                                DateTimeOffset notBefore = index == 0 ? requestStarted :
                                    requestStarted.AddMilliseconds(
                                        (toNext > 0 ? toNext : lifetime) + ((index - 1) * lifetime));
                                m_references.Track(reference, policy, epoch, info.Endpoint, notBefore);
                                references.Add((JsonNode)new JsonObject
                                {
                                    ["reference"] = reference.Reference,
                                    ["resource"] = resource,
                                    ["tokenId"] = tokenId,
                                    ["expiresAt"] = expiry,
                                    ["current"] = index == 0,
                                    ["notBefore"] = index == 0 ? null :
                                        JsonValue.Create(requestStarted.AddMilliseconds(
                                            (toNext > 0 ? toNext : lifetime) + ((index - 1) * lifetime)))
                                });
                            }
                            catch (McpSecretPersistenceException failure)
                            {
                                result["error"] = true;
                                result["partial"] = true;
                                result["remoteKeysRetrieved"] = true;
                                result["localPersistence"] = "failed";
                                result["cleanupRequired"] = failure.CleanupFailed;
                                result["cleanupReference"] = failure.CleanupFailed ? failure.Reference.Reference : null;
                                result["providerStatus"] = failure.ProviderStatus.Code;
                                break;
                            }
                        }
                        return result;
                    }
                    finally
                    {
                        Clear(keys);
                    }
                }, sessionName, ct);
        }

        /// <summary>
        /// Pushes one authorized current key to an explicitly authorized existing target server.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_push_keys", Destructive = true)]
        [Description("Push one current SKS key from an owned protected reference to this exact server group through " +
            "SetSecurityKeys. Requires encrypted authorized target; returns method completion, never key bytes.")]
        public Task<CallToolResult> PushKeysAsync(
            [Description("Current SKS group/policy/token metadata and keyReference/resource copied from " +
                "pubsub_security_pull_keys. expiresAt is RFC 3339 and cannot exceed the returned expiry. " +
                "No raw keys; the reference remains caller-, group- and target-bound.")]
            PubSubSecurityInput input,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_push_keys", ObjectIds.PublishSubscribe.ToString(),
                ObjectTypeIds.PublishSubscribeType, async (info, node, token) =>
                {
                    ArgumentNullException.ThrowIfNull(input);
                    McpSecretDependency dependency = m_references.Dependency(input);
                    if (dependency.Reference.Purpose != "pubsub-key:" + input.GroupId ||
                        dependency.Reference.CredentialId != input.TokenId.ToString(CultureInfo.InvariantCulture) ||
                        input.ExpiresAt > dependency.Reference.ExpiresAt ||
                        input.ExpiresAt <= m_time.GetUtcNow())
                    {
                        throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                            "The requested group/token/expiry does not match the protected key.");
                    }
                    _ = McpSecretReferences.GetResourceBinding(info, node);
                    using ISecret secret = await m_secrets.ResolveAsync(
                        input.KeyReference, input.Resource, McpCapability.PubSubSecurity, token).ConfigureAwait(false);
                    byte[] material = secret.Bytes.ToArray();
                    try
                    {
                        double remaining = (input.ExpiresAt - m_time.GetUtcNow()).TotalMilliseconds;
                        await new PublishSubscribeTypeClient(info.Session, node, info.Session.MessageContext.Telemetry)
                            .SetSecurityKeysAsync(input.GroupId, input.PolicyUri, input.TokenId,
                                ByteString.From(material), [], remaining, remaining, token).ConfigureAwait(false);
                        return McpPubSubRemote.Completed();
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(material);
                    }
                }, sessionName, ct);
        }

        /// <summary>
        /// Creates an explicit remote SKS push-target entry without credentials.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_add_target", Destructive = true)]
        [Description("Add a remote SKS push target using public application/endpoint/token-policy metadata. " +
            "Host endpoint allowlist and server trust still apply; credentials are never embedded in the entry. " +
            "Returns the created target NodeId, not acknowledgement of key delivery.")]
        public Task<CallToolResult> AddTargetAsync(
            [Description("Exact parent PubSubKeyPushTargetFolder NodeId, e.g. ns=2;s=PushTargets; not a path.")]
            string folderId,
            [Description("Recipient's exact application URI, e.g. urn:example:line1; used for endpoint authorization.")]
            string applicationUri,
            [Description("Recipient's absolute OPC UA endpoint URL, e.g. opc.tcp://line1.example:4840; " +
                "must match host allowlisting and remote server trust. No inline credentials.")]
            string endpointUrl,
            [Description("Recipient's exact non-None OPC UA SecureChannel policy URI, e.g. " +
                "http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss; not the PubSub message policy.")]
            string securityPolicyUri,
            [Description("Recipient user-token kind: Anonymous, UserName, Certificate or IssuedToken; " +
                "default Certificate. " +
                "This is public policy metadata, not credentials or a certificate-trust grant.")]
            UserTokenType userTokenType = UserTokenType.Certificate,
            [Description("Number of keys the remote push target requests per update, 1..16; default 1.")]
            ushort requestedKeyCount = 1,
            [Description("Remote retry interval in milliseconds, finite 1..2592000000 (30 days); default 1000.")]
            double retryIntervalMilliseconds = 1000,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_add_target", folderId, ObjectTypeIds.PubSubKeyPushTargetFolderType,
                async (info, node, token) =>
                {
                    PubSubInputValidation.Text(applicationUri);
                    PubSubInputValidation.Text(endpointUrl);
                    PubSubInputValidation.Text(securityPolicyUri);
                    if (securityPolicyUri == SecurityPolicies.None || !Enum.IsDefined(userTokenType))
                    {
                        throw new ArgumentException("SKS push targets require a secure policy and defined token type.");
                    }
                    ArgumentOutOfRangeException.ThrowIfLessThan(requestedKeyCount, 1);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(requestedKeyCount, 16);
                    Duration(retryIntervalMilliseconds);
                    _ = m_network.Authorize(new Uri(endpointUrl, UriKind.Absolute), "configure-sks-target",
                        applicationUri, McpCapability.PubSubSecurity);
                    NodeId target = await new PubSubKeyPushTargetFolderTypeClient(
                        info.Session, node, info.Session.MessageContext.Telemetry)
                        .AddPushTargetAsync(applicationUri, endpointUrl, securityPolicyUri,
                            new UserTokenPolicy { TokenType = userTokenType },
                            requestedKeyCount, retryIntervalMilliseconds, token).ConfigureAwait(false);
                    JsonObject result = McpPubSubRemote.Completed();
                    result["nodeId"] = target.ToString();
                    result["remoteEgressPolicy"] = "Remote server trust and outbound policy remain authoritative.";
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Removes an explicit remote SKS push target.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_remove_target", Destructive = true)]
        [Description("Remove one exact SKS push-target entry through its explicit folder. Does not revoke unrelated " +
            "credentials, disconnect borrowed sessions or remove another target. Returns method completion.")]
        public Task<CallToolResult> RemoveTargetAsync(
            [Description("Exact parent PubSubKeyPushTargetFolder NodeId, e.g. ns=2;s=PushTargets; not a path.")]
            string folderId,
            [Description(PubSubMcpFilters.PushTargetNodeIdDescription)]
            string targetId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_remove_target", folderId, ObjectTypeIds.PubSubKeyPushTargetFolderType,
                async (info, node, token) =>
                {
                    await new PubSubKeyPushTargetFolderTypeClient(
                        info.Session, node, info.Session.MessageContext.Telemetry)
                        .RemovePushTargetAsync(McpPubSubRemote.Node(info.Session, targetId), token)
                        .ConfigureAwait(false);
                    return McpPubSubRemote.Completed();
                }, sessionName, ct);
        }

        /// <summary>
        /// Attaches explicit security groups to one push target with real per-item results.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_connect_groups", Destructive = true)]
        [Description("Connect exact SecurityGroups to one push target. Returns real per-group statuses and partial " +
            "outcomes; no target selection by display name or implicit key deployment chain.")]
        public Task<CallToolResult> ConnectGroupsAsync(
            [Description(PubSubMcpFilters.PushTargetNodeIdDescription)]
            string targetId,
            [Description("Native array of 1..16 exact authorized SecurityGroup NodeIds to attach; " +
                "each nodeId may be local or namespace-URI-qualified, e.g. ns=2;s=SecurityGroup1.")]
            ArrayOf<PubSubNodeInput> nodes,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ConnectGroupsAsync(targetId, new PubSubNodesInput { Nodes = nodes }, sessionName, ct);
        }

        /// <summary>
        /// Attaches groups from an existing direct node selection.
        /// </summary>
        public Task<CallToolResult> ConnectGroupsAsync(
            string targetId, PubSubNodesInput input, string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_connect_groups", targetId, ObjectTypeIds.PubSubKeyPushTargetType,
                async (info, node, token) =>
                {
                    ArrayOf<NodeId> groups = Groups(info, input, "pubsub_security_connect_groups");
                    ArrayOf<StatusCode> statuses = await new PubSubKeyPushTargetTypeClient(
                        info.Session, node, info.Session.MessageContext.Telemetry)
                        .ConnectSecurityGroupsAsync(groups, token).ConfigureAwait(false);
                    JsonObject result = McpPubSubRemote.Completed();
                    McpPubSubRemote.Statuses(result, statuses);
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Detaches exact groups from a push target.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_disconnect_groups", Destructive = true)]
        [Description("Disconnect exact SecurityGroups from one push target. Returns the server's per-item status " +
            "and partial outcome rather than claiming atomic detach.")]
        public Task<CallToolResult> DisconnectGroupsAsync(
            [Description(PubSubMcpFilters.PushTargetNodeIdDescription)]
            string targetId,
            [Description("Native array of 1..16 exact authorized SecurityGroup NodeIds to detach; " +
                "each nodeId may be local or namespace-URI-qualified, e.g. ns=2;s=SecurityGroup1.")]
            ArrayOf<PubSubNodeInput> nodes,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return DisconnectGroupsAsync(targetId, new PubSubNodesInput { Nodes = nodes }, sessionName, ct);
        }

        /// <summary>
        /// Detaches groups from an existing direct node selection.
        /// </summary>
        public Task<CallToolResult> DisconnectGroupsAsync(
            string targetId, PubSubNodesInput input, string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_disconnect_groups", targetId, ObjectTypeIds.PubSubKeyPushTargetType,
                async (info, node, token) =>
                {
                    ArrayOf<NodeId> groups = Groups(info, input, "pubsub_security_disconnect_groups");
                    ArrayOf<StatusCode> statuses = await new PubSubKeyPushTargetTypeClient(
                        info.Session, node, info.Session.MessageContext.Telemetry)
                        .DisconnectSecurityGroupsAsync(groups, token).ConfigureAwait(false);
                    JsonObject result = McpPubSubRemote.Completed();
                    McpPubSubRemote.Statuses(result, statuses);
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Requests one remote push-target key update.
        /// </summary>
        [McpServerTool(Name = "pubsub_security_trigger_update", Destructive = true)]
        [Description("Trigger one explicit key update on an existing remote SKS push target. Does not poll, retry, " +
            "auto-trust certificates or claim the remote recipient applied the keys. " +
            "Returns method completion with recipientApplyAcknowledged=false.")]
        public Task<CallToolResult> TriggerUpdateAsync(
            [Description(PubSubMcpFilters.PushTargetNodeIdDescription)]
            string targetId,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return ExecuteAsync("pubsub_security_trigger_update", targetId, ObjectTypeIds.PubSubKeyPushTargetType,
                async (info, node, token) =>
                {
                    await new PubSubKeyPushTargetTypeClient(info.Session, node, info.Session.MessageContext.Telemetry)
                        .TriggerKeyUpdateAsync(token).ConfigureAwait(false);
                    JsonObject result = McpPubSubRemote.Completed();
                    result["recipientApplyAcknowledged"] = false;
                    return result;
                }, sessionName, ct);
        }

        /// <summary>
        /// Checks exact group target permissions before any remote batch mutation.
        /// </summary>
        private ArrayOf<NodeId> Groups(OpcUaSessionManager.SessionInfo info, PubSubNodesInput input, string operation)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentOutOfRangeException.ThrowIfLessThan(input.Nodes.Count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Nodes.Count, 16);
            return new ArrayOf<NodeId>(input.Nodes.ToList().Select(value =>
            {
                NodeId node = McpPubSubRemote.Node(info.Session, value.NodeId);
                McpPubSubRemote.RequireTarget(info, node, operation, m_options);
                return node;
            }).ToArray());
        }

        /// <summary>
        /// Executes one generated method after capability, caller, exact session, target and model checks.
        /// </summary>
        private Task<CallToolResult> ExecuteAsync(
            string operation, string target, ExpandedNodeId type,
            Func<OpcUaSessionManager.SessionInfo, NodeId, CancellationToken, ValueTask<JsonObject>> execute,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                try
                {
                    OpcUaSessionManager.SessionInfo info = McpPubSubRemote.Session(
                        m_sessions, sessionName, McpCapability.PubSubSecurity);
                    NodeId node = McpPubSubRemote.Node(info.Session, target);
                    McpPubSubRemote.RequireTarget(info, node, operation, m_options);
                    _ = await McpModelAccess.RequireTypeAsync(info.Session, node, type, token).ConfigureAwait(false);
                    return await execute(info, node, token).ConfigureAwait(false);
                }
                catch (ServiceResultException failure)
                {
                    return new JsonObject
                    {
                        ["error"] = true,
                        ["statusCodeValue"] = failure.StatusCode.Code,
                        ["message"] = "The protected PubSub security operation was refused or failed."
                    };
                }
            }, ct);
        }

        /// <summary>
        /// Rejects unsupported message security policies without echoing key material.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void Policy(string policy)
        {
            if (policy is not (PubSubSecurityPolicyUri.PubSubAes128Ctr or PubSubSecurityPolicyUri.PubSubAes256Ctr))
            {
                throw new ArgumentException("Only the existing PubSub AES-128/256-CTR policies are supported.");
            }
        }

        /// <summary>
        /// Bounds finite standard Duration values in milliseconds.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void Duration(double value)
        {
            if (!double.IsFinite(value) || value < 1 || value > TimeSpan.FromDays(30).TotalMilliseconds)
            {
                throw new ArgumentException("Duration must be positive, finite and at most thirty days.");
            }
        }

        /// <summary>
        /// Clears every returned key buffer after protected persistence or failure.
        /// </summary>
        private static void Clear(ArrayOf<ByteString> keys)
        {
            foreach (ByteString key in keys)
            {
                if (MemoryMarshal.TryGetArray(key.Memory, out ArraySegment<byte> segment) && segment.Array is not null)
                {
                    CryptographicOperations.ZeroMemory(segment.AsSpan());
                }
            }
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly McpPubSubOptions m_options;
        private readonly McpSecretReferences m_secrets;
        private readonly McpPubSubKeyReferences m_references;
        private readonly McpNetworkPolicy m_network;
        private readonly TimeProvider m_time;
    }
}
