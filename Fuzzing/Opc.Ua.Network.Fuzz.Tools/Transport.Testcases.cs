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
using System.IO;
using Opc.Ua.Bindings;

namespace Opc.Ua.Fuzzing
{
    public static partial class Testcases
    {
        private static void WriteTransportTestcases(string workPath, ITelemetryContext telemetry)
        {
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);

            WriteBodyAndChunkSeeds(
                workPath,
                "Hello",
                TcpMessageType.Hello,
                CreateHelloBody(context, "opc.tcp://localhost:4840"),
                CreateHelloBody(context, "opc.tcp://127.0.0.1:4840/UA/Fuzz"));
            WriteBodyAndChunkSeeds(
                workPath,
                "Ack",
                TcpMessageType.Acknowledge,
                CreateAcknowledgeBody(context),
                CreateAcknowledgeBody(context, 32768, 32768));
            WriteBodyAndChunkSeeds(
                workPath,
                "Err",
                TcpMessageType.Error,
                CreateErrorBody(context, StatusCodes.BadTcpMessageTypeInvalid, "invalid message type"),
                CreateErrorBody(context, StatusCodes.BadDecodingError, "decode failed"));
            WriteBodyAndChunkSeeds(
                workPath,
                "Rhe",
                TcpMessageType.ReverseHello,
                CreateReverseHelloBody(context, "urn:localhost:fuzz", "opc.tcp://localhost:4840"),
                CreateReverseHelloBody(context, "urn:127.0.0.1:fuzz", "opc.tcp://127.0.0.1:4840"));
            WriteBodyAndChunkSeeds(
                workPath,
                "AsymHdr",
                TcpMessageType.Open | TcpMessageType.Final,
                CreateAsymmetricHeaderBody(context, FuzzableCode.TestChannelId, SecurityPolicies.None),
                CreateAsymmetricHeaderBody(context, FuzzableCode.TestChannelId + 1, SecurityPolicies.Basic256Sha256));
        }

        private static void WriteBodyAndChunkSeeds(
            string workPath,
            string name,
            uint messageType,
            params byte[][] bodies)
        {
            string path = Path.Combine(workPath + ".Tcp." + name);
            Directory.CreateDirectory(path);

            for (int ii = 0; ii < bodies.Length; ii++)
            {
                File.WriteAllBytes(Path.Combine(path, $"body-{ii:000}.bin"), bodies[ii]);
                File.WriteAllBytes(Path.Combine(path, $"chunk-{ii:000}.bin"), FuzzableCode.BuildChunk(messageType, bodies[ii]));
            }
        }

        private static byte[] CreateHelloBody(
            IServiceMessageContext context,
            string endpointUrl)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 65536);
                encoder.WriteUInt32(null, 65536);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteString(null, endpointUrl);
            }

            return stream.ToArray();
        }

        private static byte[] CreateAcknowledgeBody(
            IServiceMessageContext context,
            uint receiveBufferSize = 65536,
            uint sendBufferSize = 65536)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, receiveBufferSize);
                encoder.WriteUInt32(null, sendBufferSize);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteUInt32(null, 8192);
            }

            return stream.ToArray();
        }

        private static byte[] CreateErrorBody(
            IServiceMessageContext context,
            StatusCode statusCode,
            string reason)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteStatusCode(null, statusCode);
                encoder.WriteString(null, reason);
            }

            return stream.ToArray();
        }

        private static byte[] CreateReverseHelloBody(
            IServiceMessageContext context,
            string serverUri,
            string endpointUrl)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteString(null, serverUri);
                encoder.WriteString(null, endpointUrl);
            }

            return stream.ToArray();
        }

        private static byte[] CreateAsymmetricHeaderBody(
            IServiceMessageContext context,
            uint secureChannelId,
            string securityPolicyUri)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteUInt32(null, secureChannelId);
                encoder.WriteString(null, securityPolicyUri);
                encoder.WriteByteString(null, Array.Empty<byte>());
                encoder.WriteByteString(null, Array.Empty<byte>());
            }

            return stream.ToArray();
        }
    }
}
