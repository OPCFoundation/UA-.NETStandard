/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
    }
}
