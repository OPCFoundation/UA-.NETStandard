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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Pcap.Tests.McpServerTools
{
    /// <summary>
    /// Locates types in the MCP server's output folder for tests that reach it
    /// reflectively rather than by project reference.
    /// </summary>
    /// <remarks>
    /// The MCP tools are split across several assemblies - the executable plus
    /// the library packages it composes - and which assembly owns a given type
    /// is a packaging decision these tests do not want to encode. Probing every
    /// <c>Opc.Ua.Mcp*.dll</c> beside the server keeps the tests about behaviour
    /// rather than about the current split.
    /// </remarks>
    internal static class McpAssemblyProbe
    {
        /// <summary>
        /// Finds a type by full name across the MCP assemblies, or returns
        /// <c>null</c> when no assembly declares it.
        /// </summary>
        public static Type? ResolveType(string fullName)
        {
            foreach (Assembly assembly in LoadAssemblies())
            {
                Type? type = assembly.GetType(fullName);
                if (type is not null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a type by full name across the MCP assemblies, failing the
        /// test when no assembly declares it.
        /// </summary>
        public static Type GetRequiredType(string fullName)
        {
            return ResolveType(fullName)
                ?? throw new InvalidOperationException($"{fullName} type not found.");
        }

        /// <summary>
        /// Loads every MCP assembly in the server's output folder.
        /// </summary>
        public static List<Assembly> LoadAssemblies()
        {
            string directory = FindOutputDirectory();
            var assemblies = new List<Assembly>();
            foreach (string path in Directory.EnumerateFiles(directory, "Opc.Ua.Mcp*.dll"))
            {
                try
                {
                    assemblies.Add(Assembly.LoadFrom(path));
                }
                catch (BadImageFormatException)
                {
                }
            }

            if (assemblies.Count == 0)
            {
                Assert.Ignore(
                    "The net10.0 MCP assemblies are not built for this CI leg " +
                    "(the MCP server only targets net10.0); skipping the reflective MCP server test.");
            }

            return assemblies;
        }

        private static string FindOutputDirectory()
        {
            string repoRoot = FindRepositoryRoot();
            string configuration = GetBuildConfiguration();
            string directory = Path.Combine(
                repoRoot, "tools", "Opc.Ua.Mcp", "bin", configuration, "net10.0");

            if (Directory.Exists(directory))
            {
                return directory;
            }

            string binPath = Path.Combine(repoRoot, "tools", "Opc.Ua.Mcp", "bin");
            string? found = Directory.Exists(binPath)
                ? Directory.EnumerateFiles(binPath, "Opc.Ua.Mcp.dll", SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName)
                    .FirstOrDefault(d => d is not null)
                : null;

            if (found is null)
            {
                Assert.Ignore(
                    "The net10.0 MCP assemblies are not built for this CI leg " +
                    "(the MCP server only targets net10.0); skipping the reflective MCP server test.");
            }

            return found!;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Unable to locate repository root.");
            throw new InvalidOperationException("Unable to locate repository root.");
        }

        private static string GetBuildConfiguration()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase))
                {
                    return directory.Name;
                }

                directory = directory.Parent;
            }

            return "Debug";
        }
    }
}
#endif
