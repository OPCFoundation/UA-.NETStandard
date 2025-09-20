using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Security;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Schema
{
    /// <summary>
    /// Tests for the CertificateValidator class.
    /// </summary>
    [TestFixture]
    [Category("SecuredApplicationHelpers")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class SecuredApplicationHelpersTests
    {
        /// <summary>
        /// Verify CalculateSecurityLevel encryption is a higher security Level than signing
        /// </summary>
        [Test]
        public void CalculateSecurityLevelEncryptionStrongerSigning()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<SecuredApplicationHelpersTests>();

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Basic128Rsa15,
                    logger) <
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Basic128Rsa15,
                    logger));
            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Basic128Rsa15,
                    logger) <
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Aes256_Sha256_RsaPss,
                    logger));
        }

        /// <summary>
        /// Verify CalculateSecurityLevel none or Invalid MessageSecurityMode return 0
        /// </summary>
        [Test]
        public void CalculateSecurityLevelNoneOrInvalidZero()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<SecuredApplicationHelpersTests>();

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.None,
                    SecurityPolicies.Basic128Rsa15,
                    logger) == 0);
            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Invalid,
                    SecurityPolicies.Basic128Rsa15,
                    logger) ==
                    0);
        }

        /// <summary>
        /// Verify CalculateSecurityLevel none or Invalid MessageSecurityMode return 0
        /// </summary>
        [Test]
        public void CalculateSecurityLevelOrderValid()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<SecuredApplicationHelpersTests>();

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Basic128Rsa15, logger) <
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Basic256, logger));

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Basic256, logger) <
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Basic256Sha256, logger));

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Basic256Sha256, logger) <
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Aes128_Sha256_RsaOaep, logger));

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Aes128_Sha256_RsaOaep,
                    logger
                ) <
                SecuredApplication.CalculateSecurityLevel(
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Aes256_Sha256_RsaPss, logger));
        }
    }
}
