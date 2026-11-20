/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("KeyCredential")]
    [Parallelizable]
    public sealed class KeyCredentialConformanceTests
    {
        private const string ApplicationUri = "urn:test:keycredential-owner";

        [Test]
        public void ApplicationUriResolvesOneExactRegisteredApplication()
        {
            var applicationId = new NodeId(42);
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            database
                .Setup(db => db.FindApplications(ApplicationUri))
                .Returns(
                [
                    new ApplicationRecordDataType
                    {
                        ApplicationId = applicationId,
                        ApplicationUri = ApplicationUri
                    }
                ]);

            NodeId resolved = ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                database.Object,
                ApplicationUri);

            Assert.That(resolved, Is.EqualTo(applicationId));
        }

        [Test]
        public void UnknownApplicationUriReturnsBadNotFound()
        {
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            database
                .Setup(db => db.FindApplications(ApplicationUri))
                .Returns([]);

            Assert.That(
                () => ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                    database.Object,
                    ApplicationUri),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void NullDatabaseThrowsArgumentNullException()
        {
            Assert.That(
                () => ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                    null!,
                    ApplicationUri),
                Throws.ArgumentNullException);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void EmptyApplicationUriReturnsBadNotFound(string? applicationUri)
        {
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);

            Assert.That(
                () => ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                    database.Object,
                    applicationUri!),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void NullApplicationSearchResultReturnsBadNotFound()
        {
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            database
                .Setup(db => db.FindApplications(ApplicationUri))
                .Returns((ApplicationRecordDataType[]?)null);

            Assert.That(
                () => ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                    database.Object,
                    ApplicationUri),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void NonExactApplicationUriMatchReturnsBadNotFound()
        {
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            database
                .Setup(db => db.FindApplications(ApplicationUri))
                .Returns(
                [
                    new ApplicationRecordDataType
                    {
                        ApplicationId = new NodeId(42),
                        ApplicationUri = ApplicationUri.ToUpperInvariant()
                    }
                ]);

            Assert.That(
                () => ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                    database.Object,
                    ApplicationUri),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void NullApplicationIdReturnsBadConfigurationError()
        {
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            database
                .Setup(db => db.FindApplications(ApplicationUri))
                .Returns(
                [
                    new ApplicationRecordDataType
                    {
                        ApplicationId = NodeId.Null,
                        ApplicationUri = ApplicationUri
                    }
                ]);

            Assert.That(
                () => ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                    database.Object,
                    ApplicationUri),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void DuplicateApplicationUriReturnsBadConfigurationError()
        {
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            database
                .Setup(db => db.FindApplications(ApplicationUri))
                .Returns(
                [
                    new ApplicationRecordDataType
                    {
                        ApplicationId = new NodeId(42),
                        ApplicationUri = ApplicationUri
                    },
                    new ApplicationRecordDataType
                    {
                        ApplicationId = new NodeId(43),
                        ApplicationUri = ApplicationUri
                    }
                ]);

            Assert.That(
                () => ApplicationsNodeManager.ResolveKeyCredentialApplicationId(
                    database.Object,
                    ApplicationUri),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void PendingFinishReturnsBadRequestNotComplete()
        {
            var finished = new FinishKeyCredentialRequestResult
            {
                State = KeyCredentialRequestState.New,
                CredentialId = "must-not-be-returned",
                CredentialSecret = ByteString.From([1, 2, 3])
            };

            KeyCredentialFinishRequestMethodStateResult result =
                ApplicationsNodeManager.CreateKeyCredentialFinishResult(
                    finished,
                    cancelRequest: false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotComplete));
            Assert.That(result.CredentialId, Is.Null.Or.Empty);
            Assert.That(result.CredentialSecret.IsEmpty, Is.True);
        }

        [Test]
        public void CompletedFinishReturnsAllCredentialOutputs()
        {
            ArrayOf<NodeId> grantedRoles =
                new NodeId[] { new(1), new(2) }.ToArrayOf();
            var finished = new FinishKeyCredentialRequestResult
            {
                State = KeyCredentialRequestState.Completed,
                CredentialId = "KC-42",
                CredentialSecret = ByteString.From([1, 2, 3]),
                CertificateThumbprint = "thumbprint",
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                GrantedRoles = grantedRoles
            };

            KeyCredentialFinishRequestMethodStateResult result =
                ApplicationsNodeManager.CreateKeyCredentialFinishResult(
                    finished,
                    cancelRequest: false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.CredentialId, Is.EqualTo("KC-42"));
                Assert.That(result.CredentialSecret.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(result.CertificateThumbprint, Is.EqualTo("thumbprint"));
                Assert.That(result.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(result.GrantedRoles, Is.EqualTo(grantedRoles));
            });
        }

        [TestCase(KeyCredentialRequestState.Approved)]
        [TestCase(KeyCredentialRequestState.Completed)]
        public void NonPendingFinishStateReturnsGood(KeyCredentialRequestState state)
        {
            ServiceResult result =
                ApplicationsNodeManager.GetKeyCredentialFinishServiceResult(
                    state,
                    cancelRequest: false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void RejectedFinishReturnsBadRequestNotAllowed()
        {
            ServiceResult result =
                ApplicationsNodeManager.GetKeyCredentialFinishServiceResult(
                    KeyCredentialRequestState.Rejected,
                    cancelRequest: false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
        }

        [Test]
        public void CancelledRejectedFinishReturnsGoodWithoutCredentialOutputs()
        {
            var finished = new FinishKeyCredentialRequestResult
            {
                State = KeyCredentialRequestState.Rejected,
                CredentialId = "must-not-be-returned",
                CredentialSecret = ByteString.From([1, 2, 3])
            };

            KeyCredentialFinishRequestMethodStateResult result =
                ApplicationsNodeManager.CreateKeyCredentialFinishResult(
                    finished,
                    cancelRequest: true);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.CredentialId, Is.Null.Or.Empty);
            Assert.That(result.CredentialSecret.IsEmpty, Is.True);
        }
    }
}
