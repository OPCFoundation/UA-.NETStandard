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
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Performance;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class PerformanceTargetDialogWorkflowTests
{
    [TestCase("i=11", -1, 3, BuiltInType.Double)]
    [TestCase("i=12", 1, 1, BuiltInType.String)]
    [TestCase("i=6", -1, 0, BuiltInType.Int32)]
    [TestCase("ns=2;i=11", 2, 3, BuiltInType.Int32)]
    [TestCase("ns=2;s=CustomNumber", -1, 3, BuiltInType.Int32)]
    [TestCase("i=0", -1, 1, BuiltInType.Int32)]
    [TestCase("i=99999", 1, 3, BuiltInType.Int32)]
    public Task ExplicitHintOverridesWorkspaceSelectionAndWriteMetadataUsesExactAttributeOrder(
        string dataType, int rank, int access, BuiltInType expectedType)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var protocol = new TargetProtocol();
            var workspace = new Mock<IPluginWorkspace>();
            NodeViewModel selection = Node(context, "WrongSelection", NodeClass.Method);
            NodeViewModel hint = Node(context, "Temperature", NodeClass.Variable);
            workspace.SetupGet(w => w.SelectedNode).Returns(selection);
            protocol.Read = _ => new ValueTask<ReadResponse>(new ReadResponse
            {
                Results =
                [
                    new DataValue(new Variant(NodeId.Parse(dataType))),
                    new DataValue(new Variant(rank)),
                    new DataValue(new Variant((byte)access))
                ]
            });
            var dialog = new PerformanceTargetDialog(workspace.Object, protocol.Session.Object, hint);
            Task<BenchmarkTarget?> shown = dialog.ShowDialog<BenchmarkTarget?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "SelectionLabel").Text,
                    Is.EqualTo("Temperature  ·  ns=2;s=Temperature"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DetailsLabel").Text,
                    Is.EqualTo($"DataType: {NodeId.Parse(dataType)}  ({expectedType})\nValueRank: {rank}\n" +
                        $"AccessLevel: 0x{access:X2}"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel").Text,
                    (access & 2) != 0 ? Does.StartWith("Ready.") : Does.Contain("not writable"));
                Assert.That(protocol.Reads.Single().Select(id => id.AttributeId),
                    Is.EqualTo(new uint[] { 14, 15, 17 }));
                Assert.That(protocol.Reads[0].Select(id => id.NodeId),
                    Is.All.EqualTo(new NodeId("Temperature", 2)));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                BenchmarkTarget? result = await shown.ConfigureAwait(true);
                Assert.That(result, Is.SameAs(dialog.Result));
                Assert.That(result!.NodeId, Is.EqualTo(new NodeId("Temperature", 2)));
                Assert.That(result.ObjectId.IsNull, Is.True);
                Assert.That(result.Mode, Is.EqualTo(BenchmarkMode.Write));
                Assert.That(result.BuiltInType, Is.EqualTo(expectedType));
                Assert.That(result.ValueRank, Is.EqualTo(rank));
                Assert.That(result.InputArguments, Is.Null);
                Assert.That(result.DisplayName, Is.EqualTo("write ns=2;s=Temperature"));
                Assert.That(protocol.Browses, Is.Empty);
                AssertNoWorkload(protocol);
                workspace.VerifyGet(w => w.SelectedNode, Times.Never);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("empty", -1)]
    [TestCase("short", -1)]
    [TestCase("bad", -1)]
    [TestCase("nullValues", 0)]
    [TestCase("wrongTypes", 0)]
    public Task IncompleteWriteMetadataDisplaysFallbackAndReadOnlyWarning(string responseKind, int rank)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var protocol = new TargetProtocol();
            protocol.Read = _ => new ValueTask<ReadResponse>(new ReadResponse
            {
                Results = responseKind switch
                {
                    "empty" => [],
                    "short" => [new DataValue(new Variant(DataTypeIds.Double))],
                    "nullValues" => [new DataValue(), new DataValue(), new DataValue()],
                    "wrongTypes" =>
                    [
                        new DataValue(new Variant("Double")), new DataValue(new Variant("Scalar")),
                        new DataValue(new Variant(3))
                    ],
                    _ =>
                    [
                        DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid),
                        DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid),
                        DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied)
                    ]
                }
            });
            var workspace = new Mock<IPluginWorkspace>();
            workspace.SetupGet(w => w.SelectedNode).Returns(Node(context, "Pressure", NodeClass.Variable));
            var dialog = new PerformanceTargetDialog(workspace.Object, protocol.Session.Object);
            Task<BenchmarkTarget?> shown = dialog.ShowDialog<BenchmarkTarget?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DetailsLabel").Text,
                    Is.EqualTo($"DataType: i=0  (Int32)\nValueRank: {rank}\nAccessLevel: 0x00"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel").Text,
                    Does.Contain("benchmark will report write errors"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(protocol.Reads, Has.Count.EqualTo(1));
                AssertNoWorkload(protocol);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase(false, 0)]
    [TestCase(false, 3)]
    [TestCase(true, 0)]
    public Task CallTargetResolvesParentAndDisplaysCurrentArgumentPayloadContract(bool cachedParent, int count)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var protocol = new TargetProtocol();
            NodeId parent = new("Boiler", 2);
            NodeId method = new("Calibrate", 2);
            NodeId input = new("Calibrate.InputArguments", 2);
            Argument[] arguments = new[]
            {
                new Argument { Name = "Offset", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
                new Argument { Name = "Labels", DataType = DataTypeIds.String, ValueRank = ValueRanks.OneDimension },
                new Argument
                {
                    Name = "Mode", DataType = new NodeId("CalibrationMode", 2), ValueRank = ValueRanks.Scalar
                }
            }.Take(count).ToArray();
            protocol.Browse = description => new ValueTask<BrowseResponse>(new BrowseResponse
            {
                Results = [new BrowseResult
                {
                    References = description.BrowseDirection == BrowseDirection.Inverse
                        ? [Reference(parent, "Boiler", NodeClass.Object)]
                        : count == 0
                            ? [Reference(new NodeId("Output", 2), "OutputArguments", NodeClass.Variable)]
                            : [Reference(input, BrowseNames.InputArguments, NodeClass.Variable)]
                }]
            });
            protocol.Read = _ => new ValueTask<ReadResponse>(new ReadResponse
            {
                Results = [new DataValue(new Variant(new ArrayOf<ExtensionObject>(
                    arguments.Select(argument => new ExtensionObject(argument)).ToArray())))]
            });
            var workspace = new Mock<IPluginWorkspace>();
            workspace.SetupGet(w => w.SelectedNode).Returns(new NodeViewModel(
                context.Browser, cachedParent ? parent : NodeId.Null, method, "Calibrate", NodeClass.Method));
            var dialog = new PerformanceTargetDialog(workspace.Object, protocol.Session.Object);
            Task<BenchmarkTarget?> shown = dialog.ShowDialog<BenchmarkTarget?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DetailsLabel").Text,
                    Is.EqualTo("Write mode requires a Variable selection."));
                DesktopInteraction.Control<RadioButton>(dialog, "CallRadio").IsChecked = true;
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DetailsLabel").Text,
                    Is.EqualTo("Parent ObjectId: ns=2;s=Boiler"));
                string[] signature = DesktopInteraction.Control<StackPanel>(dialog, "SignaturePanel")
                    .Children.OfType<TextBlock>().Select(text => text.Text!).ToArray();
                if (count == 0)
                {
                    Assert.That(signature, Is.EqualTo(s_callTargetResolvesParentAndDisplaysCurrentArgumentPayloadContExpected));
                }
                else
                {
                    Assert.That(signature, Is.EqualTo(s_callTargetResolvesParentAndDisplaysCurrentArgumentPayloadContExpected2));
                }
                Assert.That(protocol.Browses, Has.Count.EqualTo(cachedParent ? 1 : 2));
                BrowseDescription properties = protocol.Browses[^1];
                Assert.That(properties.NodeId, Is.EqualTo(method));
                Assert.That(properties.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                Assert.That(properties.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
                Assert.That(properties.IncludeSubtypes, Is.False);
                Assert.That(properties.NodeClassMask, Is.EqualTo((uint)NodeClass.Variable));
                if (!cachedParent)
                {
                    Assert.That(protocol.Browses[0].BrowseDirection, Is.EqualTo(BrowseDirection.Inverse));
                    Assert.That(protocol.Browses[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                    Assert.That(protocol.Browses[0].NodeClassMask, Is.EqualTo((uint)NodeClass.Object));
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                BenchmarkTarget? result = await shown.ConfigureAwait(true);
                Assert.That(result!.Mode, Is.EqualTo(BenchmarkMode.Call));
                Assert.That(result.NodeId, Is.EqualTo(method));
                Assert.That(result.ObjectId, Is.EqualTo(parent));
                Assert.That(result.InputArguments, Has.Length.EqualTo(count));
                for (int i = 0; i < count; i++)
                {
                    Assert.That(result.InputArguments![i].IsEqual(arguments[i]), Is.True);
                }
                Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Variant));
                Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(result.DisplayName, Is.EqualTo("call ns=2;s=Calibrate"));
                Assert.That(protocol.Reads, Has.Count.EqualTo(count == 0 ? 0 : 1));
                if (count > 0)
                {
                    Assert.That(protocol.Reads[0].Single().NodeId, Is.EqualTo(input));
                    Assert.That(protocol.Reads[0][0].AttributeId, Is.EqualTo(Attributes.Value));
                }
                AssertNoWorkload(protocol);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestCase("noSelection")]
    [TestCase("object")]
    [TestCase("wrongMode")]
    [TestCase("missingParent")]
    [TestCase("parentFailure")]
    public Task MissingWrongClassOrUnresolvedParentCannotProduceATarget(string kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var protocol = new TargetProtocol();
            var workspace = new Mock<IPluginWorkspace>();
            workspace.SetupGet(w => w.SelectedNode).Returns(kind == "noSelection"
                ? null : Node(context, "Selection", kind == "object" ? NodeClass.Object : NodeClass.Method));
            protocol.Browse = request => kind == "parentFailure" && request.BrowseDirection == BrowseDirection.Inverse
                ? ValueTask.FromException<BrowseResponse>(new ServiceResultException(StatusCodes.BadUserAccessDenied))
                : new ValueTask<BrowseResponse>(new BrowseResponse());
            var dialog = new PerformanceTargetDialog(workspace.Object, protocol.Session.Object);
            Task<BenchmarkTarget?> shown = dialog.ShowDialog<BenchmarkTarget?>(DesktopInteraction.Owner);
            try
            {
                if (kind is "missingParent" or "parentFailure")
                {
                    DesktopInteraction.Control<RadioButton>(dialog, "CallRadio").IsChecked = true;
                    Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel").Text,
                        Does.StartWith("Cannot resolve parent ObjectId"));
                }
                else
                {
                    Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel").Text,
                        kind == "noSelection" ? Does.StartWith("Select a node") : Does.StartWith("Pick a Variable"));
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(protocol.Reads, Is.Empty);
                AssertNoWorkload(protocol);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task FailedMetadataReadShowsOriginalErrorAndCancelDoesNotAcceptFallback()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var protocol = new TargetProtocol
            {
                Read = _ => ValueTask.FromException<ReadResponse>(
                    new ServiceResultException(StatusCodes.BadCommunicationError, "metadata link failed"))
            };
            var workspace = new Mock<IPluginWorkspace>();
            var dialog = new PerformanceTargetDialog(
                workspace.Object, protocol.Session.Object, Node(context, "Temperature", NodeClass.Variable));
            Task<BenchmarkTarget?> shown = dialog.ShowDialog<BenchmarkTarget?>(DesktopInteraction.Owner);
            try
            {
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DetailsLabel").Text,
                    Is.EqualTo("(read failed)"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel").Text,
                    Is.EqualTo("Read failed: metadata link failed"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                Assert.That(protocol.Reads.Single(), Has.Length.EqualTo(3));
                AssertNoWorkload(protocol);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Test]
    public Task CancelDuringMetadataReadNeverAcceptsATargetAndTestDrainsTheLateReply()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            var response = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var protocol = new TargetProtocol { Read = _ => new ValueTask<ReadResponse>(response.Task) };
            var workspace = new Mock<IPluginWorkspace>();
            var dialog = new PerformanceTargetDialog(
                workspace.Object, protocol.Session.Object, Node(context, "Temperature", NodeClass.Variable));
            Task<BenchmarkTarget?> shown = dialog.ShowDialog<BenchmarkTarget?>(DesktopInteraction.Owner);
            TextBlock status = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
            try
            {
                Assert.That(protocol.Reads, Has.Count.EqualTo(1));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DetailsLabel").Text,
                    Is.EqualTo("(reading DataType + ValueRank…)"));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await shown.ConfigureAwait(true), Is.Null);
                await DesktopInteraction.ChangedAsync(status, () => status.Text?.StartsWith(
                    "Ready.", StringComparison.Ordinal) == true, () =>
                    {
                        response.SetResult(new ReadResponse
                        {
                            Results =
                            [
                                new DataValue(new Variant(DataTypeIds.Double)),
                                new DataValue(new Variant(ValueRanks.Scalar)),
                                new DataValue(new Variant((byte)AccessLevels.CurrentReadOrWrite))
                            ]
                        });
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);

                Assert.That(dialog.IsVisible, Is.False);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "DetailsLabel").Text,
                    Is.EqualTo("DataType: i=11  (Double)\nValueRank: -1\nAccessLevel: 0x03"));
                AssertNoWorkload(protocol);
            }
            finally
            {
                dialog.Close();
                response.TrySetResult(new ReadResponse());
                await response.Task.ConfigureAwait(true);
            }
        });
    }

    private static NodeViewModel Node(DesktopConnectionContext context, string name, NodeClass nodeClass)
    {
        return new NodeViewModel(context.Browser, NodeId.Null, new NodeId(name, 2), name, nodeClass);
    }

    private static ReferenceDescription Reference(NodeId id, string name, NodeClass nodeClass)
    {
        return new ReferenceDescription
        {
            NodeId = id, BrowseName = new QualifiedName(name),
            DisplayName = new LocalizedText(name), NodeClass = nodeClass
        };
    }

    private static void AssertNoWorkload(TargetProtocol protocol)
    {
        Assert.That(protocol.Session.Invocations.Where(call => call.Method.Name is "WriteAsync" or "CallAsync"),
            Is.Empty);
    }

    private sealed class TargetProtocol
    {
        public TargetProtocol()
        {
            ServiceMessageContext messages = ServiceMessageContext.Create(
                new UaLens.Telemetry.AppTelemetryContext(new UaLens.Telemetry.LogRingBuffer(32)));
            Session.SetupGet(s => s.NamespaceUris).Returns(messages.NamespaceUris);
            Session.SetupGet(s => s.MessageContext).Returns(messages);
            Session.Setup(s => s.ReadAsync(It.IsAny<RequestHeader?>(), It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, double maxAge, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> ids, CancellationToken token) =>
                {
                    Assert.That(header, Is.Null);
                    Assert.That(maxAge, Is.Zero);
                    Assert.That(timestamps, Is.EqualTo(TimestampsToReturn.Neither));
                    Assert.That(token, Is.EqualTo(CancellationToken.None));
                    Reads.Add(ids.ToArray() ?? throw new AssertionException("Read nodes must not be null."));
                    return Read(ids);
                });
            Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ViewDescription? view, uint limit,
                    ArrayOf<BrowseDescription> ids, CancellationToken token) =>
                {
                    Assert.That(header, Is.Null);
                    Assert.That(view, Is.Null);
                    Assert.That(limit, Is.Zero);
                    Assert.That(ids.Count, Is.EqualTo(1));
                    Assert.That(ids[0].ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
                    Assert.That(token, Is.EqualTo(CancellationToken.None));
                    Browses.Add(ids[0]);
                    return Browse(ids[0]);
                });
        }

        public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
        public List<ReadValueId[]> Reads { get; } = [];
        public List<BrowseDescription> Browses { get; } = [];
        public Func<ArrayOf<ReadValueId>, ValueTask<ReadResponse>> Read { get; set; } =
            _ => throw new InvalidOperationException("Unexpected target metadata read.");
        public Func<BrowseDescription, ValueTask<BrowseResponse>> Browse { get; set; } =
            _ => throw new InvalidOperationException("Unexpected target browse.");
    }

    private static readonly string[] s_callTargetResolvesParentAndDisplaysCurrentArgumentPayloadContExpected =
    [
        "(method has no input arguments)",
    ];
    private static readonly string[] s_callTargetResolvesParentAndDisplaysCurrentArgumentPayloadContExpected2 =
    [
        "InputArguments (synthesised per op):",
        "  [0] Offset  : i=11  (rank=-1) — synth as Double",
        "  [1] Labels  : i=12  (rank=1) — synth as String",
        "  [2] Mode  : ns=2;s=CalibrationMode  (rank=-1) — synth as Int32",
    ];
}
