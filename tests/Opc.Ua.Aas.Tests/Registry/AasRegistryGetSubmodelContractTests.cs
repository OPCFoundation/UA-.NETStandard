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
using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Tests.Registry
{
    /// <summary>
    /// Tests the security-critical GetSubmodel contract from clause 6.5.5.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasRegistryGetSubmodelContractTests
    {
        /// <summary>
        /// Verifies an authorized caller receives the document and parse metadata.
        /// </summary>
        [Test]
        public async Task AuthorizedCallerGetsDocumentFormatAndContentType()
        {
            Mock<IAasRegistryAuthorizationEvaluator> auth = Auth(authorized: true, authenticated: true);
            var service = new AasRegistryService(authorizationEvaluator: auth.Object);
            await service.UpsertResourceAsync(Request("submodel", "document"));

            AasGetSubmodelResult result = await service.GetSubmodelAsync("submodel");

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(Encoding.UTF8.GetString(result.Document.ToArray()), Is.EqualTo("document"));
                Assert.That(result.Format, Is.EqualTo("aas/3.0+json"));
                Assert.That(result.ContentType, Is.EqualTo("application/aas+json"));
            });
        }

        /// <summary>
        /// Verifies target RolePermissions and UserRolePermissions denial returns Bad_UserAccessDenied.
        /// </summary>
        [Test]
        public async Task TargetRolePermissionsDenialReturnsUserAccessDenied()
        {
            Mock<IAasRegistryAuthorizationEvaluator> auth = Auth(authorized: false, authenticated: true);
            var service = new AasRegistryService(authorizationEvaluator: auth.Object);
            await service.UpsertResourceAsync(ControlledRequest("submodel", conceal: false));

            AasGetSubmodelResult result = await service.GetSubmodelAsync("submodel");

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                AssertNoTargetMetadata(result);
            });
        }

        /// <summary>
        /// Verifies permission to call the method is never substituted for target read authorization.
        /// </summary>
        [Test]
        public async Task MethodCallPermissionDoesNotAuthorizeTargetRead()
        {
            Mock<IAasRegistryAuthorizationEvaluator> auth = Auth(authorized: false, authenticated: true);
            var service = new AasRegistryService(authorizationEvaluator: auth.Object);
            await service.UpsertResourceAsync(ControlledRequest("submodel", conceal: false));

            AasGetSubmodelResult result = await service.GetSubmodelAsync("submodel");

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                AssertNoTargetMetadata(result);
                auth.Verify(evaluator => evaluator.CanReadSubmodel(
                    It.IsAny<ISystemContext?>(),
                    It.IsAny<AasRegistryResource>()), Times.Once);
            });
        }

        /// <summary>
        /// Verifies concealment and true absence are both externally Bad_NotFound.
        /// </summary>
        [Test]
        public async Task ConcealmentAndNonexistentTargetsBothReturnNotFound()
        {
            Mock<IAasRegistryAuthorizationEvaluator> auth = Auth(authorized: false, authenticated: true);
            var service = new AasRegistryService(authorizationEvaluator: auth.Object);
            await service.UpsertResourceAsync(ControlledRequest("submodel", conceal: true));

            AasGetSubmodelResult concealed = await service.GetSubmodelAsync("submodel");
            AasGetSubmodelResult missing = await service.GetSubmodelAsync("missing");

            Assert.Multiple(() =>
            {
                Assert.That(concealed.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                AssertNoTargetMetadata(concealed);
                AssertNoTargetMetadata(missing);
            });
        }

        /// <summary>
        /// Concealment has to hold in the AddressSpace, not only at the Method.
        /// The disclosure decision lived entirely inside GetSubmodel, so a
        /// concealed submodel answered BadNotFound while the projection still
        /// published its node - and with it the identifier, the semanticId and
        /// the content digest - to an anonymous Browse. Clause 6.5.7 requires
        /// a Server that must not reveal the existence of controlled content
        /// to omit the entry rather than mark it, so the two views have to
        /// agree.
        /// </summary>
        [Test]
        public async Task AConcealedResourceIsAbsentFromTheProjectionAsWellAsTheMethod()
        {
            Mock<IAasRegistryAuthorizationEvaluator> auth = Auth(authorized: false, authenticated: true);
            var service = new AasRegistryService(authorizationEvaluator: auth.Object);
            await service.UpsertResourceAsync(ControlledRequest("concealed", conceal: true));
            await service.UpsertResourceAsync(ControlledRequest("visible", conceal: false));

            AasGetSubmodelResult concealed = await service.GetSubmodelAsync("concealed");
            var projected = new List<string>();
            foreach (IXRegistryProjectionGroup group in ((IXRegistryProjectionSnapshot)service.Current).Groups)
            {
                foreach (IXRegistryProjectionResource resource in group.Resources)
                {
                    projected.Add(resource.ResourceId);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(concealed.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(projected, Does.Not.Contain("concealed"),
                    "Browse must not reveal what GetSubmodel refuses to admit exists.");
                Assert.That(projected, Does.Contain("visible"));
            });
        }

        /// <summary>
        /// Verifies concealed and nonexistent paths pass the same final branch point.
        /// </summary>
        [Test]
        public async Task ConcealedAndNonexistentTargetsUseSameStructuralBranchPoint()
        {
            var observer = new RecordingObserver();
            Mock<IAasRegistryAuthorizationEvaluator> auth = Auth(authorized: false, authenticated: true);
            var service = new AasRegistryService(
                authorizationEvaluator: auth.Object,
                accessPathObserver: observer);
            await service.UpsertResourceAsync(ControlledRequest("submodel", conceal: true));

            await service.GetSubmodelAsync("submodel");
            await service.GetSubmodelAsync("missing");

            Assert.Multiple(() =>
            {
                Assert.That(observer.Calls, Is.EqualTo(2));
                Assert.That(observer.Stages, Is.EqualTo(ExpectedBranchStages));
                Assert.That(observer.ExistsFlags, Is.EqualTo(ExpectedExistsFlags));
            });
        }

        private static AasUpsertResourceRequest Request(string submodel, string document)
        {
            return new AasUpsertResourceRequest
            {
                GroupSourceIdentity = "shell",
                ResourceSourceIdentity = submodel,
                GroupKind = AasRegistryEntityKind.Shell,
                ResourceKind = AasRegistryEntityKind.Submodel,
                Content = ByteString.From(Encoding.UTF8.GetBytes(document)),
                ContentType = "application/aas+json",
                Format = "aas/3.0+json"
            };
        }

        private static AasUpsertResourceRequest ControlledRequest(string submodel, bool conceal)
        {
            AasUpsertResourceRequest request = Request(submodel, "secret");
            request.DisclosureTier = AASDisclosureTierDataType.Controlled;
            request.ConcealFromUnauthorized = conceal;
            return request;
        }

        private static Mock<IAasRegistryAuthorizationEvaluator> Auth(bool authorized, bool authenticated)
        {
            var mock = new Mock<IAasRegistryAuthorizationEvaluator>(MockBehavior.Strict);
            mock.Setup(evaluator => evaluator.IsAuthenticated(It.IsAny<ISystemContext?>()))
                .Returns(authenticated);
            mock.Setup(evaluator => evaluator.CanReadSubmodel(
                    It.IsAny<ISystemContext?>(),
                    It.IsAny<AasRegistryResource>()))
                .Returns(authorized);
            return mock;
        }

        private static void AssertNoTargetMetadata(AasGetSubmodelResult result)
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.Document.Length, Is.Zero);
                Assert.That(result.Format, Is.Empty);
                Assert.That(result.ContentType, Is.Empty);
                Assert.That(typeof(AasGetSubmodelResult).GetProperty("Size"), Is.Null);
                Assert.That(typeof(AasGetSubmodelResult).GetProperty("ContentLength"), Is.Null);
                Assert.That(typeof(AasGetSubmodelResult).GetProperty("Digest"), Is.Null);
                Assert.That(typeof(AasGetSubmodelResult).GetProperty("DigestHex"), Is.Null);
            });
        }

        private sealed class RecordingObserver : IAasRegistryAccessPathObserver
        {
            public int Calls { get; private set; }
            public string[] Stages => m_stages.ToArray();
            public bool[] ExistsFlags => m_exists.ToArray();

            public void OnResolvedAndAuthorized(
                string submodelIdentifier,
                bool exists,
                bool authorized,
                bool concealed)
            {
                Calls++;
                m_stages.Add("resolved-authorized-branch");
                m_exists.Add(exists);
            }

            private readonly System.Collections.Generic.List<string> m_stages = [];
            private readonly System.Collections.Generic.List<bool> m_exists = [];
        }

        private static readonly string[] ExpectedBranchStages =
        [
            "resolved-authorized-branch",
            "resolved-authorized-branch"
        ];

        private static readonly bool[] ExpectedExistsFlags = [true, false];
    }
}
