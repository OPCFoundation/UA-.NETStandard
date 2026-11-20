/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Keeps test certificate stores in an explicitly selected artifact root, short enough for .NET Framework paths.
    /// </summary>
    internal static class CraneTestPaths
    {
        /// <summary>
        /// Creates a unique test-owned PKI path without using the operating system's temporary directory.
        /// </summary>
        internal static string NewPkiPath()
        {
            string root = Environment.GetEnvironmentVariable("OPCUA_CRANE_TEST_PKI") ??
                Path.Combine(TestContext.CurrentContext.TestDirectory, "pki");
            return Path.Combine(root, Guid.NewGuid().ToString("N"));
        }
    }
}
