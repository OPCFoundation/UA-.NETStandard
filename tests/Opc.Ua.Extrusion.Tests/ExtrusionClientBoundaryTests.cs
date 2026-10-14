/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Extrusion.Client;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Public variable operations against strict services, without a server fixture.
    /// </summary>
    [TestFixture]
    public sealed class ExtrusionClientBoundaryTests
    {
        /// <summary>
        /// Build-time generation preserves the complete published variable and placeholder surface.
        /// </summary>
        [Test]
        public void GeneratedAccessorsMatchThePublishedMemberInventory()
        {
            MethodInfo[] methods = typeof(ExtrusionClientMembers).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            Assert.Multiple(() =>
            {
                Assert.That(methods.Count(method => method.ReturnType.IsGenericType &&
                    method.ReturnType.GetGenericTypeDefinition() == typeof(ClientVariable<>)), Is.EqualTo(141));
                Assert.That(methods.Count(method => method.ReturnType.IsGenericType &&
                    method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)), Is.EqualTo(17));
                Assert.That(methods.All(method => method.GetParameters().Length > 0 &&
                    typeof(ObjectTypeClient).IsAssignableFrom(method.GetParameters()[0].ParameterType)), Is.True);
            });
        }

        [TestCase("Absent")]
        [TestCase("Foreign")]
        [TestCase("Unique")]
        [TestCase("Repeated")]
        [TestCase("Distinct")]
        [TestCase("NullThenUnique")]
        [TestCase("MissingNamespace")]
        public async Task ExtrusionVariablesRequireUniqueQualifiedBrowseNamesAsync(string shape)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(includeExtrusion: shape != "MissingNamespace",
                token: cancellation.Token);
            NodeId variable = peer.NextNode();
            if (shape != "MissingNamespace")
            {
                ReferenceDescription correct = ExtrusionClientPeer.Variable(variable, peer.General("IsPresent"));
                ReferenceDescription foreign = ExtrusionClientPeer.Variable(peer.NextNode(),
                    new QualifiedName("IsPresent", 2));
                ReferenceDescription[] references = shape switch
                {
                    "Absent" => [],
                    "Foreign" => [foreign],
                    "Unique" => [foreign, correct],
                    "Repeated" => [correct, correct],
                    "Distinct" => [correct, ExtrusionClientPeer.Variable(peer.NextNode(), peer.General("IsPresent"))],
                    "NullThenUnique" => [ExtrusionClientPeer.Variable(NodeId.Null, peer.General("IsPresent")), correct],
                    _ => throw new ArgumentOutOfRangeException(nameof(shape))
                };
                peer.ExpectBrowse(ExtrusionClientPeer.DeviceId, 0, references);
            }
            ClientVariable<bool> member = peer.Client.Extruder(ExtrusionClientPeer.DeviceId).IsPresentVariable();
            if (shape is "Repeated" or "Distinct")
            {
                ExtrusionClientPeer.AssertFailure(async () =>
                    await member.ResolveAsync(cancellation.Token).ConfigureAwait(false),
                        StatusCodes.BadBrowseNameDuplicated);
            }
            else
            {
                NodeId resolved = await member.ResolveAsync(cancellation.Token).ConfigureAwait(false);
                Assert.That(resolved, Is.EqualTo(shape is "Unique" or "NullThenUnique" ? variable : NodeId.Null));
            }
            Assert.That(member.Writable, Is.False);
            if (shape == "MissingNamespace")
            {
                peer.VerifyNoServices();
            }
            peer.Verify();
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        public async Task ExtrusionBrowseShapesAcceptOmittedDiagnosticsAndRejectMalformedResultsAsync(int count,
            bool denied)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            StatusCode status = denied ? StatusCodes.BadUserAccessDenied : StatusCodes.Good;
            NodeId variable = peer.NextNode();
            var results = new BrowseResult[count];
            for (int index = 0; index < count; index++)
            {
                results[index] = new BrowseResult
                {
                    StatusCode = status,
                    References = [ExtrusionClientPeer.Variable(variable, peer.General("LineId"))]
                };
            }
            peer.ExpectBrowseResults(ExtrusionClientPeer.DeviceId, 0, results);
            ClientVariable<string> member = peer.Client.Extruder(ExtrusionClientPeer.DeviceId).LineIdVariable();
            if (count == 1 && status == StatusCodes.Good)
            {
                Assert.That(await member.ResolveAsync(cancellation.Token).ConfigureAwait(false), Is.EqualTo(variable));
            }
            else
            {
                ExtrusionClientPeer.AssertFailure(async () =>
                    await member.ResolveAsync(cancellation.Token).ConfigureAwait(false),
                    count == 1 ? status : StatusCodes.BadUnexpectedError);
            }
            Assert.That(member.Writable, Is.True);
            peer.Verify();
        }

        [TestCase("Boolean", "Good")]
        [TestCase("Boolean", "Bad")]
        [TestCase("String", "Good")]
        [TestCase("Double", "Good")]
        [TestCase("Double", "Uncertain")]
        public async Task ExtrusionReadsPreserveDecodedValuesQualityAndBothTimestampsAsync(string kind, string quality)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            StatusCode status = quality switch
            {
                "Bad" => StatusCodes.BadDataLost,
                "Uncertain" => StatusCodes.UncertainSensorNotAccurate,
                _ => StatusCodes.Good
            };
            NodeId variable = peer.NextNode();
            NodeId owner = kind == "Double" ? ExtrusionClientPeer.FilterId : ExtrusionClientPeer.DeviceId;
            QualifiedName name = kind switch
            {
                "Boolean" => peer.General("IsPresent"),
                "String" => peer.General("LineId"),
                _ => peer.FilterName("BackflushPressure")
            };
            peer.ExpectBrowse(owner, 0, ExtrusionClientPeer.Variable(variable, name));
            switch (kind)
            {
                case "Boolean":
                    bool present = status == StatusCodes.Good;
                    peer.ExpectRead(variable, ExtrusionClientPeer.Sample(Variant.From(present), status));
                    ExtrusionClientPeer.AssertReading(
                        await peer.Client.Extruder(owner).IsPresentVariable().ReadAsync(cancellation.Token)
                            .ConfigureAwait(false),
                        present, true, status);
                    break;
                case "String":
                    peer.ExpectRead(variable, ExtrusionClientPeer.Sample(Variant.From("Line-27/β"), status));
                    ExtrusionClientPeer.AssertReading(
                        await peer.Client.Extruder(owner).LineIdVariable().ReadAsync(cancellation.Token)
                            .ConfigureAwait(false),
                        "Line-27/β", true, status);
                    break;
                case "Double":
                    peer.ExpectRead(variable, ExtrusionClientPeer.Sample(Variant.From(43.75), status));
                    ExtrusionClientPeer.AssertReading(
                        await peer.Client.Filter(owner).BackflushPressureVariable().ReadAsync(cancellation.Token)
                            .ConfigureAwait(false),
                        43.75, true, status);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            peer.Verify();
        }

        [TestCase("Missing")]
        [TestCase("Null")]
        [TestCase("GoodWrongType")]
        [TestCase("BadWrongType")]
        [TestCase("EmptyResults")]
        [TestCase("ExtraResults")]
        public async Task ExtrusionReadsDistinguishMissingWrongTypeAndNullValuesAsync(string shape)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId variable = peer.NextNode();
            peer.ExpectBrowse(ExtrusionClientPeer.FilterId, 0, shape == "Missing"
                ? [] : [ExtrusionClientPeer.Variable(variable, peer.FilterName("BackflushPressure"))]);
            StatusCode quality = shape == "BadWrongType" ? StatusCodes.BadSensorFailure : StatusCodes.Good;
            Variant payload = shape == "Null" ? Variant.Null : Variant.From("not a pressure");
            if (shape != "Missing")
            {
                DataValue sample = ExtrusionClientPeer.Sample(payload, quality);
                peer.ExpectRead(variable, shape switch
                {
                    "EmptyResults" => [],
                    "ExtraResults" => [sample, sample],
                    _ => [sample]
                });
            }
            ClientVariable<double> member =
                peer.Client.Filter(ExtrusionClientPeer.FilterId).BackflushPressureVariable();
            if (shape is "Null" or "BadWrongType")
            {
                ClientRead<double> value = await member.ReadAsync(cancellation.Token).ConfigureAwait(false);
                ExtrusionClientPeer.AssertReading(value, 0.0, false, quality);
                Assert.That(value.DataValue.WrappedValue, Is.EqualTo(payload));
            }
            else
            {
                ExtrusionClientPeer.AssertFailure(async () =>
                    await member.ReadAsync(cancellation.Token).ConfigureAwait(false), shape switch
                    {
                        "Missing" => StatusCodes.BadNotFound,
                        "GoodWrongType" => StatusCodes.BadTypeMismatch,
                        _ => StatusCodes.BadUnexpectedError
                    });
            }
            peer.Verify();
        }

        [TestCase("String")]
        [TestCase("DoubleClamped")]
        [TestCase("ReadOnly")]
        [TestCase("Missing")]
        [TestCase("Denied")]
        [TestCase("EmptyResults")]
        [TestCase("ExtraResults")]
        [TestCase("ValidDiagnostics")]
        [TestCase("ExtraDiagnostics")]
        public async Task ExtrusionWritesUseResolvedIdsAndValidateResponsesAsync(string shape)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            bool text = shape == "String";
            NodeId owner = text ? ExtrusionClientPeer.DeviceId : ExtrusionClientPeer.FilterId;
            NodeId variable = peer.NextNode();
            if (shape != "ReadOnly")
            {
                peer.ExpectBrowse(owner, 0, shape == "Missing" ? [] :
                    [ExtrusionClientPeer.Variable(variable, text
                        ? peer.General("LineId") : peer.FilterName("BackflushPressure"))]);
            }
            if (shape is not ("ReadOnly" or "Missing"))
            {
                StatusCode status = shape switch
                {
                    "Denied" => StatusCodes.BadUserAccessDenied,
                    "DoubleClamped" => StatusCodes.GoodClamped,
                    _ => StatusCodes.Good
                };
                peer.ExpectWrite(variable, text ? Variant.From("Line-Δ-43") : Variant.From(18.625),
                    shape switch
                    {
                        "EmptyResults" => [],
                        "ExtraResults" => [status, status],
                        _ => [status]
                    },
                    shape switch
                    {
                        "ValidDiagnostics" => [new DiagnosticInfo()],
                        "ExtraDiagnostics" => [new DiagnosticInfo(), new DiagnosticInfo()],
                        _ => []
                    });
            }
            async Task WriteAsync()
            {
                if (shape == "ReadOnly")
                {
                    await peer.Client.Extruder(ExtrusionClientPeer.DeviceId).IsPresentVariable()
                        .WriteAsync(true, cancellation.Token).ConfigureAwait(false);
                }
                else if (text)
                {
                    await peer.Client.Extruder(owner).LineIdVariable().WriteAsync("Line-Δ-43", cancellation.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    await peer.Client.Filter(owner).BackflushPressureVariable().WriteAsync(18.625, cancellation.Token)
                        .ConfigureAwait(false);
                }
            }
            if (shape is "String" or "DoubleClamped" or "ValidDiagnostics")
            {
                await WriteAsync().ConfigureAwait(false);
                Assert.That(text ? peer.Client.Extruder(owner).LineIdVariable().Writable :
                    peer.Client.Filter(owner).BackflushPressureVariable().Writable, Is.True);
            }
            else
            {
                ExtrusionClientPeer.AssertFailure(WriteAsync, shape switch
                {
                    "ReadOnly" => StatusCodes.BadNotWritable,
                    "Missing" => StatusCodes.BadNotFound,
                    "Denied" => StatusCodes.BadUserAccessDenied,
                    _ => StatusCodes.BadUnexpectedError
                });
            }
            if (shape == "ReadOnly")
            {
                peer.VerifyNoServices();
            }
            peer.Verify();
        }

        [TestCase("Good")]
        [TestCase("MissingMember")]
        [TestCase("MissingUnits")]
        [TestCase("BadQuality")]
        [TestCase("WrongType")]
        [TestCase("Null")]
        public async Task ExtrusionUnitsUseNamespaceZeroAndPreserveEuInformationAsync(string shape)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId pressure = peer.NextNode();
            NodeId units = peer.NextNode();
            peer.ExpectBrowse(ExtrusionClientPeer.FilterId, 0, shape == "MissingMember" ? [] :
                [ExtrusionClientPeer.Variable(pressure, peer.FilterName("BackflushPressure"))]);
            if (shape != "MissingMember")
            {
                ReferenceDescription foreign = ExtrusionClientPeer.Variable(peer.NextNode(),
                    new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits, 2));
                peer.ExpectBrowse(pressure, 0, shape == "MissingUnits" ? [foreign] :
                    [foreign, ExtrusionClientPeer.Variable(units,
                        new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits))]);
            }
            if (shape is not ("MissingMember" or "MissingUnits"))
            {
                Variant payload = shape switch
                {
                    "WrongType" => Variant.From("bar"),
                    "Null" => Variant.Null,
                    _ => Variant.FromStructure(new EUInformation
                    {
                        NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                        UnitId = 0x424152,
                        DisplayName = new LocalizedText("en", "bar"),
                        Description = new LocalizedText("en", "bar [unit of pressure]")
                    })
                };
                peer.ExpectRead(units, ExtrusionClientPeer.Sample(payload,
                    shape == "BadQuality" ? StatusCodes.BadOutOfService : StatusCodes.Good));
            }
            ClientVariable<double> member =
                peer.Client.Filter(ExtrusionClientPeer.FilterId).BackflushPressureVariable();
            if (shape == "Good")
            {
                EUInformation actual = await member.ReadEngineeringUnitsAsync(cancellation.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(actual.UnitId, Is.EqualTo(0x424152));
                    Assert.That(actual.NamespaceUri, Is.EqualTo("http://www.opcfoundation.org/UA/units/un/cefact"));
                    Assert.That(actual.DisplayName, Is.EqualTo(new LocalizedText("en", "bar")));
                    Assert.That(actual.Description, Is.EqualTo(new LocalizedText("en", "bar [unit of pressure]")));
                });
            }
            else
            {
                ExtrusionClientPeer.AssertFailure(async () =>
                    await member.ReadEngineeringUnitsAsync(cancellation.Token).ConfigureAwait(false), shape switch
                    {
                        "MissingMember" or "MissingUnits" => StatusCodes.BadNotFound,
                        "BadQuality" => StatusCodes.BadOutOfService,
                        _ => StatusCodes.BadTypeMismatch
                    });
            }
            peer.Verify();
        }

        [TestCase("Empty")]
        [TestCase("Singleton")]
        [TestCase("Mixed")]
        [TestCase("MissingNamespace")]
        public async Task ExtrusionPlaceholdersDeduplicateIdsAndFilterExpectedTypesAsync(string shape)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(includeFilter: shape != "MissingNamespace", token: cancellation.Token);
            NodeId first = peer.NextNode();
            NodeId second = peer.NextNode();
            NodeId expectedType = peer.Type(Filter.ObjectTypeIds.FilterPackageType);
            if (shape != "MissingNamespace")
            {
                var expected = new List<NodeId>();
                var references = new List<ReferenceDescription>();
                if (shape != "Empty")
                {
                    references.Add(ExtrusionClientPeer.Reference(first, expectedType, new QualifiedName("Package", 1)));
                    expected.Add(first);
                    peer.ExpectType(expectedType, expectedType, true);
                }
                if (shape == "Mixed")
                {
                    var vendorType = new NodeId("VendorFilterPackage", 2);
                    var wrongType = new NodeId("Unrelated", 2);
                    NodeId contradictory = peer.NextNode();
                    references.Add(references[0]);
                    references.Add(ExtrusionClientPeer.Reference(second, vendorType, new QualifiedName("Package", 2)));
                    references.Add(ExtrusionClientPeer.Reference(NodeId.Null, expectedType,
                        new QualifiedName("Null", 1)));
                    references.Add(ExtrusionClientPeer.Reference(new ExpandedNodeId("foreign", "urn:absent"),
                        expectedType, new QualifiedName("Remote", 1)));
                    references.Add(ExtrusionClientPeer.Reference(peer.NextNode(), NodeId.Null,
                        new QualifiedName("NoType", 1)));
                    references.Add(ExtrusionClientPeer.Reference(contradictory, wrongType,
                        new QualifiedName("Wrong", 1)));
                    references.Add(ExtrusionClientPeer.Reference(contradictory, expectedType,
                        new QualifiedName("LaterDuplicate", 1)));
                    expected.Add(second);
                    peer.ExpectType(vendorType, expectedType, true);
                    peer.ExpectType(wrongType, expectedType, false);
                }
                peer.ExpectBrowse(ExtrusionClientPeer.FilterId, (uint)NodeClass.Object, references.ToArray());
                List<Filter.FilterPackageTypeClient> values = await ExtrusionClientPeer.CollectAsync(
                    peer.Client.Filter(ExtrusionClientPeer.FilterId).EnumerateFilterPackageAsync(cancellation.Token))
                    .ConfigureAwait(false);
                Assert.That(values.Select(value => value.ObjectId), Is.EqualTo(expected));
                foreach (Filter.FilterPackageTypeClient value in values)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(value.GetType(), Is.EqualTo(typeof(Filter.FilterPackageTypeClient)));
                        Assert.That(value.Session, Is.SameAs(peer.Session.Object));
                        Assert.That(value.Telemetry, Is.SameAs(peer.Telemetry));
                    });
                }
            }
            else
            {
                Assert.That(await ExtrusionClientPeer.CollectAsync(peer.Client.Filter(ExtrusionClientPeer.FilterId)
                    .EnumerateFilterPackageAsync(cancellation.Token)).ConfigureAwait(false), Is.Empty);
                peer.VerifyNoServices();
            }
            peer.Verify();
        }

        [Test]
        public void ExtrusionPlaceholderCancellationReachesTypeChecking()
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId type = peer.Type(Filter.ObjectTypeIds.FilterPackageType);
            peer.ExpectBrowse(ExtrusionClientPeer.FilterId, (uint)NodeClass.Object,
                ExtrusionClientPeer.Reference(peer.NextNode(), type, new QualifiedName("Package", 1)));
            peer.ExpectType(type, type, true, beforeReturn: cancellation.Cancel);
            OperationCanceledException? failure = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await ExtrusionClientPeer.CollectAsync(peer.Client.Filter(ExtrusionClientPeer.FilterId)
                    .EnumerateFilterPackageAsync(cancellation.Token)).ConfigureAwait(false));
            Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
            peer.Verify();
        }

        [TestCase("Complete")]
        [TestCase("Break")]
        [TestCase("Empty")]
        [TestCase("GoodWrongType")]
        public async Task ExtrusionObservationsDecodeNotificationsAndReleaseEnumeratorsAsync(string ending)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId pressure = peer.NextNode();
            peer.ExpectBrowse(ExtrusionClientPeer.FilterId, 0,
                ExtrusionClientPeer.Variable(pressure, peer.FilterName("BackflushPressure")));
            DataValue[] samples = ending switch
            {
                "Empty" => [],
                "GoodWrongType" =>
                [
                    ExtrusionClientPeer.Sample(Variant.From(37.125)),
                    ExtrusionClientPeer.Sample(Variant.From("not a pressure"))
                ],
                _ =>
                [
                    ExtrusionClientPeer.Sample(Variant.From(37.125)),
                    ExtrusionClientPeer.Sample(Variant.From(42.25), StatusCodes.UncertainSensorNotAccurate),
                    ExtrusionClientPeer.Sample(Variant.From(47.75), StatusCodes.BadSensorFailure),
                    ExtrusionClientPeer.Sample(Variant.From("sensor offline"), StatusCodes.BadOutOfService),
                    ExtrusionClientPeer.Sample(Variant.Null)
                ]
            };
            var options = new MonitoredItemOptions { QueueSize = 7, DiscardOldest = false };
            int produced = 0;
            int disposed = 0;
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            stream.Setup(value => value.SubscribeDataChangesAsync(pressure, options, cancellation.Token))
                .Callback((NodeId _, MonitoredItemOptions actual, CancellationToken _) =>
                    Assert.That(actual, Is.SameAs(options)))
                .Returns(ExtrusionClientPeer.Notifications(
                    samples.Select(sample => new DataValueChange(null, sample, null)).ToArray(),
                    () => produced++, () => disposed++, null, cancellation.Token));
            await using (IAsyncEnumerator<ClientRead<double>> iterator =
                peer.Client.Filter(ExtrusionClientPeer.FilterId)
                .BackflushPressureVariable().ObserveAsync(stream.Object, options, cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token))
            {
                if (ending == "Empty")
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
                }
                else
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                    ExtrusionClientPeer.AssertReading(iterator.Current, 37.125, true, StatusCodes.Good);
                    if (ending == "GoodWrongType")
                    {
                        ExtrusionClientPeer.AssertFailure(async () =>
                            await iterator.MoveNextAsync().ConfigureAwait(false), StatusCodes.BadTypeMismatch);
                    }
                    else if (ending == "Complete")
                    {
                        (double Value, bool HasValue, StatusCode Quality)[] expected =
                        [
                            (42.25, true, StatusCodes.UncertainSensorNotAccurate),
                            (47.75, true, StatusCodes.BadSensorFailure),
                            (0.0, false, StatusCodes.BadOutOfService),
                            (0.0, false, StatusCodes.Good)
                        ];
                        foreach ((double value, bool hasValue, StatusCode quality) in expected)
                        {
                            Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                            ExtrusionClientPeer.AssertReading(iterator.Current, value, hasValue, quality);
                        }
                        Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.False);
                    }
                }
            }
            Assert.That(produced, Is.EqualTo(ending switch
            {
                "Complete" => 5,
                "Break" => 1,
                "GoodWrongType" => 2,
                _ => 0
            }));
            Assert.That(disposed, Is.EqualTo(1));
            stream.Verify(value => value.SubscribeDataChangesAsync(pressure, options, cancellation.Token), Times.Once);
            stream.Verify(value => value.DisposeAsync(), Times.Never);
            stream.VerifyNoOtherCalls();
            peer.Verify();
        }

        [TestCase("NoStreaming")]
        [TestCase("Missing")]
        [TestCase("Failure")]
        [TestCase("Cancel")]
        public async Task ExtrusionObservationsRequireStreamingAndPropagateFailuresAndCancellationAsync(string ending)
        {
            using var cancellation = new CancellationTokenSource();
            var peer = new ExtrusionClientPeer(token: cancellation.Token);
            NodeId variable = peer.NextNode();
            peer.ExpectBrowse(ExtrusionClientPeer.DeviceId, 0, ending == "Missing" ? [] :
                [ExtrusionClientPeer.Variable(variable, peer.General("LineId"))]);
            int produced = 0;
            int disposed = 0;
            var failure = new InvalidOperationException("The caller's data stream failed.");
            var stream = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            if (ending is "Failure" or "Cancel")
            {
                stream.Setup(value => value.SubscribeDataChangesAsync(variable, null, cancellation.Token))
                    .Returns(ExtrusionClientPeer.Notifications(
                        new[]
                        {
                            new DataValueChange(null, ExtrusionClientPeer.Sample(Variant.From("Line-East")), null),
                            new DataValueChange(null, ExtrusionClientPeer.Sample(Variant.From("Line-West")), null)
                        }, () => produced++, () => disposed++, ending == "Failure" ? failure : null,
                            cancellation.Token));
            }
            await using (IAsyncEnumerator<ClientRead<string>> iterator =
                peer.Client.Extruder(ExtrusionClientPeer.DeviceId)
                .LineIdVariable().ObserveAsync(ending == "NoStreaming" ? null : stream.Object,
                    cancellationToken: cancellation.Token).GetAsyncEnumerator(cancellation.Token))
            {
                if (ending == "NoStreaming")
                {
                    InvalidOperationException? absent = Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await iterator.MoveNextAsync().ConfigureAwait(false));
                    Assert.That(absent!.Message, Does.Contain("Supply a streaming subscription"));
                }
                else if (ending == "Missing")
                {
                    ExtrusionClientPeer.AssertFailure(async () =>
                        await iterator.MoveNextAsync().ConfigureAwait(false), StatusCodes.BadNotFound);
                }
                else
                {
                    Assert.That(await iterator.MoveNextAsync().ConfigureAwait(false), Is.True);
                    ExtrusionClientPeer.AssertReading(iterator.Current, "Line-East", true, StatusCodes.Good);
                    if (ending == "Failure")
                    {
                        Assert.That(Assert.ThrowsAsync<InvalidOperationException>(
                            async () => await iterator.MoveNextAsync().ConfigureAwait(false)), Is.SameAs(failure));
                    }
                    else
                    {
                        cancellation.Cancel();
                        OperationCanceledException? cancelled = Assert.ThrowsAsync<OperationCanceledException>(
                            async () => await iterator.MoveNextAsync().ConfigureAwait(false));
                        Assert.That(cancelled!.CancellationToken, Is.EqualTo(cancellation.Token));
                    }
                }
            }
            bool subscribed = ending is "Failure" or "Cancel";
            Assert.That(produced, Is.EqualTo(subscribed ? 1 : 0));
            Assert.That(disposed, Is.EqualTo(subscribed ? 1 : 0));
            stream.Verify(value => value.SubscribeDataChangesAsync(variable, null, cancellation.Token),
                subscribed ? Times.Once() : Times.Never());
            stream.Verify(value => value.DisposeAsync(), Times.Never);
            stream.VerifyNoOtherCalls();
            peer.Verify();
        }

        [TestCase("NullVariableOwner")]
        [TestCase("NullPlaceholderOwner")]
        [TestCase("SessionClientOnly")]
        public void ExtrusionPublicMembersRejectNullOwnersAndSessionClientOnlyProxies(string shape)
        {
            var peer = new ExtrusionClientPeer();
            if (shape == "NullVariableOwner")
            {
                ExtrusionDeviceTypeClient proxy = null!;
                ArgumentNullException? failure = Assert.Throws<ArgumentNullException>(() => proxy.IsPresentVariable());
                Assert.That(failure!.ParamName, Is.EqualTo("owner"));
            }
            else if (shape == "NullPlaceholderOwner")
            {
                Filter.Filter_InterfaceTypeClient proxy = null!;
                ArgumentNullException? failure = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    await ExtrusionClientPeer.CollectAsync(proxy.EnumerateFilterPackageAsync()).ConfigureAwait(false));
                Assert.That(failure!.ParamName, Is.EqualTo("proxy"));
            }
            else
            {
                var service = new Mock<ISessionClient>(MockBehavior.Strict);
                var proxy = new Extruder.Extruder_InterfaceTypeClient(service.Object, ExtrusionClientPeer.DeviceId,
                    peer.Telemetry);
                ArgumentException? failure = Assert.ThrowsAsync<ArgumentException>(
                    async () => await proxy.LineIdVariable().ResolveAsync().ConfigureAwait(false));
                Assert.That(failure!.ParamName, Is.EqualTo("proxy"));
                Assert.That(service.Invocations, Is.Empty);
            }
            peer.VerifyNoServices();
            peer.Verify();
        }
    }
}
