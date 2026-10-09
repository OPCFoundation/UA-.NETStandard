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
        private ValueTask<BeginSchemaUploadMethodStateResult> BeginUploadAsync(
            ISystemContext caller, SchemaRegistrationDataType registration, CancellationToken cancellationToken,
            FileState? target = null, byte mode = 6)
        {
            return m_store.PrepareFileOpenAsync(() =>
                BeginUploadCoreAsync(caller, registration, cancellationToken, target, mode), cancellationToken);
        }

        private async ValueTask<BeginSchemaUploadMethodStateResult> BeginUploadCoreAsync(
            ISystemContext caller,
            SchemaRegistrationDataType registration,
            CancellationToken cancellationToken,
            FileState? target = null,
            byte mode = 6)
        {
            ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Write);
            if (ServiceResult.IsBad(allowed))
            {
                return new BeginSchemaUploadMethodStateResult { ServiceResult = allowed };
            }
            if (target is not null)
            {
                target = FindSchemaFile(target.NodeId) ?? throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            if (target is not null && ((mode & 0xF0) != 0 || (mode & (byte)OpenFileMode.Write) == 0 ||
                (mode & (byte)(OpenFileMode.EraseExisting | OpenFileMode.Append)) ==
                    (byte)(OpenFileMode.EraseExisting | OpenFileMode.Append)))
            {
                return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadInvalidArgument };
            }
            SchemaReferenceDataType exactReference = m_store.RegistrationReference(registration);
            TypedSchemaReadResultDataType existingVersion = m_store.Read(exactReference);
            if (StatusCode.IsGood(existingVersion.StatusCode) && !IsVisible(caller, existingVersion.Document.Reference))
            {
                return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadUserAccessDenied };
            }
            NodeId session = caller is ISessionSystemContext { SessionId: { IsNull: false } id }
                ? id : throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            var file = target ?? new FileState(m_root)
            {
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
            };
            if (target is null)
            {
                file.Create(SystemContext, Instance("/uploads/" + Guid.NewGuid().ToString("N")),
                    new QualifiedName("Upload", NamespaceIndexes[0]), new LocalizedText("Schema upload"),
                    assignNodeIds: false);
            }
            Upload? pending = new Upload(session, RegistryAccessPolicy.AuthorizationView(caller),
                (SchemaRegistrationDataType)registration.Clone(), file, target is null);
            try
            {
                Upload upload = pending;
                if (target is not null && (mode & (byte)OpenFileMode.EraseExisting) == 0)
                {
                    SchemaReferenceDataType reference = m_store.RegistrationReference(registration);
                    TypedSchemaReadResultDataType existing = m_store.Read(reference);
                    if (StatusCode.IsGood(existing.StatusCode))
                    {
                        ByteString bytes = m_store.DocumentBytes(reference);
                        if (bytes.Length > kMaxUploadBytes)
                        {
                            upload.Dispose();
                            return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadEncodingLimitsExceeded };
                        }
                        byte[] initial = bytes.ToArray();
                        upload.Buffer.Write(initial, 0, initial.Length);
                        upload.Buffer.Position = (mode & (byte)OpenFileMode.Append) != 0 ? upload.Buffer.Length : 0;
                    }
                }
                var oldWrite = file.Write!.OnCall;
                var oldGet = file.GetPosition!.OnCall;
                var oldSet = file.SetPosition!.OnCall;
                var oldClose = file.Close!.OnCall;
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
                    if (m_uploadBytes > kMaxRetainedUploadBytes - upload.Buffer.Length)
                    {
                        upload.Dispose();
                        return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadResourceUnavailable };
                    }
                    if (!m_uploads.TryAdd(file.NodeId, upload))
                    {
                        upload.Dispose();
                        return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
                    }
                    m_uploadBytes += upload.Buffer.Length;
                    pending = null;
                }
                XRegistryProjectionEngine.SetValue(file.Writable, true);
                XRegistryProjectionEngine.SetValue(file.UserWritable, true);
                XRegistryProjectionEngine.SetValue(file.OpenCount, (ushort)1);
                XRegistryProjectionEngine.SetValue(file.Size, (ulong)upload.Buffer.Length);
                file.Write!.OnCall = (context, _, _, handle, bytes) =>
                {
                    if (handle != upload.Handle && oldWrite is not null)
                    {
                        return oldWrite(context, file.Write, file.NodeId, handle, bytes);
                    }
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
                    if (handle != upload.Handle && oldGet is not null)
                    {
                        return oldGet(context, file.GetPosition, file.NodeId, handle, ref position);
                    }
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
                    if (handle != upload.Handle && oldSet is not null)
                    {
                        return oldSet(context, file.SetPosition, file.NodeId, handle, position);
                    }
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
                    if (handle != upload.Handle && oldClose is not null)
                    {
                        return new CloseMethodStateResult { ServiceResult = oldClose(context, file.Close, file.NodeId, handle) };
                    }
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
                        if (target is null)
                        {
                            await DeleteNodeAsync(SystemContext, file.NodeId, CancellationToken.None).ConfigureAwait(false);
                        }
                        else
                        {
                            await ProjectSchemasAsync(m_store.Entries, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                };
                AssignSchemaNodeIds(file);
                XRegistryProjectionEngine.LinkMethodArguments(file, SystemContext);
                try
                {
                    if (target is null)
                    {
                        await AddPredefinedNodeAsync(SystemContext, file, cancellationToken).ConfigureAwait(false);
                    }
                    bool active;
                    lock (m_uploadGate)
                    {
                        active = m_uploads.ContainsKey(file.NodeId);
                    }
                    if (!active)
                    {
                        if (target is null)
                        {
                            await DeleteNodeAsync(SystemContext, file.NodeId, CancellationToken.None).ConfigureAwait(false);
                        }
                        return new BeginSchemaUploadMethodStateResult { ServiceResult = StatusCodes.BadSessionClosed };
                    }
                }
                catch
                {
                    lock (m_uploadGate)
                    {
                        if (m_uploads.Remove(file.NodeId))
                        {
                            m_uploadBytes -= upload.Buffer.Length;
                        }
                        upload.Dispose();
                    }
                    if (target is null)
                    {
                        await DeleteNodeAsync(SystemContext, file.NodeId, CancellationToken.None).ConfigureAwait(false);
                    }
                    throw;
                }
                return new BeginSchemaUploadMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    UploadFile = file.NodeId,
                    FileHandle = upload.Handle
                };
            }
            finally
            {
                pending?.Dispose();
            }
        }

        private ServiceResult CheckUpload(ISystemContext context, Upload upload, uint handle)
        {
            ServiceResult allowed = m_authorize(context, RegistryAccessKind.Write);
            if (ServiceResult.IsBad(allowed))
            {
                return allowed;
            }
            if (handle != upload.Handle || context is not ISessionSystemContext { SessionId: { } session } ||
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

        private void CheckPublication()
        {
            lock (m_uploadGate)
            {
                foreach (Upload upload in m_uploads.Values)
                {
                    if (!upload.Temporary)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "Close the active inherited FileType writer before another catalog mutation.");
                    }
                }
            }
        }

        private async ValueTask ReleaseUploadsAsync(NodeId? session, CancellationToken cancellationToken)
        {
            var files = new List<NodeId>();
            var temporaryFiles = new List<NodeId>();
            bool restoreTargets = false;
            lock (m_uploadGate)
            {
                foreach (Upload upload in m_uploads.Values)
                {
                    if (session is null || session.Value == upload.Session)
                    {
                        files.Add(upload.File.NodeId);
                        if (upload.Temporary)
                        {
                            temporaryFiles.Add(upload.File.NodeId);
                        }
                        restoreTargets |= !upload.Temporary;
                        m_uploadBytes -= upload.Buffer.Length;
                        upload.Dispose();
                    }
                }
                foreach (NodeId file in files)
                {
                    m_uploads.Remove(file);
                }
            }
            foreach (NodeId file in temporaryFiles)
            {
                await DeleteNodeAsync(SystemContext, file, cancellationToken).ConfigureAwait(false);
            }
            if (restoreTargets && session is not null)
            {
                await ProjectSchemasAsync(m_store.Entries, cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class Upload(
            NodeId session, string view, SchemaRegistrationDataType registration, FileState file,
            bool temporary) : IDisposable
        {
            public NodeId Session { get; } = session;

            public string AuthorizationView { get; } = view;

            public SchemaRegistrationDataType Registration { get; } = registration;

            public FileState File { get; } = file;

            public bool Temporary { get; } = temporary;

            public uint Handle { get; } = temporary ? 1u : uint.MaxValue;

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
