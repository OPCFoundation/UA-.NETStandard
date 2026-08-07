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

// Opc.Ua.Mcp targets net10.0 only and is loaded reflectively from its net10.0
// build output, so these tests only build and run on net10.0.
#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Pcap.Tests.McpServerTools
{
    /// <summary>
    /// Precedence tests for <c>McpServerOptions</c>: DI registration
    /// wins over the per-tool environment variable, which wins over
    /// the per-tool default. Validates both <c>NodeSetExportRoot</c>
    /// and <c>PcapBaseFolder</c> entry points.
    /// </summary>
    [TestFixture]
    public sealed class McpServerOptionsTests
    {
        private const string c_nodeSetEnvVar = "OPCUA_MCP_EXPORT_ROOT";

        private string? m_priorNodeSetEnv;
        private Dictionary<string, Type>? m_mcpTypes;

        /// <summary>
        /// Captures and clears the NodeSet env var so it cannot
        /// leak into tests that exercise fallback behaviour.
        /// </summary>
        [SetUp]
        public void Setup()
        {
            m_priorNodeSetEnv = Environment.GetEnvironmentVariable(c_nodeSetEnvVar);
            Environment.SetEnvironmentVariable(c_nodeSetEnvVar, null);
        }

        /// <summary>
        /// Restores the NodeSet env var to its pre-test value.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(c_nodeSetEnvVar, m_priorNodeSetEnv);
        }

        [Test]
        public void NodeSetExportRootFromDIWinsOverEnvVarAndDefault()
        {
            string diRoot = Path.GetFullPath(Path.Combine("test-artifacts", "via-di"));
            Environment.SetEnvironmentVariable(c_nodeSetEnvVar, "/tmp/via-env");

            using ServiceProvider sp = BuildProviderWithMcpOptions(
                nodeSetExportRoot: diRoot,
                pcapBaseFolder: null);

            string resolved = InvokeResolveExportRoot(sp);

            Assert.That(resolved, Is.EqualTo(diRoot));
        }

        [Test]
        public void NodeSetExportRootFallsBackToEnvVarWhenDINotConfigured()
        {
            string envRoot = Path.GetFullPath(Path.Combine("test-artifacts", "via-env"));
            Environment.SetEnvironmentVariable(c_nodeSetEnvVar, envRoot);

            // Build a provider that has no McpServerOptions registered.
            using ServiceProvider sp = new ServiceCollection().BuildServiceProvider();

            string resolved = InvokeResolveExportRoot(sp);

            Assert.That(resolved, Is.EqualTo(envRoot));
        }

        [Test]
        public void NodeSetExportRootFallsBackToDefaultWhenNeitherSet()
        {
            using ServiceProvider sp = new ServiceCollection().BuildServiceProvider();

            string resolved = InvokeResolveExportRoot(sp);

            // Default is under %TEMP%\Opc.Ua.Mcp\exports — assert the
            // shape rather than an exact value so the test works on
            // all platforms.
            Assert.That(resolved, Does.Contain("Opc.Ua.Mcp"));
            Assert.That(resolved, Does.Contain("exports"));
            Assert.That(Path.IsPathRooted(resolved), Is.True);
        }

        [Test]
        public void PcapBaseFolderFromDIWinsOverPcapOptions()
        {
            string diRoot = Path.GetFullPath(Path.Combine("test-artifacts", "via-mcp-options"));
            string pcapRoot = Path.GetFullPath(Path.Combine("test-artifacts", "via-pcap-options"));

            var services = new ServiceCollection();
            services.AddSingleton(new PcapOptions { BaseFolder = pcapRoot });
            object mcpOptions = CreateMcpServerOptions(
                nodeSetExportRoot: null,
                pcapBaseFolder: diRoot);
            services.AddSingleton(GetMcpServerOptionsType(), mcpOptions);
            using ServiceProvider sp = services.BuildServiceProvider();

            string resolved = InvokeGetDecodeAllowedRoot(sp);

            Assert.That(resolved, Is.EqualTo(diRoot));
        }

        [Test]
        public void PcapBaseFolderFallsBackToPcapOptionsWhenDINotConfigured()
        {
            string pcapRoot = Path.GetFullPath(Path.Combine("test-artifacts", "via-pcap-options"));

            var services = new ServiceCollection();
            services.AddSingleton(new PcapOptions { BaseFolder = pcapRoot });
            using ServiceProvider sp = services.BuildServiceProvider();

            string resolved = InvokeGetDecodeAllowedRoot(sp);

            Assert.That(resolved, Is.EqualTo(pcapRoot));
        }

        [Test]
        public void PcapBaseFolderFallsBackToDefaultWhenNeitherSet()
        {
            using ServiceProvider sp = new ServiceCollection().BuildServiceProvider();

            string resolved = InvokeGetDecodeAllowedRoot(sp);

            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OPCFoundation",
                "opcua-pcap");
            Assert.That(resolved, Is.EqualTo(expected));
        }

        [Test]
        public void McpServerOptionsHasExpectedProperties()
        {
            Type type = GetMcpServerOptionsType();

            Assert.That(type.GetProperty("NodeSetExportRoot"), Is.Not.Null,
                "McpServerOptions.NodeSetExportRoot must be declared.");
            Assert.That(type.GetProperty("PcapBaseFolder"), Is.Not.Null,
                "McpServerOptions.PcapBaseFolder must be declared.");
        }

        private static ServiceProvider BuildProviderWithMcpOptions(
            string? nodeSetExportRoot,
            string? pcapBaseFolder)
        {
            object mcpOptions = CreateMcpServerOptions(nodeSetExportRoot, pcapBaseFolder);
            var services = new ServiceCollection();
            services.AddSingleton(GetMcpServerOptionsType(), mcpOptions);
            return services.BuildServiceProvider();
        }

        private static object CreateMcpServerOptions(
            string? nodeSetExportRoot,
            string? pcapBaseFolder)
        {
            Type type = GetMcpServerOptionsType();
            object instance = Activator.CreateInstance(type)!;
            type.GetProperty("NodeSetExportRoot")!.SetValue(instance, nodeSetExportRoot);
            type.GetProperty("PcapBaseFolder")!.SetValue(instance, pcapBaseFolder);
            return instance;
        }

        private string InvokeResolveExportRoot(IServiceProvider services)
        {
            Type toolsType = GetRequiredMcpType("Opc.Ua.Mcp.Tools.NodeSetExportTools");
            MethodInfo method = toolsType.GetMethod(
                "ResolveExportRoot",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ResolveExportRoot not found.");
            return (string)method.Invoke(null, [services])!;
        }

        private string InvokeGetDecodeAllowedRoot(IServiceProvider services)
        {
            Type toolsType = GetRequiredMcpType("Opc.Ua.Mcp.Tools.PacketDecodeTools");
            MethodInfo method = toolsType.GetMethod(
                "GetDecodeAllowedRoot",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GetDecodeAllowedRoot not found.");
            return (string)method.Invoke(null, [services])!;
        }

        private static Type GetMcpServerOptionsType()
        {
            Type? type = ResolveMcpType("Opc.Ua.Mcp.McpServerOptions");
            Assert.That(type, Is.Not.Null,
                "Opc.Ua.Mcp.McpServerOptions type must be present in an MCP assembly.");
            return type!;
        }

        /// <summary>
        /// Finds a type by full name across the MCP assemblies in the server's
        /// output folder.
        /// </summary>
        /// <remarks>
        /// The MCP tools are split across several assemblies - the executable
        /// plus the library packages it composes - and which assembly owns a
        /// given type is a packaging decision this test does not want to encode.
        /// Probing every <c>Opc.Ua.Mcp*.dll</c> beside the server keeps the test
        /// about behaviour rather than about the current split.
        /// </remarks>
        private static Type? ResolveMcpType(string fullName)
        {
            foreach (Assembly assembly in LoadMcpAssemblies())
            {
                Type? type = assembly.GetType(fullName);
                if (type is not null)
                {
                    return type;
                }
            }
            return null;
        }

        private Type GetRequiredMcpType(string fullName)
        {
            m_mcpTypes ??= [];
            if (m_mcpTypes.TryGetValue(fullName, out Type? cached))
            {
                return cached;
            }
            Type type = ResolveMcpType(fullName)
                ?? throw new InvalidOperationException($"{fullName} type not found.");
            m_mcpTypes[fullName] = type;
            return type;
        }

        private static List<Assembly> LoadMcpAssemblies()
        {
            string directory = FindMcpOutputDirectory();
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

        private static string FindMcpOutputDirectory()
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
