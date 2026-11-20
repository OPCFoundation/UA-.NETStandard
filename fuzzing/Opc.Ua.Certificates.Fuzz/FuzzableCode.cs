/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        /// <summary>
        /// Print information about the fuzzer target.
        /// </summary>
        public static void FuzzInfo()
        {
            Console.WriteLine("OPC UA certificate, CRL, PEM and ASN.1 fuzzer for afl-fuzz and libFuzzer.");
            Console.WriteLine("Targets include certificate loading, both chain parsers and canonical CRL encoding.");
        }

        /// <summary>
        /// Read all fuzzer input bytes from a stream.
        /// </summary>
        private static byte[] ReadAllBytes(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
    }
}
