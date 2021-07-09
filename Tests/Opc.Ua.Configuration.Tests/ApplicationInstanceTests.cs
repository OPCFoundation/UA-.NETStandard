/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("ApplicationInstance")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationInstanceTests
    {
        #region Test Constants
        public const string ApplicationName = "UA Configuration Test";
        public const string ApplicationUri = "urn:localhost:opcfoundation.org:ConfigurationTest";
        public const string ProductUri = "http://opcfoundation.org/UA/ConfigurationTest";
        public const string SubjectName = "CN=UA Configuration Test";
        public const string EndpointUrl = "opc.tcp://localhost:51000";
        #endregion

        #region Test Methods
        [Test]
        public bool HttpHandler()
        {

            // auto validate server cert, if supported
            // if unsupported, the TLS server cert must be trusted by a root CA
            var handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            // send client certificate for servers that require TLS client authentication
            X509Certificate2 clientCertificate = new X509Certificate2();
            var propertyInfo = handler.GetType().GetProperty("ClientCertificates");
            X509CertificateCollection clientCertificates = (X509CertificateCollection)propertyInfo.GetValue(handler);
            clientCertificates.Add(clientCertificate);

            propertyInfo = handler.GetType().GetProperty("ServerCertificateCustomValidationCallback");
            Func<HttpRequestMessage, X509Certificate2, X509Chain, System.Net.Security.SslPolicyErrors, bool>
                serverCertificateCustomValidationCallback = (Func<HttpRequestMessage, X509Certificate2, X509Chain, System.Net.Security.SslPolicyErrors, bool>)propertyInfo.GetValue(handler);



            // OSX platform cannot auto validate certs and throws
            // on PostAsync, do not set validation handler
            //if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {

                try
                    {
                        serverCertificateCustomValidationCallback =
                            (httpRequestMessage, cert, chain, policyErrors) => {
                                try
                                {
                                    //m_quotas.CertificateValidator?.Validate(cert);
                                    return true;
                                }
                                catch (Exception ex)
                                {
                                    Utils.Trace("HTTPS: Failed to validate server cert: " + cert.Subject);
                                    Utils.Trace("HTTPS: Exception:" + ex.Message);
                                }
                                return false;
                            };
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // client may throw if not supported (e.g. UWP)
                        handler.ServerCertificateCustomValidationCallback = null;
                    }

            }
            return false;
        }

        /// <summary>
        /// Load a file configuration.
        /// </summary>
        [Test]
        public async Task TestFileConfig()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            string configPath = Opc.Ua.Utils.GetAbsoluteFilePath("Opc.Ua.Configuration.Tests.Config.xml", true, false, false);
            Assert.NotNull(configPath);
            ApplicationConfiguration applicationConfiguration = await applicationInstance.LoadApplicationConfiguration(configPath, true).ConfigureAwait(false);
            Assert.NotNull(applicationConfiguration);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsClient()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestBadApplicationInstance()
        {
            // no app name
            var applicationInstance = new ApplicationInstance();
            Assert.NotNull(applicationInstance);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsServer(new string[] { EndpointUrl })
                   .AddSecurityConfiguration(SubjectName)
                   .Create()
            );
            // discoveryserver can not be combined with client/server
            applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.DiscoveryServer
            };
            Assert.ThrowsAsync<ArgumentException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsClient()
                   .AddSecurityConfiguration(SubjectName)
                   .Create()
            );
            Assert.ThrowsAsync<ArgumentException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsServer(new string[] { EndpointUrl })
                   .AddSecurityConfiguration(SubjectName)
                   .Create()
            );
            // server overrides client settings
            applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            };

            var config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsServer(new string[] { EndpointUrl })
                .AddSecurityConfiguration(SubjectName)
                .Create();
            Assert.AreEqual(ApplicationType.Server, applicationInstance.ApplicationType);

            // client overrides server setting
            applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Server
            };

            await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName)
                .Create();
            Assert.AreEqual(ApplicationType.Client, applicationInstance.ApplicationType);

            // invalid sec policy testing
            applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            // invalid use, use AddUnsecurePolicyNone instead
            Assert.ThrowsAsync<ArgumentException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsServer(new string[] { EndpointUrl })
                   .AddPolicy(MessageSecurityMode.None, SecurityPolicies.None)
                   .AddSecurityConfiguration(SubjectName)
                   .Create()
            );
            // invalid mix sign / none
            Assert.ThrowsAsync<ArgumentException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsServer(new string[] { EndpointUrl })
                   .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.None)
                   .AddSecurityConfiguration(SubjectName)
                   .Create()
            );
            // invalid policy
            Assert.ThrowsAsync<ArgumentException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsServer(new string[] { EndpointUrl })
                   .AddPolicy(MessageSecurityMode.Sign, "123")
                   .AddSecurityConfiguration(SubjectName)
                   .Create()
            );
            // invalid user token policy
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsServer(new string[] { EndpointUrl })
                   .AddUserTokenPolicy(null)
                   .AddSecurityConfiguration(SubjectName)
                   .Create()
            );
        }

        [Test]
        public async Task TestNoFileConfigAsServerMinimal()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .SetOperationTimeout(10000)
                .AsServer(new string[] { EndpointUrl })
                .AddSecurityConfiguration(SubjectName)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsServerMaximal()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .SetTransportQuotas(new TransportQuotas() { OperationTimeout = 10000 })
                .AsServer(new string[] { EndpointUrl })
                .AddSignPolicies()
                .AddSignAndEncryptPolicies()
                .AddUnsecurePolicyNone()
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15)
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .AddUserTokenPolicy(new UserTokenPolicy(UserTokenType.Certificate) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 })
                .SetDiagnosticsEnabled(true)
                .SetPublishingResolution(100)
                .AddSecurityConfiguration(SubjectName)
                .SetAddAppCertToTrustedStore(true)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetMinimumCertificateKeySize(1024)
                .SetRejectSHA1SignedCertificates(false)
                .SetSendCertificateChain(true)
                .SetSuppressNonceValidationErrors(true)
                .SetRejectUnknownRevocationStatus(true)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsClientAndServer()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .SetMaxBufferSize(32768)
                .AsServer(new string[] { EndpointUrl })
                .AddUnsecurePolicyNone()
                .AddSignPolicies()
                .AddSignAndEncryptPolicies()
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                .SetDiagnosticsEnabled(true)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, CertificateStoreType.Directory, CertificateStoreType.X509Store)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsServerX509Store()
        {
#if NETCOREAPP2_1_OR_GREATER
            // this test fails on macOS, ignore
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("X509Store trust lists not supported on mac OS.");
            }
#endif
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsServer(new string[] { EndpointUrl })
                .AddUnsecurePolicyNone()
                .AddSignAndEncryptPolicies()
                .AddUserTokenPolicy(UserTokenType.UserName)
                .AsClient()
                .SetDefaultSessionTimeout(10000)
                .AddSecurityConfiguration(SubjectName, CertificateStoreType.X509Store)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            using (ICertificateStore store = applicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.OpenStore())
            {
                await store.Add(applicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate);
            }
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsServerCustom()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsServer(new string[] { EndpointUrl, "https://localhost:51001" }, new string[] { "opc.tcp://192.168.1.100:51000" })
                .AddSecurityConfiguration(SubjectName)
                .SetAddAppCertToTrustedStore(true)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }
        #endregion
    }
}
