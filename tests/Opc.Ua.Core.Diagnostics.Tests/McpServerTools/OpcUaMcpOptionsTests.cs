// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// Precedence tests for <c>OpcUaMcpOptions</c>: DI registration
    /// wins over the per-tool environment variable, which wins over
    /// the per-tool default. Validates both <c>NodeSetExportRoot</c>
    /// and <c>PcapBaseFolder</c> entry points.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaMcpOptionsTests
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

            // Build a provider that has no OpcUaMcpOptions registered.
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
            object mcpOptions = CreateOpcUaMcpOptions(
                nodeSetExportRoot: null,
                pcapBaseFolder: diRoot);
            services.AddSingleton(GetOpcUaMcpOptionsType(), mcpOptions);
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
        public void OpcUaMcpOptionsHasExpectedProperties()
        {
            Type type = GetOpcUaMcpOptionsType();

            Assert.That(type.GetProperty("NodeSetExportRoot"), Is.Not.Null,
                "OpcUaMcpOptions.NodeSetExportRoot must be declared.");
            Assert.That(type.GetProperty("PcapBaseFolder"), Is.Not.Null,
                "OpcUaMcpOptions.PcapBaseFolder must be declared.");
        }

        private static ServiceProvider BuildProviderWithMcpOptions(
            string? nodeSetExportRoot,
            string? pcapBaseFolder)
        {
            object mcpOptions = CreateOpcUaMcpOptions(nodeSetExportRoot, pcapBaseFolder);
            var services = new ServiceCollection();
            services.AddSingleton(GetOpcUaMcpOptionsType(), mcpOptions);
            return services.BuildServiceProvider();
        }

        private static object CreateOpcUaMcpOptions(
            string? nodeSetExportRoot,
            string? pcapBaseFolder)
        {
            Type type = GetOpcUaMcpOptionsType();
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

        private static Type GetOpcUaMcpOptionsType()
        {
            Type? type = McpAssemblyProbe.ResolveType("Opc.Ua.Mcp.OpcUaMcpOptions");
            Assert.That(type, Is.Not.Null,
                "Opc.Ua.Mcp.OpcUaMcpOptions type must be present in an MCP assembly.");
            return type!;
        }

        private Type GetRequiredMcpType(string fullName)
        {
            m_mcpTypes ??= [];
            if (m_mcpTypes.TryGetValue(fullName, out Type? cached))
            {
                return cached;
            }

            Type type = McpAssemblyProbe.GetRequiredType(fullName);
            m_mcpTypes[fullName] = type;
            return type;
        }
    }
}
#endif
