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
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// The protocol diagnostics tools have to be usable from a host that never
    /// references the <c>opcua-mcp</c> executable, and the tools that disclose
    /// channel keys have to stay off unless that host opts in.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class OpcUaMcpDiagnosticsEmbeddingTests
    {
        [Test]
        public void AHostCanComposeDiagnosticsToolsWithCoreTools()
        {
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaCoreTools(McpToolProfile.Core)
                .WithOpcUaDiagnosticsTools(McpToolProfile.Diagnostics);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("Connect"));
            Assert.That(tools, Does.Contain("start_capture"));
        }

        /// <summary>
        /// The key-disclosing tools must not appear unless the host asks for
        /// them, because they hand symmetric channel keys to the MCP client.
        /// </summary>
        [TestCase(McpToolProfile.Diagnostics)]
        [TestCase(McpToolProfile.Full)]
        public void KeyDisclosingToolsAreAbsentUnlessOptedIn(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer().WithOpcUaDiagnosticsTools(profile);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("start_capture"),
                "The capture tool is not key-disclosing and stays available.");
            Assert.That(tools, Does.Not.Contain("dump_keys"));
            Assert.That(tools, Does.Not.Contain("replay_pcap"));
        }

        [TestCase(McpToolProfile.Diagnostics)]
        [TestCase(McpToolProfile.Full)]
        public void KeyDisclosingToolsArePresentWhenOptedIn(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaDiagnosticsTools(profile, diagnosticsToolsEnabled: true);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("dump_keys"));
            Assert.That(tools, Does.Contain("replay_pcap"));
        }

        /// <summary>
        /// A profile that does not select diagnostics must contribute nothing
        /// rather than throwing, so a host can pass one profile to every OPC UA
        /// tool package it references.
        /// </summary>
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Services)]
        [TestCase(McpToolProfile.Administration)]
        [TestCase(McpToolProfile.PubSub)]
        [TestCase(McpToolProfile.Robotics)]
        public void ProfilesThatDoNotSelectDiagnosticsContributeNoTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaDiagnosticsTools(profile, diagnosticsToolsEnabled: true);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Is.Empty);
        }

        /// <summary>
        /// The service registration has to stand on its own, because an
        /// embedding host calls it instead of the executable's host builder.
        /// </summary>
        [Test]
        public void AddOpcUaMcpDiagnosticsRegistersCaptureServicesAndDefaultsToDisabled()
        {
            var services = new ServiceCollection();

            services.AddOpcUaMcpDiagnostics();

            using ServiceProvider provider = services.BuildServiceProvider();
            var options = provider.GetService<PcapOptions>();

            Assert.That(options, Is.Not.Null);
            Assert.That(options!.EnableDiagnosticsTools, Is.False,
                "The key-disclosing gate must default to disabled.");
        }

        [Test]
        public void AddOpcUaMcpDiagnosticsAppliesTheConfigureCallback()
        {
            var services = new ServiceCollection();

            services.AddOpcUaMcpDiagnostics(options => options.MaxActiveSessions = 7);

            using ServiceProvider provider = services.BuildServiceProvider();
            var options = provider.GetService<PcapOptions>();

            Assert.That(options!.MaxActiveSessions, Is.EqualTo(7));
        }

        [Test]
        public void ExtensionsRejectNullArguments()
        {
            Assert.That(
                () => ((IServiceCollection)null!).AddOpcUaMcpDiagnostics(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaDiagnosticsTools(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithOpcUaDiagnosticsToolsRejectsAnUnknownProfile()
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();

            ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.WithOpcUaDiagnosticsTools((McpToolProfile)int.MaxValue));

            Assert.That(exception!.ParamName, Is.EqualTo("toolProfile"));
        }

        private static HashSet<string> ResolveToolNames(IServiceCollection services)
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
        }
    }
}
#endif
