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
    public static partial class FuzzableCode
    {
        public static void AflfuzzTcpChunkHeader(Stream stream)
        {
            LibfuzzTcpChunkHeader(ReadCapped(stream));
        }

        public static void LibfuzzTcpChunkHeader(ReadOnlySpan<byte> input)
        {
            _ = TcpMessageParsers.TryParseChunkHeader(input, out _, out _, out _);
        }

        public static void AflfuzzHelloMessage(Stream stream)
        {
            LibfuzzHelloMessage(ReadCapped(stream));
        }

        public static void LibfuzzHelloMessage(ReadOnlySpan<byte> input)
        {
            TryParseBody(input, TcpMessageParsers.ReadHelloMessage);
        }

        public static void AflfuzzAcknowledgeMessage(Stream stream)
        {
            LibfuzzAcknowledgeMessage(ReadCapped(stream));
        }

        public static void LibfuzzAcknowledgeMessage(ReadOnlySpan<byte> input)
        {
            TryParseBody(input, TcpMessageParsers.ReadAcknowledgeMessage);
        }

        public static void AflfuzzErrorMessage(Stream stream)
        {
            LibfuzzErrorMessage(ReadCapped(stream));
        }

        public static void LibfuzzErrorMessage(ReadOnlySpan<byte> input)
        {
            TryParseBody(input, TcpMessageParsers.ReadErrorMessage);
        }

        public static void AflfuzzReverseHelloMessage(Stream stream)
        {
            LibfuzzReverseHelloMessage(ReadCapped(stream));
        }

        public static void LibfuzzReverseHelloMessage(ReadOnlySpan<byte> input)
        {
            TryParseBody(input, TcpMessageParsers.ReadReverseHelloMessage);
        }

        public static void AflfuzzAsymmetricMessageHeader(Stream stream)
        {
            LibfuzzAsymmetricMessageHeader(ReadCapped(stream));
        }

        public static void LibfuzzAsymmetricMessageHeader(ReadOnlySpan<byte> input)
        {
            TryParseBody(input, TcpMessageParsers.ReadAsymmetricMessageHeader);
        }

        private static void TryParseBody<T>(ReadOnlySpan<byte> input, Func<ArraySegment<byte>, T> parser)
        {
            byte[] bytes = CopyCapped(input);

            try
            {
                _ = parser(new ArraySegment<byte>(bytes));
            }
            catch (ServiceResultException ex) when (IsExpectedTransportParserStatus(ex.StatusCode))
            {
            }
        }

        private static bool IsExpectedTransportParserStatus(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadTcpMessageTypeInvalid ||
                statusCode == StatusCodes.BadDecodingError ||
                statusCode == StatusCodes.BadEncodingLimitsExceeded;
        }
    }
}
