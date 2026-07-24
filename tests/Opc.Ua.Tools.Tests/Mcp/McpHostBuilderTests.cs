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
        public void CreateMcpServerOptionsReadsEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(kExportRootVariable, "export-root");
            Environment.SetEnvironmentVariable(kPcapRootVariable, "pcap-root");

            Opc.Ua.Mcp.McpServerOptions options =
                McpHostBuilder.CreateMcpServerOptions();

            Assert.That(options.NodeSetExportRoot, Is.EqualTo("export-root"));
            Assert.That(options.PcapBaseFolder, Is.EqualTo("pcap-root"));
            Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Full));
        }

        [TestCase("core", McpToolProfile.Core)]
        [TestCase("SERVICES", McpToolProfile.Services)]
        [TestCase("administration", McpToolProfile.Administration)]
        [TestCase("pubsub", McpToolProfile.PubSub)]
        [TestCase("diagnostics", McpToolProfile.Diagnostics)]
        [TestCase("full", McpToolProfile.Full)]
        public void CreateMcpServerOptionsParsesConfiguredProfile(
            string configuredProfile,
            McpToolProfile expectedProfile)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = configuredProfile
                })
                .Build();

            Opc.Ua.Mcp.McpServerOptions options = McpHostBuilder.CreateMcpServerOptions(
                configuration,
                null);

            Assert.That(options.ToolProfile, Is.EqualTo(expectedProfile));
        }

        [Test]
        public void CreateMcpServerOptionsCliOverrideWinsOverConfiguration()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = "services"
                })
                .Build();

            Opc.Ua.Mcp.McpServerOptions options = McpHostBuilder.CreateMcpServerOptions(
                configuration,
                McpToolProfile.Core);

            Assert.That(options.ToolProfile, Is.EqualTo(McpToolProfile.Core));
        }

        [Test]
        public void CreateMcpServerOptionsRejectsUnknownConfiguredProfile()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:ToolProfile"] = "unknown"
                })
                .Build();

            Assert.That(
                () => McpHostBuilder.CreateMcpServerOptions(configuration, null),
                Throws.InvalidOperationException.With.Message.Contains("Unknown MCP tool profile"));
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

            Assert.That(full, Does.Contain("Browse"));
            Assert.That(full, Does.Contain("ListCertificates"));
            Assert.That(full, Does.Contain("SetConfiguration"));
            Assert.That(full, Does.Contain("SetTransportConfiguration"));
            Assert.That(full, Does.Contain("pubsub_runtime_start_publisher"));
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
                () => McpHostBuilder.CreateMcpServerOptions(null!, null),
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
