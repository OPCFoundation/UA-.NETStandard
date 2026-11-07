/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// The binary body of a Publish notification that stays raw bytes in a pooled
    /// buffer until it is read, normally right before dispatch. A message that waits
    /// for its dispatch is then a flat buffer instead of a graph of objects that
    /// every collection has to walk and promote.
    /// </summary>
    internal sealed class DeferredExtensionObjectBody
    {
        /// <summary>
        /// Create the body. It owns the rented buffer. The bodies of one response share
        /// <paramref name="failureLogged"/>, so that a response full of malformed bodies
        /// logs once, as the eager decoder does.
        /// </summary>
        public DeferredExtensionObjectBody(
            ExpandedNodeId typeId,
            byte[] buffer,
            int length,
            IEncodeableType activator,
            IServiceMessageContext context,
            uint nestingLevel,
            StrongBox<int> failureLogged)
        {
            m_typeId = typeId;
            m_buffer = buffer;
            m_length = length;
            m_activator = activator;
            m_context = context;
            m_nestingLevel = nestingLevel;
            m_failureLogged = failureLogged;
        }

        /// <summary>
        /// Whether a body of this type is decoded on first use.
        /// </summary>
        public static bool IsDeferred(IServiceMessageContext context, NodeId typeId)
        {
            return context is ServiceMessageContext { DeferNotificationBodies: true } &&
                (typeId == s_dataChangeEncodingId || typeId == s_eventsEncodingId);
        }

        /// <summary>
        /// Rents the buffer of a body.
        /// </summary>
        public static byte[] Rent(int length)
        {
            return ArrayPool<byte>.Shared.Rent(length);
        }

        /// <summary>
        /// Returns a buffer that no body took over.
        /// </summary>
        public static void Return(byte[] buffer)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        /// <summary>
        /// The decoded body. A body that cannot be decoded stays available as its
        /// bytes, like a known type the eager decoder recovered from. Null after
        /// <see cref="Release"/>.
        /// </summary>
        public object? Value
        {
            get
            {
                if (!Volatile.Read(ref m_resolved))
                {
                    Resolve();
                }
                return m_value;
            }
        }

        /// <summary>
        /// Drops the value once it was dispatched. The body can have been promoted
        /// while it waited, and would keep the value alive until that generation is
        /// collected although nothing else references the message any more.
        /// </summary>
        public void Release()
        {
            lock (m_lock)
            {
                if (m_buffer != null)
                {
                    Return(m_buffer);
                    m_buffer = null;
                }
                m_value = null;
                Volatile.Write(ref m_resolved, true);
            }
        }

        private void Resolve()
        {
            lock (m_lock)
            {
                if (m_resolved)
                {
                    return;
                }
                byte[] buffer = m_buffer!;
                m_value = Decode(buffer);
                m_buffer = null;
                Return(buffer);
                Volatile.Write(ref m_resolved, true);
            }
        }

        private object Decode(byte[] buffer)
        {
            string errorMessage;
            Exception? exception = null;
            try
            {
                IEncodeable value = m_activator.CreateInstance();
                using var decoder = new BinaryDecoder(buffer, 0, m_length, m_context);
                decoder.InheritDecodingState(null, null, m_nestingLevel, m_length);
                value.Decode(decoder);
                if (decoder.Position == m_length)
                {
                    return value;
                }
                errorMessage = "Length mismatch";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                errorMessage = ex.Message;
                exception = ex;
            }
            if (Interlocked.Exchange(ref m_failureLogged.Value, 1) == 0)
            {
                m_context.Telemetry.CreateLogger<DeferredExtensionObjectBody>().DecodeEncodeableRecovered(
                    exception,
                    errorMessage,
                    m_activator.XmlName,
                    m_typeId);
            }
            return ByteString.From(buffer.AsSpan(0, m_length).ToArray());
        }

        private static readonly NodeId s_dataChangeEncodingId = new(811u);
        private static readonly NodeId s_eventsEncodingId = new(916u);
        private readonly Lock m_lock = new();
        private readonly ExpandedNodeId m_typeId;
        private readonly int m_length;
        private readonly IEncodeableType m_activator;
        private readonly IServiceMessageContext m_context;
        private readonly uint m_nestingLevel;
        private readonly StrongBox<int> m_failureLogged;
        private byte[]? m_buffer;
        private object? m_value;
        private bool m_resolved;
    }
}
