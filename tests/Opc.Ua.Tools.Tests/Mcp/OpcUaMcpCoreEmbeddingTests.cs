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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// The capability the library split exists to provide: an application can
    /// take the OPC UA tools it wants and add tools of its own to the same MCP
    /// server.
    /// </summary>
    /// <remarks>
    /// These tests use only what a third-party host can reach - the public
    /// extension methods on <c>Opc.Ua.Mcp.Core</c> - rather than the executable's
    /// internal host builder, so they fail if that public surface stops being
    /// sufficient on its own.
    /// </remarks>
    [TestFixture]
    [Category("Mcp")]
    public sealed class OpcUaMcpCoreEmbeddingTests
    {
        /// <summary>
        /// A host registers the OPC UA tools and its own tool type, and gets
        /// both.
        /// </summary>
        [Test]
        public void AHostCanComposeOpcUaToolsWithItsOwnTools()
        {
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaMcpFilters()
                .WithOpcUaCoreTools(McpToolProfile.Core)
                .WithTools<ApplicationSpecialtyTools>();

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("Connect"),
                "The OPC UA tools must be present.");
            Assert.That(tools, Does.Contain("application_specialty"),
                "The host's own tools must be present alongside them.");
        }

        /// <summary>
        /// Registering only the host's own tools must not implicitly pull in
        /// the OPC UA tools, or composition would not be a choice.
        /// </summary>
        [Test]
        public void AHostThatRegistersOnlyItsOwnToolsGetsOnlyThose()
        {
            var services = new ServiceCollection();

            services.AddMcpServer().WithTools<ApplicationSpecialtyTools>();

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("application_specialty"));
            Assert.That(tools, Does.Not.Contain("Connect"));
        }

        /// <summary>
        /// A profile naming tools this package does not own contributes
        /// nothing rather than throwing, so a host can pass one profile to
        /// every OPC UA tool package it references.
        /// </summary>
        [TestCase(McpToolProfile.PubSub)]
        [TestCase(McpToolProfile.Diagnostics)]
        [TestCase(McpToolProfile.Robotics)]
        public void ProfilesOwnedByOtherPackagesContributeNoCoreTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer().WithOpcUaCoreTools(profile);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Not.Contain("Connect"));
            Assert.That(tools, Does.Not.Contain("Browse"));
        }

        /// <summary>
        /// The service registration has to stand on its own, because an
        /// embedding host calls it instead of the executable's host builder.
        /// </summary>
        [Test]
        public void AddOpcUaMcpCoreRegistersTheSessionManagerAndOptions()
        {
            var services = new ServiceCollection();

            services.AddOpcUaMcpCore(options => options.NodeSetExportRoot = "/tmp/exports");

            using ServiceProvider provider = services.BuildServiceProvider();
            var options = provider.GetService<OpcUaMcpOptions>();

            Assert.That(options, Is.Not.Null);
            Assert.That(options!.NodeSetExportRoot, Is.EqualTo("/tmp/exports"));
            Assert.That(
                services.Any(d => d.ServiceType == typeof(OpcUaSessionManager)),
                Is.True,
                "The session manager the tools resolve must be registered.");
        }

        [Test]
        public void ExtensionsRejectNullArguments()
        {
            Assert.That(
                () => ((IServiceCollection)null!).AddOpcUaMcpCore(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaCoreTools(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaMcpFilters(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithOpcUaCoreToolsRejectsAnUnknownProfile()
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();

            ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.WithOpcUaCoreTools((McpToolProfile)int.MaxValue));

            Assert.That(exception!.ParamName, Is.EqualTo("toolProfile"));
        }

        /// <summary>
        /// The composition mechanism the library split exists to enable: an
        /// embedding host declares the profiles it wants and gets a single
        /// composed catalogue - one <c>Connect</c>, one <c>Disconnect</c> -
        /// using only the public extension methods.
        /// </summary>
        [Test]
        public void AHostCanComposeSeveralBoundedProfilesInOneServer()
        {
            var toolProfiles = new McpToolProfileSet(
                new[] { McpToolProfile.Vision, McpToolProfile.Robotics });
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaMcpFilters()
                .WithOpcUaCoreTools(toolProfiles)
                .WithOpcUaVisionTools(toolProfiles)
                .WithOpcUaRoboticsTools(toolProfiles)
                .WithTools<ApplicationSpecialtyTools>();

            using ServiceProvider provider = services.BuildServiceProvider();
            List<McpServerTool> tools = provider.GetServices<McpServerTool>().ToList();

            Assert.That(
                tools.Count(t => string.Equals(t.ProtocolTool.Name, "Connect", StringComparison.Ordinal)),
                Is.EqualTo(1),
                "Composed set-overloads must register the connection tools exactly once.");
            HashSet<string> names = tools
                .Select(t => t.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(names, Does.Contain("vision_get_frame"));
            Assert.That(names, Does.Contain("robotics_submit_linear_move"));
            Assert.That(names, Does.Contain("application_specialty"));
        }

        /// <summary>
        /// The idempotent connection-tool helper is what makes it safe for a
        /// host to register connection tools directly and still call any of
        /// the set-overloads that also depend on them.
        /// </summary>
        [Test]
        public void WithOpcUaConnectionToolsRegistersConnectionToolsExactlyOnce()
        {
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaConnectionTools()
                .WithOpcUaConnectionTools();

            using ServiceProvider provider = services.BuildServiceProvider();
            int connectCount = provider
                .GetServices<McpServerTool>()
                .Count(t => string.Equals(t.ProtocolTool.Name, "Connect", StringComparison.Ordinal));

            Assert.That(connectCount, Is.EqualTo(1));
        }

        private static HashSet<string> ResolveToolNames(IServiceCollection services)
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// Stands in for the application-level tools an embedding host adds.
        /// </summary>
        [SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated reflectively by the MCP server when the tool is invoked.")]
        internal sealed class ApplicationSpecialtyTools
        {
            [McpServerTool(Name = "application_specialty")]
            [Description("A tool the embedding application contributes.")]
            public static string Specialty()
            {
                return "ok";
            }
        }
    }
}
#endif
