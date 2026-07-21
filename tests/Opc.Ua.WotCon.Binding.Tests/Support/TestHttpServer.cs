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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Binding.Tests.Support
{
    /// <summary>A single response returned by the in-process test HTTP server.</summary>
    public sealed class TestHttpResponse
    {
        public TestHttpResponse(int status, string contentType, byte[] body)
        {
            Status = status;
            ContentType = contentType;
            Body = body;
        }

        public int Status { get; }

        public string ContentType { get; }

        public byte[] Body { get; }

        public static TestHttpResponse Json(int status, string json)
            => new TestHttpResponse(status, "application/json", Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// A minimal in-process HTTP/1.1 server built on <see cref="TcpListener"/>
    /// (avoiding <c>HttpListener</c> URL-ACL requirements). It routes each request
    /// to a supplied handler and is used for the HTTP executor end-to-end tests.
    /// </summary>
    public sealed class TestHttpServer : IDisposable
    {
        public TestHttpServer(Func<string, string, byte[], TestHttpResponse> handler)
        {
            m_handler = handler;
            m_listener = new TcpListener(IPAddress.Loopback, 0);
            m_listener.Start();
            int port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";
            m_loop = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }

        public void Dispose()
        {
            m_cts.Cancel();
            m_listener.Stop();
            m_listener.Dispose();
            try
            {
                m_loop.Wait(2000);
            }
            catch (AggregateException)
            {
                // Ignore teardown faults.
            }
            m_cts.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!m_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    (string method, string path, byte[] body) = await ReadRequestAsync(stream).ConfigureAwait(false);
                    if (method is null)
                    {
                        return;
                    }
                    TestHttpResponse response = m_handler(method, path, body);
                    await WriteResponseAsync(stream, response).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // Client disconnected.
                }
            }
        }

        private static async Task<(string Method, string Path, byte[] Body)> ReadRequestAsync(NetworkStream stream)
        {
            var header = new MemoryStream();
            byte[] one = new byte[1];
            int matched = 0;
            byte[] terminator = Encoding.ASCII.GetBytes("\r\n\r\n");
            while (matched < terminator.Length)
            {
                int read = await stream.ReadAsync(one.AsMemory(0, 1)).ConfigureAwait(false);
                if (read == 0)
                {
                    return (null!, null!, Array.Empty<byte>());
                }
                header.WriteByte(one[0]);
                matched = one[0] == terminator[matched] ? matched + 1 : (one[0] == terminator[0] ? 1 : 0);
            }

            string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
            string[] requestLine = lines[0].Split(' ');
            string method = requestLine.Length > 0 ? requestLine[0] : "GET";
            string path = requestLine.Length > 1 ? requestLine[1] : "/";
            int contentLength = 0;
            foreach (string line in lines)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                    !int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength))
                {
                    contentLength = 0;
                }
            }

            byte[] body = Array.Empty<byte>();
            if (contentLength > 0)
            {
                body = new byte[contentLength];
                int offset = 0;
                while (offset < contentLength)
                {
                    int read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset)).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    offset += read;
                }
            }
            return (method, path, body);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, TestHttpResponse response)
        {
            byte[] body = response.Body ?? Array.Empty<byte>();
            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(Reason(response.Status)).Append("\r\n");
            builder.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
            builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            builder.Append("Connection: close\r\n\r\n");
            byte[] head = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(head).ConfigureAwait(false);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body).ConfigureAwait(false);
            }
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static string Reason(int status)
        {
            return status switch
            {
                200 => "OK",
                204 => "No Content",
                400 => "Bad Request",
                404 => "Not Found",
                500 => "Internal Server Error",
                _ => "Status"
            };
        }

        private readonly Func<string, string, byte[], TestHttpResponse> m_handler;
        private readonly TcpListener m_listener;
        private readonly Task m_loop;
        private readonly CancellationTokenSource m_cts = new CancellationTokenSource();
    }
}
