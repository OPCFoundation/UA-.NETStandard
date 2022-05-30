/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace Opc.Ua.Bindings
{
    /// <remarks/>
    public sealed class WebSocketConnection : IDisposable
    {
        private bool m_disposed;
        private TcpClient m_client;
        private Stream m_stream;
        private BufferManager m_bufferManager;
        private bool m_writeQueueTaskActive;
        private bool m_isServerSide;
        private RNGCryptoServiceProvider m_random = new RNGCryptoServiceProvider();
        private Queue<ArraySegment<byte>> m_writeQueue = new Queue<ArraySegment<byte>>();
        private string m_websocketKey;

        private class WebSocketFrame
        {
            public bool Final;
            public byte OpCode;
            public bool Masked;
            public byte[] Payload;
            public int Offset;
            public int Length;
        }

        /// <remarks/>
        public WebSocketConnection(TcpClient client, Stream stream, BufferManager bufferManager, bool isServerSide)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (bufferManager == null)
            {
                throw new ArgumentNullException("bufferManager");
            }

            m_client = client;
            m_stream = stream;
            m_bufferManager = bufferManager;
            m_isServerSide = isServerSide;

            State = ConnectionState.WaitingForHttpUpgrade;
        }

        #region IDisposable Support
        void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    if (m_stream != null)
                    {
                        m_stream.Close();
                        m_stream = null;
                    }

                    if (m_client != null)
                    {
                        m_client.Close();
                        m_client = null;
                    }
                }

                m_disposed = true;
            }
        }

        /// <remarks/>
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        /// <remarks/>
        public object Handle { get; set; }

        /// <remarks/>
        public enum ConnectionState
        {
            /// <remarks/>
            WaitingForHttpUpgrade,
            /// <remarks/>
            WaitingForHello,
            /// <remarks/>
            Open,
            /// <remarks/>
            Closed
        }

        /// <remarks/>
        public ConnectionState State
        {
            get; internal set;
        }

        /// <remarks/>
        public TcpClient TcpClient
        {
            get { return m_client; }
        }

        /// <remarks/>
        public Stream Stream
        {
            get { return m_stream; }
        }

        /// <remarks/>
        public void Upgrade(Stream stream)
        {
            m_stream = stream;
        }

        /// <remarks/>
        public string MessageEncoding { get; set; }

        private async Task<string> ReceiveHttpHeader()
        {
            var chunk = m_bufferManager.TakeBuffer(UInt16.MaxValue, "WebSocketConnection.ReceiveHttpHeader");

            try
            {
                int offset = 0;

                while (true)
                {
                    int bytesRead = await m_stream.ReadAsync(chunk, offset, 1);

                    if (bytesRead == 0)
                    {
                        throw new EndOfStreamException("Peer closed socket gracefully.");
                    }

                    if (offset > 4 && chunk[offset] == '\n')
                    {
                        if (chunk[offset - 3] == '\r' && chunk[offset - 2] == '\n' && chunk[offset - 1] == '\r')
                        {
                            break;
                        }
                    }

                    offset++;
                }

                return Encoding.UTF8.GetString(chunk, 0, offset - 1);
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(chunk, "WebSocketConnection.ReceiveHttpHeader");
                throw new Exception(e.Message, e);
            }
        }

        private string ExtractToken(string source, ref int offset, params char[] terminators)
        {
            int start = offset;

            while (offset < source.Length)
            {
                if (terminators != null)
                {
                    for (int ii = 0; ii < terminators.Length; ii++)
                    {
                        if (terminators[ii] == source[offset])
                        {
                            return source.Substring(start, offset++ - start).Trim();
                        }
                    }
                }

                offset++;
            }

            return null;
        }

        private async Task SendErrorResponse(HttpStatusCode code)
        {
            using (var writer = new StreamWriter(m_stream, new UTF8Encoding(false), UInt16.MaxValue, false))
            {
                await writer.WriteAsync("HTTP/1.1 ").ConfigureAwait(false); 
                await writer.WriteAsync(((int)code).ToString()).ConfigureAwait(false);
                await writer.WriteAsync(" ").ConfigureAwait(false);
                await writer.WriteAsync(code.ToString()).ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
                await writer.WriteAsync("Date: ").ConfigureAwait(false);
                await writer.WriteAsync(DateTime.Now.ToString("r")).ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
                await writer.WriteAsync("Connection: Close").ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
            }
        }

        private async Task<string> SendHttpUpgradeRequest(Uri url)
        {
            var bytes = new byte[32];
            m_random.GetBytes(bytes);
            m_websocketKey = Convert.ToBase64String(bytes);

            using (var writer = new StreamWriter(m_stream, new UTF8Encoding(false), UInt16.MaxValue, true))
            {
                await writer.WriteAsync("GET ");
                await writer.WriteAsync((url == null) ? "/" : url.PathAndQuery);
                await writer.WriteAsync(" ");
                await writer.WriteAsync("HTTP/1.1");
                await writer.WriteAsync("\r\n");
                await writer.WriteAsync("Connection: Upgrade,Keep-Alive");
                await writer.WriteAsync("\r\n");
                await writer.WriteAsync("Upgrade: WebSocket");
                await writer.WriteAsync("\r\n");
                await writer.WriteAsync("Sec-WebSocket-Key: ");
                await writer.WriteAsync(m_websocketKey);
                await writer.WriteAsync("\r\n");
                await writer.WriteAsync("Sec-WebSocket-Version: 13");
                await writer.WriteAsync("\r\n");
                await writer.WriteAsync("Sec-WebSocket-Protocol: opcua+uatcp");
                await writer.WriteAsync("\r\n");
                await writer.WriteAsync("\r\n");
            }

            return await ReceiveHttpHeader();
        }

        private async Task ProcessHttpUpgradeRequest(string message)
        {
            StringBuilder response = new StringBuilder();

            int ii = 0;

            string method = ExtractToken(message, ref ii, ' ', '\t');
            string url = ExtractToken(message, ref ii, ' ', '\t');
            string version = ExtractToken(message, ref ii, '\n');

            if (version != "HTTP/1.1")
            {
                await SendErrorResponse(HttpStatusCode.HttpVersionNotSupported);
                throw new WebException("HTTP version not supported.", WebExceptionStatus.ProtocolError);
            }

            if (method == "POST")
            {
                await SendErrorResponse(HttpStatusCode.MethodNotAllowed);
                throw new WebException("HTTP method not supported.", WebExceptionStatus.ProtocolError);
            }

            if (method != "GET")
            {
                await SendErrorResponse(HttpStatusCode.MethodNotAllowed);
                throw new WebException("HTTP method not supported.", WebExceptionStatus.ProtocolError);
            }

            Dictionary<string, string> headers = new Dictionary<string, string>();

            do
            {
                string name = ExtractToken(message, ref ii, ':');

                if (name == null)
                {
                    break;
                }

                string value = ExtractToken(message, ref ii, '\r');
                headers[name] = value;
            }
            while (true);

            string upgradeType = null;

            if (!headers.TryGetValue("Upgrade", out upgradeType) || !upgradeType.ToLower().Contains("websocket"))
            {
                await SendErrorResponse(HttpStatusCode.BadRequest);
                throw new WebException("Upgrade header missing or not 'websocket'.", WebExceptionStatus.ProtocolError);
            }

            string protocolVersion = null;

            if (!headers.TryGetValue("Sec-WebSocket-Version", out protocolVersion) || protocolVersion != "13")
            {
                await SendErrorResponse(HttpStatusCode.BadRequest);
                throw new WebException("WebSocket version header missing or not supported.", WebExceptionStatus.ProtocolError);
            }

            string subprotocol = null;

            if (!headers.TryGetValue("Sec-WebSocket-Protocol", out subprotocol))
            {
                await SendErrorResponse(HttpStatusCode.NotImplemented);
                throw new WebException("WebSocket protocol header missing.", WebExceptionStatus.ProtocolError);
            }

            if (!subprotocol.ToLower().Contains("opcua+uatcp") && !subprotocol.ToLower().Contains("opcua+uajson"))
            {
                await SendErrorResponse(HttpStatusCode.NotImplemented);
                throw new WebException("WebSocket protocol is not supported.", WebExceptionStatus.ProtocolError);
            }

            MessageEncoding = subprotocol;

            string origin = null;

            if (!headers.TryGetValue("Origin", out origin))
            {
                origin = null;
            }

            string encodedKey = null;

            if (!headers.TryGetValue("Sec-WebSocket-Key", out encodedKey))
            {
                await SendErrorResponse(HttpStatusCode.BadRequest);
                throw new WebException("WebSocket key header missing.", WebExceptionStatus.ProtocolError);
            }

            string acceptKey = encodedKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            var hash = new System.Security.Cryptography.SHA1Managed().ComputeHash(new UTF8Encoding(false).GetBytes(acceptKey));

            using (var writer = new StreamWriter(m_stream, new UTF8Encoding(false), UInt16.MaxValue, true))
            {
                await writer.WriteAsync("HTTP/1.1 101 Switching Protocols\r\n").ConfigureAwait(false);
                await writer.WriteAsync("Date: ").ConfigureAwait(false);
                await writer.WriteAsync(DateTime.Now.ToString("r")).ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
                await writer.WriteAsync("Connection: Upgrade").ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
                await writer.WriteAsync("Upgrade: WebSocket").ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
                await writer.WriteAsync("Sec-WebSocket-Accept: ").ConfigureAwait(false);
                await writer.WriteAsync(Convert.ToBase64String(hash)).ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);
                await writer.WriteAsync("Sec-WebSocket-Protocol: ").ConfigureAwait(false);
                await writer.WriteAsync(MessageEncoding).ConfigureAwait(false);
                await writer.WriteAsync("\r\n").ConfigureAwait(false);

                if (origin != null)
                {
                    await writer.WriteAsync("Access-Control-Allow-Origin: ").ConfigureAwait(false);
                    await writer.WriteAsync(origin).ConfigureAwait(false);
                    await writer.WriteAsync("\r\n").ConfigureAwait(false);
                }

                await writer.WriteAsync("\r\n").ConfigureAwait(false); 
                await writer.FlushAsync().ConfigureAwait(false);
            }

            await m_stream.FlushAsync().ConfigureAwait(false);
            State = ConnectionState.WaitingForHello;
        }

        private void ProcessHttpUpgradeResponse(string message)
        {
            int ii = 0;

            string version = ExtractToken(message, ref ii, ' ', '\t');
            string code = ExtractToken(message, ref ii, ' ', '\t');
            string reason = ExtractToken(message, ref ii, '\n');

            if (version != "HTTP/1.1")
            {
                throw new WebException("HTTP version not supported.", WebExceptionStatus.ServerProtocolViolation);
            }

            ushort numericCode = 0;

            if (!UInt16.TryParse(code, out numericCode) || numericCode < 100 || numericCode >= 600)
            {
                throw new WebException("HTTP status code not valid.", WebExceptionStatus.ServerProtocolViolation);
            }

            if (numericCode != 101)
            {
                throw new WebException(String.Format("Upgrade failed with status {0} {1}.", numericCode, reason), WebExceptionStatus.ProtocolError);
            }

            Dictionary<string, string> headers = new Dictionary<string, string>();

            do
            {
                string name = ExtractToken(message, ref ii, ':');

                if (name == null)
                {
                    break;
                }

                string value = ExtractToken(message, ref ii, '\r');
                headers[name] = value;
            }
            while (true);

            string upgradeType = null;

            if (!headers.TryGetValue("Upgrade", out upgradeType) || !upgradeType.ToLower().Contains("websocket"))
            {
                throw new WebException("Upgrade header missing or not 'websocket'.", WebExceptionStatus.ProtocolError);
            }

            string subprotocol = null;

            if (!headers.TryGetValue("Sec-WebSocket-Protocol", out subprotocol))
            {
                throw new WebException("WebSocket protocol header missing.", WebExceptionStatus.ProtocolError);
            }

            if (!subprotocol.ToLower().Contains("opcua+uatcp") && !subprotocol.ToLower().Contains("opcua+uajson"))
            {
                throw new WebException("WebSocket protocol is not supported.", WebExceptionStatus.ProtocolError);
            }

            MessageEncoding = subprotocol;
            
            string encodedKey = null;

            if (!headers.TryGetValue("Sec-WebSocket-Accept", out encodedKey))
            {
                throw new WebException("WebSocket accept header missing.", WebExceptionStatus.ProtocolError);
            }

            string acceptKey = m_websocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            var hash = new System.Security.Cryptography.SHA1Managed().ComputeHash(new UTF8Encoding(false).GetBytes(acceptKey));

            if (Convert.ToBase64String(hash) != encodedKey)
            {
                throw new WebException("WebSocket accept header not correct.", WebExceptionStatus.ProtocolError);
            }
            
            State = ConnectionState.WaitingForHello;
        }

        private async Task SendCloseAsync(ushort code, string reason, bool masked)
        {
            var frame = new WebSocketFrame();

            frame.Final = true;
            frame.OpCode = 0x08;
            frame.Masked = masked;

            if (!String.IsNullOrEmpty(reason))
            {
                frame.Length = reason.Length + 2;
                frame.Payload = new byte[frame.Length];
                new UTF8Encoding(false).GetBytes(reason, 0, reason.Length, frame.Payload, 2);
            }
            else
            {
                frame.Length = 2;
                frame.Payload = new byte[frame.Length];
            }

            frame.Payload[0] = (byte)((code >> 8) & 0xFF);
            frame.Payload[1] = (byte)(code & 0xFF);

            await SendFrameAsync(frame);
        }

        private async Task SendPongAsync(WebSocketFrame ping)
        {
            var frame = new WebSocketFrame();

            frame.Final = true;
            frame.OpCode = 0x0A;
            frame.Masked = !m_isServerSide;
            frame.Payload = ping.Payload;
            frame.Offset = ping.Offset;
            frame.Length = ping.Length;

            await SendFrameAsync(frame);
        }

        private static void EncodeInteger(ushort value, byte[] buffer, int offset)
        {
            int start = sizeof(ushort) * 8 - 8;

            for (int ii = 0; ii < sizeof(ushort); ii++)
            {
                buffer[ii + offset] = (byte)((value >> (start - ii * 8)) & 0xFF);
            }
        }

        private static void EncodeInteger(ulong value, byte[] buffer, int offset)
        {
            int start = sizeof(ulong) * 8 - 8;

            for (int ii = 0; ii < sizeof(ulong); ii++)
            {
                buffer[ii + offset] = (byte)((value >> (start - ii * 8)) & 0xFF);
            }
        }

        private async Task SendFrameAsync(WebSocketFrame frame)
        {
            byte[] buffer = new byte[14];

            buffer[0] = frame.OpCode;

            if (frame.Final)
            {
                buffer[0] |= 0x80;
            }

            int length = (frame.Payload != null) ? (frame.Payload.Length - frame.Offset < frame.Length) ? frame.Payload.Length - frame.Offset : frame.Length : 0;

            int offset = 2;

            if (length < 126)
            {
                buffer[1] = (byte)length;
            }
            else if (length <= UInt16.MaxValue)
            {
                buffer[1] = 126;
                EncodeInteger((ushort)length, buffer, 2);
                offset = 4;
            }
            else
            {
                buffer[1] = 127;
                EncodeInteger((ulong)length, buffer, 8);
                offset = 10;
            }

            if (frame.Masked)
            {
                buffer[1] |= 0x80;
            }

            if (frame.Payload != null && frame.Masked)
            {
                byte[] mask = new byte[4];
                m_random.GetNonZeroBytes(mask);

                for (int ii = 0; ii < mask.Length; ii++)
                {
                    buffer[offset++] = mask[ii];
                }

                for (int ii = 0; ii < length; ii++)
                {
                    frame.Payload[ii + frame.Offset] = (byte)(frame.Payload[ii + frame.Offset] ^ mask[ii % 4]);
                }
            }

            await m_stream.WriteAsync(buffer, 0, offset);

            if (length > 0)
            {
                await m_stream.WriteAsync(frame.Payload, frame.Offset, length);
            }
        }

        private static void SwapBytes(byte[] buffer, int offset, int count)
        {
            for (int ii = 0; ii < count / 2; ii++)
            {
                var msb = buffer[ii + offset];
                buffer[ii + offset] = buffer[offset + count - ii - 1];
                buffer[offset + count - ii - 1] = msb;
            }
        }

        private async Task<int> ReceiveAsync(byte[] buffer, int offset, int bytesToRead)
        {
            int bytesRead = 0;

            do
            {
                bytesRead = await m_stream.ReadAsync(buffer, offset, bytesToRead).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    return 0;
                }

                offset += bytesRead;
                bytesToRead -= bytesRead;
            }
            while (bytesToRead > 0);

            return bytesRead;
        }

        private async Task<WebSocketFrame> ReceiveFrameAsync(Stream stream, byte[] buffer, int offset, int length)
        {
            int pos = offset;
            int result = 0;
            var frame = new WebSocketFrame();

            result = await ReceiveAsync(buffer, pos, 2).ConfigureAwait(false);

            if (result == 0)
            {
                throw new ServiceResultException(StatusCodes.BadDisconnect);
            }

            frame.Final = (buffer[pos] & 0x80) != 0;
            frame.OpCode = (byte)(buffer[pos] & 0x0F);
            frame.Masked = (buffer[pos+1] & 0x80) != 0;

            ulong payloadLength = (byte)(buffer[pos+1] & 0x7F);

            pos += 2;

            if (payloadLength == 126)
            {
                result = await ReceiveAsync(buffer, pos, 2).ConfigureAwait(false);

                if (result == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadDisconnect);
                }

                SwapBytes(buffer, pos, 2);
                payloadLength = BitConverter.ToUInt16(buffer, pos);
                pos += 2;

            }
            else if (payloadLength == 127)
            {
                result = await ReceiveAsync(buffer, pos, 8).ConfigureAwait(false);

                if (result == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadDisconnect);
                }

                SwapBytes(buffer, pos, 8);
                payloadLength = BitConverter.ToUInt64(buffer, pos);
                pos += 8;
            }

            int mask = -1;

            if (frame.Masked)
            {
                mask = pos;
                result = await ReceiveAsync(buffer, pos, 4).ConfigureAwait(false);

                if (result == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadDisconnect);
                }

                pos += 4;
            }

            if (payloadLength > 0)
            {
                if (payloadLength > (ulong)(length - (pos - offset)))
                {
                    throw new ServiceResultException(StatusCodes.BadTcpMessageTooLarge);
                }

                result = await ReceiveAsync(buffer, pos, (int)payloadLength).ConfigureAwait(false);

                if (result == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadDisconnect);
                }

                if (mask != -1)
                {
                    for (int ii = 0; ii < (int)payloadLength; ii++)
                    {
                        buffer[ii+pos] = (byte)(buffer[ii+pos] ^ buffer[(ii % 4)+mask]);
                    }
                }

                frame.Payload = buffer;
                frame.Offset = pos;
                frame.Length = (int)payloadLength;
            }

            return frame;
        }

        /// <remarks/>
        public async Task ConnectAsync()
        {
            var response = await SendHttpUpgradeRequest(null).ConfigureAwait(false);
            ProcessHttpUpgradeResponse(response);
        }

        /// <remarks/>
        public async Task DisconnectAsync()
        {
            await SendCloseAsync((ushort)HttpStatusCode.ServiceUnavailable, null, !m_isServerSide).ConfigureAwait(false);
        }

        /// <remarks/>
        public async Task<ArraySegment<byte>> ReceiveMessage()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException("WebSocketConnection");
            }

            var chunk = m_bufferManager.TakeBuffer(UInt16.MaxValue, "WebSocketConnection.ReceiveFrameAsync");

            try
            {

                WebSocketFrame frame = null;

                do
                {
                    if (State == ConnectionState.WaitingForHttpUpgrade)
                    {
                        if (m_isServerSide)
                        {
                            var request = await ReceiveHttpHeader().ConfigureAwait(false);
                            await ProcessHttpUpgradeRequest(request).ConfigureAwait(false);
                        }
                    }

                    frame = await ReceiveFrameAsync(m_stream, chunk, 0, chunk.Length).ConfigureAwait(false);

                    if (frame.OpCode == 0x1 || frame.OpCode == 0x2)
                    {
                        break;
                    }

                    if (frame.OpCode == 0x8)
                    {
                        throw new ServiceResultException(StatusCodes.BadDisconnect);
                    }

                    if (frame.OpCode == 0x9)
                    {
                        await SendPongAsync(frame).ConfigureAwait(false);
                    }
                }
                while (true);

                if (MessageEncoding == "opcua+uatcp")
                {
                    var messageType = BitConverter.ToUInt32(frame.Payload, frame.Offset);
                    var messageSize = BitConverter.ToUInt32(frame.Payload, frame.Offset + 4);

                    if (messageSize > frame.Length)
                    {
                        throw new ServiceResultException(StatusCodes.BadTcpMessageTooLarge);
                    }
                }

                return new ArraySegment<byte>(frame.Payload, frame.Offset, frame.Length);
            }
            catch (Exception)
            {
                m_bufferManager.ReturnBuffer(chunk, "WebSocketConnection.ReceiveFrameAsync");
                throw;
            }
        }

        /// <remarks/>
        public void SendMessage(IList<ArraySegment<byte>> buffers)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException("WebSocketConnection");
            }

            lock (m_writeQueue)
            {
                foreach (var buffer in buffers)
                {
                    m_writeQueue.Enqueue(buffer);

                    if (!m_writeQueueTaskActive)
                    {
                        m_writeQueueTaskActive = true;
                        Task.Run(() => Dequeue());
                    }
                }
            }

            Task.Run(() => Dequeue());
        }

        /// <remarks/>
        public void SendMessage(ArraySegment<byte> buffer)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException("WebSocketConnection");
            }

            lock (m_writeQueue)
            {
                m_writeQueue.Enqueue(buffer);

                if (!m_writeQueueTaskActive)
                {
                    m_writeQueueTaskActive = true;
                    Task.Run(() => Dequeue());
                }
            }
        }

        private async void Dequeue()
        {
            var stream = m_stream;

            do
            {
                ArraySegment <byte> message;

                lock (m_writeQueue)
                {
                    if (m_writeQueue.Count == 0)
                    {
                        m_writeQueueTaskActive = false;
                        break;
                    }

                    message = m_writeQueue.Dequeue();
                }

                try
                {
                    WebSocketFrame frame = new WebSocketFrame();

                    frame.Final = true;
                    frame.Masked = !m_isServerSide;
                    frame.OpCode = (byte)((MessageEncoding == "opcua+uajson") ? 0x1 : 0x2);
                    frame.Payload = message.Array;
                    frame.Offset = message.Offset;
                    frame.Length = message.Count;

                    await SendFrameAsync(frame).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "WebSocketConnection.Dequeue: WriteAsync failed.");
                }
                finally
                {
                    m_bufferManager.ReturnBuffer(message.Array, "WebSocketConnection.Dequeue");
                }
            }
            while (true);

            try
            {
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "WebSocketConnection.Dequeue: FlushAsync failed.");
            }
        }
    }
}
