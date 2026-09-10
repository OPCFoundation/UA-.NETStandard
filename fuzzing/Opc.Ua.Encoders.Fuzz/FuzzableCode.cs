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
using System.IO;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static ServiceMessageContext MessageContext { get; } =
            CreateMessageContext();

        /// <summary>
        /// Print information about the fuzzer target.
        /// </summary>
        public static void FuzzInfo()
        {
            Console.WriteLine("OPC UA Core Encoder Fuzzer for afl-fuzz and libfuzzer.");
            Console.WriteLine("Fuzzing targets for various aspects of the Binary, Json and Xml encoders.");
        }

        internal static MemoryStream PrepareArraySegmentStream(ReadOnlySpan<byte> input, int segmentSize = 0x40)
        {
            if (segmentSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentSize));
            }

            var buffers = new BufferCollection();
            while (!input.IsEmpty)
            {
                int length = Math.Min(input.Length, segmentSize);
                buffers.Add(new ArraySegment<byte>(input[..length].ToArray()));
                input = input[length..];
            }
            if (buffers.Count == 0)
            {
                buffers.Add(new ArraySegment<byte>(Array.Empty<byte>()));
            }
            return new ArraySegmentStream(buffers);
        }

        internal static ServiceMessageContext CreateMessageContext()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(new FuzzTelemetryContext());
            context.NamespaceUris.Append("urn:opcfoundation:fuzzing:application");
            context.NamespaceUris.Append("urn:opcfoundation:fuzzing:devices");
            context.NamespaceUris.Append("urn:opcfoundation:fuzzing:diagnostics");
            context.NamespaceUris.Append("urn:opcfoundation:fuzzing:types");
            context.ServerUris.Append("urn:opcfoundation:fuzzing:server");
            return context;
        }

        /// <summary>
        /// Prepare a seekable memory stream from the input stream.
        /// </summary>
        private static MemoryStream PrepareArraySegmentStream(Stream stream)
        {
            const int segmentSize = 0x40;

            // AFL stdin is non-seekable; separate buffers also exercise chunk boundaries.
            MemoryStream memoryStream;
            using (var binaryStream = new BinaryReader(stream))
            {
                var bufferCollection = new BufferCollection();
                byte[] buffer;
                do
                {
                    buffer = binaryStream.ReadBytes(segmentSize);
                    bufferCollection.Add(new ArraySegment<byte>(buffer));
                } while (buffer.Length == segmentSize);
                memoryStream = new ArraySegmentStream(bufferCollection);
            }

            return memoryStream;
        }

        private static bool IsExpectedDecodingError(ServiceResultException exception)
        {
            if (exception.StatusCode != StatusCodes.BadDecodingError &&
                exception.StatusCode != StatusCodes.BadEncodingLimitsExceeded)
            {
                return false;
            }

            return exception.InnerException switch
            {
                null or EndOfStreamException or XmlException or JsonException or FormatException => true,
                ServiceResultException inner => IsExpectedDecodingError(inner),
                _ => false
            };
        }

        private sealed class FuzzTelemetryContext : TelemetryContextBase
        {
            public FuzzTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
