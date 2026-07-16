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
using System.IO;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Configuration
{
    [TestFixture]
    [Category("Configuration")]
    [Parallelizable]
    public sealed class ConfigurationSupportTests
    {
        [Test]
        public void OAuth2CredentialLoadReturnsCachedList()
        {
            ApplicationConfiguration configuration = CreateConfigurationWithOAuthCredential();

            ArrayOf<OAuth2Credential> credentials = OAuth2CredentialCollection.Load(configuration);

            Assert.That(credentials, Has.Count.EqualTo(1));
            Assert.That(credentials[0].ClientId, Is.EqualTo("client"));
            ArrayOf<OAuth2Credential> secondLoad = OAuth2CredentialCollection.Load(configuration);
            Assert.That(secondLoad, Has.Count.EqualTo(1));
            Assert.That(secondLoad[0].ClientId, Is.EqualTo(credentials[0].ClientId));
        }

        [Test]
        public void OAuth2CredentialFindByServerUriClonesCredentialAndSelectsServer()
        {
            ApplicationConfiguration configuration = CreateConfigurationWithOAuthCredential();
            string host = Dns.GetHostName().ToLowerInvariant();

            OAuth2Credential credential = OAuth2CredentialCollection.FindByServerUri(
                configuration,
                $"https://{host}/server");

            Assert.That(credential, Is.Not.Null);
            Assert.That(credential.ClientId, Is.EqualTo("client"));
            Assert.That(credential.ClientSecret, Is.EqualTo("secret"));
            Assert.That(credential.SelectedServer, Is.Not.Null);
            Assert.That(credential.SelectedServer.ResourceId, Is.EqualTo("resource"));
            Assert.That(credential.Servers, Is.Empty);
        }

        [Test]
        public void OAuth2CredentialFindByAuthorityUrlNormalizesTrailingSlashAndHost()
        {
            ApplicationConfiguration configuration = CreateConfigurationWithOAuthCredential();
            string host = Dns.GetHostName().ToLowerInvariant();

            OAuth2Credential credential = OAuth2CredentialCollection.FindByAuthorityUrl(
                configuration,
                $"https://{host}/issuer/");

            Assert.That(credential, Is.Not.Null);
            Assert.That(credential.AuthorityUrl, Is.EqualTo($"https://{host}/issuer/"));
            Assert.That(credential.TokenEndpoint, Is.EqualTo("https://issuer.example/token"));
            Assert.That(credential.SelectedServer, Is.Null);
        }

        [Test]
        public void OAuth2CredentialFindRejectsInvalidArgumentsAndReturnsNullForMiss()
        {
            ApplicationConfiguration configuration = CreateConfigurationWithOAuthCredential();

            Assert.That(
                () => OAuth2CredentialCollection.Load(null),
                Throws.ArgumentNullException);
            Assert.That(
                () => OAuth2CredentialCollection.FindByServerUri(configuration, null),
                Throws.ArgumentException);
            Assert.That(
                () => OAuth2CredentialCollection.FindByAuthorityUrl(configuration, "not a uri"),
                Throws.ArgumentException);
            Assert.That(
                OAuth2CredentialCollection.FindByServerUri(configuration, "https://example.invalid/server"),
                Is.Null);
            Assert.That(
                OAuth2CredentialCollection.FindByAuthorityUrl(configuration, "https://example.invalid/issuer"),
                Is.Null);
        }

        [Test]
        public void ConfigurationWatcherRaisesChangedWhenSourceFileTimestampAdvances()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "ConfigurationWatcherTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, "app.config.xml");
            File.WriteAllText(filePath, "<configuration />");
            var configuration = new ApplicationConfiguration();
            SetSourceFilePath(configuration, filePath);
            var timeProvider = new FakeTimeProvider();
            int changedCount = 0;
            ConfigurationWatcherEventArgs raised = null;

            using (var watcher = new ConfigurationWatcher(
                configuration,
                NUnitTelemetryContext.Create(),
                timeProvider))
            {
                EventHandler<ConfigurationWatcherEventArgs> handler = (_, e) =>
                {
                    changedCount++;
                    raised = e;
                };
                watcher.Changed += handler;
                File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(1));

                timeProvider.Advance(TimeSpan.FromSeconds(5));

                watcher.Changed -= handler;
            }

            Assert.That(changedCount, Is.EqualTo(1));
            Assert.That(raised, Is.Not.Null);
            Assert.That(raised.Configuration, Is.SameAs(configuration));
            Assert.That(raised.FilePath, Is.EqualTo(filePath));
            Directory.Delete(directory, true);
        }

        [Test]
        public void ConfigurationWatcherValidatesConfigurationAndSourceFile()
        {
            Assert.That(
                () => _ = new ConfigurationWatcher(null, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);

            var configuration = new ApplicationConfiguration();
            SetSourceFilePath(
                configuration,
                Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "missing-configuration-file.xml"));

            Assert.That(
                () => _ = new ConfigurationWatcher(configuration, NUnitTelemetryContext.Create()),
                Throws.TypeOf<FileNotFoundException>());
        }

        private static ApplicationConfiguration CreateConfigurationWithOAuthCredential()
        {
            var configuration = new ApplicationConfiguration();
            configuration.Properties["OAuth2Credentials"] = new ArrayOf<OAuth2Credential>(new OAuth2Credential[]
            {
                new()
                {
                    AuthorityUrl = "https://localhost/issuer",
                    GrantType = "client_credentials",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUrl = "https://localhost/redirect",
                    TokenEndpoint = "https://issuer.example/token",
                    AuthorizationEndpoint = "https://issuer.example/authorize",
                    Servers = new ArrayOf<OAuth2ServerSettings>(new[]
                    {
                        new OAuth2ServerSettings
                        {
                            ApplicationUri = "https://localhost/server",
                            ResourceId = "resource",
                            Scopes = new ArrayOf<string>(s_scopes)
                        }
                    })
                }
            });
            return configuration;
        }

        private static void SetSourceFilePath(ApplicationConfiguration configuration, string filePath)
        {
            typeof(ApplicationConfiguration)
                .GetProperty(nameof(ApplicationConfiguration.SourceFilePath))!
                .SetValue(configuration, filePath);
        }

        private static readonly string[] s_scopes = ["scope-a", "scope-b"];
    }
}
