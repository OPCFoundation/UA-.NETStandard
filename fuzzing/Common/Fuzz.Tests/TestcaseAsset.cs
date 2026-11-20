// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Tests;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// A testcase as test asset.
    /// </summary>
    public sealed class TestcaseAsset : IAsset, IFormattable
    {
        public string Path { get; private set; } = null!;

        public byte[] Testcase { get; private set; } = null!;

        public void Initialize(byte[] blob, string path)
        {
            Path = path;
            Testcase = blob;
        }

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            string file = System.IO.Path.GetFileName(Path);
            return $"{file}";
        }
    }
}
