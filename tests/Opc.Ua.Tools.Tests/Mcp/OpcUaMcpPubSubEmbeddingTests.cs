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

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// The PubSub tools have to be usable from a host that never references the
    /// <c>opcua-mcp</c> executable.
    /// </summary>
    /// <remarks>
    /// Only the public extension methods on <c>Opc.Ua.Mcp.PubSub</c> are used,
    /// so these tests fail if that surface stops being sufficient on its own.
    /// </remarks>
    [TestFixture]
    [Category("Mcp")]
    public sealed class OpcUaMcpPubSubEmbeddingTests
    {
        [Test]
        public void AHostCanComposePubSubToolsWithCoreTools()
        {
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaCoreTools(McpToolProfile.Core)
                .WithOpcUaPubSubTools(McpToolProfile.PubSub);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("Connect"));
            Assert.That(tools, Does.Contain("pubsub_runtime_start_publisher"));
            Assert.That(tools, Does.Contain("pubsub_discover_metadata"));
            Assert.That(tools, Does.Contain("pubsub_invoke_action"));
        }

        /// <summary>
        /// A profile that does not select PubSub must contribute nothing rather
        /// than throwing, so a host can pass one profile to every OPC UA tool
        /// package it references.
        /// </summary>
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Services)]
        [TestCase(McpToolProfile.Administration)]
        [TestCase(McpToolProfile.Diagnostics)]
        [TestCase(McpToolProfile.Robotics)]
        public void ProfilesThatDoNotSelectPubSubContributeNoTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer().WithOpcUaPubSubTools(profile);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Is.Empty);
        }

        [TestCase(McpToolProfile.PubSub)]
        [TestCase(McpToolProfile.Full)]
        public void ProfilesThatSelectPubSubContributeTheRuntimeTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();

            services.AddMcpServer().WithOpcUaPubSubTools(profile);

            HashSet<string> tools = ResolveToolNames(services);

            Assert.That(tools, Does.Contain("pubsub_runtime_status"));
            Assert.That(tools, Does.Contain("pubsub_discover_writer_config"));
            Assert.That(tools, Does.Contain("pubsub_list_action_targets"));
        }

        /// <summary>
        /// The service registration has to stand on its own, because an
        /// embedding host calls it instead of the executable's host builder.
        /// </summary>
        [Test]
        public void AddOpcUaMcpPubSubRegistersTheRuntimeManager()
        {
            var services = new ServiceCollection();

            services.AddOpcUaMcpCore().AddOpcUaMcpPubSub();

            Assert.That(
                services.Any(d => d.ServiceType == typeof(PubSubRuntimeManager)),
                Is.True,
                "The runtime manager the PubSub tools resolve must be registered.");
        }

        [Test]
        public void ExtensionsRejectNullArguments()
        {
            Assert.That(
                () => ((IServiceCollection)null!).AddOpcUaMcpPubSub(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaPubSubTools(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithOpcUaPubSubToolsRejectsAnUnknownProfile()
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();

            ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.WithOpcUaPubSubTools((McpToolProfile)int.MaxValue));

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
