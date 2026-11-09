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
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Selects a directory pull source or server push configuration for certificate reads.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<GdsCertificateSource>))]
    public enum GdsCertificateSource
    {
        /// <summary>
        /// A GDS CertificateDirectoryType.
        /// </summary>
        Directory,

        /// <summary>
        /// A ServerConfigurationType.
        /// </summary>
        Server
    }

    /// <summary>
    /// Explicit pull/push certificate workflows. Private material never crosses the public artifact boundary.
    /// </summary>
    [McpServerToolType]
    public sealed partial class GdsCertificateTools
    {
        /// <summary>
        /// Creates tools with shared push ownership and borrowed session/store services.
        /// </summary>
        public GdsCertificateTools(
            OpcUaSessionManager sessions, McpFileTransfers transfers, McpSecretReferences secrets,
            GdsMcpOptions options, GdsOperationState state, TimeProvider? timeProvider = null)
        {
            m_access = new GdsAccess(sessions, transfers, secrets, options, timeProvider ?? TimeProvider.System);
            m_state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Lists application-authorized certificate groups.
        /// </summary>
        [McpServerTool(Name = "certificate_get_groups", ReadOnly = true, Destructive = false)]
        [Description("List certificate groups authorized for one registered application. Returns actual group ids " +
            "and status in a bounded page; use certificate_inspect_group for advertised types.")]
        public Task<CallToolResult> GetGroupsAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
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
                NodeId application = GdsAccess.Node(applicationId);
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.CertificateDirectoryType_GetCertificateGroups, [new Variant(application)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["groups"] = McpCompanionTools.Page(
                        GdsJson.Nodes(call, 0), node => JsonValue.Create(node.ToString()), offset, maxResults);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads certificate type and trust-list metadata on an actual group.
        /// </summary>
        [McpServerTool(Name = "certificate_inspect_group", ReadOnly = true, Destructive = false)]
        [Description("Inspect a typed certificate group. Returns advertised CertificateTypes and TrustList id " +
            "with original DataValue status/timestamps; no private material is read.")]
        public Task<CallToolResult> InspectGroupAsync(
            [Description("NodeId of an actual CertificateGroupType instance from certificate_get_groups or " +
                "certificate_inspect_push; selects the group whose advertised types and TrustList will be read.")]
            string groupNodeId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                SessionRequestContext context = await m_access.CaptureAsync(
                    sessionName, McpCapability.CertificateAdministration, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable contextScope = context.ConfigureAwait(false);
                ISession session = context.Session;
                NodeId group = GdsAccess.Node(groupNodeId);
                await McpModelAccess.RequireTypeAsync(session, group, ObjectTypeIds.CertificateGroupType, token)
                    .ConfigureAwait(false);
                JsonObject result = await McpModelAccess.PropertiesAsync(
                    session, group, [new QualifiedName("CertificateTypes")], token).ConfigureAwait(false);
                NodeId trustList = await McpModelAccess.ChildAsync(
                    session, group, new QualifiedName("TrustList"), token).ConfigureAwait(false);
                result["trustListId"] = trustList.IsNull ? null : trustList.ToString();
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads the issuer's update-required decision.
        /// </summary>
        [McpServerTool(Name = "certificate_get_status", ReadOnly = true, Destructive = false)]
        [Description("Read the directory's certificate update-required flag for an application/group/type. " +
            "Returns the real Boolean and method status, not a locally inferred expiry decision.")]
        public Task<CallToolResult> GetStatusAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
            [Description(GdsToolDescriptions.CertificateTypeId)]
            string certificateTypeId,
            [Description(GdsToolDescriptions.CertificateGroupId)]
            string? certificateGroupId = null,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArrayOf<Variant> arguments = [new Variant(GdsAccess.Node(applicationId)),
                    new Variant(GdsAccess.OptionalNode(certificateGroupId)),
                    new Variant(GdsAccess.Node(certificateTypeId))];
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.CertificateDirectoryType_GetCertificateStatus, arguments, token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["updateRequired"] = GdsJson.Boolean(call, 0);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Gets the application-authorized trust-list identity from the directory.
        /// </summary>
        [McpServerTool(Name = "certificate_get_trust_list", ReadOnly = true, Destructive = false)]
        [Description("Get the directory-authorized TrustList node for an application/group. " +
            "Returns its real NodeId; download and staging remain separate explicit operations.")]
        public Task<CallToolResult> GetTrustListAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
            [Description(GdsToolDescriptions.CertificateGroupId)]
            string? certificateGroupId = null,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArrayOf<Variant> arguments = [new Variant(GdsAccess.Node(applicationId)),
                    new Variant(GdsAccess.OptionalNode(certificateGroupId))];
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.CertificateDirectoryType_GetTrustList, arguments, token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["trustListId"] = GdsJson.Node(call, 0).ToString();
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads aligned certificate-type/certificate pairs from either supported model.
        /// </summary>
        [McpServerTool(Name = "certificate_get_certificates", ReadOnly = true, Destructive = false)]
        [Description("Read public certificates from a Directory or Server root. Returns aligned type ids and " +
            "fingerprints; optional artifactPath exports only this page as a public DER/base64 JSON bundle.")]
        public Task<CallToolResult> GetCertificatesAsync(
            [Description("NodeId of the actual CertificateDirectoryType instance for source='Directory', " +
                "or ServerConfigurationType instance for source='Server'; not a certificate group.")]
            string nodeId,
            [Description("Certificate source model: 'Directory' reads a registered application's certificates; " +
                "'Server' reads the target server's configured certificates.")]
            GdsCertificateSource source,
            [Description("Registered application NodeId, required when source='Directory'; " +
                "not sent when source='Server'. Use the id returned by the directory, not its application URI.")]
            string? applicationId = null,
            [Description(GdsToolDescriptions.CertificateGroupId)]
            string? certificateGroupId = null,
            [Description("Optional output path beneath TransferRoot for this page's public DER/base64 JSON bundle, " +
                "e.g. 'certificates.json'; omit for fingerprint/type metadata only.")]
            string? artifactPath = null,
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
                if (!Enum.IsDefined(source))
                {
                    throw new ArgumentOutOfRangeException(nameof(source));
                }
                if (artifactPath is not null)
                {
                    m_access.Transfers.ValidateDownloadPath(artifactPath);
                }
                NodeId group = GdsAccess.OptionalNode(certificateGroupId);
                ArrayOf<Variant> arguments = source == GdsCertificateSource.Directory
                    ? [new Variant(GdsAccess.Node(applicationId!)), new Variant(group)] : [new Variant(group)];
                GdsEndpoint endpoint = await m_access.EndpointAsync(nodeId,
                    source == GdsCertificateSource.Directory
                        ? GdsTarget.CertificateDirectory : GdsTarget.ServerConfiguration,
                    sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(source == GdsCertificateSource.Directory
                    ? GdsMethods.CertificateDirectoryType_GetCertificates
                    : MethodIds.ServerConfigurationType_GetCertificates,
                    arguments, token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    ArrayOf<NodeId> types = GdsJson.Nodes(call, 0);
                    ArrayOf<ByteString> certificates = GdsJson.ByteStrings(call, 1);
                    if (types.Count != certificates.Count)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError);
                    }
                    var items = new JsonArray();
                    int end = Math.Min(certificates.Count, offset + maxResults);
                    for (int index = offset; index < end; index++)
                    {
                        JsonNode item = GdsJson.Certificate(certificates[index]);
                        item["certificateTypeId"] = types[index].ToString();
                        items.Add(item);
                    }
                    result["certificates"] = items;
                    result["offset"] = offset;
                    result["nextOffset"] = end < certificates.Count ? end : null;
                    result["totalCount"] = certificates.Count;
                    if (artifactPath is not null && items.Count != 0)
                    {
                        result["artifact"] = await CertificateBundleAsync(artifactPath,
                            certificates.Slice(offset, items.Count), token).ConfigureAwait(false);
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Checks the directory's revocation result for a public certificate.
        /// </summary>
        [McpServerTool(Name = "certificate_check_revocation", ReadOnly = true, Destructive = false)]
        [Description("Check revocation for a public DER certificate beneath TransferRoot. Returns the certificate's " +
            "actual status and validityTime separately from the method status; never reads a private-key file.")]
        public Task<CallToolResult> CheckRevocationAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.PublicCertificatePath)]
            string certificatePath,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                ByteString certificate = await PublicCertificateAsync(certificatePath, token).ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.CertificateDirectoryType_CheckRevocationStatus, [new Variant(certificate)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    StatusCode status = GdsJson.Status(call, 0);
                    result["certificateStatus"] = status.ToString();
                    result["certificateStatusValue"] = status.Code;
                    result["validityTime"] = (DateTime)GdsJson.Time(call, 1);
                    result["error"] = StatusCode.IsBad(status);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Starts a request to sign an existing public CSR.
        /// </summary>
        [McpServerTool(Name = "certificate_start_signing_request", ReadOnly = false, Destructive = true)]
        [Description("Start signing a public DER CSR beneath TransferRoot. Returns the real requestId and status; " +
            "does not approve, poll or collect it. Private keys remain at their existing owner.")]
        public Task<CallToolResult> StartSigningRequestAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
            [Description(GdsToolDescriptions.CertificateTypeId)]
            string certificateTypeId,
            [Description("Existing public DER PKCS#10 signing-request file beneath TransferRoot, e.g. 'request.csr'; " +
                "its private key stays with the existing owner and is not uploaded.")]
            string csrPath,
            [Description(GdsToolDescriptions.CertificateGroupId)]
            string? certificateGroupId = null,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                m_access.Secrets.RequireIssuanceStore();
                NodeId application = GdsAccess.Node(applicationId);
                NodeId group = GdsAccess.OptionalNode(certificateGroupId);
                NodeId type = GdsAccess.Node(certificateTypeId);
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                ByteString csr = await m_access.PublicInputAsync(csrPath, token).ConfigureAwait(false);
                ValidateCsr(csr);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.CertificateDirectoryType_StartSigningRequest,
                    [new Variant(application), new Variant(group), new Variant(type), new Variant(csr)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["applicationId"] = applicationId;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["requestId"] = GdsJson.Node(call, 0).ToString();
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Starts a new-key-pair request with an authorized password reference.
        /// </summary>
        [McpServerTool(Name = "certificate_start_keypair_request", ReadOnly = false, Destructive = true)]
        [Description("Request a new certificate/key pair explicitly using input.passwordReference. " +
            "Returns requestId and the public directory label; secret use additionally binds the approved " +
            "registration/certificate. Does not approve, finish or deploy the request.")]
        public Task<CallToolResult> StartKeyPairRequestAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description("Registered application/group/type NodeIds, subject and domain names; " +
                "privateKeyFormat must be server-advertised. passwordReference must be an authorized protected " +
                "UTF-8 password reference bound to this directory, e.g. 'alias:key-password'; " +
                "never supply a plaintext password.")]
            GdsKeyPairInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                m_access.Secrets.RequireIssuanceStore();
                NodeId application = GdsAccess.Node(input.ApplicationId);
                NodeId group = GdsAccess.OptionalNode(input.CertificateGroupId);
                NodeId type = GdsAccess.Node(input.CertificateTypeId);
                GdsAccess.Text(input.SubjectName);
                GdsAccess.Text(input.PrivateKeyFormat);
                GdsAccess.Strings(input.DomainNames);
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                string secretResource = endpoint.SecretResource;
                using ISecret password = await m_access.Secrets.ResolveAsync(
                    input.PasswordReference, secretResource, McpCapability.CertificateAdministration, token)
                    .ConfigureAwait(false);
                string text = s_utf8.GetString(password.Bytes);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.CertificateDirectoryType_StartNewKeyPairRequest,
                    [new Variant(application), new Variant(group), new Variant(type), new Variant(input.SubjectName),
                        new Variant(input.DomainNames), new Variant(input.PrivateKeyFormat), new Variant(text)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["applicationId"] = input.ApplicationId;
                result["resource"] = endpoint.Resource;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["requestId"] = GdsJson.Node(call, 0).ToString();
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Collects one certificate request without masking successful issuance by local publication failures.
        /// </summary>
        [McpServerTool(Name = "certificate_finish_request", ReadOnly = false, Destructive = true)]
        [Description("Finish one pull request once. Returns status, public-chain bundle and private-key reference. " +
            "Optional certificatePath also exports leaf DER for later explicit push/revoke; never auto-retries.")]
        public Task<CallToolResult> FinishRequestAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
            [Description("Request NodeId returned by certificate_start_signing_request or " +
                "certificate_start_keypair_request for this directory/application; " +
                "finish checks it once, without polling.")]
            string requestId,
            [Description("Output path beneath TransferRoot for the issued public leaf/issuer DER/base64 JSON bundle, " +
                "e.g. 'issued-chain.json'. Private keys are stored as protected references, never in this file.")]
            string artifactPath,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            [Description("Optional output path beneath TransferRoot for the issued public leaf DER certificate, " +
                "e.g. 'issued-leaf.der'; omit to export only the chain bundle.")]
            string? certificatePath = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                NodeId application = GdsAccess.Node(applicationId);
                NodeId request = GdsAccess.Node(requestId);
                m_access.Transfers.ValidateDownloadPath(artifactPath);
                if (certificatePath is not null)
                {
                    m_access.Transfers.ValidateDownloadPath(certificatePath);
                }
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                string issuer = endpoint.SecretResource;
                string resource = await ApplicationResourceAsync(endpoint, application, token).ConfigureAwait(false);
                using McpSecretReservation reservation =
                    m_access.Secrets.ReserveIssuance(McpCapability.CertificateAdministration);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.CertificateDirectoryType_FinishRequest,
                    [new Variant(application), new Variant(request)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["applicationId"] = applicationId;
                result["requestId"] = requestId;
                result["resource"] = resource;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    ByteString certificate = GdsJson.Bytes(call, 0);
                    ByteString privateKey = GdsJson.Bytes(call, 1);
                    result["remoteIssuance"] = certificate.IsEmpty ? "noCertificateReturned" : "issued";
                    if (!privateKey.IsEmpty)
                    {
                        try
                        {
                            using var persistence = new CancellationTokenSource(
                                TimeSpan.FromSeconds(30), m_access.Time);
                            result["privateKey"] = GdsJson.Reference(await reservation.StoreForIssuerAsync(
                                privateKey, "certificate-private-key", resource, issuer,
                                m_access.Time.GetUtcNow() + m_access.Options.ReferenceLifetime,
                                Convert.ToHexString(SHA256.HashData(certificate.Span)), persistence.Token)
                                .ConfigureAwait(false));
                        }
                        catch (Exception failure) when (failure is IMcpSecretPersistenceFailure persistence)
                        {
                            result["error"] = true;
                            result["privateKeyPersistence"] = GdsJson.PersistenceFailure(persistence);
                        }
                        catch (ServiceResultException failure)
                        {
                            result["error"] = true;
                            result["privateKeyPersistence"] = GdsJson.Error(failure.StatusCode,
                                "Remote issuance succeeded, but the returned key exceeds local storage policy.");
                        }
                    }
                    if (!certificate.IsEmpty)
                    {
                        try
                        {
                            result["certificate"] = GdsJson.Certificate(certificate);
                            ArrayOf<ByteString> issuers = GdsJson.ByteStrings(call, 2);
                            using var publication = new CancellationTokenSource(
                                TimeSpan.FromSeconds(30), m_access.Time);
                            result["artifact"] = await CertificateBundleAsync(
                                artifactPath, [certificate, .. issuers], publication.Token).ConfigureAwait(false);
                            if (certificatePath is not null)
                            {
                                result["certificateArtifact"] = await m_access.PublicOutputAsync(
                                    certificatePath, certificate, publication.Token).ConfigureAwait(false);
                            }
                        }
                        catch (Exception failure) when (failure is ServiceResultException or IOException or
                            CryptographicException or ArgumentException or OperationCanceledException)
                        {
                            result["error"] = true;
                            result["publicArtifact"] = "publicationFailedAfterRemoteIssuance";
                        }
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Revokes one public certificate explicitly.
        /// </summary>
        [McpServerTool(Name = "certificate_revoke", ReadOnly = false, Destructive = true)]
        [Description("Revoke one application's public DER certificate through its directory. Returns actual status " +
            "and applicationId; does not delete a local key, apply push changes or reconnect.")]
        public Task<CallToolResult> RevokeAsync(
            [Description(GdsToolDescriptions.CertificateDirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
            [Description(GdsToolDescriptions.PublicCertificatePath)]
            string certificatePath,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                NodeId application = GdsAccess.Node(applicationId);
                GdsEndpoint endpoint = await DirectoryAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                string resource = await ApplicationResourceAsync(endpoint, application, token).ConfigureAwait(false);
                ByteString certificate = await PublicCertificateAsync(certificatePath, token).ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.CertificateDirectoryType_RevokeCertificate,
                    [new Variant(application), new Variant(certificate)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["applicationId"] = applicationId;
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    try
                    {
                        await m_access.Secrets.RevokeCredentialAsync(
                            resource, Convert.ToHexString(SHA256.HashData(certificate.Span)), CancellationToken.None)
                            .ConfigureAwait(false);
                        result["localReferences"] = "invalidated";
                    }
                    catch (Exception failure) when (failure is IMcpSecretPersistenceFailure persistence)
                    {
                        result["error"] = true;
                        result["localCleanup"] = GdsJson.PersistenceFailure(persistence);
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Resolves the typed certificate directory on the current authorized session.
        /// </summary>
        private ValueTask<GdsEndpoint> DirectoryAsync(string node, string? session, CancellationToken ct)
        {
            return m_access.EndpointAsync(node, GdsTarget.CertificateDirectory, session, ct);
        }

        /// <summary>
        /// Binds a collected key to the registered application, not a caller-supplied target or directory alias.
        /// The read happens before issuance so a committed finish never depends on a later live connection.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<string> ApplicationResourceAsync(
            GdsEndpoint endpoint, NodeId applicationId, CancellationToken ct)
        {
            CallMethodResult call = await endpoint.CallAsync(
                GdsMethods.DirectoryType_GetApplication, [new Variant(applicationId)], ct).ConfigureAwait(false);
            if (StatusCode.IsBad(call.StatusCode))
            {
                throw new ServiceResultException(call.StatusCode);
            }
            if (!GdsJson.Output(call, 0).TryGetStructure<Gds.ApplicationRecordDataType>(
                endpoint.Context, out Gds.ApplicationRecordDataType? application))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            return "opcua-app:" + GdsAccess.Text(application.ApplicationUri ?? string.Empty);
        }

        /// <summary>
        /// Rejects PKCS#12 and trailing bytes at the public certificate boundary.
        /// </summary>
        private async ValueTask<ByteString> PublicCertificateAsync(string path, CancellationToken ct)
        {
            ByteString bytes = await m_access.PublicInputAsync(path, ct).ConfigureAwait(false);
            _ = GdsJson.Certificate(bytes);
            return bytes;
        }

        /// <summary>
        /// Serializes only explicitly selected public certificates into a bounded artifact before publication.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<JsonObject> CertificateBundleAsync(
            string path, ArrayOf<ByteString> certificates, CancellationToken ct)
        {
            if (certificates.Count > 500)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            using var buffer = new McpBoundedMemoryStream(m_access.Options.MaxArtifactBytes, ct);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("format", "opcua-public-certificates-v1");
                writer.WriteStartArray("certificates");
                foreach (ByteString certificate in certificates)
                {
                    _ = GdsJson.Certificate(certificate);
                    writer.WriteBase64StringValue(certificate.Span);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            buffer.Position = 0;
            return await m_access.Transfers.DownloadAsync(path,
                async (output, token) => await buffer.CopyToAsync(output, token).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Accepts a DER PKCS#10 request, not a private-key container under a public file name.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateCsr(ByteString csr)
        {
            _ = CertificateRequest.LoadSigningRequest(csr.Span, HashAlgorithmName.SHA256,
                out int consumed, CertificateRequestLoadOptions.Default);
            if (consumed != csr.Length)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
        }

        private readonly GdsAccess m_access;
        private readonly GdsOperationState m_state;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
