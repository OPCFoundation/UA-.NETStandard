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

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Normalizes Modbus scalar data types and returns their encoded register width.
    /// </summary>
    internal static class ModbusDataTypes
    {
        public static int RegisterCount(string? type)
        {
            return Normalize(type) switch
            {
                "int16" or "uint16" => 1,
                "int32" or "uint32" or "float32" => 2,
                "int64" or "uint64" or "float64" => 4,
                _ => 1
            };
        }

        public static string Normalize(string? type)
        {
            switch ((type ?? "uint16").Trim().ToLowerInvariant())
            {
                case "short":
                case "int16":
                    return "int16";
                case "ushort":
                case "uint16":
                case "word":
                    return "uint16";
                case "int":
                case "int32":
                    return "int32";
                case "uint":
                case "uint32":
                case "dword":
                    return "uint32";
                case "float":
                case "float32":
                case "single":
                    return "float32";
                case "long":
                case "int64":
                    return "int64";
                case "ulong":
                case "uint64":
                    return "uint64";
                case "double":
                case "float64":
                    return "float64";
                default:
                    return "uint16";
            }
        }
    }
}
