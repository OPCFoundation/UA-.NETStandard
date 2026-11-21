// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
