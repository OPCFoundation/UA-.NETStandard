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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Lightweight scriptable mock around <see cref="ISessionClient"/> that
    /// captures every <c>CallMethodRequest</c> and dispatches the call to a
    /// per-method handler keyed by the method NodeId.
    /// </summary>
    internal sealed class FileTypeSessionMock
    {
        public FileTypeSessionMock()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);

            m_sessionMock = new Mock<ISessionClient>(MockBehavior.Strict);
            m_sessionMock.SetupGet(s => s.MessageContext).Returns(messageContext);
            m_sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        CallMethodRequest req = requests[0];
                        Capture.Add(req);
                        Variant[] outputs = Dispatch(req) ?? [];
                        var result = new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = outputs.ToArrayOf()
                        };
                        var response = new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = new[] { result }.ToArrayOf(),
                            DiagnosticInfos = default
                        };
                        return new ValueTask<CallResponse>(response);
                    });
        }

        public ISessionClient Session => m_sessionMock.Object;

        public List<CallMethodRequest> Capture { get; } = [];

        public List<CallMethodRequest> CapturedFor(uint methodId)
        {
            var matching = new List<CallMethodRequest>();
            foreach (CallMethodRequest req in Capture)
            {
                if (req.MethodId.IdType == IdType.Numeric &&
                    req.MethodId.TryGetValue(out uint identifier) &&
                    identifier == methodId)
                {
                    matching.Add(req);
                }
            }
            return matching;
        }

        public void OnOpen(Func<byte, uint> handler)
        {
            m_handlers[Methods.FileType_Open] = req =>
            {
                req.InputArguments[0].TryGetValue(out byte mode);
                uint handle = handler(mode);
                return [new Variant(handle)];
            };
        }

        public void OnRead(Func<uint, int, byte[]> handler)
        {
            m_handlers[Methods.FileType_Read] = req =>
            {
                req.InputArguments[0].TryGetValue(out uint h);
                req.InputArguments[1].TryGetValue(out int len);
                byte[] payload = handler(h, len) ?? [];
                return [new Variant(payload.ToByteString())];
            };
        }

        public void OnWrite(Action<uint, byte[]> handler)
        {
            m_handlers[Methods.FileType_Write] = req =>
            {
                req.InputArguments[0].TryGetValue(out uint h);
                req.InputArguments[1].TryGetValue(out ByteString data);
                handler(h, data.ToArray() ?? []);
                return [];
            };
        }

        public void OnSetPosition(Action<uint, ulong> handler)
        {
            m_handlers[Methods.FileType_SetPosition] = req =>
            {
                req.InputArguments[0].TryGetValue(out uint h);
                req.InputArguments[1].TryGetValue(out ulong pos);
                handler(h, pos);
                return [];
            };
        }

        public void OnGetPosition(Func<uint, ulong> handler)
        {
            m_handlers[Methods.FileType_GetPosition] = req =>
            {
                req.InputArguments[0].TryGetValue(out uint h);
                ulong pos = handler(h);
                return [new Variant(pos)];
            };
        }

        public void OnClose(Action<uint> handler)
        {
            m_handlers[Methods.FileType_Close] = req =>
            {
                req.InputArguments[0].TryGetValue(out uint h);
                handler(h);
                return [];
            };
        }

        private Variant[] Dispatch(CallMethodRequest req)
        {
            if (!req.MethodId.TryGetValue(out uint methodId))
            {
                methodId = 0;
            }
            if (m_handlers.TryGetValue(methodId, out Func<CallMethodRequest, Variant[]> handler))
            {
                return handler(req);
            }
            throw new InvalidOperationException(
                $"FileTypeSessionMock: no handler registered for method id {methodId}.");
        }

        private readonly Mock<ISessionClient> m_sessionMock;
        private readonly Dictionary<uint, Func<CallMethodRequest, Variant[]>> m_handlers = [];
    }
}
