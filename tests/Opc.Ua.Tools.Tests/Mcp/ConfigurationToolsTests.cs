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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class ConfigurationToolsTests
    {
        [Test]
        public async Task GetConfigurationAsyncReturnsAllSectionsAsync()
        {
            string json = await ConfigurationTools.GetConfigurationAsync(
                McpTestEnvironment.SessionManager).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(
                GetRequiredProperty(root, "applicationName").GetString(),
                Is.Not.Null.And.Not.Empty);
            Assert.That(
                GetRequiredProperty(root, "transportQuotas").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
            Assert.That(
                GetRequiredProperty(root, "security").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
            Assert.That(
                GetRequiredProperty(root, "clientConfiguration").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
        }

        [Test]
        public async Task SetConfigurationAsyncUpdatesEverySupportedSettingAsync()
        {
            ApplicationConfiguration configuration = await McpTestEnvironment.SessionManager
                .EnsureConfigurationAsync()
                .ConfigureAwait(false);
            TransportQuotas quotas = configuration.TransportQuotas
                ?? throw new InvalidOperationException("Transport quotas are not configured.");
            ClientConfiguration client = configuration.ClientConfiguration
                ?? throw new InvalidOperationException("Client configuration is not configured.");
            SecurityConfiguration security = configuration.SecurityConfiguration
                ?? throw new InvalidOperationException("Security configuration is not configured.");
            int originalOperationTimeout = quotas.OperationTimeout;
            int originalMaxStringLength = quotas.MaxStringLength;
            int originalMaxByteStringLength = quotas.MaxByteStringLength;
            int originalMaxArrayLength = quotas.MaxArrayLength;
            int originalMaxMessageSize = quotas.MaxMessageSize;
            int originalMaxBufferSize = quotas.MaxBufferSize;
            int originalChannelLifetime = quotas.ChannelLifetime;
            int originalSecurityTokenLifetime = quotas.SecurityTokenLifetime;
            int originalSessionTimeout = client.DefaultSessionTimeout;
            bool originalAutoAccept = security.AutoAcceptUntrustedCertificates;
            bool originalRejectSha1 = security.RejectSHA1SignedCertificates;
            ushort originalMinimumKeySize = security.MinimumCertificateKeySize;

            try
            {
                string json = await ConfigurationTools.SetConfigurationAsync(
                    McpTestEnvironment.SessionManager,
                    operationTimeout: 101001,
                    maxStringLength: 101002,
                    maxByteStringLength: 101003,
                    maxArrayLength: 101004,
                    maxMessageSize: 101005,
                    maxBufferSize: 101006,
                    channelLifetime: 101007,
                    securityTokenLifetime: 101008,
                    defaultSessionTimeout: 101009,
                    autoAcceptUntrustedCertificates: !originalAutoAccept,
                    rejectSha1SignedCertificates: !originalRejectSha1,
                    minimumCertificateKeySize: 3072).ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement changes = GetRequiredProperty(root, "changes");

                Assert.That(GetRequiredProperty(root, "success").GetBoolean(), Is.True);
                Assert.That(changes.GetArrayLength(), Is.EqualTo(12));
                Assert.That(
                    changes.EnumerateArray().Select(element => element.GetString()),
                    Does.Contain("OperationTimeout=101001"));
                Assert.That(quotas.OperationTimeout, Is.EqualTo(101001));
                Assert.That(quotas.MaxStringLength, Is.EqualTo(101002));
                Assert.That(quotas.MaxByteStringLength, Is.EqualTo(101003));
                Assert.That(quotas.MaxArrayLength, Is.EqualTo(101004));
                Assert.That(quotas.MaxMessageSize, Is.EqualTo(101005));
                Assert.That(quotas.MaxBufferSize, Is.EqualTo(101006));
                Assert.That(quotas.ChannelLifetime, Is.EqualTo(101007));
                Assert.That(quotas.SecurityTokenLifetime, Is.EqualTo(101008));
                Assert.That(client.DefaultSessionTimeout, Is.EqualTo(101009));
                Assert.That(
                    security.AutoAcceptUntrustedCertificates,
                    Is.EqualTo(!originalAutoAccept));
                Assert.That(
                    security.RejectSHA1SignedCertificates,
                    Is.EqualTo(!originalRejectSha1));
                Assert.That(security.MinimumCertificateKeySize, Is.EqualTo(3072));
            }
            finally
            {
                _ = await ConfigurationTools.SetConfigurationAsync(
                    McpTestEnvironment.SessionManager,
                    operationTimeout: originalOperationTimeout,
                    maxStringLength: originalMaxStringLength,
                    maxByteStringLength: originalMaxByteStringLength,
                    maxArrayLength: originalMaxArrayLength,
                    maxMessageSize: originalMaxMessageSize,
                    maxBufferSize: originalMaxBufferSize,
                    channelLifetime: originalChannelLifetime,
                    securityTokenLifetime: originalSecurityTokenLifetime,
                    defaultSessionTimeout: originalSessionTimeout,
                    autoAcceptUntrustedCertificates: originalAutoAccept,
                    rejectSha1SignedCertificates: originalRejectSha1,
                    minimumCertificateKeySize: originalMinimumKeySize).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FocusedConfigurationToolsApplyEquivalentChangesAsync()
        {
            ApplicationConfiguration configuration = await McpTestEnvironment.SessionManager
                .EnsureConfigurationAsync()
                .ConfigureAwait(false);
            TransportQuotas quotas = configuration.TransportQuotas
                ?? throw new InvalidOperationException("Transport quotas are not configured.");
            ClientConfiguration client = configuration.ClientConfiguration
                ?? throw new InvalidOperationException("Client configuration is not configured.");
            SecurityConfiguration security = configuration.SecurityConfiguration
                ?? throw new InvalidOperationException("Security configuration is not configured.");
            int originalOperationTimeout = quotas.OperationTimeout;
            int originalSessionTimeout = client.DefaultSessionTimeout;
            ushort originalMinimumKeySize = security.MinimumCertificateKeySize;
            int updatedMinimumKeySize = originalMinimumKeySize == 3072 ? 2048 : 3072;

            try
            {
                string transportJson = await ConfigurationUpdateTools
                    .SetTransportConfigurationAsync(
                        McpTestEnvironment.SessionManager,
                        operationTimeout: originalOperationTimeout + 1)
                    .ConfigureAwait(false);
                string clientJson = await ConfigurationUpdateTools
                    .SetClientConfigurationAsync(
                        McpTestEnvironment.SessionManager,
                        defaultSessionTimeout: originalSessionTimeout + 1)
                    .ConfigureAwait(false);
                string securityJson = await ConfigurationUpdateTools
                    .SetSecurityConfigurationAsync(
                        McpTestEnvironment.SessionManager,
                        minimumCertificateKeySize: updatedMinimumKeySize)
                    .ConfigureAwait(false);
                string readJson = await ConfigurationReadTools.GetConfigurationAsync(
                    McpTestEnvironment.SessionManager).ConfigureAwait(false);

                AssertSuccessfulChange(transportJson, "OperationTimeout");
                AssertSuccessfulChange(clientJson, "DefaultSessionTimeout");
                AssertSuccessfulChange(securityJson, "MinimumCertificateKeySize");
                using JsonDocument readDocument = JsonDocument.Parse(readJson);
                Assert.That(readDocument.RootElement.TryGetProperty("error", out _), Is.False);
                Assert.That(quotas.OperationTimeout, Is.EqualTo(originalOperationTimeout + 1));
                Assert.That(client.DefaultSessionTimeout, Is.EqualTo(originalSessionTimeout + 1));
                Assert.That(security.MinimumCertificateKeySize, Is.EqualTo(updatedMinimumKeySize));
            }
            finally
            {
                _ = await ConfigurationTools.SetConfigurationAsync(
                    McpTestEnvironment.SessionManager,
                    operationTimeout: originalOperationTimeout,
                    defaultSessionTimeout: originalSessionTimeout,
                    minimumCertificateKeySize: originalMinimumKeySize).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetConfigurationAsyncWithoutValuesReturnsGuidanceAsync()
        {
            string json = await ConfigurationTools.SetConfigurationAsync(
                McpTestEnvironment.SessionManager).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(GetRequiredProperty(root, "success").GetBoolean(), Is.False);
            Assert.That(GetRequiredProperty(root, "changes").GetArrayLength(), Is.Zero);
            Assert.That(
                GetRequiredProperty(root, "message").GetString(),
                Does.StartWith("No changes specified."));
        }

        [Test]
        public async Task ConfigurationToolsReturnErrorForDisposedManagerAsync()
        {
            var services = new ServiceCollection();
            McpHostBuilder.ConfigureServices(services, new PcapOptions());
            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            manager.Dispose();

            string getJson = await ConfigurationTools.GetConfigurationAsync(manager)
                .ConfigureAwait(false);
            string setJson = await ConfigurationTools.SetConfigurationAsync(
                manager,
                operationTimeout: 1000).ConfigureAwait(false);
            string focusedJson = await ConfigurationUpdateTools.SetClientConfigurationAsync(
                manager,
                defaultSessionTimeout: 1000).ConfigureAwait(false);

            using JsonDocument getDocument = JsonDocument.Parse(getJson);
            using JsonDocument setDocument = JsonDocument.Parse(setJson);
            using JsonDocument focusedDocument = JsonDocument.Parse(focusedJson);

            Assert.That(
                GetRequiredProperty(getDocument.RootElement, "error").GetBoolean(),
                Is.True);
            Assert.That(
                GetRequiredProperty(setDocument.RootElement, "error").GetBoolean(),
                Is.True);
            Assert.That(
                GetRequiredProperty(focusedDocument.RootElement, "error").GetBoolean(),
                Is.True);
        }

        private static void AssertSuccessfulChange(string json, string expectedChangeName)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(GetRequiredProperty(root, "success").GetBoolean(), Is.True);
            Assert.That(
                GetRequiredProperty(root, "changes")
                    .EnumerateArray()
                    .Select(element => element.GetString()),
                Has.Some.StartsWith(expectedChangeName + "="));
        }

        private static JsonElement GetRequiredProperty(
            JsonElement element,
            string propertyName)
        {
            Assert.That(
                element.TryGetProperty(propertyName, out JsonElement property),
                Is.True);
            return property;
        }
    }
}
#endif
