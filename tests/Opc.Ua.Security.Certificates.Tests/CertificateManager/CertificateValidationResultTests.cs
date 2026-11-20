/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateValidationResultTests
    {
        [Test]
        public void SuccessStaticIsValid()
        {
            Assert.That(CertificateValidationResult.Success.IsValid, Is.True);
        }

        [Test]
        public void SuccessStaticHasGoodStatusCode()
        {
            Assert.That(
                CertificateValidationResult.Success.StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ErrorResultIsNotValid()
        {
            var result = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.Bad,
                errors: [new ServiceResult(StatusCodes.Bad)],
                isSuppressible: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void IsSuppressibleReflectsConstructorArg()
        {
            var suppressible = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.Bad,
                errors: [],
                isSuppressible: true);

            var notSuppressible = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.Bad,
                errors: [],
                isSuppressible: false);

            Assert.That(suppressible.IsSuppressible, Is.True);
            Assert.That(notSuppressible.IsSuppressible, Is.False);
        }

        [Test]
        public void ThrowIfInvalidReturnsSilentlyOnSuccess()
        {
            Assert.DoesNotThrow(CertificateValidationResult.Success.ThrowIfInvalid);
        }

        [Test]
        public void ThrowIfInvalidThrowsWithFirstErrorWhenErrorsPresent()
        {
            var firstError = new ServiceResult(StatusCodes.BadCertificateUntrusted);
            var secondError = new ServiceResult(StatusCodes.BadCertificateInvalid);
            var result = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.BadCertificateChainIncomplete,
                errors: [firstError, secondError],
                isSuppressible: false);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                result.ThrowIfInvalid);
            Assert.That(
                ex.StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateUntrusted),
                "ThrowIfInvalid should surface the first inner error, not the aggregate StatusCode.");
        }

        [Test]
        public void ThrowIfInvalidThrowsWithStatusCodeWhenErrorsEmpty()
        {
            var result = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.BadCertificateUntrusted,
                errors: [],
                isSuppressible: false);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                result.ThrowIfInvalid);
            Assert.That(
                ex.StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateUntrusted));
        }
    }
}
