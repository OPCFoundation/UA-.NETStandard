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
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Registry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.Aas.Tests.Registry
{
    /// <summary>
    /// Tests for the AAS registry service snapshot and discovery behavior.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasRegistryServiceTests
    {
        /// <summary>
        /// Verifies committed snapshots advance generations and previous readers keep immutable views.
        /// </summary>
        [Test]
        public async Task UpsertResourceAdvancesGenerationAndPreservesExistingSnapshots()
        {
            var service = new AasRegistryService();
            AasRegistrySnapshot before = service.Current;

            await service.UpsertResourceAsync(NewSubmodelRequest("urn:shell:1", "urn:submodel:1", "one"));

            AasRegistrySnapshot after = service.Current;
            Assert.Multiple(() =>
            {
                Assert.That(after.Generation, Is.GreaterThan(before.Generation));
                Assert.That(before.GroupsById, Is.Empty);
                Assert.That(after.GroupsById, Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// Verifies source identities use the xRegistry construction and do not change when bytes change.
        /// </summary>
        [Test]
        public async Task IdentifierConstructionMatchesXRegistryAndIsInvariantAcrossVersions()
        {
            var service = new AasRegistryService();
            string shell = "https://example.com/assets/pump/42";
            string submodel = "https://example.com/submodels/nameplate";

            await service.UpsertResourceAsync(NewSubmodelRequest(shell, submodel, "one"));
            AasRegistryResource first = service.Current.FindSubmodelBySourceIdentity(submodel)!;
            await service.UpsertResourceAsync(NewSubmodelRequest(shell, submodel, "two"));
            AasRegistryResource second = service.Current.FindSubmodelBySourceIdentity(submodel)!;

            Assert.Multiple(() =>
            {
                Assert.That(first.GroupId, Is.EqualTo(XRegistryIdentifier.FromSourceIdentity(shell)));
                Assert.That(first.ResourceId, Is.EqualTo(XRegistryIdentifier.FromSourceIdentity(submodel)));
                Assert.That(second.ResourceId, Is.EqualTo(first.ResourceId));
                Assert.That(second.Versions.Select(v => v.DigestHex).Distinct().ToArray(), Has.Length.EqualTo(2));
            });
        }

        /// <summary>
        /// Verifies version timestamps define as-of resolution while AAS labels remain unchanged metadata.
        /// </summary>
        [Test]
        public async Task VersionOrderingResolvesNewestVersionNotLaterThanMoment()
        {
            var service = new AasRegistryService();
            DateTime firstTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime secondTime = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            AasUpsertResourceRequest first = NewSubmodelRequest("shell", "submodel", "one");
            first.CreatedAt = firstTime;
            first.AdministrationVersion = "aas-v1";
            first.AdministrationRevision = "r1";
            AasUpsertResourceRequest second = NewSubmodelRequest("shell", "submodel", "two");
            second.CreatedAt = secondTime;
            second.AdministrationVersion = "aas-v1";
            second.AdministrationRevision = "r2";

            await service.UpsertResourceAsync(first);
            await service.UpsertResourceAsync(second);

            AasRegistryResource resource = service.Current.FindSubmodelBySourceIdentity("submodel")!;
            AasRegistryResourceVersion? beforeFirst = resource.FindVersionAsOf(firstTime.AddTicks(-1));
            AasRegistryResourceVersion? atFirst = resource.FindVersionAsOf(firstTime);
            AasRegistryResourceVersion? between = resource.FindVersionAsOf(firstTime.AddDays(1));
            AasRegistryResourceVersion? atSecond = resource.FindVersionAsOf(secondTime);
            Assert.Multiple(() =>
            {
                Assert.That(beforeFirst, Is.Null);
                Assert.That(atFirst, Is.Not.Null);
                Assert.That(atFirst!.CreatedAt, Is.EqualTo(firstTime));
                Assert.That(between, Is.Not.Null);
                Assert.That(between!.CreatedAt, Is.EqualTo(firstTime));
                Assert.That(atSecond, Is.Not.Null);
                Assert.That(atSecond!.CreatedAt, Is.EqualTo(secondTime));
                Assert.That(resource.DefaultVersion!.CreatedAt, Is.EqualTo(secondTime));
                Assert.That(resource.DefaultVersion.AdministrationVersion, Is.EqualTo("aas-v1"));
                Assert.That(resource.DefaultVersion.AdministrationRevision, Is.EqualTo("r2"));
                Assert.That(resource.DefaultVersion.VersionId, Does.Not.Contain("aas-v1"));
                Assert.That(resource.DefaultVersion.VersionId, Does.Not.Contain("r2"));
            });
        }

        /// <summary>
        /// Verifies specific asset id lookup finds shells and unauthenticated responses are bounded.
        /// </summary>
        [Test]
        public async Task LookupShellsByAssetLinkFindsShellAndBoundsUnauthenticatedResults()
        {
            var service = new AasRegistryService(bounds: new AasRegistryPersistenceBounds
            {
                MaxUnauthenticatedCollectionResults = 1
            });
            await service.UpsertResourceAsync(NewSubmodelRequest("shell-a", "submodel-a", "one", "serial", "42"));
            await service.UpsertResourceAsync(NewSubmodelRequest("shell-b", "submodel-b", "two", "serial", "42"));

            ArrayOf<string> shells = service.LookupShellsByAssetLink("serial", "42");
            ArrayOf<string> unknown = service.LookupShellsByAssetLink("serial", "unknown");

            Assert.Multiple(() =>
            {
                Assert.That(shells, Has.Count.EqualTo(1));
                Assert.That(shells[0], Is.EqualTo("shell-a"));
                Assert.That(unknown, Is.Empty);
            });
        }

        /// <summary>
        /// Verifies GetSubmodel denies access without leaking bytes or metadata.
        /// </summary>
        [Test]
        public async Task GetSubmodelDeniedLeaksNoMetadata()
        {
            Mock<IAasRegistryAuthorizationEvaluator> auth = DenyingAuthenticatedState(false);
            var service = new AasRegistryService(authorizationEvaluator: auth.Object);
            AasUpsertResourceRequest request = NewSubmodelRequest("shell", "controlled", "secret");
            request.DisclosureTier = AASDisclosureTierDataType.Controlled;
            await service.UpsertResourceAsync(request);

            AasGetSubmodelResult result = await service.GetSubmodelAsync("controlled");

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                AssertDeniedLeaksNoMetadata(result);
            });
        }

        /// <summary>
        /// Verifies concealed unauthorized and nonexistent targets share the same structural path.
        /// </summary>
        [Test]
        public async Task GetSubmodelConcealmentUsesSameStructuralPathAsNotFound()
        {
            var observer = new RecordingObserver();
            Mock<IAasRegistryAuthorizationEvaluator> auth = DenyingAuthenticatedState(false);
            var service = new AasRegistryService(
                authorizationEvaluator: auth.Object,
                accessPathObserver: observer);
            AasUpsertResourceRequest request = NewSubmodelRequest("shell", "controlled", "secret");
            request.DisclosureTier = AASDisclosureTierDataType.Controlled;
            request.ConcealFromUnauthorized = true;
            await service.UpsertResourceAsync(request);

            AasGetSubmodelResult concealed = await service.GetSubmodelAsync("controlled");
            AasGetSubmodelResult missing = await service.GetSubmodelAsync("missing");

            Assert.Multiple(() =>
            {
                Assert.That(concealed.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(observer.Calls, Is.EqualTo(2));
                Assert.That(observer.LastObservedResolvedAndAuthorizedPoint, Is.True);
            });
        }

        /// <summary>
        /// Verifies public data is readable anonymously and authorization options carry no credential material.
        /// </summary>
        [Test]
        public async Task DisclosureTierAndAuthorizationAdvertiseConfigurationOnly()
        {
            var service = new AasRegistryService();
            AasUpsertResourceRequest request = NewSubmodelRequest("shell", "public", "public");
            request.Authorization = new ArrayOf<AASAuthorizationOptionDataType>(new[]
            {
                new AASAuthorizationOptionDataType
                {
                    Type = "OAuth2",
                    AuthorityUri = "https://issuer.example/",
                    ResourceUri = "urn:resource"
                }
            });
            await service.UpsertResourceAsync(request);

            AasGetSubmodelResult result = await service.GetSubmodelAsync("public");
            string[] propertyNames = typeof(AASAuthorizationOptionDataType)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Document.Length, Is.GreaterThan(0));
                Assert.That(propertyNames, Does.Not.Contain("Password"));
                Assert.That(propertyNames, Does.Not.Contain("Token"));
                Assert.That(propertyNames, Does.Not.Contain("Key"));
                Assert.That(propertyNames, Does.Not.Contain("Secret"));
            });
        }


        /// <summary>
        /// Verifies controlled documents require an authorized caller while public documents do not.
        /// </summary>
        [Test]
        public async Task ControlledDisclosureTierRequiresAuthenticationButPublicDoesNot()
        {
            var service = new AasRegistryService();
            await service.UpsertResourceAsync(NewSubmodelRequest("shell", "public", "public"));
            AasUpsertResourceRequest controlled = NewSubmodelRequest("shell", "controlled", "controlled");
            controlled.DisclosureTier = AASDisclosureTierDataType.Controlled;
            await service.UpsertResourceAsync(controlled);

            AasGetSubmodelResult publicResult = await service.GetSubmodelAsync("public");
            AasGetSubmodelResult controlledResult = await service.GetSubmodelAsync("controlled");

            Assert.Multiple(() =>
            {
                Assert.That(publicResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(controlledResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                AssertDeniedLeaksNoMetadata(controlledResult);
            });
        }

        /// <summary>
        /// Verifies persistence bounds reject oversized documents and excessive resource growth.
        /// </summary>
        [Test]
        public async Task PersistenceBoundsRejectOversizedDocumentsTooManyVersionsAndTooManyResources()
        {
            var service = new AasRegistryService(bounds: new AasRegistryPersistenceBounds
            {
                MaxDocumentBytes = 4,
                MaxVersionsPerResource = 1,
                MaxResourcesPerGroup = 1
            });

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.UpsertResourceAsync(NewSubmodelRequest("shell", "oversized", "12345")));
            await service.UpsertResourceAsync(NewSubmodelRequest("shell", "one", "1234"));

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await service.UpsertResourceAsync(NewSubmodelRequest("shell", "one", "4321")));
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await service.UpsertResourceAsync(NewSubmodelRequest("shell", "two", "1234")));
            });
        }

        private static AasUpsertResourceRequest NewSubmodelRequest(
            string shell,
            string submodel,
            string content,
            string? assetName = null,
            string? assetValue = null)
        {
            var request = new AasUpsertResourceRequest
            {
                GroupSourceIdentity = shell,
                ResourceSourceIdentity = submodel,
                GroupKind = AasRegistryEntityKind.Shell,
                ResourceKind = AasRegistryEntityKind.Submodel,
                Content = ByteString.From(System.Text.Encoding.UTF8.GetBytes(content)),
                ContentType = "application/aas+json",
                Format = "aas/3.0+json"
            };
            if (assetName is not null && assetValue is not null)
            {
                request.SpecificAssetIds = new ArrayOf<AasRegistryAssetLink>(new[]
                {
                    new AasRegistryAssetLink { Name = assetName, Value = assetValue }
                });
            }
            return request;
        }


        private static void AssertDeniedLeaksNoMetadata(AasGetSubmodelResult result)
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

        private static Mock<IAasRegistryAuthorizationEvaluator> DenyingAuthenticatedState(bool authenticated)
        {
            var mock = new Mock<IAasRegistryAuthorizationEvaluator>(MockBehavior.Strict);
            mock.Setup(evaluator => evaluator.IsAuthenticated(It.IsAny<ISystemContext?>()))
                .Returns(authenticated);
            mock.Setup(evaluator => evaluator.CanReadSubmodel(
                    It.IsAny<ISystemContext?>(),
                    It.IsAny<AasRegistryResource>()))
                .Returns(false);
            return mock;
        }

        private sealed class RecordingObserver : IAasRegistryAccessPathObserver
        {
            public int Calls { get; private set; }
            public bool LastObservedResolvedAndAuthorizedPoint { get; private set; }

            public void OnResolvedAndAuthorized(
                string submodelIdentifier,
                bool exists,
                bool authorized,
                bool concealed)
            {
                Calls++;
                LastObservedResolvedAndAuthorizedPoint = true;
            }
        }
    }
}
