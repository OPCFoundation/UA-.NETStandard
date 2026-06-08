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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Binds OPC 10000-12 §8 KeyCredentialConfiguration nodes to an <see cref="IKeyCredentialStore"/>.
    /// </summary>
    public sealed class KeyCredentialPushSubject
    {
        /// <summary>
        /// Namespace URI used for dynamic credential configuration instances.
        /// </summary>
        public const string NamespaceUri = "urn:opcfoundation:netstandard:keycredential-push";

        /// <summary>
        /// Standard NodeId of ServerConfiguration/KeyCredentialConfiguration.
        /// </summary>
        public static readonly NodeId StandardConfigurationFolderNodeId = new(18155u);

        private readonly IKeyCredentialStore m_store;
        private readonly KeyCredentialPushOptions m_options;
        private Func<BaseInstanceState, CancellationToken, ValueTask>? m_addNodeAsync;
        private Func<BaseInstanceState, CancellationToken, ValueTask>? m_removeNodeAsync;

        /// <summary>
        /// Creates a KeyCredential push subject.
        /// </summary>
        public KeyCredentialPushSubject(
            IKeyCredentialStore store,
            KeyCredentialPushOptions? options = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_options = options ?? new KeyCredentialPushOptions();
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
                        Array.Empty<string>());
                    state.CredentialId!.Value = credentialId;
                    await AddNodeAsync(state, ct).ConfigureAwait(false);
                }
            }

            folder.ClearChangeMasks(context, includeChildren: true);
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

            var credential = new KeyCredential(
                credentialSecret.ToArray(),
                DateTime.MaxValue,
                subject,
                Array.Empty<string>());

            await m_store.UpdateAsync(credentialId, credential, ct).ConfigureAwait(false);

            if (method.Parent is KeyCredentialConfigurationState state)
            {
                state.CredentialId ??= state.CreateOrReplaceCredentialId(context, state.CredentialId!);
                state.CredentialId.Value = credentialId;
                state.ServiceStatus ??= state.CreateOrReplaceServiceStatus(context, state.ServiceStatus!);
                state.ServiceStatus.Value = StatusCodes.Good;
                state.ClearChangeMasks(context, includeChildren: true);
            }

            return new KeyCredentialUpdateMethodStateResult { ServiceResult = ServiceResult.Good };
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
            ushort namespaceIndex = GetNamespaceIndex(context);
            QualifiedName browseName = new(name, namespaceIndex);
            KeyCredentialConfigurationState state = folder.AddServiceName_Placeholder(context, browseName);
            state.NodeId = CreateCredentialNodeId(name, namespaceIndex);
            state.SymbolicName = name;
            state.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            state.TypeDefinitionId = ObjectTypeIds.KeyCredentialConfigurationType;
            state.DisplayName = LocalizedText.From(name);
            state.ResourceUri ??= state.CreateOrReplaceResourceUri(context, state.ResourceUri!);
            state.ResourceUri.Value = resourceUri ?? string.Empty;
            state.ProfileUri ??= state.CreateOrReplaceProfileUri(context, state.ProfileUri!);
            state.ProfileUri.Value = string.IsNullOrWhiteSpace(profileUri)
                ? KeyCredentialBridgeOptions.DefaultProfileUri
                : profileUri;
            state.EndpointUrls ??= state.CreateOrReplaceEndpointUrls(context, state.EndpointUrls!);
            state.EndpointUrls.Value = [.. endpointUrls];
            state.CredentialId ??= state.CreateOrReplaceCredentialId(context, state.CredentialId!);
            state.ServiceStatus ??= state.CreateOrReplaceServiceStatus(context, state.ServiceStatus!);
            state.ServiceStatus.Value = StatusCodes.Good;
            WireCredentialState(state, context);
            return state;
        }

        private void WireCredentialState(KeyCredentialConfigurationState state, ISystemContext context)
        {
            state
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

        private static ushort GetNamespaceIndex(ISystemContext context)
        {
            NamespaceTable? namespaces = context.NamespaceUris;
            if (namespaces == null)
            {
                return 1;
            }

            int index = namespaces.GetIndex(NamespaceUri);
            if (index < 0)
            {
                index = namespaces.Append(NamespaceUri);
            }
            return (ushort)index;
        }

        private static NodeId CreateCredentialNodeId(string name, ushort namespaceIndex)
        {
            return new NodeId("KeyCredentialConfiguration/" + name, namespaceIndex);
        }
    }
}
