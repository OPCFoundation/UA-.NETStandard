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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Exercises bounded AMB traversal and quality preservation through the
    /// same typed interface used by applications, without a live server.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    public sealed partial class AmbBoundedClientTests
    {
        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(1, 0)]
        [TestCase(1, -1)]
        public void BoundedConstructorRejectsInvalidLimits(int references, int pages)
        {
            Assert.That(() => CreateClient(CreateSession(), references, pages),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void BoundedConstructorRejectsNullOptions()
        {
            Mock<ISession> session = CreateSession();
            Assert.That(() => new AmbClient(session.Object, NUnitTelemetryContext.Create(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task HealthReadPreservesRemappedSourceQualityAndBothTimestampsAsync()
        {
            Mock<ISession> session = CreateSession();
            var source = new NodeId("Health", 4);
            DataValue original = TimedValue(Variant.From(2), StatusCodes.GoodClamped);
            SetupTranslate(session, paths =>
            {
                Assert.That(paths[0].RelativePath.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(3));
                return [Resolved(new ExpandedNodeId("Health", InstanceNamespace))];
            });
            SetupRead(session, (node, attribute) =>
            {
                Assert.That(node, Is.EqualTo(source));
                Assert.That(attribute, Is.EqualTo(Attributes.Value));
                return original;
            });

            AssetReadValue field = await CreateClient(session).ReadDeviceHealthValueAsync(s_asset)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(field.IsAbsent, Is.False);
                Assert.That(field.SourceNodeId, Is.EqualTo(source));
                Assert.That(field.Value, Is.EqualTo(original));
                Assert.That(field.BrowsePath[0], Is.EqualTo(new QualifiedName(Opc.Ua.Di.BrowseNames.DeviceHealth, 3)));
            });
            VerifyNoMutationOrDisposal(session);
        }

        [Test]
        public async Task OptionalHealthIsAbsentWithoutReadingAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => [Missing()]);

            AssetReadValue value = await CreateClient(session).ReadDeviceHealthValueAsync(s_asset)
                .ConfigureAwait(false);

            Assert.That(value.IsAbsent, Is.True);
            Assert.That(value.SourceNodeId.IsNull, Is.True);
            Assert.That(value.Value.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            VerifyNoRead(session);
            VerifyNoMutationOrDisposal(session);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task DeniedOrUncertainReadRetainsOriginalEvidenceAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            var source = new NodeId("Health", 4);
            DataValue original = TimedValue(Variant.From(3), status);
            SetupTranslate(session, _ => [Resolved(source)]);
            SetupRead(session, (_, _) => original);

            AmbValueReadException failure = await FailureAsync<AmbValueReadException>(
                () => CreateClient(session).ReadDeviceHealthValueAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(status));
            Assert.That(failure.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(failure.ReadValue.IsAbsent, Is.False);
            Assert.That(failure.ReadValue.Value, Is.EqualTo(original));
        }

        [Test]
        public async Task WrongHealthTypeRetainsOriginalGoodValueAsync()
        {
            Mock<ISession> session = CreateSession();
            DataValue original = TimedValue(Variant.From("not an enumeration"), StatusCodes.Good);
            SetupTranslate(session, _ => [Resolved(new NodeId("Health", 4))]);
            SetupRead(session, (_, _) => original);

            AmbValueReadException failure = await FailureAsync<AmbValueReadException>(
                () => CreateClient(session).ReadDeviceHealthAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(failure.ReadValue!.Value, Is.EqualTo(original));
            Assert.That(failure.ReadValue.IsAbsent, Is.False);
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task ReadRejectsWrongCardinalityAsync(int count)
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => [Resolved(new NodeId("Health", 4))]);
            session.Setup(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = new DataValue[count]
                });

            await AssertStatusAsync(
                () => CreateClient(session).ReadDeviceHealthAsync(s_asset).AsTask(),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task ReadRejectsNonGoodServiceStatusAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => [Resolved(new NodeId("Health", 4))]);
            session.Setup(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = status },
                    Results = [new DataValue(Variant.From(0))]
                });

            await AssertStatusAsync(
                () => CreateClient(session).ReadDeviceHealthAsync(s_asset).AsTask(), status).ConfigureAwait(false);
        }

        [TestCase("invalid-diagnostics")]
        [TestCase("extra-result")]
        [TestCase("missing-result")]
        [TestCase("ambiguous")]
        [TestCase("incomplete")]
        [TestCase("null-target")]
        [TestCase("missing-target")]
        [TestCase("absent-with-target")]
        public async Task ResolutionRejectsMalformedShapesAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            BrowsePathResult result = Resolved(new NodeId("Health", 4));
            var response = new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [result]
            };
            switch (shape)
            {
                case "invalid-diagnostics":
                    response.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
                    break;
                case "extra-result":
                    response.Results = [result, result];
                    break;
                case "missing-result":
                    response.Results = [];
                    break;
                case "ambiguous":
                    result.Targets = [result.Targets[0], result.Targets[0]];
                    break;
                case "incomplete":
                    result.Targets[0].RemainingPathIndex = 0;
                    break;
                case "null-target":
                    result.Targets[0].TargetId = ExpandedNodeId.Null;
                    break;
                case "missing-target":
                    result.Targets = [];
                    break;
                case "absent-with-target":
                    result.StatusCode = StatusCodes.BadNoMatch;
                    break;
            }
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            await AssertStatusAsync(
                () => CreateClient(session).ReadDeviceHealthAsync(s_asset).AsTask(),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
            VerifyNoRead(session);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task DeniedOrUncertainResolutionIsNotAbsenceAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => [new BrowsePathResult { StatusCode = status }]);

            await AssertStatusAsync(
                () => CreateClient(session).ReadDeviceHealthValueAsync(s_asset).AsTask(), status).ConfigureAwait(false);
            VerifyNoRead(session);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResolutionRejectsRemoteAndUnknownNamespaceTargetsAsync(bool remote)
        {
            Mock<ISession> session = CreateSession();
            ExpandedNodeId target = remote
                ? new ExpandedNodeId(new NodeId("Health", 4), null, 1)
                : new ExpandedNodeId("Health", "urn:missing");
            SetupTranslate(session, _ => [Resolved(target)]);

            await AssertStatusAsync(
                () => CreateClient(session).ReadDeviceHealthAsync(s_asset).AsTask(),
                remote ? StatusCodes.BadNotSupported : StatusCodes.BadNodeIdInvalid).ConfigureAwait(false);
            VerifyNoRead(session);
        }

        [Test]
        public async Task IdentificationFallsBackOnlyForAbsentPathsAndKeepsEvidenceAsync()
        {
            Mock<ISession> session = CreateSession();
            int grouped = 0;
            SetupTranslate(session, paths =>
            {
                var results = new BrowsePathResult[paths.Count];
                for (int ii = 0; ii < paths.Count; ii++)
                {
                    ArrayOf<RelativePathElement> elements = paths[ii].RelativePath.Elements;
                    string name = elements[elements.Count - 1].TargetName.Name!;
                    if (elements.Count == 2)
                    {
                        grouped++;
                        Assert.That(name, Is.EqualTo(Opc.Ua.Di.BrowseNames.AssetId));
                        results[ii] = Resolved(new NodeId("grouped-AssetId", 4));
                    }
                    else
                    {
                        results[ii] = name == Opc.Ua.Di.BrowseNames.AssetId
                            ? Missing()
                            : Resolved(new NodeId(name, 4));
                    }
                }
                return results;
            });
            DataValue text = TimedValue(Variant.From("value"), StatusCodes.GoodClamped);
            SetupRead(session, (node, _) =>
            {
                node.TryGetValue(out string? name);
                return name switch
                {
                    Opc.Ua.Di.BrowseNames.Manufacturer or Opc.Ua.Di.BrowseNames.Model =>
                        new DataValue(Variant.From(LocalizedText.Null)),
                    Opc.Ua.Di.BrowseNames.RevisionCounter => new DataValue(Variant.From(-1)),
                    _ => text
                };
            });

            AssetIdentificationRecord identification = await CreateClient(session)
                .ReadIdentificationAsync(s_asset).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(grouped, Is.EqualTo(1));
                Assert.That(identification.Values.Count, Is.EqualTo(10));
                Assert.That(identification.AssetId, Is.EqualTo("value"));
                Assert.That(identification.Manufacturer.IsNull, Is.True);
                Assert.That(identification.RevisionCounter, Is.Null);
                Assert.That(identification.Values[1].SourceNodeId, Is.EqualTo(new NodeId("grouped-AssetId", 4)));
                Assert.That(identification.Values[1].BrowsePath.Count, Is.EqualTo(2));
                Assert.That(identification.Values[1].Value, Is.EqualTo(text));
                Assert.That(identification.Values[2].IsAbsent, Is.False, "a null structure is not a missing variable");
                Assert.That(identification.Values[9].Value.WrappedValue, Is.EqualTo(Variant.From(-1)));
            });
        }

        [Test]
        public async Task DeniedIdentificationNeverFallsBackAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, paths =>
            {
                Assert.That(paths[0].RelativePath.Elements.Count, Is.EqualTo(1));
                var results = new BrowsePathResult[paths.Count];
                for (int ii = 0; ii < results.Length; ii++)
                {
                    results[ii] = new BrowsePathResult { StatusCode = StatusCodes.BadUserAccessDenied };
                }
                return results;
            });

            await AssertStatusAsync(
                () => CreateClient(session).ReadIdentificationAsync(s_asset).AsTask(),
                StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            VerifyNoRead(session);
        }

        [Test]
        public async Task ExactReferenceLimitAndEmptyPagesReleaseNoConsumedTokensAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString first = ByteString.From(1);
            ByteString second = ByteString.From(2);
            int browseCalls = 0;
            SetupBrowse(session, _ => ++browseCalls == 1
                ? new BrowseResult { References = [Reference("one")], ContinuationPoint = first }
                : new BrowseResult());
            var nextCalls = new List<ByteString>();
            SetupNext(session, (release, token, _) =>
            {
                Assert.That(release, Is.False, "successful continuation consumption needs no release");
                nextCalls.Add(token);
                return token == first
                    ? new BrowseResult { ContinuationPoint = second }
                    : new BrowseResult { References = [Reference("two")] };
            });

            ArrayOf<AssetLocationRecord> locations = await CreateClient(session, 2, 3)
                .ReadLocationsOfAssetAsync(s_asset).ConfigureAwait(false);

            Assert.That(locations.Count, Is.EqualTo(2));
            Assert.That(locations[0].Location, Is.EqualTo(new NodeId("one", 4)));
            Assert.That(locations[1].Location, Is.EqualTo(new NodeId("two", 4)));
            Assert.That(nextCalls, Is.EqualTo(new[] { first, second }));
            VerifyNoMutationOrDisposal(session);
        }

        [Test]
        public async Task OneBeyondReferenceLimitReleasesAcquiredContinuationAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(7);
            SetupBrowse(session, _ => new BrowseResult
            {
                References = [Reference("one"), Reference("two"), Reference("three")],
                ContinuationPoint = point
            });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await AssertStatusAsync(
                () => CreateClient(session, 2).ReadLocationsOfAssetAsync(s_asset).AsTask(),
                StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { point }));
            VerifyNoRead(session);
        }

        [Test]
        public async Task ReferenceBudgetSpansBothLocationKindsAsync()
        {
            Mock<ISession> session = CreateSession();
            int calls = 0;
            SetupBrowse(session, _ => ++calls == 1
                ? new BrowseResult { References = [Reference("hierarchical")] }
                : new BrowseResult { References = [Reference("operational-one"), Reference("operational-two")] });

            await AssertStatusAsync(
                () => CreateClient(session, 2).ReadLocationsOfAssetAsync(s_asset).AsTask(),
                StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public async Task EmptyPagesCountAndReusedContinuationIsReleasedOnceAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(8);
            SetupBrowse(session, _ => new BrowseResult { ContinuationPoint = point });
            int advanced = 0;
            int released = 0;
            SetupNext(session, (release, token, _) =>
            {
                Assert.That(token, Is.EqualTo(point));
                if (release)
                {
                    released++;
                    return new BrowseResult();
                }
                advanced++;
                return new BrowseResult { ContinuationPoint = point };
            });

            await AssertStatusAsync(
                () => CreateClient(session, 2, 3).ReadLocationsOfAssetAsync(s_asset).AsTask(),
                StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);

            Assert.That(advanced, Is.EqualTo(2));
            Assert.That(released, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationReleasesOnlyTheCurrentlyOwnedContinuationAsync(bool afterNext)
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            ByteString first = ByteString.From(11);
            ByteString second = ByteString.From(12);
            SetupBrowse(session, _ =>
            {
                if (!afterNext)
                {
                    cancellation.Cancel();
                }
                return new BrowseResult { ContinuationPoint = first };
            });
            var released = new List<ByteString>();
            SetupNext(session, (release, token, cleanup) =>
            {
                if (release)
                {
                    Assert.That(cleanup.CanBeCanceled, Is.True);
                    Assert.That(cleanup.IsCancellationRequested, Is.False);
                    Assert.That(cleanup, Is.Not.EqualTo(cancellation.Token));
                    released.Add(token);
                    return new BrowseResult();
                }
                Assert.That(token, Is.EqualTo(first));
                cancellation.Cancel();
                return new BrowseResult { ContinuationPoint = second };
            });

            await Assert.ThatAsync(async () => await CreateClient(session)
                .ReadLocationsOfAssetAsync(s_asset, cancellation.Token).ConfigureAwait(false),
                Throws.TypeOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { afterNext ? second : first }));
        }

        [Test]
        public async Task PrimaryAndCleanupFailuresAreBothRetainedAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, _ => new BrowseResult
            {
                References = [Reference("one"), Reference("two")],
                ContinuationPoint = ByteString.From(17)
            });
            var cleanupFailure = new IOException("release failed");
            session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(cleanupFailure);

            AggregateException failure = await FailureAsync<AggregateException>(
                () => CreateClient(session, 1).ReadLocationsOfAssetAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(cleanupFailure));
        }

        [Test]
        public async Task MalformedNextResponseReleasesOldAndAllReturnedContinuationsAsync()
        {
            Mock<ISession> session = CreateSession();
            ByteString first = ByteString.From(21);
            ByteString second = ByteString.From(22);
            ByteString third = ByteString.From(23);
            SetupBrowse(session, _ => new BrowseResult { ContinuationPoint = first });
            session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult { ContinuationPoint = second },
                        new BrowseResult { ContinuationPoint = second },
                        new BrowseResult { ContinuationPoint = third }
                    ]
                });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await AssertStatusAsync(
                () => CreateClient(session).ReadLocationsOfAssetAsync(s_asset).AsTask(),
                StatusCodes.BadDecodingError).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { first, second, third }));
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task NonGoodBrowseIsNeverAnEmptyResultAndReleasesItsTokenAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(31);
            SetupBrowse(session, _ => new BrowseResult { StatusCode = status, ContinuationPoint = point });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await AssertStatusAsync(
                () => CreateClient(session).ReadLocationsOfAssetAsync(s_asset).AsTask(), status).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { point }));
        }

        [Test]
        public async Task BrowseDiscoveryRemapsAndDeduplicatesLocalAssetsWithoutCallingMethodsAsync()
        {
            Mock<ISession> session = CreateSession();
            var alias = new NodeId("alias", 4);
            var remote = new ExpandedNodeId(new NodeId("remote", 4), null, 1);
            SetupBrowse(session, request =>
            {
                if (request.NodeId == alias)
                {
                    return new BrowseResult
                    {
                        References =
                        [
                            Reference(new ExpandedNodeId("Asset", InstanceNamespace)),
                            Reference(s_asset),
                            Reference(remote),
                            Reference(ExpandedNodeId.Null)
                        ]
                    };
                }
                return request.NodeId == ExpandedNodeId.ToNodeId(ObjectIds.AssetsByProductInstanceUri,
                    session.Object.NamespaceUris)
                    ? new BrowseResult
                    {
                        References = [Reference(alias), Reference(alias), Reference(ExpandedNodeId.Null)]
                    }
                    : new BrowseResult();
            });

            ArrayOf<NodeId> assets = await CreateClient(session, 7).DiscoverAssetsAsync().ConfigureAwait(false);

            Assert.That(assets.ToArray(), Is.EqualTo(new[] { s_asset }));
            VerifyNoMutationOrDisposal(session);
        }

        [TestCase(2, true)]
        [TestCase(1, false)]
        public async Task AliasEnumerationCountsCategoryAndTargetReferencesAsync(int maximum, bool succeeds)
        {
            Mock<ISession> session = CreateSession();
            var alias = new NodeId("alias", 4);
            SetupBrowse(session, request => new BrowseResult
            {
                References = [Reference(request.NodeId == alias ? s_asset : alias)]
            });
            var aliases = new List<AssetAlias>();
            async Task EnumerateAsync()
            {
                await foreach (AssetAlias found in CreateClient(session, maximum)
                    .EnumerateAssetsAsync().ConfigureAwait(false))
                {
                    aliases.Add(found);
                }
            }
            if (succeeds)
            {
                await EnumerateAsync().ConfigureAwait(false);
                Assert.That(aliases, Has.Count.EqualTo(1));
                Assert.That(aliases[0].ReferencedNodes[0], Is.EqualTo((ExpandedNodeId)s_asset));
            }
            else
            {
                await AssertStatusAsync(EnumerateAsync, StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
                Assert.That(aliases, Is.Empty);
            }
        }

        [TestCase(2, true)]
        [TestCase(1, false)]
        public async Task FindAliasBoundsAliasesPlusTargetsBeforeCopyingAsync(int maximum, bool succeeds)
        {
            Mock<ISession> session = CreateSession();
            SetupCall(session, _ => new CallMethodResult
            {
                OutputArguments =
                [
                    Variant.FromStructure((ArrayOf<AliasNameDataType>)
                    [
                        new AliasNameDataType
                        {
                            AliasName = new QualifiedName("asset"),
                            ReferencedNodes = [s_asset]
                        }
                    ])
                ]
            });
            if (succeeds)
            {
                ArrayOf<AssetAlias> aliases = await CreateClient(session, maximum).FindAssetsAsync("%")
                    .ConfigureAwait(false);
                Assert.That(aliases.Count, Is.EqualTo(1));
                Assert.That(aliases[0].ReferencedNodes[0], Is.EqualTo((ExpandedNodeId)s_asset));
            }
            else
            {
                await AssertStatusAsync(
                    () => CreateClient(session, maximum).FindAssetsAsync("%").AsTask(),
                    StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            }
            VerifyOneCall(session);
        }

        [TestCase(16, true)]
        [TestCase(17, false)]
        public async Task LocationsRespectExactDepthAndRejectOneBeyondAsync(int levels, bool succeeds)
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, request =>
            {
                if (request.ReferenceTypeId == Ua.ReferenceTypeIds.Organizes)
                {
                    return new BrowseResult { References = [Reference(new NodeId(1u, 4))] };
                }
                if (request.ReferenceTypeId == Ua.ReferenceTypeIds.HasComponent &&
                    request.NodeId.TryGetValue(out uint number) && number < levels)
                {
                    return new BrowseResult { References = [Reference(new NodeId(number + 1, 4))] };
                }
                return new BrowseResult();
            });
            AmbClient client = CreateClient(session, 32);
            if (succeeds)
            {
                ArrayOf<AssetLocationNode> locations = await client.BrowseLocationsAsync(AssetLocationKind.Hierarchical)
                    .ConfigureAwait(false);
                Assert.That(locations.Count, Is.EqualTo(levels));
                Assert.That(locations[levels - 1].NodeId, Is.EqualTo(new NodeId((uint)levels, 4)));
            }
            else
            {
                await AssertStatusAsync(
                    () => client.BrowseLocationsAsync(AssetLocationKind.Hierarchical).AsTask(),
                    StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SubAssetsHandleNullCyclesDuplicatesAndRemoteTargetsWithoutFollowingThemAsync()
        {
            Mock<ISession> session = CreateSession();
            var alias = new NodeId("alias", 4);
            var child = new NodeId("child", 4);
            var remote = new ExpandedNodeId(new NodeId("remote", 4), null, 2);
            NodeId category = ExpandedNodeId.ToNodeId(
                ObjectIds.AssetsByProductInstanceUri, session.Object.NamespaceUris);
            var browsed = new List<NodeId>();
            SetupBrowse(session, request =>
            {
                browsed.Add(request.NodeId);
                if (request.NodeId == category)
                {
                    return new BrowseResult { References = [Reference(alias)] };
                }
                if (request.NodeId == alias)
                {
                    return new BrowseResult { References = [Reference(child)] };
                }
                if (request.NodeId == s_asset)
                {
                    return new BrowseResult
                    {
                        References =
                        [
                            Reference(s_asset), Reference(child), Reference(child),
                            Reference(remote), Reference(remote), Reference(ExpandedNodeId.Null)
                        ]
                    };
                }
                if (request.NodeId == child)
                {
                    return new BrowseResult { References = [Reference(s_asset)] };
                }
                return new BrowseResult();
            });
            var found = new List<ExpandedNodeId>();
            await foreach (ExpandedNodeId sub in CreateClient(session, 9).EnumerateSubAssetsAsync(s_asset)
                .ConfigureAwait(false))
            {
                found.Add(sub);
            }

            Assert.That(found, Is.EqualTo(new[] { (ExpandedNodeId)child, remote }));
            Assert.That(browsed.Count(node => node == child), Is.EqualTo(1));
            Assert.That(browsed, Does.Not.Contain(new NodeId("remote", 4)));
            VerifyNoMutationOrDisposal(session);
        }

        [Test]
        public async Task NullSubAssetRootNeedsNoRequestsAsync()
        {
            Mock<ISession> session = CreateSession();
            var found = new List<ExpandedNodeId>();
            await foreach (ExpandedNodeId sub in CreateClient(session).EnumerateSubAssetsAsync(NodeId.Null)
                .ConfigureAwait(false))
            {
                found.Add(sub);
            }
            Assert.That(found, Is.Empty);
            session.Verify(s => s.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            VerifyNoMutationOrDisposal(session);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingConditionFolderDoesNotTriggerImplicitRefreshAsync(bool maintenance)
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, _ => new BrowseResult());
            SetupTranslate(session, _ => [Missing()]);
            AmbClient client = CreateClient(session);

            await AssertStatusAsync(
                () => maintenance
                    ? client.ReadMaintenanceActivitiesAsync(s_asset).AsTask()
                    : client.ReadHealthAlarmsAsync(s_asset).AsTask(),
                StatusCodes.BadNotSupported).ConfigureAwait(false);
            VerifyNoMutationOrDisposal(session);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyConstructorKeepsTolerantReadBehaviorAsync(bool missingResult)
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => [Resolved(new NodeId("Health", 4))]);
            session.Setup(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Source,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = missingResult ? [] : [new DataValue(Variant.From(3), StatusCodes.BadUserAccessDenied)]
                });
            var legacy = new AmbClient(session.Object, NUnitTelemetryContext.Create());

            Opc.Ua.Di.DeviceHealthEnumeration? health = await legacy.ReadDeviceHealthAsync(s_asset)
                .ConfigureAwait(false);

            Assert.That(health, Is.Null);
            VerifyNoMutationOrDisposal(session);
        }

        private static IEnumerable<TestCaseData> NonGoodStatuses()
        {
            yield return new TestCaseData(StatusCodes.BadUserAccessDenied);
            yield return new TestCaseData(StatusCodes.Uncertain);
        }

        private static Mock<ISession> CreateSession()
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend("urn:unrelated");
            context.NamespaceUris.GetIndexOrAppend(Namespaces.AMB);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            context.NamespaceUris.GetIndexOrAppend(InstanceNamespace);
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(context);
            return session;
        }

        private static AmbClient CreateClient(Mock<ISession> session, int references = 1024, int pages = 128)
        {
            return new AmbClient(session.Object, NUnitTelemetryContext.Create(), new AmbClientOptions
            {
                MaximumBrowseReferences = references,
                MaximumBrowsePages = pages
            });
        }

        private static BrowsePathResult Resolved(ExpandedNodeId target)
        {
            return new BrowsePathResult
            {
                Targets = [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
            };
        }

        private static BrowsePathResult Missing()
        {
            return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
        }

        private static ReferenceDescription Reference(string name)
        {
            return Reference(new NodeId(name, 4));
        }

        private static ReferenceDescription Reference(ExpandedNodeId node)
        {
            return new ReferenceDescription
            {
                NodeId = node,
                BrowseName = new QualifiedName("entry", 4),
                NodeClass = NodeClass.Object
            };
        }

        private static DataValue TimedValue(Variant value, StatusCode status)
        {
            return new DataValue(
                value,
                status,
                new DateTimeUtc(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc)),
                new DateTimeUtc(new DateTime(2026, 10, 10, 12, 0, 1, DateTimeKind.Utc)),
                12,
                34);
        }

        private static void SetupTranslate(
            Mock<ISession> session,
            Func<ArrayOf<BrowsePath>, BrowsePathResult[]> response)
        {
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = response(paths).ToArrayOf()
                    }));
        }

        private static void SetupRead(Mock<ISession> session, Func<NodeId, uint, DataValue> response)
        {
            session.Setup(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> reads,
                    CancellationToken _) =>
                {
                    Assert.That(reads.Count, Is.InRange(1, 64));
                    var results = new DataValue[reads.Count];
                    for (int ii = 0; ii < results.Length; ii++)
                    {
                        results[ii] = response(reads[ii].NodeId, reads[ii].AttributeId);
                    }
                    return new ValueTask<ReadResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results.ToArrayOf()
                    });
                });
        }

        private static void SetupBrowse(Mock<ISession> session, Func<BrowseDescription, BrowseResult> response)
        {
            session.Setup(s => s.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint maximum, ArrayOf<BrowseDescription> requests,
                    CancellationToken _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(maximum, Is.InRange(1u, 64u));
                    return new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [response(requests[0])]
                    });
                });
        }

        private static void SetupNext(
            Mock<ISession> session,
            Func<bool, ByteString, CancellationToken, BrowseResult> response)
        {
            session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<bool>(),
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, bool release, ArrayOf<ByteString> points, CancellationToken token) =>
                {
                    Assert.That(points.Count, Is.EqualTo(1));
                    return new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [response(release, points[0], token)]
                    });
                });
        }

        private static void SetupRelease(Mock<ISession> session, List<ByteString> released)
        {
            session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, bool _, ArrayOf<ByteString> points, CancellationToken token) =>
                {
                    Assert.That(points.Count, Is.EqualTo(1));
                    Assert.That(token.CanBeCanceled, Is.True);
                    Assert.That(token.IsCancellationRequested, Is.False);
                    released.Add(points[0]);
                    return new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowseResult()]
                    });
                });
        }

        private static void SetupCall(Mock<ISession> session, Func<CallMethodRequest, CallMethodResult> response)
        {
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [response(requests[0])]
                    });
                });
        }

        private static async Task AssertStatusAsync(Func<Task> action, StatusCode status)
        {
            await Assert.ThatAsync(action, Throws.InstanceOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(status)).ConfigureAwait(false);
        }

        private static async Task<TException> FailureAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            Exception? failure = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<TException>());
            return (TException)failure!;
        }

        private static void VerifyNoRead(Mock<ISession> session)
        {
            session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static void VerifyNoMutationOrDisposal(Mock<ISession> session)
        {
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            session.Verify(s => s.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            session.Verify(s => s.Dispose(), Times.Never);
        }

        private const string InstanceNamespace = "urn:amb:bounded:instances";
        private static readonly NodeId s_asset = new("Asset", 4);
    }
}
