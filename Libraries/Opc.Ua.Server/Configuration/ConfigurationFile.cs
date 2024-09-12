/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The implementation of a server trustlist.
    /// </summary>
    public class ConfigurationFile
    {

        #region Private Fields
        private readonly object m_lock = new object();
        private SecureAccess m_readAccess;
        private SecureAccess m_writeAccess;
        private NodeId m_sessionId;
        private uint m_fileHandle;
        private ConfigurationFileState m_node;
        private Stream m_strm;
        private bool m_readMode;
        #endregion

        const int kDefaultTrustListCapacity = 0x10000;

        #region Constructors
        /// <summary>
        /// Initialize the trustlist with default values.
        /// </summary>
        public ConfigurationFile(
            ConfigurationFileState node,
            SecureAccess readAccess,
            SecureAccess writeAccess)
        {
            m_node = node;
            m_readAccess = readAccess;
            m_writeAccess = writeAccess;

            node.Open.OnCall = new OpenMethodStateMethodCallHandler(Open);
            node.Read.OnCall = new ReadMethodStateMethodCallHandler(Read);
            node.Write.OnCall = new WriteMethodStateMethodCallHandler(Write);
            node.Close.OnCall = new CloseMethodStateMethodCallHandler(Close);
            node.CloseAndUpdate.OnCall = new ConfigurationFileCloseAndUpdateMethodStateMethodCallHandler(CloseAndUpdate);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Delegate to validate the access to the trust list.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="trustedStorePath">the path to identify the trustList</param>
        public delegate void SecureAccess(ISystemContext context, string trustedStorePath);
        #endregion

        #region Private Methods
        private ServiceResult Open(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            HasSecureReadAccess(context);

            if ((OpenFileMode)mode == OpenFileMode.Read)
            {
                HasSecureReadAccess(context);
            }
            else if ((OpenFileMode)mode == (OpenFileMode.Write | OpenFileMode.EraseExisting))
            {
                HasSecureWriteAccess(context);
            }
            else
            {
                return StatusCodes.BadNotWritable;
            }

            lock (m_lock)
            {
                if (m_sessionId != null)
                {
                    // to avoid deadlocks, last open always wins
                    m_sessionId = null;
                    m_strm = null;
                    m_node.OpenCount.Value = 0;
                }

                m_readMode = (OpenFileMode)mode == OpenFileMode.Read;
                m_sessionId = context.SessionId;
                fileHandle = ++m_fileHandle;

                if (m_readMode)
                {
                    var configuration = new ApplicationConfigurationDataType();
                    m_strm = Encode(context, configuration);
                }
                else
                {
                    m_strm = new MemoryStream(kDefaultTrustListCapacity);
                }

                m_node.OpenCount.Value = 1;
            }

            return ServiceResult.Good;
        }

        private ServiceResult Read(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref byte[] data)
        {
            HasSecureReadAccess(context);

            lock (m_lock)
            {
                if (m_sessionId != context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (m_fileHandle != fileHandle)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                data = new byte[length];

                int bytesRead = m_strm.Read(data, 0, length);

                if (bytesRead < 0)
                {
                    return StatusCodes.BadUnexpectedError;
                }

                if (bytesRead < length)
                {
                    byte[] bytes = new byte[bytesRead];
                    Array.Copy(data, bytes, bytesRead);
                    data = bytes;
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult Write(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            byte[] data)
        {
            HasSecureWriteAccess(context);

            lock (m_lock)
            {
                if (m_sessionId != context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (m_fileHandle != fileHandle)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                m_strm.Write(data, 0, data.Length);

            }

            return ServiceResult.Good;
        }

        private ServiceResult Close(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            HasSecureReadAccess(context);

            lock (m_lock)
            {
                if (m_sessionId != context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (m_fileHandle != fileHandle)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                m_sessionId = null;
                m_strm = null;
                m_node.OpenCount.Value = 0;
            }

            return ServiceResult.Good;
        }

        private ServiceResult CloseAndUpdate(
            ISystemContext _context,
            MethodState _method,
            NodeId _objectId,
            uint fileHandle,
            uint versionToUpdate,
            ConfigurationUpdateTargetType[] targets,
            double revertAfterTime,
            double restartDelayTime,
            bool allOrNothing,
            bool validateOnly,
            ref bool configurationUpdated,
            ref StatusCode[] validationResults,
            ref Uuid updateId)
        {
            object[] inputParameters = new object[] { fileHandle };
            // m_node.ReportTrustListUpdateRequestedAuditEvent(context, objectId, "Method/CloseAndUpdate", method.NodeId, inputParameters);

            HasSecureWriteAccess(_context);

            ServiceResult result = StatusCodes.Good;

            lock (m_lock)
            {
                if (m_sessionId != _context.SessionId)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (m_fileHandle != fileHandle)
                {
                    return StatusCodes.BadInvalidArgument;
                }

                try
                {
                    var configuration = Decode(_context, m_strm);

                    foreach (var target in targets)
                    {
                        
                    }
                }
                catch
                {
                    result = StatusCodes.BadInvalidState;
                }
                finally
                {
                    m_sessionId = null;
                    m_strm = null;
                    m_node.LastUpdateTime.Value = DateTime.UtcNow;
                    m_node.OpenCount.Value = 0;
                }
            }

            // report the TrustListUpdatedAuditEvent
            // m_node.ReportTrustListUpdatedAuditEvent(context, objectId, "Method/CloseAndUpdate", method.NodeId, inputParameters, result.StatusCode);

            return result;
        }

        private Stream Encode(
            ISystemContext context,
            ApplicationConfigurationDataType configuration)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext() {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };
            MemoryStream strm = new MemoryStream();
            using (BinaryEncoder encoder = new BinaryEncoder(strm, messageContext, true))
            {
                encoder.WriteEncodeable(null, configuration, null);
            }
            strm.Position = 0;
            return strm;
        }

        private ApplicationConfigurationDataType Decode(
            ISystemContext context,
            Stream strm)
        {
            var trustList = new ApplicationConfigurationDataType();
            IServiceMessageContext messageContext = new ServiceMessageContext() {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Factory = context.EncodeableFactory
            };
            strm.Position = 0;
            using (IDecoder decoder = new BinaryDecoder(strm, messageContext))
            {
                trustList.Decode(decoder);
            }
            return trustList;
        }

        private void HasSecureReadAccess(ISystemContext context)
        {
            if (m_readAccess != null)
            {
                m_readAccess.Invoke(context, null);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        private void HasSecureWriteAccess(ISystemContext context)
        {
            if (m_writeAccess != null)
            {
                m_writeAccess.Invoke(context, null);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }
        #endregion
    }
}
