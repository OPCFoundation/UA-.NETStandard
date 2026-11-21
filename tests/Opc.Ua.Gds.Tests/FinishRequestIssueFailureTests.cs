// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
