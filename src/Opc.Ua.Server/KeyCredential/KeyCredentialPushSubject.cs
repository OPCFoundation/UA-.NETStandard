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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Binds OPC 10000-12 §8 KeyCredentialConfiguration nodes to an <see cref="IKeyCredentialStore"/>.
    /// </summary>
    public sealed class KeyCredentialPushSubject
    {
        /// <summary>
        /// Fallback namespace URI used for standalone credential configuration instances.
        /// </summary>
        /// <remarks>
        /// The standard <c>ServerConfiguration/KeyCredentialConfiguration</c> folder lives in
        /// namespace 0, which is reserved for the OPC UA standard address space. A node-manager
        /// binding uses that manager's nonstandard namespace. Standalone hosts using this fallback
        /// must register it with the node manager that indexes the credential nodes.
        /// </remarks>
        public const string NamespaceUri = "urn:opcfoundation:netstandard:keycredential-push";

        /// <summary>
        /// Standard NodeId of ServerConfiguration/KeyCredentialConfiguration.
        /// </summary>
        public static readonly NodeId StandardConfigurationFolderNodeId = new(18155u);

        /// <summary>
        /// Creates a KeyCredential push subject.
        /// </summary>
        public KeyCredentialPushSubject(
            IKeyCredentialStore store,
            KeyCredentialPushOptions? options = null)
            : this(store, options, null)
        {
        }

        /// <summary>
        /// Creates a push subject using the application's certificate and security-policy registries.
        /// </summary>
        public KeyCredentialPushSubject(
            IKeyCredentialStore store,
            KeyCredentialPushOptions? options,
            ICertificateRegistry? certificates,
            ISecurityPolicyRegistry? securityPolicies = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_options = options ?? new KeyCredentialPushOptions();
            m_certificates = certificates;
            m_securityPolicies = securityPolicies ?? SecurityPolicies.Default;
        }

        /// <summary>
        /// Configures a source-generated KeyCredentialConfigurationFolderState.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="folder"/> or <paramref name="context"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// Thrown when <c>ConfigurationFolderPath</c> is set to a path other than
        /// <c>ServerConfiguration/KeyCredentialConfiguration</c>.
        /// </exception>
        public async ValueTask BindAsync(
            KeyCredentialConfigurationFolderState folder,
            ISystemContext context,
            Func<BaseInstanceState, CancellationToken, ValueTask>? addNodeAsync = null,
            Func<BaseInstanceState, CancellationToken, ValueTask>? removeNodeAsync = null,
            CancellationToken ct = default)
        {
            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!string.IsNullOrEmpty(m_options.ConfigurationFolderPath) &&
                !string.Equals(
                    m_options.ConfigurationFolderPath,
                    "ServerConfiguration/KeyCredentialConfiguration",
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "Only the standard ServerConfiguration/KeyCredentialConfiguration folder is currently supported.");
            }

            m_addNodeAsync = addNodeAsync;
            m_removeNodeAsync = removeNodeAsync;

            folder.AddCreateCredential(context, c =>
            {
                c.OnCall = null;
                c.OnCallAsync = OnCreateCredentialAsync;
            });

            IList<BaseInstanceState> children = [];
            folder.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is KeyCredentialConfigurationState credentialState)
                {
                    WireCredentialState(credentialState, context);
                }
            }

            IReadOnlyList<string> credentialIds = await m_store.ListAsync(ct).ConfigureAwait(false);
            foreach (string credentialId in credentialIds)
            {
                if (FindCredentialState(folder, context, credentialId) == null)
                {
                    KeyCredentialConfigurationState state = CreateCredentialState(
                        folder,
                        context,
                        credentialId,
                        credentialId,
                        KeyCredentialBridgeOptions.DefaultProfileUri,
                        []);
                    state.CredentialId!.Value = credentialId;
                    await AddNodeAsync(state, ct).ConfigureAwait(false);
                }
            }

            await folder.ClearChangeMasksAsync(context, includeChildren: true, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Supplies the application's certificate registry unless one was explicitly provided at construction.
        /// </summary>
        internal void ConfigureEncryption(ICertificateRegistry? certificates)
        {
            m_certificates ??= certificates;
        }

        private async ValueTask<CreateCredentialMethodStateResult> OnCreateCredentialAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string name,
            string resourceUri,
            string profileUri,
            ArrayOf<string> endpointUrls,
            CancellationToken ct = default)
        {
            ServiceResult authorization = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(authorization))
            {
                return new CreateCredentialMethodStateResult { ServiceResult = authorization };
            }

            if (method.Parent is not KeyCredentialConfigurationFolderState folder)
            {
                return new CreateCredentialMethodStateResult
                {
                    ServiceResult = new ServiceResult(StatusCodes.BadInvalidState)
                };
            }

            string browseName = string.IsNullOrWhiteSpace(name) ? resourceUri : name;
            if (string.IsNullOrWhiteSpace(browseName))
            {
                return new CreateCredentialMethodStateResult
                {
                    ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument)
                };
            }

            IList<BaseInstanceState> existingChildren = [];
            folder.GetChildren(context, existingChildren);
            if (existingChildren.OfType<KeyCredentialConfigurationState>()
                .Any(child => string.Equals(
                    child.BrowseName.Name,
                    browseName,
                    StringComparison.Ordinal)))
            {
                return new CreateCredentialMethodStateResult
                {
                    ServiceResult = new ServiceResult(StatusCodes.BadNodeIdExists)
                };
            }

            KeyCredentialConfigurationState state = CreateCredentialState(
                folder,
                context,
                browseName,
                resourceUri,
                profileUri,
                endpointUrls.ToArray() ?? []);
            await AddNodeAsync(state, ct).ConfigureAwait(false);
            return new CreateCredentialMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                CredentialNodeId = state.NodeId
            };
        }

        /// <summary>
        /// Authorizes a credential update, decodes its secret and clears the temporary plaintext after persistence.
        /// </summary>
        private async ValueTask<KeyCredentialUpdateMethodStateResult> OnUpdateCredentialAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string credentialId,
            ByteString credentialSecret,
            string certificateThumbprint,
            string securityPolicyUri,
            CancellationToken ct = default)
        {
            ServiceResult authorization = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(authorization))
            {
                return new KeyCredentialUpdateMethodStateResult { ServiceResult = authorization };
            }

            if (string.IsNullOrWhiteSpace(credentialId) || credentialSecret.IsNull)
            {
                return new KeyCredentialUpdateMethodStateResult
                {
                    ServiceResult = new ServiceResult(StatusCodes.BadInvalidArgument)
                };
            }
            ct.ThrowIfCancellationRequested();

            var subject = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sub"] = credentialId
            };
            if (!string.IsNullOrWhiteSpace(certificateThumbprint))
            {
                subject["x509.thumbprint"] = certificateThumbprint;
            }
            if (!string.IsNullOrWhiteSpace(securityPolicyUri))
            {
                subject["ua.securityPolicyUri"] = securityPolicyUri;
            }

            byte[]? secret = null;
            try
            {
                string? previousCredentialId = null;
                if (method.Parent is KeyCredentialConfigurationState existingState)
                {
                    previousCredentialId = existingState.CredentialId?.Value;
                }

                secret = await DecodeSecretAsync(
                    context, credentialSecret, certificateThumbprint, securityPolicyUri, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var credential = new KeyCredential(secret, DateTime.MaxValue, subject, []);
                await m_store.UpdateAsync(credentialId, credential, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(previousCredentialId) &&
                    !string.Equals(previousCredentialId, credentialId, StringComparison.Ordinal))
                {
                    await m_store.DeleteAsync(previousCredentialId, ct).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException ex)
            {
                context.Telemetry.CreateLogger<KeyCredentialPushSubject>().CredentialSecretRejected(ex.StatusCode);
                return new KeyCredentialUpdateMethodStateResult { ServiceResult = ex.Result };
            }
            finally
            {
                if (secret != null)
                {
                    CryptoUtils.ZeroMemory(secret);
                }
            }

            if (method.Parent is KeyCredentialConfigurationState state)
            {
                state.CredentialId ??= state.CreateOrReplaceCredentialId(context, state.CredentialId);
                state.CredentialId.Value = credentialId;
                state.ServiceStatus ??= state.CreateOrReplaceServiceStatus(context, state.ServiceStatus);
                state.ServiceStatus.Value = StatusCodes.Good;
                await state.ClearChangeMasksAsync(context, includeChildren: true, ct)
                    .ConfigureAwait(false);
            }

            return new KeyCredentialUpdateMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Accepts a plaintext secret or validates and decrypts its RSA encrypted-secret envelope.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<byte[]> DecodeSecretAsync(
            ISystemContext context,
            ByteString encrypted,
            string thumbprint,
            string policyUri,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(policyUri))
            {
                if (!string.IsNullOrEmpty(thumbprint))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                return encrypted.ToArray();
            }

            _ = ResolveEncryptionPolicy(policyUri);
            if (string.IsNullOrEmpty(thumbprint) || m_certificates == null)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using CertificateEntryCollection entries = m_certificates.SnapshotApplicationCertificates();
            Certificate? receiver = null;
            foreach (CertificateEntry entry in entries)
            {
                if (string.Equals(entry.Certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase) &&
                    entry.Certificate.HasPrivateKey)
                {
                    receiver = entry.Certificate;
                    break;
                }
            }
            if (receiver == null)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using RSA? key = receiver.GetRSAPrivateKey() ?? throw new ServiceResultException(StatusCodes.BadCertificateInvalid);

            byte[] encoded = encrypted.ToArray();
            try
            {
                using (var decoder = new BinaryDecoder(encoded, context.AsMessageContext()))
                {
                    if (decoder.ReadNodeId(null) != DataTypeIds.RsaEncryptedSecret ||
                        decoder.ReadByte(null) != (byte)ExtensionObjectEncoding.Binary)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                    uint length = decoder.ReadUInt32(null);
                    if (length != encoded.Length - decoder.Position)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                }
                using var decryptor = EncryptedSecret.CreateForRsa(context.AsMessageContext(), policyUri, receiver);
                (bool success, byte[]? decoded) = await decryptor.TryDecryptAsync(encoded, null, ct)
                    .ConfigureAwait(false);
                if (!success || decoded == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                return decoded;
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode != StatusCodes.BadCertificateInvalid &&
                ex.StatusCode != StatusCodes.BadSecurityPolicyRejected &&
                ex.StatusCode != StatusCodes.BadInvalidArgument)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The encrypted credential is invalid.", ex);
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or ArgumentException or OverflowException)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "The encrypted credential is invalid.", ex);
            }
            finally
            {
                CryptoUtils.ZeroMemory(encoded);
            }
        }

        /// <summary>
        /// Resolves an allowed RSA encryption policy and rejects unsupported or ephemeral-key policies.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private SecurityPolicyInfo ResolveEncryptionPolicy(string policyUri)
        {
            SecurityPolicyInfo? policy = m_securityPolicies.GetInfo(policyUri);
            if (!m_options.AllowedSecurityPolicyUris.Contains(policyUri) ||
                policy == null ||
                policy.CertificateKeyFamily != CertificateKeyFamily.RSA ||
                policy.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None ||
                policyUri is not (SecurityPolicies.Basic256Sha256 or
                    SecurityPolicies.Aes128_Sha256_RsaOaep or SecurityPolicies.Aes256_Sha256_RsaPss))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
            }
            return policy;
        }

        /// <summary>
        /// Adapts encrypting-key selection to the asynchronous configuration-method callback.
        /// </summary>
        private ValueTask<GetEncryptingKeyMethodStateResult> OnGetEncryptingKeyAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string credentialId,
            string requestedSecurityPolicyUri,
            CancellationToken ct = default)
        {
            return new ValueTask<GetEncryptingKeyMethodStateResult>(
                GetEncryptingKey(context, credentialId, requestedSecurityPolicyUri, ct));
        }

        /// <summary>
        /// Returns an authorized caller's application certificate and the accepted credential-encryption policy.
        /// </summary>
        private GetEncryptingKeyMethodStateResult GetEncryptingKey(
            ISystemContext context,
            string credentialId,
            string requestedSecurityPolicyUri,
            CancellationToken ct)
        {
            ServiceResult authorization = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(authorization))
            {
                return new GetEncryptingKeyMethodStateResult { ServiceResult = authorization };
            }
            ct.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(credentialId))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                string policyUri = requestedSecurityPolicyUri;
                if (string.IsNullOrEmpty(policyUri))
                {
                    policyUri = m_options.AllowedSecurityPolicyUris.IsEmpty
                        ? string.Empty
                        : m_options.AllowedSecurityPolicyUris[0];
                }
                _ = ResolveEncryptionPolicy(policyUri);
                using CertificateEntry? certificate = m_certificates?.AcquireApplicationCertificateBySecurityPolicy(policyUri);
                if (certificate == null || !certificate.Certificate.HasPrivateKey)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
                return new GetEncryptingKeyMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    PublicKey = new ByteString(certificate.Certificate.RawData),
                    RevisedSecurityPolicyUri = policyUri
                };
            }
            catch (ServiceResultException ex)
            {
                context.Telemetry.CreateLogger<KeyCredentialPushSubject>().CredentialSecretRejected(ex.StatusCode);
                return new GetEncryptingKeyMethodStateResult { ServiceResult = ex.Result };
            }
        }

        private async ValueTask<ServiceResult> OnDeleteCredentialAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken ct = default)
        {
            ServiceResult authorization = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(authorization))
            {
                return authorization;
            }

            if (method.Parent is not KeyCredentialConfigurationState state)
            {
                return new ServiceResult(StatusCodes.BadInvalidState);
            }

            string credentialId = state.CredentialId?.Value ?? state.BrowseName.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(credentialId))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            await m_store.DeleteAsync(credentialId, ct).ConfigureAwait(false);
            state.Parent?.RemoveChild(state);
            await RemoveNodeAsync(state, ct).ConfigureAwait(false);
            return ServiceResult.Good;
        }

        private KeyCredentialConfigurationState CreateCredentialState(
            KeyCredentialConfigurationFolderState folder,
            ISystemContext context,
            string name,
            string resourceUri,
            string profileUri,
            IEnumerable<string> endpointUrls)
        {
            ushort namespaceIndex = GetInstanceNamespaceIndex(folder, context);
            QualifiedName browseName = new(name, namespaceIndex);
            KeyCredentialConfigurationState state = folder.AddServiceName_Placeholder(context, browseName);
            state.NodeId = CreateCredentialNodeId(name, namespaceIndex);
            state.SymbolicName = name;
            state.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            state.TypeDefinitionId = ObjectTypeIds.KeyCredentialConfigurationType;
            state.DisplayName = LocalizedText.From(name);
            state.ResourceUri ??= state.CreateOrReplaceResourceUri(context, state.ResourceUri);
            state.ResourceUri.Value = resourceUri ?? string.Empty;
            state.ProfileUri ??= state.CreateOrReplaceProfileUri(context, state.ProfileUri);
            state.ProfileUri.Value = string.IsNullOrWhiteSpace(profileUri)
                ? KeyCredentialBridgeOptions.DefaultProfileUri
                : profileUri;
            state.EndpointUrls ??= state.CreateOrReplaceEndpointUrls(context, state.EndpointUrls);
            state.EndpointUrls.Value = [.. endpointUrls];
            state.CredentialId ??= state.CreateOrReplaceCredentialId(context, state.CredentialId);
            state.ServiceStatus ??= state.CreateOrReplaceServiceStatus(context, state.ServiceStatus);
            state.ServiceStatus.Value = StatusCodes.Good;
            WireCredentialState(state, context);
            context.AssignInstanceChildNodeIds(state);
            return state;
        }

        /// <summary>
        /// Connects encrypting-key, credential-update and credential-deletion methods to this subject.
        /// </summary>
        private void WireCredentialState(KeyCredentialConfigurationState state, ISystemContext context)
        {
            state
                .AddGetEncryptingKey(context, c =>
                {
                    c.OnCall = null;
                    c.OnCallAsync = OnGetEncryptingKeyAsync;
                })
                .AddUpdateCredential(context, c =>
                {
                    c.OnCall = null;
                    c.OnCallAsync = OnUpdateCredentialAsync;
                })
                .AddDeleteCredential(context, c =>
                {
                    c.OnCallMethod2 = null;
                    c.OnCallMethod2Async = OnDeleteCredentialAsync;
                });
        }

        private async ValueTask AddNodeAsync(BaseInstanceState state, CancellationToken ct)
        {
            if (m_addNodeAsync != null)
            {
                await m_addNodeAsync(state, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask RemoveNodeAsync(BaseInstanceState state, CancellationToken ct)
        {
            if (m_removeNodeAsync != null)
            {
                await m_removeNodeAsync(state, ct).ConfigureAwait(false);
            }
        }

        private static KeyCredentialConfigurationState? FindCredentialState(
            KeyCredentialConfigurationFolderState folder,
            ISystemContext context,
            string credentialId)
        {
            IList<BaseInstanceState> children = [];
            folder.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is KeyCredentialConfigurationState state &&
                    (string.Equals(state.CredentialId?.Value, credentialId, StringComparison.Ordinal) ||
                        string.Equals(state.BrowseName.Name, credentialId, StringComparison.Ordinal)))
                {
                    return state;
                }
            }
            return null;
        }

        private static NodeId CreateCredentialNodeId(string name, ushort namespaceIndex)
        {
            return new NodeId("KeyCredentialConfiguration/" + name, namespaceIndex);
        }

        /// <summary>
        /// Resolves the namespace that owns dynamically created credential instances.
        /// </summary>
        /// <remarks>
        /// A folder hosted in a server-owned namespace keeps its own namespace. The standard
        /// folder is in namespace 0, so it uses a nonstandard namespace owned by the context's
        /// node manager. Standalone bindings without a node manager use <see cref="NamespaceUri"/>.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown when the server namespace cannot be resolved.
        /// </exception>
        private static ushort GetInstanceNamespaceIndex(
            KeyCredentialConfigurationFolderState folder,
            ISystemContext context)
        {
            ushort folderNamespaceIndex = folder.NodeId.NamespaceIndex;
            if (folderNamespaceIndex != 0)
            {
                return folderNamespaceIndex;
            }

            if (context.NodeIdFactory is AsyncCustomNodeManager nodeManager)
            {
                foreach (ushort namespaceIndex in nodeManager.NamespaceIndexes)
                {
                    if (namespaceIndex != 0)
                    {
                        return namespaceIndex;
                    }
                }
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "The node manager must own a nonstandard namespace for credential nodes.");
            }

            NamespaceTable? namespaces = context.NamespaceUris
                ?? throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "The namespace table required to create credential nodes is not available.");

            int index = namespaces.GetIndex(NamespaceUri);
            if (index < 0)
            {
                index = namespaces.Append(NamespaceUri);
            }
            if (index <= 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "Credential nodes cannot be created in the standard namespace.");
            }
            return (ushort)index;
        }

        private readonly IKeyCredentialStore m_store;
        private readonly KeyCredentialPushOptions m_options;

        /// <summary>
        /// Resolves metadata for requested encryption policies.
        /// </summary>
        private readonly ISecurityPolicyRegistry m_securityPolicies;

        /// <summary>
        /// Provides the application certificates used to receive encrypted credentials.
        /// </summary>
        private ICertificateRegistry? m_certificates;
        private Func<BaseInstanceState, CancellationToken, ValueTask>? m_addNodeAsync;
        private Func<BaseInstanceState, CancellationToken, ValueTask>? m_removeNodeAsync;
    }

    /// <summary>
    /// Records rejected KeyCredential encryption inputs without exposing secret material.
    /// </summary>
    internal static partial class KeyCredentialPushSubjectLog
    {
        /// <summary>
        /// Reports the status code explaining why cryptographic input was rejected.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.KeyCredentialPushSubject, Level = LogLevel.Warning,
            Message = "KeyCredential cryptographic input was rejected: {Status}.")]
        public static partial void CredentialSecretRejected(this ILogger logger, StatusCode status);
    }
}
