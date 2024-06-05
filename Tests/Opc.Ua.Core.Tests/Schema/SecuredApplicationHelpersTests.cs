using Opc.Ua.Security;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Schema
{
    /// <summary>
    /// Tests for the CertificateValidator class.
    /// </summary>
    [TestFixture, Category("SecuredApplicationHelpers")]
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
            Assert.That(
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                <
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15));
            Assert.That(
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                <
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Aes256_Sha256_RsaPss));
        }
        /// <summary>
        /// Verify CalculateSecurityLevel none or Invalid MessageSecurityMode return 0
        /// </summary>
        [Test]
        public void CalculateSecurityLevelNoneOrInvalidZero()
        {
            Assert.That(
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.None, SecurityPolicies.Basic128Rsa15)
                == 0);
            Assert.That(
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Invalid, SecurityPolicies.Basic128Rsa15)
                == 0);
        }

        /// <summary>
        /// Verify CalculateSecurityLevel none or Invalid MessageSecurityMode return 0
        /// </summary>
        [Test]
        public void CalculateSecurityLevelOrderValid()
        {
            Assert.That(
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                <
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Basic256));

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                <
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256));

            Assert.That(
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256)
                <
                SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Aes128_Sha256_RsaOaep));

            Assert.That(
               SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Aes128_Sha256_RsaOaep)
               <
               SecuredApplication.CalculateSecurityLevel(MessageSecurityMode.Sign, SecurityPolicies.Aes256_Sha256_RsaPss));

        }
    }
}
