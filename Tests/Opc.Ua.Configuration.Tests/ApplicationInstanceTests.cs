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
using System.Runtime.InteropServices;
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
        #endregion

        #region Test Methods
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
        public void TestBadNoApplicationNameConfigAsServer()
        {
            var applicationInstance = new ApplicationInstance();
            Assert.NotNull(applicationInstance);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
               await applicationInstance.Build(ApplicationUri, ProductUri)
                   .AsServer(new string[] { "opc.tcp://localhost:51000" })
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
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
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
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
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
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
                .AddUnsecurePolicyNone()
                .AddSignPolicies()
                .AddSignAndEncryptPolicies()
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                .SetDiagnosticsEnabled(true)
                .AsClient()
                .AddSecurityConfiguration(SubjectName)
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
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
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
                .AsServer(new string[] { "opc.tcp://localhost:51000", "https://localhost:51001" }, new string[] { "opc.tcp://192.168.1.100:51000" })
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
