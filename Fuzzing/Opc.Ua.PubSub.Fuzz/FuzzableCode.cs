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
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Shared helpers for PubSub fuzz targets.
    /// </summary>
    public static partial class FuzzableCode
    {
        private const int MaxFuzzInputBytes = 1024 * 1024;

        /// <summary>
        /// Prints information about the fuzzer target.
        /// </summary>
        public static void FuzzInfo()
        {
            Console.WriteLine("OPC UA PubSub fuzzer for UADP, UADP chunk reassembly and JSON decode.");
        }

        internal static PubSubNetworkMessageContext NewContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        internal static byte[] CopyCapped(ReadOnlySpan<byte> input)
        {
            return input.Length <= MaxFuzzInputBytes
                ? input.ToArray()
                : input[..MaxFuzzInputBytes].ToArray();
        }

        internal static byte[] ReadCapped(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            byte[] buffer = new byte[4096];
            int remaining = MaxFuzzInputBytes;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    break;
                }

                memoryStream.Write(buffer, 0, read);
                remaining -= read;
            }

            return memoryStream.ToArray();
        }

        internal static bool IsExpected(Exception ex)
        {
            if (ex is ServiceResultException or InvalidOperationException or ArgumentException or
                FormatException or IOException or OverflowException)
            {
                return true;
            }

            return false;
        }
    }
}
