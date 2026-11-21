// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
