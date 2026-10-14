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
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    public sealed partial class GdsCertificateTools
    {
        /// <summary>
        /// Reads server-advertised push capabilities and actual transaction diagnostics.
        /// </summary>
        [McpServerTool(Name = "certificate_inspect_push", ReadOnly = true, Destructive = false)]
        [Description("Inspect a typed ServerConfiguration root, groups and transaction diagnostics. Returns " +
            "actual supports/quality flags and public resource binding; does not create or apply a transaction.")]
        public Task<CallToolResult> InspectPushAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
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
                GdsEndpoint endpoint = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                JsonObject result = await McpModelAccess.PropertiesAsync(endpoint.Session, endpoint.Proxy.ObjectId,
                    [new QualifiedName("SupportedPrivateKeyFormats"), new QualifiedName("MaxTrustListSize"),
                        new QualifiedName("MulticastDnsEnabled"), new QualifiedName("ServerCapabilities"),
                        new QualifiedName("SupportsTransactions"), new QualifiedName("ApplicationUri"),
                        new QualifiedName("HasSecureElement")], token).ConfigureAwait(false);
                result["resource"] = endpoint.Resource;
                result["privateKeyResource"] = await PushApplicationResourceAsync(endpoint, token)
                    .ConfigureAwait(false);
                result["sessionName"] = endpoint.Info.Name;
                result["sessionId"] = endpoint.SessionId.ToString();
                result["sessionConnectedAt"] = endpoint.Info.ConnectedAt;
                result["groups"] = await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(endpoint.Session, endpoint.Proxy.ObjectId,
                        ObjectTypeIds.CertificateGroupType, ct: token),
                    reference => McpModelAccess.Reference(endpoint.Session, reference),
                    offset, maxResults, token).ConfigureAwait(false);
                result["methods"] = await McpCompanionTools.PageAsync(
                    endpoint.Session.EnumerateReferencesAsync(endpoint.Proxy.ObjectId,
                        ReferenceTypeIds.HasComponent, nodeClassMask: (uint)NodeClass.Method, ct: token),
                    reference => McpModelAccess.Reference(endpoint.Session, reference),
                    offset, maxResults, token).ConfigureAwait(false);
                NodeId diagnostics = await McpModelAccess.ChildAsync(endpoint.Session, endpoint.Proxy.ObjectId,
                    new QualifiedName("TransactionDiagnostics"), token).ConfigureAwait(false);
                result["transactionDiagnostics"] = diagnostics.IsNull ? null :
                    await McpModelAccess.PropertiesAsync(endpoint.Session, diagnostics,
                        [new QualifiedName("StartTime"), new QualifiedName("EndTime"), new QualifiedName("Result"),
                            new QualifiedName("AffectedTrustLists"), new QualifiedName("AffectedCertificateGroups")],
                        token).ConfigureAwait(false);
                if (!diagnostics.IsNull)
                {
                    (NodeId errorNode, DataValue errors) = await McpModelAccess.PropertyAsync(
                        endpoint.Session, diagnostics, new QualifiedName("Errors"), token).ConfigureAwait(false);
                    var projected = new JsonArray();
                    if (!errorNode.IsNull &&
                        !StatusCode.IsBad(errors.StatusCode) &&
                        errors.WrappedValue.TryGetStructure(endpoint.Context,
                            out ArrayOf<TransactionErrorType> values))
                    {
                        int end = Math.Min(values.Count, offset + maxResults);
                        for (int index = offset; index < end; index++)
                        {
                            JsonNode error = new JsonObject
                            {
                                ["targetId"] = values[index].TargetId.ToString(),
                                ["statusCodeValue"] = values[index].Error.Code
                            };
                            projected.Add(error);
                        }
                        result["transactionErrorCount"] = values.Count;
                    }
                    result["transactionErrors"] = projected;
                    result["transactionErrorsStatus"] = errors.StatusCode.Code;
                    result["transactionErrorMessages"] = "omittedToProtectProviderDiagnostics";
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Lists public rejected-certificate metadata with optional public bundle export.
        /// </summary>
        [McpServerTool(Name = "certificate_get_rejected", ReadOnly = true, Destructive = false)]
        [Description("Read the server's rejected certificates. Returns a bounded fingerprint page and optional " +
            "public DER/base64 bundle beneath TransferRoot; does not trust or approve any certificate.")]
        public Task<CallToolResult> GetRejectedAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description("Optional output path beneath TransferRoot for this page's rejected public certificates " +
                "as a DER/base64 JSON bundle, e.g. 'rejected.json'; omit for fingerprint metadata only.")]
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
                if (artifactPath is not null)
                {
                    m_access.Transfers.ValidateDownloadPath(artifactPath);
                }
                GdsEndpoint endpoint = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    MethodIds.ServerConfigurationType_GetRejectedList, [], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    ArrayOf<ByteString> certificates = GdsJson.ByteStrings(call, 0);
                    result["certificates"] = McpCompanionTools.Page(
                        certificates, GdsJson.Certificate, offset, maxResults);
                    int count = Math.Max(0, Math.Min(maxResults, certificates.Count - offset));
                    if (artifactPath is not null && count != 0)
                    {
                        result["artifact"] = await CertificateBundleAsync(
                            artifactPath, certificates.Slice(offset, count), token).ConfigureAwait(false);
                    }
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Creates exactly one CSR, optionally regenerating a key only when explicitly requested.
        /// </summary>
        [McpServerTool(Name = "certificate_create_csr", ReadOnly = false, Destructive = true)]
        [Description("Create a server CSR and export public DER beneath TransferRoot. Key regeneration is explicit " +
            "and requires a protected nonce reference. Returns status/artifact; never auto-signs or applies changes.")]
        public Task<CallToolResult> CreateCsrAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description("Certificate group/type NodeIds and subject, e.g. subjectName='CN=Plant server'. " +
                "regeneratePrivateKey defaults to false; true requires a protected nonceReference bound to this " +
                "configuration with at least 32 nonce bytes. Never supply private key or nonce bytes inline.")]
            GdsCsrInput input,
            [Description("Output path beneath TransferRoot for the generated public DER PKCS#10 CSR, " +
                "e.g. 'server.csr'; not a private-key destination.")]
            string artifactPath,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                if (input.RegeneratePrivateKey && input.NonceReference is null)
                {
                    throw new ArgumentException("Key regeneration requires a protected nonce reference.");
                }
                NodeId group = GdsAccess.OptionalNode(input.CertificateGroupId);
                NodeId type = GdsAccess.Node(input.CertificateTypeId);
                GdsAccess.Text(input.SubjectName);
                m_access.Transfers.ValidateDownloadPath(artifactPath);
                GdsEndpoint endpoint = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                using ISecret? secret = input.NonceReference is null ? null :
                    await m_access.Secrets.ResolveAsync(input.NonceReference, endpoint.Resource,
                        McpCapability.CertificateAdministration, token).ConfigureAwait(false);
                ByteString nonce = secret is null ? default : ByteString.From(secret.Bytes.ToArray());
                if (input.RegeneratePrivateKey && nonce.Length < 32)
                {
                    throw new ArgumentException("A regeneration nonce must contain at least 32 bytes.");
                }
                using GdsOperationState.PushOperation owner = EnterPush(endpoint);
                CallMethodResult call = await endpoint.CallAsync(MethodIds.ServerConfigurationType_CreateSigningRequest,
                    [new Variant(group), new Variant(type), new Variant(input.SubjectName),
                        new Variant(input.RegeneratePrivateKey), new Variant(nonce)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    owner.Refused();
                }
                else
                {
                    result["remoteOperation"] = "completed";
                    await PublishCreatedAsync(result, GdsJson.Bytes(call, 0), artifactPath, csr: true)
                        .ConfigureAwait(false);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Calls the optional self-signed method only when the actual root advertises it.
        /// </summary>
        [McpServerTool(Name = "certificate_create_self_signed", ReadOnly = false, Destructive = true)]
        [Description("Explicitly create a self-signed certificate where advertised. Returns actual status and " +
            "public DER artifact; server slot, lifetime and key-size restrictions remain authoritative.")]
        public Task<CallToolResult> CreateSelfSignedAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description("Certificate group/type NodeIds, subject and DNS/IP alternative names for the server slot. " +
                "lifetimeInDays defaults to 365 and keySizeInBits to 2048; the server decides supported values. " +
                "E.g. subjectName='CN=Plant server'.")]
            GdsSelfSignedInput input,
            [Description("Output path beneath TransferRoot for the generated public DER certificate, " +
                "e.g. 'self-signed.der'; no private key is exported.")]
            string artifactPath,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                NodeId group = GdsAccess.OptionalNode(input.CertificateGroupId);
                NodeId type = GdsAccess.Node(input.CertificateTypeId);
                GdsAccess.Text(input.SubjectName);
                GdsAccess.Strings(input.DnsNames);
                GdsAccess.Strings(input.IpAddresses);
                m_access.Transfers.ValidateDownloadPath(artifactPath);
                GdsEndpoint endpoint = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                await RequireMethodAsync(endpoint, "CreateSelfSignedCertificate", token).ConfigureAwait(false);
                using GdsOperationState.PushOperation owner = EnterPush(endpoint);
                CallMethodResult call = await endpoint.CallAsync(
                    MethodIds.ServerConfigurationType_CreateSelfSignedCertificate,
                    [new Variant(group), new Variant(type), new Variant(input.SubjectName),
                        new Variant(input.DnsNames), new Variant(input.IpAddresses),
                        new Variant(input.LifetimeInDays), new Variant(input.KeySizeInBits)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    owner.Refused();
                }
                else
                {
                    result["remoteOperation"] = "completed";
                    await PublishCreatedAsync(result, GdsJson.Bytes(call, 0), artifactPath, csr: false)
                        .ConfigureAwait(false);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Stages a public certificate with optional protected private-key material.
        /// </summary>
        [McpServerTool(Name = "certificate_stage_update", ReadOnly = false, Destructive = true)]
        [Description("Stage one certificate update using public DER paths and an optional private-key reference. " +
            "Returns actual applyChangesRequired; never calls ApplyChanges, reconnects or elevates the session.")]
        public Task<CallToolResult> StageUpdateAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description("Target group/type NodeIds and public leaf/issuer DER paths beneath TransferRoot. " +
                "An optional privateKeyReference must match the target's privateKeyResource and advertised format; " +
                "omit it to use the server's pending key. Never supply private-key bytes or a private-key file.")]
            GdsPushCertificateInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                NodeId group = GdsAccess.OptionalNode(input.CertificateGroupId);
                NodeId type = GdsAccess.Node(input.CertificateTypeId);
                GdsAccess.Text(input.PrivateKeyFormat, allowEmpty: true);
                GdsAccess.Strings(input.IssuerCertificatePaths, 32);
                GdsEndpoint endpoint = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                ByteString certificate = await PublicCertificateAsync(input.CertificatePath, token)
                    .ConfigureAwait(false);
                string resource = await PushApplicationResourceAsync(endpoint, token).ConfigureAwait(false);
                var issuers = new List<ByteString>();
                for (int index = 0; index < input.IssuerCertificatePaths.Count; index++)
                {
                    issuers.Add(await PublicCertificateAsync(input.IssuerCertificatePaths[index], token)
                        .ConfigureAwait(false));
                }
                using ISecret? privateKey = input.PrivateKeyReference is null ? null :
                    await m_access.Secrets.ResolveAsync(input.PrivateKeyReference,
                        resource,
                        McpCapability.CertificateAdministration, token).ConfigureAwait(false);
                ByteString key = privateKey is null ? default : ByteString.From(privateKey.Bytes.ToArray());
                using GdsOperationState.PushOperation owner = EnterPush(endpoint);
                CallMethodResult call = await endpoint.CallAsync(MethodIds.ServerConfigurationType_UpdateCertificate,
                    [new Variant(group), new Variant(type), new Variant(certificate),
                        new Variant([.. issuers]), new Variant(input.PrivateKeyFormat),
                        new Variant(key)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    owner.Refused();
                }
                else
                {
                    result["applyChangesRequired"] = GdsJson.Boolean(call, 0);
                    result["transactionOwnerSessionId"] = endpoint.SessionId.ToString();
                    result["applyInvoked"] = false;
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Stages deletion of one advertised certificate slot.
        /// </summary>
        [McpServerTool(Name = "certificate_stage_delete", ReadOnly = false, Destructive = true)]
        [Description("Stage deletion of one certificate group/type slot where supported. Returns actual status; " +
            "the explicit certificate_apply_changes tool is separate and may be refused by the server.")]
        public Task<CallToolResult> StageDeleteAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
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
                NodeId group = GdsAccess.OptionalNode(certificateGroupId);
                NodeId type = GdsAccess.Node(certificateTypeId);
                GdsEndpoint endpoint = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                await RequireMethodAsync(endpoint, "DeleteCertificate", token).ConfigureAwait(false);
                using GdsOperationState.PushOperation owner = EnterPush(endpoint);
                CallMethodResult call = await endpoint.CallAsync(MethodIds.ServerConfigurationType_DeleteCertificate,
                    [new Variant(group), new Variant(type)], token).ConfigureAwait(false);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    owner.Refused();
                }
                JsonObject result = endpoint.Result(call);
                result["applyInvoked"] = false;
                return result;
            }, ct);
        }

        /// <summary>
        /// Applies only this caller's tracked pending changes on the same session generation.
        /// </summary>
        [McpServerTool(Name = "certificate_apply_changes", ReadOnly = false, Destructive = true)]
        [Description("Explicitly apply this caller's staged push changes. Returns the real status and captured " +
            "session identity even if the server closes the connection. Never reconnects or repeats the mutation.")]
        public Task<CallToolResult> ApplyChangesAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return FinishPushAsync(configurationNodeId, cancel: false, sessionName, ct);
        }

        /// <summary>
        /// Cancels only this caller's tracked push transaction where the server advertises cancellation.
        /// </summary>
        [McpServerTool(Name = "certificate_cancel_changes", ReadOnly = false, Destructive = true)]
        [Description("Explicitly cancel this caller's pending push changes where CancelChanges is advertised. " +
            "Returns the real status, including BadNothingToDo; cannot cancel another caller's staged work.")]
        public Task<CallToolResult> CancelChangesAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return FinishPushAsync(configurationNodeId, cancel: true, sessionName, ct);
        }

        /// <summary>
        /// Preserves the remote commit independently from public artifact publication.
        /// </summary>
        private async ValueTask PublishCreatedAsync(
            JsonObject result, ByteString value, string path, bool csr)
        {
            try
            {
                if (csr)
                {
                    ValidateCsr(value);
                }
                else
                {
                    result["certificate"] = GdsJson.Certificate(value);
                }
                using var publication = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_access.Time);
                result["artifact"] = await m_access.PublicOutputAsync(path, value, publication.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is ServiceResultException or IOException or ArgumentException or
                CryptographicException or OperationCanceledException)
            {
                result["error"] = true;
                result["publicArtifact"] = "publicationFailedAfterRemoteCompletion";
            }
        }

        /// <summary>
        /// Does not reread a disconnected session or replay a committed apply/cancel.
        /// </summary>
        private Task<CallToolResult> FinishPushAsync(
            string node, bool cancel, string? sessionName, CancellationToken ct)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsEndpoint endpoint = await PushAsync(node, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                if (cancel)
                {
                    await RequireMethodAsync(endpoint, "CancelChanges", token).ConfigureAwait(false);
                }
                using GdsOperationState.PushOperation owner = EnterPush(endpoint, requireExisting: true);
                CallMethodResult call = await endpoint.CallAsync(cancel
                    ? MethodIds.ServerConfigurationType_CancelChanges : MethodIds.ServerConfigurationType_ApplyChanges,
                    [], token).ConfigureAwait(false);
                owner.Complete(!StatusCode.IsBad(call.StatusCode) || call.StatusCode == StatusCodes.BadNothingToDo);
                JsonObject result = endpoint.Result(call);
                result["operation"] = cancel ? "cancelChanges" : "applyChanges";
                result["reconnectInvoked"] = false;
                return result;
            }, ct);
        }

        /// <summary>
        /// Resolves a ServerConfigurationType or an advertised subtype.
        /// </summary>
        private ValueTask<GdsEndpoint> PushAsync(string node, string? sessionName, CancellationToken ct)
        {
            return m_access.EndpointAsync(node, GdsTarget.ServerConfiguration, sessionName, ct);
        }

        /// <summary>
        /// Uses a managed application's advertised URI when present, otherwise the connected server identity.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask<string> PushApplicationResourceAsync(GdsEndpoint endpoint, CancellationToken ct)
        {
            (NodeId node, DataValue value) = await McpModelAccess.PropertyAsync(
                endpoint.Session, endpoint.Proxy.ObjectId, new QualifiedName("ApplicationUri"), ct)
                .ConfigureAwait(false);
            if (node.IsNull)
            {
                return "opcua-app:" +
                    GdsAccess.Text(
                        endpoint.Session.ConfiguredEndpoint.Description.Server.ApplicationUri ?? string.Empty);
            }
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            return value.WrappedValue.TryGetValue(out string uri)
                ? "opcua-app:" + GdsAccess.Text(uri)
                : throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        /// <summary>
        /// Enforces MCP caller ownership in addition to the server's UA-session transaction ownership.
        /// </summary>
        private GdsOperationState.PushOperation EnterPush(GdsEndpoint endpoint, bool requireExisting = false)
        {
            return m_state.EnterPush(endpoint,
                m_access.Sessions.OperationPolicy.Require(McpCapability.CertificateAdministration), requireExisting);
        }

        /// <summary>
        /// Checks an optional method's qualified model name on the actual object.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask RequireMethodAsync(GdsEndpoint endpoint, string name, CancellationToken ct)
        {
            NodeId method = await McpModelAccess.ChildAsync(
                endpoint.Session, endpoint.Proxy.ObjectId, new QualifiedName(name), ct).ConfigureAwait(false);
            if (method.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The selected server object does not advertise the optional method.");
            }
        }
    }
}
