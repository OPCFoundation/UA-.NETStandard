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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server
{
    public sealed partial class SchemaRegistryNodeManager
    {
        private async ValueTask<BeginSchemaUploadMethodStateResult> BeginUploadAsync(
            ISystemContext caller,
            SchemaRegistrationDataType registration,
            CancellationToken cancellationToken)
        {
            ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Write);
            if (ServiceResult.IsBad(allowed))
            {
                return new BeginSchemaUploadMethodStateResult { ServiceResult = allowed };
            }
            m_store.RegistrationReference(registration);
            NodeId session = caller is ISessionSystemContext { SessionId: { IsNull: false } id }
                ? id : throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            var file = new FileState(m_root)
            {
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
            };
            file.Create(SystemContext, Instance("/uploads/" + Guid.NewGuid().ToString("N")),
                new QualifiedName("Upload", NamespaceIndexes[0]), new LocalizedText("Schema upload"),
                assignNodeIds: false);
            var upload = new Upload(session, RegistryAccessPolicy.AuthorizationView(caller),
                (SchemaRegistrationDataType)registration.Clone(), file);
            lock (m_uploadGate)
            {
                int owned = 0;
                foreach (Upload other in m_uploads.Values)
                {
                    owned += other.Session == session ? 1 : 0;
                }
                if (owned >= 8 || m_uploads.Count >= 32)
                {
                    upload.Dispose();
                    return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadTooManyOperations };
                }
                m_uploads.Add(file.NodeId, upload);
            }
            XRegistryProjectionEngine.SetValue(file.Writable, true);
            XRegistryProjectionEngine.SetValue(file.UserWritable, true);
            XRegistryProjectionEngine.SetValue(file.OpenCount, (ushort)1);
            XRegistryProjectionEngine.SetValue(file.Size, 0ul);
            file.Write!.OnCall = (context, _, _, handle, bytes) =>
            {
                ServiceResult access = CheckUpload(context, upload, handle);
                if (ServiceResult.IsBad(access))
                {
                    return access;
                }
                lock (m_uploadGate)
                {
                    if (!m_uploads.ContainsKey(file.NodeId))
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    if (bytes.IsNull || upload.Buffer.Position > kMaxUploadBytes - bytes.Length)
                    {
                        return StatusCodes.BadEncodingLimitsExceeded;
                    }
                    long old = upload.Buffer.Length;
                    long growth = Math.Max(old, upload.Buffer.Position + bytes.Length) - old;
                    if (m_uploadBytes > kMaxRetainedUploadBytes - growth)
                    {
                        return StatusCodes.BadEncodingLimitsExceeded;
                    }
                    byte[] next = bytes.ToArray();
                    upload.Buffer.Write(next, 0, next.Length);
                    m_uploadBytes += upload.Buffer.Length - old;
                    XRegistryProjectionEngine.SetValue(file.Size, (ulong)upload.Buffer.Length);
                    return ServiceResult.Good;
                }
            };
            file.GetPosition!.OnCall = (ISystemContext context, MethodState _, NodeId _, uint handle, ref ulong position) =>
            {
                ServiceResult access = CheckUpload(context, upload, handle);
                if (ServiceResult.IsGood(access))
                {
                    lock (m_uploadGate)
                    {
                        if (!m_uploads.ContainsKey(file.NodeId))
                        {
                            return StatusCodes.BadInvalidState;
                        }
                        position = (ulong)upload.Buffer.Position;
                    }
                }
                return access;
            };
            file.SetPosition!.OnCall = (context, _, _, handle, position) =>
            {
                ServiceResult access = CheckUpload(context, upload, handle);
                if (ServiceResult.IsBad(access))
                {
                    return access;
                }
                lock (m_uploadGate)
                {
                    if (!m_uploads.ContainsKey(file.NodeId))
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    if (position > (ulong)upload.Buffer.Length)
                    {
                        return StatusCodes.BadOutOfRange;
                    }
                    upload.Buffer.Position = (long)position;
                }
                return ServiceResult.Good;
            };
            file.Close!.OnCallAsync = async (context, _, _, handle, ct) =>
            {
                ServiceResult access = CheckUpload(context, upload, handle);
                if (ServiceResult.IsBad(access))
                {
                    return new CloseMethodStateResult { ServiceResult = access };
                }
                ByteString bytes;
                lock (m_uploadGate)
                {
                    if (!m_uploads.Remove(file.NodeId))
                    {
                        return new CloseMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                    }
                    bytes = ByteString.From(upload.Buffer.ToArray());
                    m_uploadBytes -= upload.Buffer.Length;
                    upload.Dispose();
                }
                try
                {
                    TypedSchemaReadResultDataType committed = await m_store.RegisterRawAsync(upload.Registration, bytes, ct)
                        .ConfigureAwait(false);
                    return new CloseMethodStateResult
                    {
                        ServiceResult = committed.Issues.Count == 0
                            ? new ServiceResult(committed.StatusCode)
                            : new ServiceResult(committed.StatusCode, new LocalizedText(committed.Issues[0].Detail))
                    };
                }
                finally
                {
                    await DeleteNodeAsync(SystemContext, file.NodeId, CancellationToken.None).ConfigureAwait(false);
                }
            };
            SystemContext.AssignInstanceChildNodeIds(file);
            XRegistryProjectionEngine.LinkMethodArguments(file, SystemContext);
            try
            {
                await AddPredefinedNodeAsync(SystemContext, file, cancellationToken).ConfigureAwait(false);
                bool active;
                lock (m_uploadGate)
                {
                    active = m_uploads.ContainsKey(file.NodeId);
                }
                if (!active)
                {
                    await DeleteNodeAsync(SystemContext, file.NodeId, CancellationToken.None).ConfigureAwait(false);
                    return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadSessionClosed };
                }
            }
            catch
            {
                lock (m_uploadGate)
                {
                    m_uploads.Remove(file.NodeId);
                    upload.Dispose();
                }
                await DeleteNodeAsync(SystemContext, file.NodeId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return new BeginSchemaUploadMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                UploadFile = file.NodeId,
                FileHandle = 1
            };
        }

        private ServiceResult CheckUpload(ISystemContext context, Upload upload, uint handle)
        {
            ServiceResult allowed = m_authorize(context, RegistryAccessKind.Write);
            if (ServiceResult.IsBad(allowed))
            {
                return allowed;
            }
            if (handle != 1 || context is not ISessionSystemContext { SessionId: { } session } ||
                session != upload.Session ||
                RegistryAccessPolicy.AuthorizationView(context) != upload.AuthorizationView)
            {
                return StatusCodes.BadUserAccessDenied;
            }
            lock (m_uploadGate)
            {
                return m_uploads.ContainsKey(upload.File.NodeId) ? ServiceResult.Good : StatusCodes.BadInvalidState;
            }
        }

        private async ValueTask ReleaseUploadsAsync(NodeId? session, CancellationToken cancellationToken)
        {
            var files = new List<NodeId>();
            lock (m_uploadGate)
            {
                foreach (Upload upload in m_uploads.Values)
                {
                    if (session is null || session.Value == upload.Session)
                    {
                        files.Add(upload.File.NodeId);
                        m_uploadBytes -= upload.Buffer.Length;
                        upload.Dispose();
                    }
                }
                foreach (NodeId file in files)
                {
                    m_uploads.Remove(file);
                }
            }
            foreach (NodeId file in files)
            {
                await DeleteNodeAsync(SystemContext, file, cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class Upload(
            NodeId session, string view, SchemaRegistrationDataType registration, FileState file) : IDisposable
        {
            public NodeId Session { get; } = session;

            public string AuthorizationView { get; } = view;

            public SchemaRegistrationDataType Registration { get; } = registration;

            public FileState File { get; } = file;

            public MemoryStream Buffer { get; } = new();

            public void Dispose() => Buffer.Dispose();
        }

        private const long kMaxUploadBytes = 16 * 1024 * 1024;
        private const long kMaxRetainedUploadBytes = 64 * 1024 * 1024;
        private readonly Lock m_uploadGate = new();
        private readonly Dictionary<NodeId, Upload> m_uploads = [];
        private long m_uploadBytes;
    }
}
