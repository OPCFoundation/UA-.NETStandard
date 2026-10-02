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
using NUnit.Framework;
using Opc.Ua.Gds.Server;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Tests the FinishRequest result for a certificate the group failed to
    /// issue (OPC 10000-12 §7.9.5).
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public sealed class FinishRequestIssueFailureTests
    {
        private static readonly NodeId s_applicationId = new(Guid.NewGuid(), 2);

        [Test]
        public void ServiceResultExceptionKeepsItsStatus()
        {
            var exception = new ServiceResultException(
                StatusCodes.BadInvalidArgument,
                "The CertificateRequest has an RSA public key.");

            ServiceResult result = ApplicationsNodeManager.CreateIssueFailureResult(
                exception,
                "Certificate",
                s_applicationId,
                CreateApplication("App"));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.LocalizedText.Text, Does.Contain("RSA public key"));
            Assert.That(result.LocalizedText.Text, Does.Contain("ApplicationName=App"));
        }

        [Test]
        public void OtherExceptionMapsToBadRequestNotAllowed()
        {
            var exception = new InvalidOperationException("CA key unavailable.");

            ServiceResult result = ApplicationsNodeManager.CreateIssueFailureResult(
                exception,
                "New Key Pair Certificate",
                s_applicationId,
                CreateApplication("App"));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(result.StatusCode, Is.Not.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(
                result.LocalizedText.Text,
                Does.Contain("New Key Pair Certificate=CA key unavailable."));
        }

        [Test]
        public void EmptyApplicationNamesDoNotMaskTheFailure()
        {
            var exception = new ServiceResultException(StatusCodes.BadNotSupported, "Unsupported key.");
            var application = new ApplicationRecordDataType { ApplicationUri = "urn:test:app" };

            ServiceResult result = ApplicationsNodeManager.CreateIssueFailureResult(
                exception,
                "Certificate",
                s_applicationId,
                application);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(result.LocalizedText.Text, Does.Contain("Unsupported key."));
            Assert.That(result.LocalizedText.Text, Does.Contain("ApplicationUri=urn:test:app"));
        }

        private static ApplicationRecordDataType CreateApplication(string name)
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = "urn:test:app",
                ApplicationNames = [new LocalizedText(name)]
            };
        }
    }
}