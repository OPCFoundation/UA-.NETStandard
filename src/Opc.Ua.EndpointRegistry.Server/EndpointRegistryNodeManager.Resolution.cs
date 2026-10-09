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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Server
{
    public sealed partial class EndpointRegistryNodeManager
    {
        /// <summary>
        /// Binds typed Message resolution on a registry root.
        /// </summary>
        private void BindResolution(
            ISystemContext context,
            EndpointRegistryState root,
            RegistryNativeHost host,
            EndpointRegistryCatalogOptions catalogOptions)
        {
            EndpointRegistryResolutionOptions? options = m_options.Resolution;
            if (options is null)
            {
                return;
            }
            RegistryEntityReferenceDataType localOrigin = LocalOrigin(root, catalogOptions, options);
            var resolver = new EndpointRegistryMessageResolver();
            root.AddResolveMessage(context);
            root.ResolveMessage!.OnCallAsync = async (caller, _, _, request, ct) =>
            {
                ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Read);
                if (ServiceResult.IsBad(allowed))
                {
                    return new ResolveMessageMethodStateResult { ServiceResult = allowed };
                }
                NativeMessageResolutionResultDataType result = await resolver.ResolveAsync(
                    request,
                    new EndpointRegistryMessageResolutionContext
                    {
                        LocalOrigin = localOrigin,
                        Mapper = host.Mapper,
                        Provider = new LocalResolutionProvider(host, host.Current, localOrigin, options.Provider)
                    },
                    ct).ConfigureAwait(false);
                return new ResolveMessageMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Result = result
                };
            };
            XRegistryProjectionEngine.LinkMethodArguments(root.ResolveMessage, context);
        }

        private RegistryEntityReferenceDataType LocalOrigin(
            EndpointRegistryState root,
            EndpointRegistryCatalogOptions catalogOptions,
            EndpointRegistryResolutionOptions options)
        {
            return new RegistryEntityReferenceDataType
            {
                OriginUri = options.LocalOriginUri ?? string.Empty,
                ApplicationUri = options.LocalOriginUri is null ? m_applicationUri : string.Empty,
                RegistryNode = options.LocalOriginUri is null
                    ? new ExpandedNodeId(root.NumericId, 0, Namespaces.EndpointRegistry)
                    : ExpandedNodeId.Null,
                Xid = "/",
                Role = "Registry",
                Locator = string.Empty,
                HasNativeTarget = true,
                NativeTarget = new ExpandedNodeId(root.NodeId, Namespaces.EndpointRegistry, 0)
            };
        }

        private sealed class LocalResolutionProvider : IEndpointRegistryResolutionProvider
        {
            public LocalResolutionProvider(
                RegistryNativeHost host,
                RegistryCommittedState state,
                RegistryEntityReferenceDataType localOrigin,
                IEndpointRegistryResolutionProvider? remote)
            {
                m_host = host;
                m_state = state;
                m_localOrigin = localOrigin;
                m_remote = remote;
            }

            public async ValueTask<EndpointRegistryMessageObservation?> ReadMessageAsync(
                RegistryEntityReferenceDataType reference,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!EndpointRegistryOriginKey.SameOrigin(reference, m_localOrigin))
                {
                    return m_remote is null
                        ? null
                        : await m_remote.ReadMessageAsync(reference, cancellationToken).ConfigureAwait(false);
                }
                try
                {
                    if (string.IsNullOrEmpty(reference.Xid))
                    {
                        return null;
                    }
                    string logicalXid = reference.Xid;
                    int suffix = logicalXid.IndexOf("/versions/", StringComparison.Ordinal);
                    if (suffix >= 0)
                    {
                        logicalXid = logicalXid.Substring(0, suffix);
                    }
                    (RegistryRecordDataType record, uint epoch) = m_host.ReadRecord(m_state, logicalXid);
                    if (record is not MessageDefinitionDataType message)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                            "The selected entity is not a Message Definition.");
                    }
                    if (m_host.Mapper.Restore(record) is not RegistryObjectValueDataType metadata)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The committed Message cannot be represented as metadata.");
                    }
                    var source = (RegistryEntityReferenceDataType)reference.Clone();
                    string rootPath = m_localOrigin.NativeTarget.InnerNodeId.TryGetValue(out uint rootId) &&
                        rootId == Objects.MediaEndpointRegistry
                        ? BrowseNames.MediaEndpointRegistry : BrowseNames.EndpointRegistry;
                    var target = new ExpandedNodeId(rootPath + logicalXid, 0, Namespaces.EndpointRegistry);
                    source.LocalNode = target;
                    source.HasNativeTarget = true;
                    source.NativeTarget = target;
                    return new EndpointRegistryMessageObservation
                    {
                        Source = source,
                        Metadata = metadata,
                        Epoch = epoch,
                        VersionId = string.IsNullOrEmpty(message.VersionId) ? "1" : message.VersionId
                    };
                }
                catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadNotFound)
                {
                    return null;
                }
            }

            public async ValueTask<SchemaDocumentDataType?> ResolveSchemaAsync(
                MessageDefinitionDataType definition,
                RegistryEntityReferenceDataType origin,
                CancellationToken cancellationToken)
            {
                return m_remote is null
                    ? null
                    : await m_remote.ResolveSchemaAsync(definition, origin, cancellationToken).ConfigureAwait(false);
            }

            private readonly RegistryNativeHost m_host;
            private readonly RegistryCommittedState m_state;
            private readonly RegistryEntityReferenceDataType m_localOrigin;
            private readonly IEndpointRegistryResolutionProvider? m_remote;
        }
    }
}
