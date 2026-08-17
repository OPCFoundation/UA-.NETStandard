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
    /// The composition capability the library split exists to provide: a host
    /// can select the tools of several bounded profiles at once, and the
    /// packages together register each contributing tool exactly once - the
    /// connection tools most of all, because every session-scoped tool
    /// resolves the session the connection tools open.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpToolProfileCompositionTests
    {
        [Test]
        public void ComposingVisionAndRoboticsProducesTheUnionOfBothCatalogues()
        {
            HashSet<string> vision = GetToolNames(new McpToolProfileSet(McpToolProfile.Vision));
            HashSet<string> robotics = GetToolNames(new McpToolProfileSet(McpToolProfile.Robotics));
            HashSet<string> composed = GetToolNames(new McpToolProfileSet(
                new[] { McpToolProfile.Vision, McpToolProfile.Robotics }));

            Assert.That(composed.IsSupersetOf(vision), Is.True,
                "The composed catalogue must expose every Vision tool.");
            Assert.That(composed.IsSupersetOf(robotics), Is.True,
                "The composed catalogue must expose every Robotics tool.");

            HashSet<string> unionByHand = new(vision, StringComparer.Ordinal);
            unionByHand.UnionWith(robotics);
            Assert.That(composed, Is.EquivalentTo(unionByHand));
        }

        [Test]
        public void ComposingVisionAndRoboticsIsDecisivelySmallerThanFull()
        {
            HashSet<string> composed = GetToolNames(new McpToolProfileSet(
                new[] { McpToolProfile.Vision, McpToolProfile.Robotics }));
            HashSet<string> full = GetToolNames(new McpToolProfileSet(McpToolProfile.Full));

            Assert.That(composed, Has.Count.LessThan(full.Count),
                "Composing bounded profiles must produce a smaller catalogue than Full.");
            // 26 (vision) + 40 (robotics) - 4 (shared ConnectionTools) = 62.
            Assert.That(composed, Has.Count.EqualTo(62));
        }

        /// <summary>
        /// The specific failure mode the composition primitive exists to
        /// prevent. Vision and Robotics both need <c>ConnectionTools</c>
        /// because every one of their tools resolves a named OPC UA session,
        /// but composing them on the same MCP server must yield one set of
        /// connection tools, not two - and a name-based
        /// <see cref="HashSet{T}"/> would hide a duplicate registration
        /// because the second registration produces the same tool name.
        /// This test counts registrations rather than names so a duplicate
        /// cannot slip through.
        /// </summary>
        [Test]
        public void ComposingSessionScopedProfilesRegistersConnectionToolsExactlyOnce()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaCoreTools(new McpToolProfileSet(
                new[] { McpToolProfile.Vision, McpToolProfile.Robotics }));

            using ServiceProvider provider = services.BuildServiceProvider();
            List<McpServerTool> tools = provider.GetServices<McpServerTool>().ToList();

            Assert.That(
                tools.Count(t => string.Equals(t.ProtocolTool.Name, "Connect", StringComparison.Ordinal)),
                Is.EqualTo(1),
                "Connect must be registered exactly once when Vision and Robotics are composed.");
            Assert.That(
                tools.Count(t => string.Equals(t.ProtocolTool.Name, "Disconnect", StringComparison.Ordinal)),
                Is.EqualTo(1),
                "Disconnect must be registered exactly once when Vision and Robotics are composed.");
            Assert.That(
                tools.Count(t => string.Equals(t.ProtocolTool.Name, "GetEndpoints", StringComparison.Ordinal)),
                Is.EqualTo(1),
                "GetEndpoints must be registered exactly once when Vision and Robotics are composed.");
            Assert.That(
                tools.Count(t => string.Equals(t.ProtocolTool.Name, "GetConnectionStatus", StringComparison.Ordinal)),
                Is.EqualTo(1),
                "GetConnectionStatus must be registered exactly once when Vision and Robotics are composed.");
        }

        /// <summary>
        /// The dedupe has to hold whichever package's set-overload runs
        /// first, so a host is free to call them in the order it prefers.
        /// </summary>
        [Test]
        public void ComposedSetPathRegistersConnectionToolsOnceAcrossPackages()
        {
            var toolProfiles = new McpToolProfileSet(
                new[] { McpToolProfile.Vision, McpToolProfile.Robotics });
            var services = new ServiceCollection();

            services.AddMcpServer()
                .WithOpcUaCoreTools(toolProfiles)
                .WithOpcUaVisionTools(toolProfiles)
                .WithOpcUaRoboticsTools(toolProfiles);

            using ServiceProvider provider = services.BuildServiceProvider();
            List<McpServerTool> tools = provider.GetServices<McpServerTool>().ToList();

            int connectCount = tools.Count(t =>
                string.Equals(t.ProtocolTool.Name, "Connect", StringComparison.Ordinal));
            Assert.That(connectCount, Is.EqualTo(1),
                "Composed set-overloads must not race on the ConnectionTools registration.");
        }

        [Test]
        public void WithOpcUaConnectionToolsIsIdempotentAcrossCalls()
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            builder.WithOpcUaConnectionTools();
            builder.WithOpcUaConnectionTools();
            builder.WithOpcUaConnectionTools();

            using ServiceProvider provider = services.BuildServiceProvider();
            int connectCount = provider.GetServices<McpServerTool>()
                .Count(t => string.Equals(t.ProtocolTool.Name, "Connect", StringComparison.Ordinal));
            Assert.That(connectCount, Is.EqualTo(1));
        }

        [Test]
        public void SingleProfileSetPreservesLegacyBehaviour()
        {
            HashSet<string> viaLegacy = GetToolNamesLegacy(McpToolProfile.Vision);
            HashSet<string> viaSet = GetToolNames(new McpToolProfileSet(McpToolProfile.Vision));

            Assert.That(viaSet, Is.EquivalentTo(viaLegacy),
                "A one-profile set must reproduce the legacy single-profile catalogue.");
        }

        [Test]
        public void ComposingWithFullYieldsTheSameCatalogueAsFullAlone()
        {
            HashSet<string> viaFull = GetToolNames(new McpToolProfileSet(McpToolProfile.Full));
            HashSet<string> viaFullPlus = GetToolNames(new McpToolProfileSet(
                new[] { McpToolProfile.Full, McpToolProfile.Vision, McpToolProfile.Robotics }));

            Assert.That(viaFullPlus, Is.EquivalentTo(viaFull));
        }

        [Test]
        public void ComposingWithPubSubOnlyDoesNotRegisterConnectionTools()
        {
            HashSet<string> pubSub = GetToolNames(new McpToolProfileSet(McpToolProfile.PubSub));

            Assert.That(pubSub, Does.Not.Contain("Connect"));
        }

        [Test]
        public void ComposingSetOverloadsRejectNullArguments()
        {
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaCoreTools(
                    new McpToolProfileSet(McpToolProfile.Core)),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaVisionTools(
                    new McpToolProfileSet(McpToolProfile.Vision)),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaRoboticsTools(
                    new McpToolProfileSet(McpToolProfile.Robotics)),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaPubSubTools(
                    new McpToolProfileSet(McpToolProfile.PubSub)),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaDiagnosticsTools(
                    new McpToolProfileSet(McpToolProfile.Diagnostics),
                    diagnosticsToolsEnabled: false),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaPubSubDiagnosticsTools(
                    new McpToolProfileSet(McpToolProfile.PubSub),
                    diagnosticsToolsEnabled: false),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IMcpServerBuilder)null!).WithOpcUaConnectionTools(),
                Throws.ArgumentNullException);
        }

        private static HashSet<string> GetToolNames(McpToolProfileSet toolProfiles)
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(builder, toolProfiles, diagnosticsToolsEnabled: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static HashSet<string> GetToolNamesLegacy(McpToolProfile toolProfile)
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(builder, toolProfile, diagnosticsToolsEnabled: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
        }
    }
}
#endif
