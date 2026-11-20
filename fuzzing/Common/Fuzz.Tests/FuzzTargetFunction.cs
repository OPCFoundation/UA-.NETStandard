// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Reflection;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// A fuzz target function.
    /// </summary>
    public sealed class FuzzTargetFunction : IFormattable
    {
        public FuzzTargetFunction(MethodInfo methodInfo)
        {
            MethodInfo = methodInfo;
        }

        public MethodInfo MethodInfo { get; }

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            string name = MethodInfo.Name;
            return $"{name}";
        }
    }
}
