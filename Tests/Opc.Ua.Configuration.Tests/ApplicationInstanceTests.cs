/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
            var configPath = Opc.Ua.Utils.GetAbsoluteFilePath("Opc.Ua.Configuration.Tests.Config.xml", true, false, false);
            Assert.NotNull(configPath);
            var applicationConfiguration = await applicationInstance.LoadApplicationConfiguration(configPath, true).ConfigureAwait(false);
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
            var config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsServerMinimal()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            var config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
                .AddSecurityConfiguration(SubjectName)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Theory]
        public async Task TestNoFileConfigAsServerMaximal(bool deprecated)
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            var config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
                .AddSignPolicies(deprecated)
                .AddSignAndEncryptPolicies(deprecated)
                .AddUnsecurePolicyNone()
                .AddUserTokenPolicy(UserTokenType.UserName, true)
                .AddUserTokenPolicy(UserTokenType.Certificate)
                .AddSecurityConfiguration(SubjectName)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Theory]
        public async Task TestNoFileConfigAsClientAndServer(bool deprecated)
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            var config = await applicationInstance.Build(ApplicationUri, ProductUri)
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
                .AddUnsecurePolicyNone()
                .AddSignAndEncryptPolicies(deprecated)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .AsClient()
                .AddSecurityConfiguration(SubjectName)
                .Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }

        [Test]
        public async Task TestNoFileConfigAsServerCustom()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ApplicationName
            };
            Assert.NotNull(applicationInstance);
            var builder = applicationInstance.Build(ApplicationUri, ProductUri)
                .AsServer(new string[] { "opc.tcp://localhost:51000" })
                .AddSecurityConfiguration(SubjectName);
            builder.ApplicationConfiguration.SecurityConfiguration.AddAppCertToTrustedStore = true;
            var config = await builder.Create().ConfigureAwait(false);
            Assert.NotNull(config);
            bool certOK = await applicationInstance.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            Assert.True(certOK);
        }
        #endregion
    }
}
