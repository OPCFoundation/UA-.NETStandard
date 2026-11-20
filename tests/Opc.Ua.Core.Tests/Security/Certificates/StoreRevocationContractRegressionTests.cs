/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Opc.Ua.X509StoreExtensions;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Covers the revocation status returned when the platform certificate store cannot process CRLs.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [NonParallelizable]
    public sealed class StoreRevocationContractRegressionTests
    {
        /// <summary>
        /// Verifies unsupported operating-system revocation checks return BadCertificateRevocationUnknown.
        /// </summary>
        [Test]
        public async Task UnsupportedOsRevocationReturnsUnknownStatusAsync()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=Revocation Capability")
                .SetRSAKeySize(2048).CreateForRSA();
            using var store = new X509CertificateStore(NUnitTelemetryContext.Create());
            FieldInfo capability = typeof(PlatformHelper).GetField(
                "s_isWindowsWithCrlSupport", BindingFlags.NonPublic | BindingFlags.Static)!;
            bool? previous = (bool?)capability.GetValue(null);
            capability.SetValue(null, false);
            try
            {
                Assert.That(store.SupportsCRLs, Is.False);
                StatusCode status = await store.IsRevokedAsync(certificate, certificate).ConfigureAwait(false);
                Assert.That(status, Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown));
            }
            finally
            {
                capability.SetValue(null, previous);
            }
        }
    }
}
