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

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Mqtt;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>
    /// Unit tests for the MQTT transport-security policy: <c>mqtts</c> enables TLS
    /// and defaults to port 8883, credentials / trust are applied through the
    /// provider, the executor fails closed when a required credential is
    /// unresolved, and username / password material never downgrades to a
    /// plaintext connection.
    /// </summary>
    [TestFixture]
    public sealed class MqttWotConnectionTests
    {
        private static WotCompiledForm Compiled(string href, bool withSecurity)
        {
            string security = withSecurity
                ? "\"securityDefinitions\":{\"basic_sc\":{\"scheme\":\"basic\"}},\"security\":\"basic_sc\","
                : string.Empty;
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," + security +
                "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"" + href +
                "\",\"op\":[\"writeproperty\"]}]}}}";
            var registry = new WotProtocolBinderRegistry(new IWotProtocolBinder[] { new MqttBindingPlanner() });
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
            return plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
        }

        private static WotExecutorContext Context(IWotCredentialProvider credentials)
            => new WotExecutorContext(credentials);

        private static Task<MqttWotConnection.MqttWotConnectPlan> PrepareAsync(
            WotCompiledForm form, MqttWotBindingOptions options, IWotCredentialProvider credentials)
            => MqttWotConnection.PrepareAsync(form, Context(credentials), options, "client-id", CancellationToken.None)
                .AsTask();

        [Test]
        public async Task PlainMqtt_UsesPlaintext_DefaultPort1883()
        {
            WotCompiledForm form = Compiled("mqtt://broker/things/p", withSecurity: false);

            MqttWotConnection.MqttWotConnectPlan plan = await PrepareAsync(
                form, new MqttWotBindingOptions(), NullWotCredentialProvider.Instance);

            Assert.That(plan.UseTls, Is.False);
            Assert.That(plan.Port, Is.EqualTo(1883));
            Assert.That(plan.HasCredentials, Is.False);
        }

        [Test]
        public async Task Mqtts_EnablesTls_DefaultPort8883()
        {
            WotCompiledForm form = Compiled("mqtts://broker/things/p", withSecurity: false);

            MqttWotConnection.MqttWotConnectPlan plan = await PrepareAsync(
                form, new MqttWotBindingOptions(), NullWotCredentialProvider.Instance);

            Assert.That(plan.UseTls, Is.True);
            Assert.That(plan.Port, Is.EqualTo(8883));
        }

        [Test]
        public async Task Mqtts_HonoursExplicitPort()
        {
            WotCompiledForm form = Compiled("mqtts://broker:9999/things/p", withSecurity: false);

            MqttWotConnection.MqttWotConnectPlan plan = await PrepareAsync(
                form, new MqttWotBindingOptions(), NullWotCredentialProvider.Instance);

            Assert.That(plan.UseTls, Is.True);
            Assert.That(plan.Port, Is.EqualTo(9999));
        }

        [Test]
        public async Task Mqtts_WithResolvedCredentials_AppliesThem()
        {
            WotCompiledForm form = Compiled("mqtts://broker/things/p", withSecurity: true);

            MqttWotConnection.MqttWotConnectPlan plan = await PrepareAsync(
                form, new MqttWotBindingOptions(), new UserPasswordCredentialProvider());

            Assert.That(plan.UseTls, Is.True);
            Assert.That(plan.HasCredentials, Is.True);
        }

        [Test]
        public void Mqtts_RequiredCredentialUnresolved_FailsClosed()
        {
            WotCompiledForm form = Compiled("mqtts://broker/things/p", withSecurity: true);

            Assert.That(form.Security, Is.Not.Empty, "The form must declare a security scheme.");
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await PrepareAsync(form, new MqttWotBindingOptions(), NullWotCredentialProvider.Instance));
        }

        [Test]
        public void PlainMqtt_WithCredentials_FailsClosed_NoPlaintextDowngrade()
        {
            WotCompiledForm form = Compiled("mqtt://broker/things/p", withSecurity: true);

            // The provider resolves username / password but the connection is plain
            // mqtt://, so sending the credentials would leak them in clear text.
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await PrepareAsync(
                    form, new MqttWotBindingOptions(), new UserPasswordCredentialProvider()));
        }

        [Test]
        public async Task PlainMqtt_WithCredentials_AllowedWhenExplicitlyOptedIn()
        {
            WotCompiledForm form = Compiled("mqtt://broker/things/p", withSecurity: true);

            MqttWotConnection.MqttWotConnectPlan plan = await PrepareAsync(
                form,
                new MqttWotBindingOptions { AllowCredentialsOverPlaintext = true },
                new UserPasswordCredentialProvider());

            Assert.That(plan.UseTls, Is.False);
            Assert.That(plan.HasCredentials, Is.True);
        }

        private sealed class UserPasswordCredentialProvider : IWotCredentialProvider
        {
            public ValueTask<WotCredential?> ResolveAsync(
                WotCredentialReference reference, CancellationToken cancellationToken = default)
                => new ValueTask<WotCredential?>(new WotCredential(
                    WotSecurityScheme.Basic,
                    properties: ImmutableDictionary<string, string>.Empty
                        .Add("username", "device")
                        .Add("password", "secret")));
        }
    }
}
