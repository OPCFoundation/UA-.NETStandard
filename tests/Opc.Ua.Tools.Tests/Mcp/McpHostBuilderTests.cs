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
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class McpHostBuilderTests
    {
        private const string kDiagnosticsVariable = "OPCUA_PCAP_ENABLE_DIAGNOSTICS";
        private const string kExportRootVariable = "OPCUA_MCP_NODESET_EXPORT_ROOT";
        private const string kPcapRootVariable = "OPCUA_MCP_PCAP_BASE_FOLDER";
        private const string kProfileVariable = "OPCUA_MCP_TOOL_PROFILE";

        private string? m_originalDiagnostics;
        private string? m_originalExportRoot;
        private string? m_originalPcapRoot;
        private string? m_originalProfile;

        [SetUp]
        public void SetUp()
        {
            m_originalDiagnostics = Environment.GetEnvironmentVariable(kDiagnosticsVariable);
            m_originalExportRoot = Environment.GetEnvironmentVariable(kExportRootVariable);
            m_originalPcapRoot = Environment.GetEnvironmentVariable(kPcapRootVariable);
            m_originalProfile = Environment.GetEnvironmentVariable(kProfileVariable);
            Environment.SetEnvironmentVariable(kDiagnosticsVariable, null);
            Environment.SetEnvironmentVariable(kExportRootVariable, null);
            Environment.SetEnvironmentVariable(kPcapRootVariable, null);
            Environment.SetEnvironmentVariable(kProfileVariable, null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(kDiagnosticsVariable, m_originalDiagnostics);
            Environment.SetEnvironmentVariable(kExportRootVariable, m_originalExportRoot);
            Environment.SetEnvironmentVariable(kPcapRootVariable, m_originalPcapRoot);
            Environment.SetEnvironmentVariable(kProfileVariable, m_originalProfile);
        }

        [Test]
        public async Task ConfigureServicesRegistersManagersAndOptionsAsSingletonsAsync()
        {
            var services = new ServiceCollection();
            var options = new PcapOptions
            {
                BaseFolder = "mcp-pcap-tests",
                MaxActiveSessions = 7,
                EnableDiagnosticsTools = true
            };

            McpHostBuilder.ConfigureServices(services, options);

            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaSessionManager sessionManager = provider.GetRequiredService<OpcUaSessionManager>();
            PubSubRuntimeManager runtimeManager = provider.GetRequiredService<PubSubRuntimeManager>();
            PcapOptions registeredOptions = provider.GetRequiredService<PcapOptions>();

            Assert.That(
                provider.GetRequiredService<OpcUaSessionManager>(),
                Is.SameAs(sessionManager));
            Assert.That(
                provider.GetRequiredService<PubSubRuntimeManager>(),
                Is.SameAs(runtimeManager));
            Assert.That(registeredOptions.BaseFolder, Is.EqualTo(options.BaseFolder));
            Assert.That(registeredOptions.MaxActiveSessions, Is.EqualTo(7));
            Assert.That(registeredOptions.EnableDiagnosticsTools, Is.True);
        }

        [Test]
        public async Task ConfigureServicesUsesProvidedOpcUaMcpOptionsAsync()
        {
            var services = new ServiceCollection();
            var expected = new Opc.Ua.Mcp.OpcUaMcpOptions
            {
                ToolProfile = McpToolProfile.Core
            };

            McpHostBuilder.ConfigureServices(services, new PcapOptions(), expected);

            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                provider.GetRequiredService<Opc.Ua.Mcp.OpcUaMcpOptions>(),
                Is.SameAs(expected));
        }

        [Test]
        public void CreateOpcUaMcpOptionsReadsEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(kExportRootVariable, "export-root");
            Environment.SetEnvironmentVariable(kPcapRootVariable, "pcap-root");

            Opc.Ua.Mcp.OpcUaMcpOptions options =
                McpHostBuilder.CreateOpcUaMcpOptions();

            Assert.That(options.NodeSetExportRoot, Is.EqualTo("export-root"));
            Assert.That(options.PcapBaseFolder, Is.EqualTo("pcap-root"));
            Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Full));
        }

        [TestCase("core", McpToolProfile.Core)]
        [TestCase("SERVICES", McpToolProfile.Services)]
        [TestCase("administration", McpToolProfile.Administration)]
        [TestCase("pubsub", McpToolProfile.PubSub)]
        [TestCase("diagnostics", McpToolProfile.Diagnostics)]
        [TestCase("robotics", McpToolProfile.Robotics)]
        [TestCase("full", McpToolProfile.Full)]
        public void CreateOpcUaMcpOptionsParsesConfiguredProfile(
            string configuredProfile,
            McpToolProfile expectedProfile)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = configuredProfile
                })
                .Build();

            Opc.Ua.Mcp.OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(
                configuration,
                null);

            Assert.That(options.ToolProfile, Is.EqualTo(expectedProfile));
        }

        [Test]
        public void CreateOpcUaMcpOptionsCliOverrideWinsOverConfiguration()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = "services"
                })
                .Build();

            Opc.Ua.Mcp.OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(
                configuration,
                "core");

            Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Core));
        }

        [Test]
        public void CreateOpcUaMcpOptionsReturnsDefaultsWhenProfileIsUnconfigured()
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();

            Opc.Ua.Mcp.OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(
                configuration,
                null);

            Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Full));
        }

        [Test]
        public void CreateOpcUaMcpOptionsReadsProfileFromEnvironment()
        {
            Environment.SetEnvironmentVariable(kProfileVariable, "pubsub");
            IConfiguration configuration = new ConfigurationBuilder().Build();

            Opc.Ua.Mcp.OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(
                configuration,
                null);

            Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.PubSub));
        }

        [Test]
        public void CreateOpcUaMcpOptionsRejectsUnknownConfiguredProfile()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = "unknown"
                })
                .Build();

            Assert.That(
                () => McpHostBuilder.CreateOpcUaMcpOptions(configuration, null),
                Throws.InvalidOperationException.With.Message.Contains("Unknown MCP tool profile"));
        }

        [TestCase("vision,robotics")]
        [TestCase("vision+robotics")]
        [TestCase("VISION, ROBOTICS")]
        public void CreateOpcUaMcpOptionsParsesComposedProfiles(string configuredProfile)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = configuredProfile
                })
                .Build();

            Opc.Ua.Mcp.OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(
                configuration,
                null);

            Assert.That(options.ToolProfiles.Count, Is.EqualTo(2));
            Assert.That(options.ToolProfiles.Contains(McpToolProfile.Vision), Is.True);
            Assert.That(options.ToolProfiles.Contains(McpToolProfile.Robotics), Is.True);
            Assert.That(options.EffectiveToolProfiles, Is.EqualTo(options.ToolProfiles));
        }

        [Test]
        public void CreateOpcUaMcpOptionsCliOverrideAcceptsComposedProfiles()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = "services"
                })
                .Build();

            Opc.Ua.Mcp.OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(
                configuration,
                "vision,robotics");

            Assert.That(options.ToolProfiles.Count, Is.EqualTo(2));
            Assert.That(options.ToolProfiles.Contains(McpToolProfile.Vision), Is.True);
            Assert.That(options.ToolProfiles.Contains(McpToolProfile.Robotics), Is.True);
        }

        [Test]
        public void CreateOpcUaMcpOptionsSingletonComposedProfileFallsBackToLegacySlot()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = "vision"
                })
                .Build();

            Opc.Ua.Mcp.OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(
                configuration,
                null);

            Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Vision));
            Assert.That(options.ToolProfiles.IsEmpty, Is.True);
            Assert.That(options.EffectiveToolProfiles.Count, Is.EqualTo(1));
            Assert.That(
                options.EffectiveToolProfiles.Contains(McpToolProfile.Vision),
                Is.True);
        }

        [Test]
        public void ConfigureMcpToolsWithProfileSetComposesBoundedCatalogues()
        {
            HashSet<string> vision = GetToolNames(McpToolProfile.Vision, false);
            HashSet<string> robotics = GetToolNames(McpToolProfile.Robotics, false);

            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(
                builder,
                new McpToolProfileSet(new[] { McpToolProfile.Vision, McpToolProfile.Robotics }),
                diagnosticsToolsEnabled: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            HashSet<string> composed = provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);

            HashSet<string> unionByHand = new(vision, StringComparer.Ordinal);
            unionByHand.UnionWith(robotics);
            Assert.That(composed, Is.EquivalentTo(unionByHand));
            Assert.That(composed, Does.Contain("Connect"));
            Assert.That(composed, Does.Contain("vision_get_frame"));
            Assert.That(composed, Does.Contain("robotics_submit_linear_move"));
        }

        [Test]
        public void ConfigureMcpToolsWithProfileSetDelegatesEmptySetToDefaultFull()
        {
            HashSet<string> full = GetToolNames(McpToolProfile.Full, false);

            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(
                builder,
                McpToolProfileSet.Empty,
                diagnosticsToolsEnabled: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            HashSet<string> tools = provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(tools, Is.EquivalentTo(full));
        }

        [Test]
        public void ConfigureMcpToolsWithSingletonProfileSetReproducesLegacyCatalogue()
        {
            HashSet<string> legacyVision = GetToolNames(McpToolProfile.Vision, false);

            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(
                builder,
                new McpToolProfileSet(McpToolProfile.Vision),
                diagnosticsToolsEnabled: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            HashSet<string> composed = provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(composed, Is.EquivalentTo(legacyVision));
        }

        [TestCase("core", 15)]
        [TestCase("services", 47)]
        [TestCase("administration", 14)]
        [TestCase("pubsub", 18)]
        [TestCase("diagnostics", 10)]
        [TestCase("robotics", 40)]
        [TestCase("vision", 26)]
        [TestCase("full", 136)]
        public void ExistingProfilesRegisterTheExpectedToolCount(string profile, int expectedCount)
        {
            var toolProfiles = McpToolProfileSet.Parse(profile);
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(builder, toolProfiles, diagnosticsToolsEnabled: false);

            using ServiceProvider provider = services.BuildServiceProvider();
            HashSet<string> tools = provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(tools, Has.Count.EqualTo(expectedCount),
                $"Profile '{profile}' must publish exactly {expectedCount} tools.");
        }

        [TestCase("true", true)]
        [TestCase("false", false)]
        [TestCase("not-a-boolean", false)]
        [TestCase(null, false)]
        public void CreatePcapOptionsParsesDiagnosticsSetting(string? value, bool expected)
        {
            var values = new Dictionary<string, string?>();
            if (value != null)
            {
                values["Pcap:EnableDiagnosticsTools"] = value;
            }
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            PcapOptions options = McpHostBuilder.CreatePcapOptions(configuration);

            Assert.That(options.EnableDiagnosticsTools, Is.EqualTo(expected));
        }

        [TestCase(false, null, false)]
        [TestCase(true, null, true)]
        [TestCase(false, "1", true)]
        [TestCase(false, "TRUE", true)]
        [TestCase(false, "0", false)]
        public void AreDiagnosticsToolsEnabledHonorsOptionsAndEnvironment(
            bool configured,
            string? environmentValue,
            bool expected)
        {
            Environment.SetEnvironmentVariable(kDiagnosticsVariable, environmentValue);
            var options = new PcapOptions { EnableDiagnosticsTools = configured };

            bool enabled = McpHostBuilder.AreDiagnosticsToolsEnabled(options);

            Assert.That(enabled, Is.EqualTo(expected));
        }

        [Test]
        public void ConfigureMcpToolsAddsOptionalDiagnosticRegistrations()
        {
            var standardServices = new ServiceCollection();
            IMcpServerBuilder standardBuilder = standardServices.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(standardBuilder, false);

            var diagnosticServices = new ServiceCollection();
            IMcpServerBuilder diagnosticBuilder = diagnosticServices.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(diagnosticBuilder, true);

            Assert.That(standardServices, Is.Not.Empty);
            Assert.That(
                diagnosticServices,
                Has.Count.GreaterThan(standardServices.Count));
        }

        [Test]
        public void ConfigureMcpToolsRegistersBoundedProfiles()
        {
            HashSet<string> core = GetToolNames(McpToolProfile.Core, false);
            HashSet<string> services = GetToolNames(McpToolProfile.Services, false);
            HashSet<string> administration = GetToolNames(McpToolProfile.Administration, false);
            HashSet<string> pubSub = GetToolNames(McpToolProfile.PubSub, false);
            HashSet<string> diagnostics = GetToolNames(McpToolProfile.Diagnostics, false);
            HashSet<string> robotics = GetToolNames(McpToolProfile.Robotics, false);
            HashSet<string> full = GetToolNames(McpToolProfile.Full, false);

            Assert.That(core, Has.Count.LessThanOrEqualTo(25));
            Assert.That(core, Does.Contain("Connect"));
            Assert.That(core, Does.Contain("BrowseAll"));
            Assert.That(core, Does.Contain("GetConfiguration"));
            Assert.That(core, Does.Contain("SetTransportConfiguration"));
            Assert.That(core, Does.Not.Contain("SetConfiguration"));
            Assert.That(core, Does.Not.Contain("Browse"));

            Assert.That(services, Does.Contain("Browse"));
            Assert.That(services, Does.Contain("ModifySubscription"));
            Assert.That(services, Does.Not.Contain("ListCertificates"));

            Assert.That(administration, Does.Contain("ListCertificates"));
            Assert.That(administration, Does.Contain("ExportNodeSet"));
            Assert.That(administration, Does.Not.Contain("Browse"));

            Assert.That(pubSub, Does.Contain("pubsub_runtime_start_publisher"));
            Assert.That(pubSub, Does.Not.Contain("Connect"));

            Assert.That(diagnostics, Does.Contain("Connect"));
            Assert.That(diagnostics, Does.Contain("start_capture"));
            Assert.That(diagnostics, Does.Not.Contain("pubsub_runtime_start_publisher"));

            Assert.That(robotics, Does.Contain("robotics_list_controllers"));
            Assert.That(robotics, Does.Contain("robotics_submit_linear_move"));
            Assert.That(robotics, Does.Contain("Connect"));

            HashSet<string> vision = GetToolNames(McpToolProfile.Vision, false);
            Assert.That(vision, Does.Contain("vision_list_sensors"));
            Assert.That(vision, Does.Contain("vision_get_frame"));
            Assert.That(vision, Does.Contain("Connect"));
            Assert.That(vision, Does.Not.Contain("robotics_list_controllers"));

            Assert.That(full, Does.Contain("Browse"));
            Assert.That(full, Does.Contain("ListCertificates"));
            Assert.That(full, Does.Contain("SetConfiguration"));
            Assert.That(full, Does.Contain("SetTransportConfiguration"));
            Assert.That(full, Does.Contain("pubsub_runtime_start_publisher"));
            Assert.That(full, Does.Contain("robotics_list_controllers"));
            Assert.That(full, Has.Count.GreaterThan(core.Count));
        }

        [Test]
        public void ConfigureMcpToolsKeepsDiagnosticToolsBehindSecurityGate()
        {
            HashSet<string> disabledDiagnostics = GetToolNames(McpToolProfile.Diagnostics, false);
            HashSet<string> enabledDiagnostics = GetToolNames(McpToolProfile.Diagnostics, true);
            HashSet<string> disabledPubSub = GetToolNames(McpToolProfile.PubSub, false);
            HashSet<string> enabledPubSub = GetToolNames(McpToolProfile.PubSub, true);

            Assert.That(disabledDiagnostics, Does.Not.Contain("dump_keys"));
            Assert.That(disabledDiagnostics, Does.Not.Contain("replay_pcap"));
            Assert.That(enabledDiagnostics, Does.Contain("dump_keys"));
            Assert.That(enabledDiagnostics, Does.Contain("replay_pcap"));

            Assert.That(disabledPubSub, Does.Not.Contain("pubsub_decode_pcap"));
            Assert.That(enabledPubSub, Does.Contain("pubsub_decode_pcap"));
        }

        [Test]
        public void ConfigureMcpToolsPairsSessionScopedProfilesWithConnectionTools()
        {
            foreach (McpToolProfile toolProfile in Enum.GetValues<McpToolProfile>())
            {
                HashSet<string> tools = GetToolNames(toolProfile, false);

                bool needsSession = tools.Any(
                    name => name.StartsWith("robotics_", StringComparison.Ordinal) ||
                        name.StartsWith("vision_", StringComparison.Ordinal));

                if (!needsSession)
                {
                    continue;
                }

                Assert.That(
                    tools,
                    Does.Contain("Connect"),
                    $"Profile '{toolProfile}' exposes tools that resolve a named OPC UA session, " +
                    "so it must also expose the connection tools that open one.");
                Assert.That(tools, Does.Contain("Disconnect"));
            }
        }

        [Test]
        public void ConfigureMcpToolsRejectsUnknownProfile()
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();

            ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => McpHostBuilder.ConfigureMcpTools(
                    builder,
                    (McpToolProfile)int.MaxValue,
                    false));

            Assert.That(exception!.ParamName, Is.EqualTo("toolProfile"));
        }

        [TestCase(true, LogLevel.Trace)]
        [TestCase(false, LogLevel.Error)]
        public void ConfigureLoggingSetsStandardErrorThreshold(
            bool useStdioTransport,
            LogLevel expectedThreshold)
        {
            var services = new ServiceCollection();
            services.AddLogging(logging =>
                McpHostBuilder.ConfigureLogging(logging, useStdioTransport));

            using ServiceProvider provider = services.BuildServiceProvider();
            ConsoleLoggerOptions options = provider
                .GetRequiredService<IOptions<ConsoleLoggerOptions>>()
                .Value;
            SimpleConsoleFormatterOptions formatterOptions = provider
                .GetRequiredService<IOptions<SimpleConsoleFormatterOptions>>()
                .Value;

            Assert.That(options.LogToStandardErrorThreshold, Is.EqualTo(expectedThreshold));
            Assert.That(formatterOptions.UseUtcTimestamp, Is.True);
            Assert.That(
                formatterOptions.TimestampFormat,
                Is.EqualTo("yyyy-MM-dd HH:mm:ss "));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LogDiagnosticsToolsWarningAcceptsBothStates(bool enabled)
        {
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace));
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => McpHostBuilder.LogDiagnosticsToolsWarning(provider, enabled),
                Throws.Nothing);
        }

        [Test]
        public void HostBuilderMethodsRejectNullArguments()
        {
            Assert.That(
                () => McpHostBuilder.ConfigureServices(null!, new PcapOptions()),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.ConfigureServices(new ServiceCollection(), null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.CreatePcapOptions(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.CreateOpcUaMcpOptions(null!, null),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.AreDiagnosticsToolsEnabled(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.ConfigureMcpTools(null!, false),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.ConfigureMcpTools(null!, McpToolProfile.Core, false),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.ConfigureMcpTools(
                    null!,
                    new McpToolProfileSet(McpToolProfile.Core),
                    false),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.LogDiagnosticsToolsWarning(null!, false),
                Throws.ArgumentNullException);
            Assert.That(
                () => McpHostBuilder.ConfigureLogging(null!),
                Throws.ArgumentNullException);
        }

        private static HashSet<string> GetToolNames(
            McpToolProfile toolProfile,
            bool diagnosticsToolsEnabled)
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(builder, toolProfile, diagnosticsToolsEnabled);

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
        }
    }
}
#endif
