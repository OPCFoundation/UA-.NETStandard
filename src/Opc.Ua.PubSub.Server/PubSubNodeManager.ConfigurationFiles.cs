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
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    public sealed partial class PubSubNodeManager
    {
        /// <summary>
        /// Binds both standard and materialized FileType instances using the generated declarations.
        /// </summary>
        private void BindConfigurationFile(PubSubConfigurationState file)
        {
            file.AddMimeType(SystemContext);
            file.MimeType!.Value = "application/opcua+uabinary";
            file.Writable!.Value = m_options.ExposeConfigurationMethods;
            file.UserWritable!.Value = m_options.ExposeConfigurationMethods;
            file.UserWritable.OnSimpleReadValue = (context, node, ref value) =>
            {
                value = Variant.From(m_options.ExposeConfigurationMethods &&
                    TryGetConfigurationFileSession(context, out _));
                return ServiceResult.Good;
            };
            file.Size!.OnSimpleReadValue = (context, node, ref value) =>
            {
                value = Variant.From((ulong)EncodeBinaryConfiguration(m_application.GetConfiguration()).Length);
                return ServiceResult.Good;
            };
            file.OpenCount!.OnSimpleReadValue = (context, node, ref value) =>
            {
                lock (m_addressSpaceGate)
                {
                    value = Variant.From((ushort)m_fileHandles.Count);
                }
                return ServiceResult.Good;
            };
            BindStandardMethod(file.Open, OnOpenPubSubConfigurationFile);
            BindStandardMethod(file.Read, OnReadPubSubConfigurationFile);
            BindStandardMethod(file.Write, OnWritePubSubConfigurationFile);
            BindStandardMethod(file.Close, OnClosePubSubConfigurationFile);
            BindStandardMethod(file.GetPosition, OnGetPubSubConfigurationFilePosition);
            BindStandardMethod(file.SetPosition, OnSetPubSubConfigurationFilePosition);
            BindAsyncConfigurationMethod(file.Open, OnOpenPubSubConfigurationFileAsync);
            BindAsyncConfigurationMethod(file.ReserveIds, OnReservePubSubConfigurationIdsAsync);
            BindAsyncConfigurationMethod(file.CloseAndUpdate, OnCloseAndUpdatePubSubConfigurationFileAsync);
        }

        /// <summary>
        /// Gives every generated file child a unique application-namespace instance identifier.
        /// </summary>
        private void AssignConfigurationFileInstanceIds(BaseInstanceState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                child.NodeId = new NodeId($"{parent.NodeId.IdentifierAsString}:{child.BrowseName.Name}",
                    parent.NodeId.NamespaceIndex);
                AssignConfigurationFileInstanceIds(child);
            }
        }

        /// <summary>
        /// Allocates configuration-file handles without requiring synchronous provider completion.
        /// </summary>
        private async ValueTask<ServiceResult> OnOpenPubSubConfigurationFileAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken cancellationToken)
        {
            if (inputs.Count != 1 || !inputs[0].TryGetValue(out byte mode))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            if (!TryGetConfigurationFileSession(context, out _))
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (mode is not (1 or 3 or 6))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            uint handle = await m_idAllocator.AllocateFileHandleAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return OpenConfigurationFile(context, mode, handle, outputs);
        }

        /// <summary>
        /// Creates a bounded file owned by the calling Session after rechecking closure.
        /// </summary>
        private ServiceResult OpenConfigurationFile(
            ISystemContext context, byte mode, uint handle, List<Variant> outputs)
        {
            if (!TryGetConfigurationFileSession(context, out NodeId owner))
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            if (mode is not (1 or 3 or 6))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            if (IsWriteMode(mode) && !m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            byte[] content = mode == 6 ? [] : EncodeBinaryConfiguration(m_application.GetConfiguration());
            if (content.Length > m_options.MaxConfigurationFileSize)
            {
                return new ServiceResult(StatusCodes.BadEncodingLimitsExceeded);
            }
            lock (m_addressSpaceGate)
            {
                if (m_configurationFilesDisposed)
                {
                    return new ServiceResult(StatusCodes.BadInvalidState);
                }
                if (m_closedConfigurationFileSessions.Contains(owner))
                {
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                }
                if (IsWriteMode(mode) && m_fileHandles.Count != 0)
                {
                    return new ServiceResult(StatusCodes.BadNotWritable);
                }
                if (!IsWriteMode(mode) && m_fileHandles.Values.Any(file => file.Writable))
                {
                    return new ServiceResult(StatusCodes.BadNotReadable);
                }
                if (m_fileHandles.Count >= m_options.MaxConfigurationFileHandles ||
                    !m_fileHandles.TryAdd(handle, new PubSubConfigurationFileHandle(owner, mode, content)))
                {
                    return new ServiceResult(StatusCodes.BadResourceUnavailable);
                }
            }
            outputs.Add(Variant.From(handle));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks actual Session ownership while the caller holds the address-space gate.
        /// </summary>
        private ServiceResult GetConfigurationFile(
            ISystemContext context, uint handle, out PubSubConfigurationFileHandle? file)
        {
            if (m_configurationFilesDisposed)
            {
                file = null;
                return new ServiceResult(StatusCodes.BadInvalidState);
            }
            if (!m_fileHandles.TryGetValue(handle, out file))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            if (!TryGetConfigurationFileSession(context, out NodeId owner) || file.Owner != owner)
            {
                file = null;
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Does not derive file ownership from a user name or application URI.
        /// </summary>
        private static bool TryGetConfigurationFileSession(ISystemContext context, out NodeId owner)
        {
            if (context is ISessionSystemContext session && session.SessionId is NodeId id &&
                !id.IsNull && session.UserIdentity is { } identity &&
                identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_ConfigureAdmin))
            {
                owner = id;
                return true;
            }
            owner = NodeId.Null;
            return false;
        }

        /// <summary>
        /// Returns the current position of a Session-owned file.
        /// </summary>
        private ServiceResult OnGetPubSubConfigurationFilePosition(
            ISystemContext context, MethodState method, ArrayOf<Variant> inputs, List<Variant> outputs)
        {
            if (inputs.Count != 1 || !inputs[0].TryGetValue(out uint handle))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            lock (m_addressSpaceGate)
            {
                ServiceResult access = GetConfigurationFile(context, handle, out PubSubConfigurationFileHandle? file);
                if (ServiceResult.IsBad(access))
                {
                    return access;
                }
                outputs.Add(Variant.From((ulong)file!.Position));
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Sets a file position, clamping offsets past the end as required by FileType.
        /// </summary>
        private ServiceResult OnSetPubSubConfigurationFilePosition(
            ISystemContext context, MethodState method, ArrayOf<Variant> inputs, List<Variant> outputs)
        {
            if (inputs.Count != 2 || !inputs[0].TryGetValue(out uint handle) ||
                !inputs[1].TryGetValue(out ulong position))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            lock (m_addressSpaceGate)
            {
                ServiceResult access = GetConfigurationFile(context, handle, out PubSubConfigurationFileHandle? file);
                if (ServiceResult.IsBad(access))
                {
                    return access;
                }
                file!.Position = (int)Math.Min(position, int.MaxValue);
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Exports the Part 5 binary-file ExtensionObject rather than vendor XML.
        /// </summary>
        private byte[] EncodeBinaryConfiguration(PubSubConfigurationDataType configuration)
        {
            PubSubConfiguration2DataType body = configuration is PubSubConfiguration2DataType extended
                ? (PubSubConfiguration2DataType)extended.Clone()
                : new PubSubConfiguration2DataType
                {
                    Enabled = configuration.Enabled,
                    Connections = configuration.Connections,
                    PublishedDataSets = configuration.PublishedDataSets
                };
            var file = new UABinaryFileDataType
            {
                Namespaces = [.. Server.NamespaceUris.ToArray().Skip(1)],
                Body = Variant.From(new ExtensionObject(body))
            };
            using var encoder = new BinaryEncoder(CreateConfigurationFileContext());
            encoder.WriteExtensionObject(null, new ExtensionObject(file));
            return encoder.CloseAndReturnBuffer() ??
                throw new ServiceResultException(StatusCodes.BadEncodingError, "No configuration file was encoded.");
        }

        /// <summary>
        /// Validates the binary envelope then remaps file namespace indices to known server namespaces.
        /// </summary>
        private PubSubConfiguration2DataType DecodeBinaryConfiguration(byte[] bytes)
        {
            ServiceMessageContext context = CreateConfigurationFileContext();
            using var probe = new BinaryDecoder(bytes, context);
            ExtensionObject encoded = probe.ReadExtensionObject(null);
            if (probe.Position != bytes.Length || !encoded.TryGetValue(out UABinaryFileDataType? file) || file is null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The file must contain one UABinaryFileDataType ExtensionObject.");
            }
            var namespaces = new NamespaceTable();
            foreach (string uri in file.Namespaces)
            {
                if (string.IsNullOrEmpty(uri) || namespaces.GetIndex(uri) >= 0 ||
                    Server.NamespaceUris.GetIndex(uri) < 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                        "The file contains a duplicate or unknown namespace URI.");
                }
                namespaces.Append(uri);
            }
            using var decoder = new BinaryDecoder(bytes, context);
            decoder.SetMappingTables(namespaces, null);
            encoded = decoder.ReadExtensionObject(null);
            if (!encoded.TryGetValue(out file) || file is null ||
                !file.Body.TryGetValue(out ExtensionObject body) ||
                !body.TryGetValue(out PubSubConfiguration2DataType? configuration) || configuration is null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The binary file Body must be PubSubConfiguration2DataType.");
            }
            return configuration;
        }

        /// <summary>
        /// Applies the same finite quotas to configuration-file decoding and encoding.
        /// </summary>
        private ServiceMessageContext CreateConfigurationFileContext()
        {
            return new ServiceMessageContext(m_telemetry, Server.Factory)
            {
                NamespaceUris = Server.NamespaceUris,
                ServerUris = Server.ServerUris,
                MaxMessageSize = m_options.MaxConfigurationFileSize,
                MaxByteStringLength = m_options.MaxConfigurationFileSize,
                MaxStringLength = m_options.MaxConfigurationFileSize,
                MaxArrayLength = m_options.MaxConfigurationFileSize
            };
        }

        /// <summary>Sessions that must not publish new file handles after their close event.</summary>
        private readonly HashSet<NodeId> m_closedConfigurationFileSessions = [];

        /// <summary>Prevents delayed allocations from recreating files after manager disposal.</summary>
        private bool m_configurationFilesDisposed;
    }
}
