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
                ApplicationsNodeManager.CreateKeyCredentialFinishResult(finished);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotComplete));
            Assert.That(result.CredentialId, Is.Null.Or.Empty);
            Assert.That(result.CredentialSecret.IsEmpty, Is.True);
        }

        [Test]
        public void CompletedFinishReturnsGood()
        {
            ServiceResult result = ApplicationsNodeManager.GetKeyCredentialFinishServiceResult(
                KeyCredentialRequestState.Completed);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }
    }
}
