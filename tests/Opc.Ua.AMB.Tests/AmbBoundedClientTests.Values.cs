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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;

namespace Opc.Ua.AMB.Tests
{
    public sealed partial class AmbBoundedClientTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task LinksPreserveUriAccessAndOptionalMarkerEvidenceAsync(bool hasMarker)
        {
            Mock<ISession> session = CreateSession();
            SetupLinks(session, 1, hasMarker);
            DataValue uri = TimedValue(Variant.From("https://example.invalid/manual"), StatusCodes.GoodClamped);
            DataValue marker = TimedValue(Variant.From(false), StatusCodes.Good);
            SetupRead(session, (node, attribute) => attribute == Attributes.UserAccessLevel
                ? new DataValue(Variant.From((byte)(AccessLevels.CurrentRead | AccessLevels.CurrentWrite)))
                : node == s_marker ? marker : uri);

            ArrayOf<DocumentationLinkRecord> links = await CreateClient(session)
                .ReadDocumentationLinksAsync(s_asset).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(links.Count, Is.EqualTo(1));
                Assert.That(links[0].NodeId, Is.EqualTo(new NodeId(100u, 4)));
                Assert.That(links[0].Uri, Is.EqualTo("https://example.invalid/manual"));
                Assert.That(links[0].IsWritable, Is.True);
                Assert.That(links[0].IsUserLink, Is.False);
                Assert.That(links[0].Values.Count, Is.EqualTo(3));
                Assert.That(links[0].Values[0].Value, Is.EqualTo(uri));
                Assert.That(links[0].Values[1].AttributeId, Is.EqualTo(Attributes.UserAccessLevel));
                Assert.That(links[0].Values[2].IsAbsent, Is.EqualTo(!hasMarker));
                if (hasMarker)
                {
                    Assert.That(links[0].Values[2].SourceNodeId, Is.EqualTo(s_marker));
                    Assert.That(links[0].Values[2].Value, Is.EqualTo(marker));
                }
                else
                {
                    Assert.That(links[0].Values[2].Value.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
                }
            });
            VerifyNoMutationOrDisposal(session);
        }

        [TestCase("uri")]
        [TestCase("access")]
        [TestCase("marker")]
        public async Task WrongLinkTypesDoNotBecomeEmptyUriOrFalsePermissionsAsync(string field)
        {
            Mock<ISession> session = CreateSession();
            SetupLinks(session, 1, true);
            SetupRead(session, (node, attribute) =>
            {
                string current = attribute == Attributes.UserAccessLevel
                    ? "access" : node == s_marker ? "marker" : "uri";
                if (current == field)
                {
                    return TimedValue(Variant.From(42), StatusCodes.Good);
                }
                return current switch
                {
                    "access" => new DataValue(Variant.From((byte)AccessLevels.CurrentRead)),
                    "marker" => new DataValue(Variant.From(false)),
                    _ => new DataValue(Variant.From("https://example.invalid/manual"))
                };
            });

            AmbValueReadException failure = await FailureAsync<AmbValueReadException>(
                () => CreateClient(session).ReadDocumentationLinksAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(failure.ReadValue!.Value.WrappedValue, Is.EqualTo(Variant.From(42)));
            Assert.That(failure.ReadValue.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(failure.ReadValue.IsAbsent, Is.False);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task NonGoodLinkAccessDoesNotBecomeReadOnlyAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            SetupLinks(session, 1, false);
            DataValue denied = TimedValue(Variant.From((byte)AccessLevels.CurrentWrite), status);
            SetupRead(session, (_, attribute) => attribute == Attributes.UserAccessLevel
                ? denied : new DataValue(Variant.From("https://example.invalid/manual")));

            AmbValueReadException failure = await FailureAsync<AmbValueReadException>(
                () => CreateClient(session).ReadDocumentationLinksAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(status));
            Assert.That(failure.ReadValue!.AttributeId, Is.EqualTo(Attributes.UserAccessLevel));
            Assert.That(failure.ReadValue.Value, Is.EqualTo(denied));
        }

        [Test]
        public async Task ReadRequestsAreBatchedBeforeSendingAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupLinks(session, 65, false);
            SetupRead(session, (_, attribute) => attribute == Attributes.UserAccessLevel
                ? new DataValue(Variant.From((byte)AccessLevels.CurrentRead))
                : new DataValue(Variant.From("https://example.invalid/manual")));

            ArrayOf<DocumentationLinkRecord> links = await CreateClient(session, 65)
                .ReadDocumentationLinksAsync(s_asset).ConfigureAwait(false);

            Assert.That(links.Count, Is.EqualTo(65));
            session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                It.Is<ArrayOf<ReadValueId>>(values => values.Count == 64), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                It.Is<ArrayOf<ReadValueId>>(values => values.Count == 1), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Test]
        public async Task MarkerBrowsesShareTheDocumentationReferenceBudgetAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupLinks(session, 1, true);
            SetupRead(session, (_, attribute) => attribute == Attributes.UserAccessLevel
                ? new DataValue(Variant.From((byte)AccessLevels.CurrentRead))
                : new DataValue(Variant.From("https://example.invalid/manual")));

            await AssertStatusAsync(
                () => CreateClient(session, 1).ReadDocumentationLinksAsync(s_asset).AsTask(),
                StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
        }

        [Test]
        public async Task HealthAndMaintenanceKeepCurrentFieldEvidenceAndNullDateSemanticsAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupConditions(session);
            SetupRead(session, ConditionValue);
            AmbClient client = CreateClient(session);

            ArrayOf<AssetAlarmRecord> alarms = await client.ReadHealthAlarmsAsync(s_asset).ConfigureAwait(false);
            ArrayOf<MaintenanceActivityRecord> activities = await client.ReadMaintenanceActivitiesAsync(s_asset)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(alarms.Count, Is.EqualTo(1));
                Assert.That(alarms[0].ConditionId, Is.EqualTo(s_alarm));
                Assert.That(alarms[0].IsActive, Is.False);
                Assert.That(activities.Count, Is.EqualTo(1));
                Assert.That(activities[0].ConditionId, Is.EqualTo(s_maintenance));
                Assert.That(activities[0].State, Is.EqualTo(MaintenanceStateKind.Planned));
                Assert.That(activities[0].PlannedDate.IsNull, Is.True);
                AssetReadValue active = alarms[0].Values.ToArray()!.Single(value =>
                    value.BrowsePath[0].Name == Ua.BrowseNames.ActiveState);
                Assert.That(active.SourceNodeId, Is.EqualTo(new NodeId("alarm/ActiveState", 4)));
                Assert.That(active.IsAbsent, Is.False);
                Assert.That(active.Value, Is.EqualTo(ConditionValue(active.SourceNodeId, Attributes.Value)));
                AssetReadValue planned = activities[0].Values.ToArray()!.Single(value =>
                    value.BrowsePath[0].Name == BrowseNames.PlannedDate);
                Assert.That(planned.IsAbsent, Is.False, "a null date has a real source");
                Assert.That(planned.Value.WrappedValue.TryGetValue(out DateTimeUtc date), Is.True);
                Assert.That(date.IsNull, Is.True);
            });
            VerifyNoMutationOrDisposal(session);
        }

        [TestCase(Ua.BrowseNames.ActiveState)]
        [TestCase(Ua.BrowseNames.ConditionClassId)]
        public async Task WrongConditionFieldsCannotBecomeFalseOrMisclassifiedAsync(string field)
        {
            Mock<ISession> session = CreateSession();
            SetupConditions(session);
            SetupRead(session, (node, attribute) =>
            {
                node.TryGetValue(out string? name);
                return name == "alarm/" + field
                    ? new DataValue(Variant.From("wrong type"))
                    : ConditionValue(node, attribute);
            });

            AmbValueReadException failure = await FailureAsync<AmbValueReadException>(
                () => CreateClient(session).ReadHealthAlarmsAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(failure.ReadValue!.SourceNodeId, Is.EqualTo(new NodeId("alarm/" + field, 4)));
        }

        [Test]
        public async Task ContextAndEntryReadsKeepSourceAndNullStructureEvidenceAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, paths =>
            {
                var results = new BrowsePathResult[paths.Count];
                for (int ii = 0; ii < paths.Count; ii++)
                {
                    results[ii] = Resolved(new NodeId(paths[ii].RelativePath.Elements[0].TargetName.Name!, 4));
                }
                return results;
            });
            SetupBrowse(session, request => request.NodeId == new NodeId(AmbBrowseNames.Requirements, 4)
                ? new BrowseResult { References = [Reference("voltage")] }
                : new BrowseResult());
            DataValue voltage = TimedValue(Variant.From(24.0), StatusCodes.GoodClamped);
            SetupRead(session, (node, _) => node == new NodeId("voltage", 4)
                ? voltage
                : node == new NodeId(Ua.BrowseNames.LocalTime, 4)
                    ? new DataValue(Variant.Null)
                    : new DataValue(Variant.From(string.Empty)));
            AmbClient client = CreateClient(session);

            AssetContextRecord context = await client.ReadContextAsync(s_asset).ConfigureAwait(false);
            ArrayOf<AssetEntryRecord> entries = await client.ReadEntriesAsync(s_asset, AssetEntryFolder.Requirements)
                .ConfigureAwait(false);

            Assert.That(context.LocalTime, Is.Null);
            Assert.That(context.Values[3].IsAbsent, Is.False);
            Assert.That(context.Values[3].Value.WrappedValue.IsNull, Is.True);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].ReadValue!.SourceNodeId, Is.EqualTo(new NodeId("voltage", 4)));
            Assert.That(entries[0].ReadValue!.Value, Is.EqualTo(voltage));
        }

        [Test]
        public async Task TypedAssetIdWriteResolvesOnlyItsTargetAndWritesOnceAsync()
        {
            Mock<ISession> session = CreateSession();
            var target = new NodeId("asset-id", 4);
            int resolutions = 0;
            SetupTranslate(session, paths =>
            {
                Assert.That(paths.Count, Is.EqualTo(1));
                ArrayOf<RelativePathElement> path = paths[0].RelativePath.Elements;
                Assert.That(path[path.Count - 1].TargetName,
                    Is.EqualTo(new QualifiedName(Opc.Ua.Di.BrowseNames.AssetId, 3)));
                return [++resolutions == 1 ? Missing() : Resolved(target)];
            });
            session.Setup(s => s.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<WriteValue> writes, CancellationToken _) =>
                {
                    Assert.That(writes.Count, Is.EqualTo(1));
                    Assert.That(writes[0].NodeId, Is.EqualTo(target));
                    Assert.That(writes[0].Value.WrappedValue, Is.EqualTo(Variant.From("P-42")));
                    return new ValueTask<WriteResponse>(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [StatusCodes.Good]
                    });
                });

            await CreateClient(session).WriteAssetIdAsync(s_asset, "P-42").ConfigureAwait(false);

            Assert.That(resolutions, Is.EqualTo(2));
            VerifyOneWrite(session);
            VerifyNoRead(session);
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task TypedWritesRejectWrongCardinalityWithoutReplayAsync(int count)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(s => s.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = new StatusCode[count]
                });

            await AssertStatusAsync(
                () => CreateClient(session).WriteDocumentationLinkAsync(s_asset, "https://example.invalid/").AsTask(),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
            VerifyOneWrite(session);
        }

        [TestCaseSource(nameof(NonGoodWriteStatuses))]
        public async Task TypedWritesRejectNonGoodServiceAndOperationStatusesAsync(StatusCode status, bool service)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(s => s.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = service ? status : StatusCodes.Good },
                    Results = [service ? StatusCodes.Good : status]
                });

            await AssertStatusAsync(
                () => CreateClient(session).WriteDocumentationLinkAsync(s_asset, "https://example.invalid/").AsTask(),
                status).ConfigureAwait(false);
            VerifyOneWrite(session);
        }

        [Test]
        public async Task CancelledWritesAreNeverSentAsync()
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(
                async () => await CreateClient(session).WriteDocumentationLinkAsync(
                    s_asset, "https://example.invalid/", cancellation.Token).ConfigureAwait(false),
                Throws.TypeOf<OperationCanceledException>()).ConfigureAwait(false);

            VerifyNoMutationOrDisposal(session);
        }

        [Test]
        public async Task TypedLinkMethodsResolveInstanceAndCallOnlyOnceAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupDocumentationMethodResolution(session);
            var link = new NodeId("new-link", 4);
            int adds = 0;
            int removes = 0;
            SetupCall(session, request =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(s_addIn));
                if (request.MethodId == new NodeId(BrowseNames.AddLink, 4))
                {
                    adds++;
                    Assert.That(request.InputArguments.Count, Is.EqualTo(4));
                    Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From("https://example.invalid/manual")));
                    Assert.That(request.InputArguments[1], Is.EqualTo(Variant.From(new QualifiedName("Manual", 4))));
                    return new CallMethodResult { OutputArguments = [Variant.From(link)] };
                }
                removes++;
                Assert.That(request.MethodId, Is.EqualTo(new NodeId(BrowseNames.RemoveLink, 4)));
                Assert.That(request.InputArguments.Count, Is.EqualTo(1));
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(link)));
                return new CallMethodResult();
            });
            AmbClient client = CreateClient(session);

            NodeId added = await client.AddDocumentationLinkAsync(
                s_asset, "https://example.invalid/manual", new QualifiedName("Manual", 4)).ConfigureAwait(false);
            await client.RemoveDocumentationLinkAsync(s_asset, added).ConfigureAwait(false);

            Assert.That(added, Is.EqualTo(link));
            Assert.That(adds, Is.EqualTo(1));
            Assert.That(removes, Is.EqualTo(1));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        [Test]
        public async Task UnknownNonIdempotentOutcomeIsNotReplayedAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupDocumentationMethodResolution(session);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadTimeout));

            await AssertStatusAsync(
                () => CreateClient(session).AddDocumentationLinkAsync(
                    s_asset, "https://example.invalid/manual", new QualifiedName("Manual", 4)).AsTask(),
                StatusCodes.BadTimeout).ConfigureAwait(false);

            VerifyOneCall(session);
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task TypedCallsRejectWrongCardinalityWithoutReplayAsync(int count)
        {
            Mock<ISession> session = CreateSession();
            SetupDocumentationMethodResolution(session);
            var results = new CallMethodResult[count];
            for (int ii = 0; ii < count; ii++)
            {
                results[ii] = new CallMethodResult();
            }
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = results.ToArrayOf()
                });

            await AssertStatusAsync(
                () => CreateClient(session).RemoveDocumentationLinkAsync(s_asset, s_asset).AsTask(),
                StatusCodes.BadDecodingError).ConfigureAwait(false);

            VerifyOneCall(session);
        }

        [TestCaseSource(nameof(NonGoodWriteStatuses))]
        public async Task TypedCallsRejectNonGoodServiceAndOperationStatusesAsync(StatusCode status, bool service)
        {
            Mock<ISession> session = CreateSession();
            SetupDocumentationMethodResolution(session);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = service ? status : StatusCodes.Good },
                    Results = [new CallMethodResult { StatusCode = service ? StatusCodes.Good : status }]
                });

            await AssertStatusAsync(
                () => CreateClient(session).RemoveDocumentationLinkAsync(s_asset, s_asset).AsTask(), status)
                .ConfigureAwait(false);

            VerifyOneCall(session);
        }

        private static IEnumerable<TestCaseData> NonGoodWriteStatuses()
        {
            yield return new TestCaseData(StatusCodes.BadUserAccessDenied, false);
            yield return new TestCaseData(StatusCodes.BadUserAccessDenied, true);
            yield return new TestCaseData(StatusCodes.Uncertain, false);
            yield return new TestCaseData(StatusCodes.Uncertain, true);
        }

        private static void SetupDocumentationMethodResolution(Mock<ISession> session)
        {
            SetupTranslate(session, paths =>
            {
                Assert.That(paths.Count, Is.EqualTo(1));
                QualifiedName name = paths[0].RelativePath.Elements[0].TargetName;
                Assert.That(name.NamespaceIndex, Is.EqualTo(2));
                return [Resolved(name.Name == BrowseNames.DocumentationLinks ? s_addIn : new NodeId(name.Name!, 4))];
            });
        }

        private static void SetupLinks(Mock<ISession> session, int count, bool hasMarker)
        {
            SetupTranslate(session, _ => [Resolved(s_addIn)]);
            SetupBrowse(session, request =>
            {
                if (request.NodeId == s_addIn)
                {
                    var references = new ReferenceDescription[count];
                    for (int ii = 0; ii < count; ii++)
                    {
                        references[ii] = new ReferenceDescription
                        {
                            NodeId = new NodeId(100u + (uint)ii, 4),
                            BrowseName = new QualifiedName("Link" + ii, 4),
                            NodeClass = NodeClass.Variable
                        };
                    }
                    return new BrowseResult { References = references.ToArrayOf() };
                }
                return hasMarker ? new BrowseResult
                {
                    References =
                    [
                        new ReferenceDescription
                        {
                            NodeId = s_marker,
                            BrowseName = new QualifiedName(DocumentationLinkProperties.UserLink, 4),
                            NodeClass = NodeClass.Variable
                        }
                    ]
                } : new BrowseResult();
            });
        }

        private static void SetupConditions(Mock<ISession> session)
        {
            var folder = new NodeId("conditions", 4);
            SetupBrowse(session, request => request.NodeId == folder
                ? new BrowseResult { References = [Reference(s_alarm), Reference(s_maintenance)] }
                : new BrowseResult());
            SetupTranslate(session, paths =>
            {
                if (paths[0].StartingNode == s_asset)
                {
                    return [Resolved(folder)];
                }
                paths[0].StartingNode.TryGetValue(out string? owner);
                var results = new BrowsePathResult[paths.Count];
                for (int ii = 0; ii < paths.Count; ii++)
                {
                    results[ii] = Resolved(new NodeId(
                        owner + "/" + paths[ii].RelativePath.Elements[0].TargetName.Name, 4));
                }
                return results;
            });
        }

        private static DataValue ConditionValue(NodeId node, uint attribute)
        {
            Assert.That(attribute, Is.EqualTo(Attributes.Value));
            node.TryGetValue(out string? name);
            bool maintenance = name!.StartsWith("maintenance/", StringComparison.Ordinal);
            string field = name.Substring(maintenance ? "maintenance/".Length : "alarm/".Length);
            Variant value = field switch
            {
                Ua.BrowseNames.ConditionClassId => Variant.From(maintenance
                    ? Ua.ObjectTypeIds.MaintenanceConditionClassType : Ua.ObjectTypeIds.BaseConditionClassType),
                Ua.BrowseNames.EventType => Variant.From(Ua.ObjectTypeIds.AlarmConditionType),
                Ua.BrowseNames.SourceNode => Variant.From(s_asset),
                Ua.BrowseNames.ActiveState or Ua.BrowseNames.Retain => Variant.From(false),
                Ua.BrowseNames.AckedState => Variant.From(true),
                Ua.BrowseNames.ConditionName => Variant.From(maintenance ? "Scheduled" : "Alarm"),
                Ua.BrowseNames.Time => Variant.From(
                    new DateTimeUtc(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc))),
                BrowseNames.MaintenanceState => maintenance
                    ? Variant.From(new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Planned, 2)) : Variant.Null,
                BrowseNames.PlannedDate => Variant.From(default(DateTimeUtc)),
                _ => Variant.Null
            };
            return TimedValue(value, StatusCodes.Good);
        }

        private static void VerifyOneWrite(Mock<ISession> session)
        {
            session.Verify(s => s.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            session.Verify(s => s.Dispose(), Times.Never);
        }

        private static void VerifyOneCall(Mock<ISession> session)
        {
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            session.Verify(s => s.Dispose(), Times.Never);
        }

        private static readonly NodeId s_addIn = new("documentation", 4);
        private static readonly NodeId s_marker = new("user-link-marker", 4);
        private static readonly NodeId s_alarm = new("alarm", 4);
        private static readonly NodeId s_maintenance = new("maintenance", 4);
    }
}
