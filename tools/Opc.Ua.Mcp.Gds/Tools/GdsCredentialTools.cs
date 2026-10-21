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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Selects one credential-service model for discovery and public metadata reads.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<GdsCredentialServiceKind>))]
    public enum GdsCredentialServiceKind
    {
        /// <summary>
        /// Non-UA resource credentials defined by Part 12 section 8.
        /// </summary>
        KeyCredential,

        /// <summary>
        /// Modern two-phase token issuance defined by Part 12 section 9.
        /// </summary>
        Authorization
    }

    /// <summary>
    /// Explicit credential issuance with protected references and no legacy RequestAccessToken surface.
    /// </summary>
    [McpServerToolType]
    public sealed class GdsCredentialTools
    {
        /// <summary>
        /// Creates tools using existing sessions, a host-provided secret gateway and shared request ownership.
        /// </summary>
        public GdsCredentialTools(
            OpcUaSessionManager sessions, McpFileTransfers transfers, McpSecretReferences secrets,
            GdsMcpOptions options, GdsOperationState state, TimeProvider? timeProvider = null)
        {
            m_access = new GdsAccess(sessions, transfers, secrets, options, timeProvider ?? TimeProvider.System);
            m_state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Discovers credential services by model identity, including vendor subtypes.
        /// </summary>
        [McpServerTool(Name = "credential_list_services", ReadOnly = true, Destructive = false)]
        [Description("Discover KeyCredential or Authorization services by actual type. " +
            "Returns a live page of node/type identities; example: kind='Authorization'.")]
        public Task<CallToolResult> ListServicesAsync(
            [Description("Service model to discover: 'KeyCredential' for non-UA resource credentials, " +
                "or 'Authorization' for modern two-phase access-token issuance.")]
            GdsCredentialServiceKind kind,
            [Description(GdsToolDescriptions.RootNodeId)]
            string rootNodeId = "i=85",
            [Description(GdsToolDescriptions.Offset)]
            int offset = 0,
            [Description(GdsToolDescriptions.MaxResults)]
            int maxResults = 100,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsAccess.Page(offset, maxResults);
                SessionRequestContext context = await m_access.CaptureAsync(
                    sessionName, McpCapability.CredentialIssuance, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable contextScope = context.ConfigureAwait(false);
                ISession session = context.Session;
                return await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(session, GdsAccess.Node(rootNodeId), GdsAccess.Type(Target(kind)),
                        ct: token),
                    reference => McpModelAccess.Reference(session, reference), offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads public resource, policy and issuer metadata without issuing a credential.
        /// </summary>
        [McpServerTool(Name = "credential_inspect_service", ReadOnly = true, Destructive = false)]
        [Description("Read public resource/policy/issuer metadata and advertised method nodes. " +
            "Returns real values and optional-support flags, never a credential or token.")]
        public Task<CallToolResult> InspectServiceAsync(
            [Description("NodeId returned by credential_list_services for an actual service instance or subtype " +
                "matching kind; not the service URI or type id.")]
            string serviceNodeId,
            [Description("Expected service model: 'KeyCredential' reads ResourceUri/ProfileUris/SecurityPolicyUris; " +
                "'Authorization' reads ServiceUri/UserTokenPolicies/SupportedRoles.")]
            GdsCredentialServiceKind kind,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, Target(kind), sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                ArrayOf<string> names = kind == GdsCredentialServiceKind.KeyCredential
                    ? ["ResourceUri", "ProfileUris", "SecurityPolicyUris"]
                    : ["ServiceUri", "UserTokenPolicies", "SupportedRoles"];
                JsonObject result = await McpModelAccess.PropertiesAsync(endpoint.Session,
                    endpoint.Proxy.ObjectId, names.ConvertAll(name =>
                        McpModelAccess.Name(endpoint.Session, Gds.Namespaces.OpcUaGds, name)), token)
                    .ConfigureAwait(false);
                result["issuerResource"] = endpoint.Resource;
                result["methods"] = await McpCompanionTools.PageAsync(
                    endpoint.Session.EnumerateReferencesAsync(endpoint.Proxy.ObjectId,
                        ReferenceTypeIds.HasComponent, nodeClassMask: (uint)NodeClass.Method, ct: token),
                    reference => McpModelAccess.Reference(endpoint.Session, reference), maxResults: 100, ct: token)
                    .ConfigureAwait(false);
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads the AuthorizationService's standard description without returning certificate private material.
        /// </summary>
        [McpServerTool(Name = "credential_get_authorization_description", ReadOnly = true, Destructive = false)]
        [Description("Get the authorization service description. Returns issuer URI, public service-certificate " +
            "fingerprint and typed user-token policies. " +
            "Optionally export the public DER certificate beneath TransferRoot.")]
        public Task<CallToolResult> GetAuthorizationDescriptionAsync(
            [Description(GdsToolDescriptions.AuthorizationServiceNodeId)]
            string serviceNodeId,
            [Description("Optional output path beneath TransferRoot for the authorization service's public DER " +
                "certificate, e.g. 'authorization.der'; omit to return fingerprint/policy metadata without a file.")]
            string? certificatePath = null,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                if (certificatePath is not null)
                {
                    m_access.Transfers.ValidateDownloadPath(certificatePath);
                }
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.Authorization, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.AuthorizationServiceType_GetServiceDescription, [], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["issuerResource"] = endpoint.Resource;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["serviceUri"] = GdsJson.String(call, 0);
                    ByteString certificate = GdsJson.Bytes(call, 1);
                    result["serviceCertificate"] = certificate.IsEmpty ? null : GdsJson.Certificate(certificate);
                    result["userTokenPolicies"] = McpCompanionTools.Page(
                        GdsJson.Structures<UserTokenPolicy>(call, 2, endpoint.Context),
                        policy => McpCompanionJson.Encode(policy, endpoint.Context));
                    if (certificatePath is not null && !certificate.IsEmpty)
                    {
                        result["artifact"] = await m_access.PublicOutputAsync(certificatePath, certificate, token)
                            .ConfigureAwait(false);
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Starts a KeyCredential request without approval or polling.
        /// </summary>
        [McpServerTool(Name = "credential_start_key_request", ReadOnly = false, Destructive = true)]
        [Description("Start a KeyCredential request; returns its real requestId and resource URI. " +
            "Requires a configured protected issuance store. Does not approve or finish the request.")]
        public Task<CallToolResult> StartKeyRequestAsync(
            [Description(GdsToolDescriptions.KeyCredentialServiceNodeId)]
            string serviceNodeId,
            [Description("Requesting application URI, optional public encryption-key file beneath TransferRoot, " +
                "advertised securityPolicyUri and requested role NodeIds. Empty securityPolicyUri lets the server " +
                "choose; requestedRoles are not role names or granted-role claims.")]
            GdsKeyCredentialInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                m_access.Secrets.RequireIssuanceStore();
                GdsAccess.Text(input.ApplicationUri);
                GdsAccess.Text(input.SecurityPolicyUri, allowEmpty: true);
                ArrayOf<NodeId> roles = GdsAccess.Strings(input.RequestedRoles).ConvertAll(GdsAccess.Node);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.KeyCredential, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                _ = endpoint.SecretResource;
                string resource = await ResourceAsync(endpoint, token).ConfigureAwait(false);
                ByteString publicKey = input.PublicKeyPath is null ? default :
                    await m_access.PublicInputAsync(input.PublicKeyPath, token).ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.KeyCredentialServiceType_StartRequest,
                    [new Variant(input.ApplicationUri), new Variant(publicKey),
                        new Variant(input.SecurityPolicyUri), new Variant(roles)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["resource"] = resource;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["requestId"] = GdsJson.Node(call, 0).ToString();
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Collects one KeyCredential result and persists its secret before publishing a reference.
        /// </summary>
        [McpServerTool(Name = "credential_finish_key_request", ReadOnly = false, Destructive = true)]
        [Description("Finish one KeyCredential request once. Returns actual pending/refusal status or " +
            "credentialId, granted roles and a protected reference; never returns credential bytes.")]
        public Task<CallToolResult> FinishKeyRequestAsync(
            [Description(GdsToolDescriptions.KeyCredentialServiceNodeId)]
            string serviceNodeId,
            [Description("Server-issued request NodeId returned by credential_start_key_request for this service. " +
                "The request is checked once; pending/refusal status is returned without polling.")]
            string requestId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                NodeId request = GdsAccess.Node(requestId);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.KeyCredential, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                string issuer = endpoint.SecretResource;
                string resource = await ResourceAsync(endpoint, token).ConfigureAwait(false);
                using McpSecretReservation reservation =
                    m_access.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.KeyCredentialServiceType_FinishRequest,
                    [new Variant(request), new Variant(false)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["requestId"] = requestId;
                result["resource"] = resource;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    ByteString secret = GdsJson.Bytes(call, 1);
                    result["credentialId"] = GdsJson.String(call, 0);
                    result["certificateThumbprint"] = GdsJson.String(call, 2);
                    result["securityPolicyUri"] = GdsJson.String(call, 3);
                    ArrayOf<NodeId> roles = GdsJson.Nodes(call, 4);
                    var granted = new JsonArray();
                    for (int index = 0; index < Math.Min(roles.Count, 500); index++)
                    {
                        JsonNode role = JsonValue.Create(roles[index].ToString());
                        granted.Add(role);
                    }
                    result["grantedRoles"] = granted;
                    result["grantedRoleCount"] = roles.Count;
                    result["grantedRolesTruncated"] = roles.Count > 500;
                    result["remoteIssuance"] = secret.IsEmpty ? "noCredentialReturned" : "issued";
                    if (!secret.IsEmpty)
                    {
                        result["credential"] = await PersistAsync(reservation, secret, "key-credential",
                            resource, m_access.Time.GetUtcNow() + m_access.Options.ReferenceLifetime,
                            GdsJson.String(call, 0), result, issuer).ConfigureAwait(false);
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Cancels exactly one pending KeyCredential request.
        /// </summary>
        [McpServerTool(Name = "credential_cancel_key_request", ReadOnly = false, Destructive = true)]
        [Description("Explicitly cancel a pending KeyCredential request. Returns its requestId and actual status; " +
            "does not collect a credential, poll or revoke an already issued credential.")]
        public Task<CallToolResult> CancelKeyRequestAsync(
            [Description(GdsToolDescriptions.KeyCredentialServiceNodeId)]
            string serviceNodeId,
            [Description("Pending request NodeId returned by credential_start_key_request for this service; " +
                "not an issued credential id or protected secret reference.")]
            string requestId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                NodeId request = GdsAccess.Node(requestId);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.KeyCredential, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.KeyCredentialServiceType_FinishRequest,
                    [new Variant(request), new Variant(true)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["requestId"] = requestId;
                return result;
            }, ct);
        }

        /// <summary>
        /// Revokes a credential remotely, then invalidates its local owned references.
        /// </summary>
        [McpServerTool(Name = "credential_revoke_key", ReadOnly = false, Destructive = true)]
        [Description("Revoke one KeyCredential id explicitly and invalidate its owned local references. " +
            "Returns remote status and separate local-cleanup outcome; no retry or secret output.")]
        public Task<CallToolResult> RevokeKeyAsync(
            [Description(GdsToolDescriptions.KeyCredentialServiceNodeId)]
            string serviceNodeId,
            [Description("Exact credential id string returned by credential_finish_key_request; " +
                "not its request NodeId or protected secret reference.")]
            string credentialId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsAccess.Text(credentialId);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.KeyCredential, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                string resource = await ResourceAsync(endpoint, token).ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.KeyCredentialServiceType_Revoke, [new Variant(credentialId)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["credentialId"] = credentialId;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    try
                    {
                        await m_access.Secrets.RevokeCredentialAsync(resource, credentialId, CancellationToken.None)
                            .ConfigureAwait(false);
                        result["localReferences"] = "invalidated";
                    }
                    catch (Exception failure) when (failure is IMcpSecretPersistenceFailure persistence)
                    {
                        result["localCleanup"] = GdsJson.PersistenceFailure(persistence);
                        result["error"] = true;
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Starts the modern two-phase token flow and protects any returned challenge data.
        /// </summary>
        [McpServerTool(Name = "credential_start_token_request", ReadOnly = false, Destructive = true)]
        [Description("Start modern AuthorizationService issuance for one resource and policy. Returns requestId " +
            "and a protected service-data reference. Example: resourceId='urn:server', policyId='jwt'.")]
        public Task<CallToolResult> StartTokenRequestAsync(
            [Description(GdsToolDescriptions.AuthorizationServiceNodeId)]
            string serviceNodeId,
            [Description("Target resource/audience identifier for the requested token, e.g. 'urn:target-server'; " +
                "not the authorization service NodeId.")]
            string resourceId,
            [Description("Authorization token policy identifier accepted by the service, e.g. 'jwt'; " +
                "passed unchanged to StartRequestToken, not an OPC UA transport security-policy URI.")]
            string policyId,
            [Description("Optional authorized protected reference to policy-specific requestor data, " +
                "e.g. 'alias:token-request-data', bound to this issuer's resource. Omit to send no requestor data; " +
                "never supply credential bytes or plaintext secrets inline.")]
            string? requestorDataReference = null,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsAccess.Text(resourceId);
                GdsAccess.Text(policyId);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.Authorization, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                McpCallerIdentity owner = m_access.Sessions.OperationPolicy.Require(McpCapability.CredentialIssuance);
                using GdsOperationState.RequestReservation request =
                    m_state.ReserveRequest(endpoint, owner, resourceId);
                string issuer = request.Issuer;
                using McpSecretReservation secret =
                    m_access.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);
                using ISecret? data = requestorDataReference is null ? null :
                    await m_access.Secrets.ResolveAsync(requestorDataReference, issuer,
                        McpCapability.CredentialIssuance, token).ConfigureAwait(false);
                ByteString requestorData = data is null ? default : ByteString.From(data.Bytes.ToArray());
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.AuthorizationServiceType_StartRequestToken,
                    [new Variant(resourceId), new Variant(policyId), new Variant(requestorData)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["resource"] = resourceId;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    string id = GdsJson.Uuid(call, 1).ToString();
                    request.Commit(id);
                    result["requestId"] = id;
                    ByteString challenge = GdsJson.Bytes(call, 0);
                    if (!challenge.IsEmpty)
                    {
                        result["serviceData"] = await PersistAsync(secret, challenge, "authorization-challenge",
                            resourceId, m_access.Time.GetUtcNow() + m_access.Options.RequestLifetime, id, result,
                            issuer)
                            .ConfigureAwait(false);
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Finishes a caller/session-bound token request with two atomic reference slots reserved in advance.
        /// </summary>
        [McpServerTool(Name = "credential_finish_token_request", ReadOnly = false, Destructive = true)]
        [Description("Finish a modern token request created in this host/session. Returns access/refresh references " +
            "and actual server expiries; proof uses protected policy-prepared bytes, never plaintext.")]
        public Task<CallToolResult> FinishTokenRequestAsync(
            [Description(GdsToolDescriptions.AuthorizationServiceNodeId)]
            string serviceNodeId,
            [Description("UUID requestId returned by credential_start_token_request in this host for this caller " +
                "and the same live UA session/service; not a KeyCredential request NodeId.")]
            string requestId,
            [Description("Requested role names and explicit user-token proof (kind, policyId and protected " +
                "credential/signature references bound to this issuer). Supply policy-prepared bytes via references, " +
                "never plaintext; the channel identity is not substituted for this proof.")]
            GdsTokenFinishInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                ArgumentNullException.ThrowIfNull(input.Proof);
                GdsAccess.Strings(input.RequestedRoles);
                Uuid id = Guid.Parse(requestId);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.Authorization, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                McpCallerIdentity owner = m_access.Sessions.OperationPolicy.Require(McpCapability.CredentialIssuance);
                string issuer = endpoint.SecretResource;
                string resource = m_state.RequireRequest(endpoint, owner, id.ToString());
                using McpSecretReservation reservation =
                    m_access.Secrets.ReserveIssuance(McpCapability.CredentialIssuance, 2);
                (UserIdentityToken identity, SignatureData signature) =
                    await ProofAsync(input.Proof, issuer, token).ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.AuthorizationServiceType_FinishRequestToken,
                    [new Variant(id), new Variant(input.RequestedRoles), Variant.FromStructure(identity),
                        Variant.FromStructure(signature)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["requestId"] = id.ToString();
                result["resource"] = resource;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    m_state.CompleteRequest(endpoint, id.ToString());
                    await TokensAsync(reservation, call, resource, id.ToString(), result, issuer)
                        .ConfigureAwait(false);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Refreshes once and protects both rotated results before returning references.
        /// </summary>
        [McpServerTool(Name = "credential_refresh_token", ReadOnly = false, Destructive = true)]
        [Description("Refresh an AuthorizationService token once using a protected reference. Returns new references " +
            "and server expiries, with separate persistence/cleanup outcomes. Never blindly retry rotation.")]
        public Task<CallToolResult> RefreshTokenAsync(
            [Description(GdsToolDescriptions.AuthorizationServiceNodeId)]
            string serviceNodeId,
            [Description("Original token resource/audience identifier, e.g. 'urn:target-server'; " +
                "must match the resource binding of an issued refresh-token reference.")]
            string resourceId,
            [Description("Protected UTF-8 refresh-token reference, never token text. An issued:... reference must " +
                "belong to this caller, resourceId and issuer; a host-authorized alias:... is bound to the issuer. " +
                "Rotation is attempted once and may invalidate the previous reference.")]
            string refreshReference,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsAccess.Text(resourceId);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    serviceNodeId, GdsTarget.Authorization, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                string issuer = endpoint.SecretResource;
                bool issued = refreshReference.StartsWith("issued:", StringComparison.Ordinal);
                if (issued)
                {
                    McpSecretReference reference = m_access.Secrets.RequireReference(
                        refreshReference, resourceId, McpCapability.CredentialIssuance);
                    if (reference.Purpose != "refresh-token" ||
                        !string.Equals(reference.Issuer, issuer, StringComparison.Ordinal))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                }
                using McpSecretReservation reservation =
                    m_access.Secrets.ReserveIssuance(McpCapability.CredentialIssuance, 2);
                using ISecret refresh = await m_access.Secrets.ResolveAsync(
                    refreshReference, issued ? resourceId : issuer,
                    McpCapability.CredentialIssuance, token).ConfigureAwait(false);
                string text = s_utf8.GetString(refresh.Bytes);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.AuthorizationServiceType_RefreshToken,
                    [new Variant(resourceId), new Variant(text)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["resource"] = resourceId;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    await TokensAsync(reservation, call, resourceId, null, result, issuer)
                        .ConfigureAwait(false);
                    if (issued)
                    {
                        try
                        {
                            await m_access.Secrets.RevokeReferenceAsync(refreshReference, CancellationToken.None)
                                .ConfigureAwait(false);
                            result["previousReference"] = "invalidated";
                        }
                        catch (Exception failure) when (failure is IMcpSecretPersistenceFailure persistence)
                        {
                            result["previousReferenceCleanup"] = GdsJson.PersistenceFailure(persistence);
                            result["error"] = true;
                        }
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Persists each token independently after atomic admission, retaining partial local outcomes.
        /// </summary>
        private async ValueTask TokensAsync(
            McpSecretReservation reservation, CallMethodResult call, string resource, string? requestId,
            JsonObject result, string issuer)
        {
            string access = GdsJson.SecretString(call, 0);
            string refresh = GdsJson.SecretString(call, 2);
            DateTimeOffset accessExpiry = new((DateTime)GdsJson.Time(call, 1));
            DateTimeOffset refreshExpiry = new((DateTime)GdsJson.Time(call, 3));
            result["remoteIssuance"] = "issued";
            result["accessTokenExpiresAt"] = accessExpiry;
            result["refreshTokenExpiresAt"] = refresh.Length == 0 ? null : refreshExpiry;
            result["accessToken"] = await PersistTokenAsync(
                reservation, access, "access-token", resource, accessExpiry, requestId, result, issuer)
                .ConfigureAwait(false);
            if (refresh.Length != 0)
            {
                result["refreshToken"] = await PersistTokenAsync(
                    reservation, refresh, "refresh-token", resource, refreshExpiry, requestId, result, issuer)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Clears the locally encoded token buffer after the configured store consumes it.
        /// </summary>
        private async ValueTask<JsonObject?> PersistTokenAsync(
            McpSecretReservation reservation, string text, string purpose, string resource,
            DateTimeOffset expiry, string? requestId, JsonObject result, string issuer)
        {
            byte[] bytes = s_utf8.GetBytes(text);
            try
            {
                return await PersistAsync(reservation, ByteString.From(bytes), purpose, resource,
                    expiry, requestId, result, issuer).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        /// <summary>
        /// Persistence uses a bounded independent token so late request cancellation cannot discard issuance.
        /// </summary>
        private async ValueTask<JsonObject?> PersistAsync(
            McpSecretReservation reservation, ByteString value, string purpose, string resource,
            DateTimeOffset expiry, string? credentialId, JsonObject result, string issuer)
        {
            try
            {
                using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_access.Time);
                McpSecretReference reference = await reservation.StoreForIssuerAsync(
                    value, purpose, resource, issuer, expiry, credentialId, persistence.Token)
                    .ConfigureAwait(false);
                return GdsJson.Reference(reference);
            }
            catch (Exception failure) when (failure is IMcpSecretPersistenceFailure persistence)
            {
                result["error"] = true;
                result[purpose + "Persistence"] = GdsJson.PersistenceFailure(persistence);
                return null;
            }
            catch (ServiceResultException failure)
            {
                result["error"] = true;
                result[purpose + "Persistence"] = GdsJson.Error(failure.StatusCode,
                    "The remote result was issued but cannot be retained under the configured local secret limits.");
                return null;
            }
        }

        /// <summary>
        /// Builds the standard typed proof from authorized bytes, never a generic JSON identity token.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private async ValueTask<(UserIdentityToken Identity, SignatureData Signature)> ProofAsync(
            GdsTokenProof proof, string issuer, CancellationToken ct)
        {
            if (!Enum.IsDefined(proof.Kind) ||
                (proof.Kind == GdsIdentityKind.Anonymous && proof.CredentialReference is not null) ||
                (proof.Kind != GdsIdentityKind.Anonymous && proof.CredentialReference is null))
            {
                throw new ArgumentException("The proof kind and credential reference are inconsistent.");
            }
            GdsAccess.Text(proof.PolicyId, allowEmpty: true);
            GdsAccess.Text(proof.EncryptionAlgorithm, allowEmpty: true);
            GdsAccess.Text(proof.SignatureAlgorithm, allowEmpty: true);
            using ISecret? secret = proof.CredentialReference is null ? null :
                await m_access.Secrets.ResolveAsync(proof.CredentialReference, issuer,
                    McpCapability.CredentialIssuance, ct).ConfigureAwait(false);
            using ISecret? signature = proof.SignatureReference is null ? null :
                await m_access.Secrets.ResolveAsync(proof.SignatureReference, issuer,
                    McpCapability.CredentialIssuance, ct).ConfigureAwait(false);
            ByteString bytes = secret is null ? default : ByteString.From(secret.Bytes.ToArray());
            UserIdentityToken identity = proof.Kind switch
            {
                GdsIdentityKind.Anonymous => new AnonymousIdentityToken { PolicyId = proof.PolicyId },
                GdsIdentityKind.UserName => new UserNameIdentityToken
                {
                    PolicyId = proof.PolicyId,
                    UserName = GdsAccess.Text(proof.UserName),
                    Password = bytes,
                    EncryptionAlgorithm = proof.EncryptionAlgorithm
                },
                GdsIdentityKind.IssuedToken => new IssuedIdentityToken
                {
                    PolicyId = proof.PolicyId,
                    TokenData = bytes,
                    EncryptionAlgorithm = proof.EncryptionAlgorithm
                },
                GdsIdentityKind.Certificate => new X509IdentityToken
                {
                    PolicyId = proof.PolicyId,
                    CertificateData = bytes
                },
                _ => throw new ArgumentOutOfRangeException(nameof(proof))
            };
            return (identity, new SignatureData
            {
                Algorithm = proof.SignatureAlgorithm,
                Signature = signature is null ? default : ByteString.From(signature.Bytes.ToArray())
            });
        }

        /// <summary>
        /// Reads and validates the resource advertised by the selected service rather than caller claims.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<string> ResourceAsync(GdsEndpoint endpoint, CancellationToken ct)
        {
            (NodeId node, DataValue value) = await McpModelAccess.PropertyAsync(
                endpoint.Session, endpoint.Proxy.ObjectId,
                McpModelAccess.Name(endpoint.Session, Gds.Namespaces.OpcUaGds, "ResourceUri"), ct)
                .ConfigureAwait(false);
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            return value.WrappedValue.TryGetValue(out string resource)
                ? GdsAccess.Text(resource) : throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        /// <summary>
        /// Maps a public read selector to a closed model target.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static GdsTarget Target(GdsCredentialServiceKind kind)
        {
            return kind switch
            {
                GdsCredentialServiceKind.KeyCredential => GdsTarget.KeyCredential,
                GdsCredentialServiceKind.Authorization => GdsTarget.Authorization,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        private readonly GdsAccess m_access;
        private readonly GdsOperationState m_state;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
