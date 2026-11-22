// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
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
        /// <exception cref="FileNotFoundException">The MCP server output is missing.</exception>
        public static List<Assembly> LoadAssemblies()
        {
            string directory = FindOutputDirectory();
            string serverAssembly = Path.Combine(directory, "Opc.Ua.Mcp.dll");
            if (!File.Exists(serverAssembly))
            {
                throw new FileNotFoundException(
                    $"Expected MCP server assembly at '{serverAssembly}'.", serverAssembly);
            }

            var assemblies = new List<Assembly> { Assembly.LoadFrom(serverAssembly) };
            foreach (string path in Directory.EnumerateFiles(directory, "Opc.Ua.Mcp*.dll"))
            {
                if (string.Equals(path, serverAssembly, StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    assemblies.Add(Assembly.LoadFrom(path));
                }
                catch (BadImageFormatException)
                {
                }
            }

            return assemblies;
        }

        /// <summary>
        /// Selects MCP assemblies from the same artifacts pivot or conventional configuration and target.
        /// </summary>
        private static string FindOutputDirectory()
        {
            var currentOutput = new DirectoryInfo(AppContext.BaseDirectory);
            if (currentOutput.Parent is { Parent: { Name: "bin" } outputBin } projectOutput &&
                projectOutput.Name == typeof(McpAssemblyProbe).Assembly.GetName().Name)
            {
                return Path.Combine(outputBin.FullName, "Opc.Ua.Mcp", currentOutput.Name);
            }

            string repoRoot = FindRepositoryRoot();
            string configuration = GetBuildConfiguration();
            return Path.Combine(
                repoRoot, "tools", "Opc.Ua.Mcp", "bin", configuration,
                FormattableString.Invariant($"net{Environment.Version.Major}.0"));
        }

        /// <summary>
        /// Resolves an explicitly configured checkout before searching assembly ancestors.
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">The configured checkout has no solution.</exception>
        private static string FindRepositoryRoot()
        {
            string? configured = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string root = Path.GetFullPath(configured);
                if (!File.Exists(Path.Combine(root, "UA.slnx")))
                {
                    throw new DirectoryNotFoundException("The configured repository root has no UA.slnx.");
                }
                return root;
            }
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

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
