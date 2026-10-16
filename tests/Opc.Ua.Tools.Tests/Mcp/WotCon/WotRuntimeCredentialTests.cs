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
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Proves that credential resolution, revocation and expiry reuse the shared secret gateway.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimeCredentialTests
    {
        [Test]
        public async Task CredentialReferencesResolveOnlyForExactResourceAsync()
        {
            var store = new GdsTestSecretStore();
            store.Seed("token", "synthetic-private-token");
            await using WotRuntimeTestContext fixture = Create(store, aliasResource: Resource + "-wrong");
            using IDisposable caller = fixture.Enter();
            string handle = await OpenAsync(fixture, "alias:token").ConfigureAwait(false);
            JsonObject result = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(store.Views, Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(result.ToJsonString(), Does.Not.Contain("synthetic-private-token"));
        }

        [Test]
        public async Task ErrorsAndMetadataNeverEchoSecretsAsync()
        {
            var store = new GdsTestSecretStore();
            store.Seed("token", "synthetic-private-token");
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            string handle = await OpenAsync(fixture, "alias:token").ConfigureAwait(false);
            JsonObject read = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            JsonObject inspected = WotRuntimeTestContext.Payload(
                await fixture.Tools.InspectAsync(handle).ConfigureAwait(false));

            Assert.That(fixture.Factory.LastCredential!.Headers["Authorization"],
                Is.EqualTo("Bearer synthetic-private-token"));
            Assert.That(read.ToJsonString(), Does.Not.Contain("synthetic-private-token"));
            Assert.That(inspected.ToJsonString(), Does.Not.Contain("synthetic-private-token"));
            Assert.That(inspected["hasAliasCredentials"]!.GetValue<bool>(), Is.True);
            Assert.That(inspected["aliasRevocation"]!.GetValue<string>(),
                Is.EqualTo("recheckedPerOperation; noAsyncAliasSignal"));
            Assert.That(store.Views, Has.Count.EqualTo(1));
            Assert.That(store.Views.Single().Disposed, Is.True);
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task AliasRotationIsResolvedOnEachOperationWithoutCachedChannelsAsync()
        {
            var store = new GdsTestSecretStore();
            store.Seed("token", "synthetic-first");
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            string handle = await OpenAsync(fixture, "alias:token").ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
            Assert.That(fixture.Factory.LastCredential!.Headers["Authorization"], Is.EqualTo("Bearer synthetic-first"));

            store.Seed("token", "synthetic-rotated");
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));

            Assert.That(fixture.Factory.LastCredential!.Headers["Authorization"],
                Is.EqualTo("Bearer synthetic-rotated"));
            Assert.That(fixture.Factory.OpenedForms, Has.Count.EqualTo(2));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(2));
            Assert.That(store.Views, Has.Count.EqualTo(2));
            Assert.That(store.Views.All(view => view.Disposed), Is.True);
        }

        [Test]
        public async Task IssuedReferenceInvalidationClosesRuntimeAndSubscriptionAsync()
        {
            var store = new GdsTestSecretStore();
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            McpSecretReferences gateway = fixture.Services.GetRequiredService<McpSecretReferences>();
            McpSecretReference secret = await gateway.StoreIssuedAsync(
                ByteString.From(Encoding.UTF8.GetBytes("synthetic-issued")), McpCapability.WotRuntime,
                "bearer", Resource, fixture.Clock.GetUtcNow().AddMinutes(1)).ConfigureAwait(false);
            string handle = await OpenAsync(fixture, secret.Reference, ["observeproperty"]).ConfigureAwait(false);
            Task<CallToolResult> pending = fixture.Tools.ObservePropertyAsync(handle);
            await McpRuntimeTestContext.AwaitAsync(fixture.Factory.Subscribed.Task).ConfigureAwait(false);

            await gateway.RevokeReferenceAsync(secret.Reference).ConfigureAwait(false);
            _ = WotRuntimeTestContext.Payload(
                await McpRuntimeTestContext.AwaitAsync(pending).ConfigureAwait(false), error: true);
            _ = WotRuntimeTestContext.Payload(
                await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Factory.OpenedForms, Has.Count.EqualTo(1));
            Assert.That(store.Removed, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task IssuedExpiryStopsAnAlreadyRunningObservationAsync()
        {
            var store = new GdsTestSecretStore();
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            McpSecretReferences gateway = fixture.Services.GetRequiredService<McpSecretReferences>();
            McpSecretReference secret = await gateway.StoreIssuedAsync(
                ByteString.From(Encoding.UTF8.GetBytes("synthetic-issued")), McpCapability.WotRuntime,
                "bearer", Resource, fixture.Clock.GetUtcNow().AddMilliseconds(100)).ConfigureAwait(false);
            string handle = await OpenAsync(fixture, secret.Reference, ["observeproperty"]).ConfigureAwait(false);
            Task<CallToolResult> pending = fixture.Tools.ObservePropertyAsync(handle, durationMilliseconds: 1000);
            await McpRuntimeTestContext.AwaitAsync(fixture.Factory.Subscribed.Task).ConfigureAwait(false);

            fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
            _ = WotRuntimeTestContext.Payload(
                await McpRuntimeTestContext.AwaitAsync(pending).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.SubscriptionDisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Factory.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Factory.OpenedForms, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ForeignIssuedReferenceCannotOpenAChannelAsync()
        {
            var store = new GdsTestSecretStore();
            await using WotRuntimeTestContext fixture = Create(store);
            McpSecretReference secret;
            using (fixture.Enter())
            {
                secret = await fixture.Services.GetRequiredService<McpSecretReferences>().StoreIssuedAsync(
                    ByteString.From(Encoding.UTF8.GetBytes("synthetic-issued")), McpCapability.WotRuntime,
                    "bearer", Resource, fixture.Clock.GetUtcNow().AddMinutes(1)).ConfigureAwait(false);
            }
            using (fixture.Enter(WotRuntimeTestContext.Other))
            {
                JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("foreign-secret",
                    new WotDocumentSource { Text = Document }, WotRuntimeTestContext.Selection(Document),
                    ["readproperty"], credentials: [Credential(secret.Reference)]).ConfigureAwait(false), error: true);

                Assert.That(result["statusCodeValue"]!.GetValue<uint>(),
                    Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(fixture.Registry.List(), Is.Empty);
                Assert.That(fixture.Factory.OpenedForms, Is.Empty);
                Assert.That(store.Views, Is.Empty);
            }
        }

        [TestCase("plaintext-token")]
        [TestCase("C:\\arbitrary\\store")]
        [TestCase("file:///secret-store")]
        public async Task ArbitraryCredentialMaterialAndStorePathsAreRejectedAsync(string reference)
        {
            var store = new GdsTestSecretStore();
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("reference",
                new WotDocumentSource { Text = Document }, WotRuntimeTestContext.Selection(Document),
                ["readproperty"], credentials: [Credential(reference)]).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(store.Views, Is.Empty);
        }

        [Test]
        public async Task PlaintextHttpCannotConsumeCredentialReferencesAsync()
        {
            var store = new GdsTestSecretStore();
            string document = Document.Replace("https://device.example", "http://device.example",
                StringComparison.Ordinal);
            await using var fixture = new WotRuntimeTestContext(register: services =>
                RegisterSecrets(services, store, Resource));
            using IDisposable caller = fixture.Enter();
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("plaintext",
                new WotDocumentSource { Text = document }, WotRuntimeTestContext.Selection(document),
                ["readproperty"], credentials: [Credential("alias:token")]).ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadSecurityModeRejected.Code));
            Assert.That(store.Views, Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }

        [Test]
        public async Task BasicCredentialsUseSeparateAuthorizedUsernameAndPasswordReferencesAsync()
        {
            var store = new GdsTestSecretStore();
            store.Seed("token", "synthetic-password");
            store.Seed("username", "test-user");
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            string document = Document.Replace("\"scheme\": \"bearer\"", "\"scheme\": \"basic\"",
                StringComparison.Ordinal);
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("basic",
                new WotDocumentSource { Text = document }, WotRuntimeTestContext.Selection(document), ["readproperty"],
                credentials: [Credential("alias:token") with { UserNameReference = "alias:username" }])
                .ConfigureAwait(false));
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.ReadPropertyAsync(
                opened["runtime"]!["handle"]!.GetValue<string>()).ConfigureAwait(false));

            Assert.That(fixture.Factory.LastCredential!.Headers["Authorization"],
                Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-user:synthetic-password"))));
            Assert.That(store.Views, Has.Count.EqualTo(2));
            Assert.That(store.Views.All(view => view.Disposed), Is.True);
            Assert.That(result.ToJsonString(), Does.Not.Contain("synthetic-password"));
        }

        [TestCase("Host")]
        [TestCase("Cookie")]
        [TestCase("Proxy-Authorization")]
        public async Task ApiKeysCannotOverrideRoutingOrAmbientIdentityHeadersAsync(string header)
        {
            var store = new GdsTestSecretStore();
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            string document = Document.Replace("\"scheme\": \"bearer\"",
                "\"scheme\": \"apikey\", \"name\": \"" + header + "\"", StringComparison.Ordinal);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("header",
                new WotDocumentSource { Text = document }, WotRuntimeTestContext.Selection(document), ["readproperty"],
                credentials: [Credential("alias:token")]).ConfigureAwait(false), error: true);

            Assert.That(store.Views, Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        [Test]
        public async Task QueryCredentialsAreRejectedBeforeSecretResolutionAsync()
        {
            var store = new GdsTestSecretStore();
            await using WotRuntimeTestContext fixture = Create(store);
            using IDisposable caller = fixture.Enter();
            string document = Document.Replace("\"in\": \"header\"", "\"in\": \"query\"", StringComparison.Ordinal);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("query",
                new WotDocumentSource { Text = document }, WotRuntimeTestContext.Selection(document), ["readproperty"],
                credentials: [Credential("alias:token")]).ConfigureAwait(false), error: true);

            Assert.That(store.Views, Is.Empty);
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        /// <summary>
        /// Creates a host that allows exactly one HTTPS resource and the shared protected store.
        /// </summary>
        private static WotRuntimeTestContext Create(GdsTestSecretStore store, string aliasResource = Resource)
        {
            return new WotRuntimeTestContext(options => options.NetworkRules =
                [WotRuntimeTestContext.Rule(McpCapability.WotRuntime, ["GET"], ["/value"], "https", port: 443)],
                register: services => RegisterSecrets(services, store, aliasResource));
        }

        /// <summary>
        /// Registers only a host-authored alias; runtime arguments cannot affect its store route.
        /// </summary>
        private static void RegisterSecrets(IServiceCollection services, GdsTestSecretStore store, string resource)
        {
            services.AddOpcUaMcpSecrets(new SecretRegistry(store),
                [
                    new McpSecretAlias("token", new SecretIdentifier("token", store.StoreType), resource),
                    new McpSecretAlias("username", new SecretIdentifier("username", store.StoreType), resource)
                ], store);
        }

        /// <summary>
        /// Opens a secret-reference-bound runtime using real preparation and authorization.
        /// </summary>
        private static async Task<string> OpenAsync(
            WotRuntimeTestContext fixture, string reference, ArrayOf<string> operations = default)
        {
            JsonObject result = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("credential",
                new WotDocumentSource { Text = Document }, WotRuntimeTestContext.Selection(Document),
                operations.IsNull ? ["readproperty"] : operations,
                credentials: [Credential(reference)]).ConfigureAwait(false));
            return result["runtime"]!["handle"]!.GetValue<string>();
        }

        /// <summary>
        /// Names the exact TD security definition and opaque secret reference.
        /// </summary>
        private static WotRuntimeCredentialInput Credential(string reference)
        {
            return new WotRuntimeCredentialInput { SchemeName = "token_sc", SecretReference = reference };
        }

        /// <summary>The host-authored secret binding for the exact HTTPS property.</summary>
        private const string Resource = "wot:https://device.example#/value";

        /// <summary>A TD with one bearer security scheme and one exact property form.</summary>
        private const string Document = /*lang=json,strict*/ """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "id": "urn:test:wot:credential",
              "title": "Protected runtime",
              "securityDefinitions": {"token_sc": {"scheme": "bearer", "in": "header"}},
              "security": "token_sc",
              "properties": {
                "temperature": {
                  "type": "number",
                  "forms": [{"href": "https://device.example/value", "op": ["readproperty", "observeproperty"]}]
                }
              }
            }
            """;
    }
}
#endif
