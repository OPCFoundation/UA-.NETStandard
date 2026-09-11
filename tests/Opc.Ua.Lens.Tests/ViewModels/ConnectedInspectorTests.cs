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
using Avalonia.Threading;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.ViewModels;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedInspectorTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task AttributeLoadsKeepObservableChangesOnTheDesktopAndRejectStaleResults(bool stale)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            using var model = new NodeAttributesViewModel(context.Desktop.Telemetry, context.Desktop.Connection);
            var completion = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            ArrayOf<ReadValueId> requested = default;
            CancellationToken received = default;
            context.Read = (ids, token) =>
            {
                requested = ids;
                received = token;
                return new ValueTask<ReadResponse>(completion.Task);
            };
            bool onDesktop = true;
            model.Rows.CollectionChanged += (_, _) => onDesktop &= Dispatcher.UIThread.CheckAccess();
            NodeId node = new("Pressure", 2);
            Task loading = model.LoadAsync(node, NodeClass.Variable);
            Assert.That(requested.Count, Is.GreaterThan(10));
            Assert.That(requested.ToArray()!.Select(value => value.NodeId), Is.All.EqualTo(node));
            Assert.That(requested[0].AttributeId, Is.EqualTo(Attributes.NodeId));
            if (stale)
            {
                model.Clear();
                Assert.That(received.IsCancellationRequested, Is.True);
            }
            DataValue[] values = requested.ToArray()!.Select(value => Attribute(value.AttributeId)).ToArray();
            await Task.Run(() => completion.SetResult(new ReadResponse { Results = values })).ConfigureAwait(true);
            await loading.ConfigureAwait(true);
            Assert.That(onDesktop, Is.True, "Observable rows belong to the desktop that requested the load.");
            if (stale)
            {
                Assert.That(model.Rows, Is.Empty);
                Assert.That(model.Header, Is.EqualTo("(no node selected)"));
            }
            else
            {
                Assert.That(model.Rows.Single(row => row.Name == "NodeClass").Value, Is.EqualTo("Variable"));
                Assert.That(model.Rows.Single(row => row.Name == "AccessLevel").Value,
                    Is.EqualTo("CurrentRead | CurrentWrite (0x03)"));
                Assert.That(model.Rows.Single(row => row.Name == "ValueRank").Value, Does.Contain("Scalar"));
                Assert.That(model.Rows.Single(row => row.Name == "RolePermissions").Value,
                    Does.StartWith("(not supported: BadAttributeIdInvalid"));
            }
        });
    }

    [Test]
    public Task ReferencesResolveUniqueTypeNamesAcrossPagesAndPublishOnTheDesktop()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            using var model = new ReferencesViewModel(context.Desktop.Telemetry, context.Desktop.Connection);
            NodeId target = new("Parent", 2);
            ByteString point = new(new byte[] { 5, 4, 3 });
            context.Browse = (ids, _) =>
            {
                Assert.That(ids.Count, Is.EqualTo(1));
                Assert.That(ids[0].NodeId, Is.EqualTo(target));
                Assert.That(ids[0].BrowseDirection, Is.EqualTo(BrowseDirection.Both));
                Assert.That(ids[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.References));
                return ValueTask.FromResult(new BrowseResponse
                {
                    Results = [new BrowseResult
                    {
                        References = [Reference("Child", true), Reference("Other", false)], ContinuationPoint = point
                    }]
                });
            };
            int continuations = 0;
            context.BrowseNext = (release, points, _) =>
            {
                Assert.That(release, Is.False);
                Assert.That(points.Count, Is.EqualTo(1));
                Assert.That(points[0], Is.EqualTo(point));
                continuations++;
                return ValueTask.FromResult(new BrowseNextResponse
                {
                    Results = [new BrowseResult { References = [Reference("Last", true)] }]
                });
            };
            var read = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Read = (ids, _) =>
            {
                Assert.That(ids.Count, Is.EqualTo(1), "One read per distinct reference type, not per link.");
                Assert.That(ids[0].NodeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.BrowseName));
                return new ValueTask<ReadResponse>(read.Task);
            };
            bool onDesktop = true;
            model.Rows.CollectionChanged += (_, _) => onDesktop &= Dispatcher.UIThread.CheckAccess();
            Task loading = model.LoadAsync(target, NodeClass.Object);
            await Task.Run(() => read.SetResult(new ReadResponse
            {
                Results = [new DataValue(Variant.From(new QualifiedName("HasComponent")))]
            })).ConfigureAwait(true);
            await loading.ConfigureAwait(true);
            Assert.That(continuations, Is.EqualTo(1));
            Assert.That(onDesktop, Is.True);
            Assert.That(model.Rows.Select(row => row.ReferenceType), Is.All.EqualTo("HasComponent"));
            Assert.That(model.Rows.Select(row => row.Direction), Is.EqualTo(s_directions));
            Assert.That(model.Rows.Select(row => row.TargetBrowseName), Is.EqualTo(s_targets));
        });
    }

    [Test]
    public Task LateReferenceTypeReadCannotRepopulateClearedSelection()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            using var model = new ReferencesViewModel(context.Desktop.Telemetry, context.Desktop.Connection);
            context.Browse = (_, _) => ValueTask.FromResult(new BrowseResponse
            {
                Results = [new BrowseResult { References = [Reference("Old selection", true)] }]
            });
            var read = new TaskCompletionSource<ReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Read = (_, _) => new ValueTask<ReadResponse>(read.Task);
            Task loading = model.LoadAsync(new NodeId(4001u), NodeClass.Object);
            model.Clear();
            read.SetResult(new ReadResponse
            {
                Results = [new DataValue(Variant.From(new QualifiedName("HasComponent")))]
            });
            await loading.ConfigureAwait(true);
            Assert.That(model.Rows, Is.Empty);
            Assert.That(model.Header, Is.EqualTo("(no node selected)"));
        });
    }

    [TestCaseSource(nameof(AttributeCases))]
    public Task AttributeFormattingPreservesTypedFlagsRanksAndValues(
        uint attribute, Variant value, string expected, int nodeClass)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            using var model = new NodeAttributesViewModel(context.Desktop.Telemetry, context.Desktop.Connection);
            int position = -1;
            context.Read = (ids, _) =>
            {
                DataValue[] results = new DataValue[ids.Count];
                for (int i = 0; i < ids.Count; i++)
                {
                    results[i] = ids[i].AttributeId == attribute
                        ? new DataValue(value) : DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid);
                    if (ids[i].AttributeId == attribute)
                    {
                        position = i;
                    }
                }
                return ValueTask.FromResult(new ReadResponse { Results = results });
            };
            await model.LoadAsync(new NodeId(4001u), (NodeClass)nodeClass).ConfigureAwait(true);
            Assert.That(position, Is.GreaterThanOrEqualTo(0), "The requested attribute must be in the service batch.");
            string name = NodeAttributeSets.SupportedAttributes((NodeClass)nodeClass)[position].Name;
            Assert.That(model.Rows.Single(row => row.Name == name).Value, Is.EqualTo(expected));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(16)]
    [TestCase(17)]
    public Task RolePermissionArraysShowActualRolesAndBoundThePreview(int count)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            using var model = new NodeAttributesViewModel(context.Desktop.Telemetry, context.Desktop.Connection);
            RolePermissionType[] roles = Enumerable.Range(0, count).Select(index => new RolePermissionType
            {
                RoleId = index == 0 ? ObjectIds.WellKnownRole_Observer : new NodeId((uint)(7000 + index)),
                Permissions = (uint)(PermissionType.Read | PermissionType.Browse)
            }).ToArray();
            context.Read = (ids, _) => ValueTask.FromResult(new ReadResponse
            {
                Results = ids.ToArray()!.Select(id => id.AttributeId == Attributes.RolePermissions
                    ? new DataValue(Variant.From((ArrayOf<ExtensionObject>)roles
                        .Select(role => new ExtensionObject(role)).ToArray()))
                    : DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)).ToArray()
            });
            await model.LoadAsync(new NodeId(4001u), NodeClass.Variable).ConfigureAwait(true);
            string text = model.Rows.Single(row => row.Name == "RolePermissions").Value;
            if (count == 0)
            {
                Assert.That(text, Is.EqualTo("(none)"));
            }
            else
            {
                Assert.That(text, Does.StartWith("[Observer = Browse|Read"));
                Assert.That(text.Split(" = ", StringSplitOptions.None), Has.Length.EqualTo(Math.Min(16, count) + 1));
                Assert.That(text.EndsWith("; …]", StringComparison.Ordinal), Is.EqualTo(count > 16));
            }
        });
    }

    private static IEnumerable<TestCaseData> AttributeCases()
    {
        yield return new(Attributes.AccessLevel, Variant.From((byte)0), "None (0x00)", (int)NodeClass.Variable);
        yield return new(Attributes.AccessLevel, Variant.From((byte)127),
            "CurrentRead | CurrentWrite | HistoryRead | HistoryWrite | SemanticChange | StatusWrite | TimestampWrite (0x7F)",
            (int)NodeClass.Variable);
        yield return new(Attributes.AccessLevelEx, Variant.From(0x1234u), "0x00001234", (int)NodeClass.Variable);
        yield return new(Attributes.AccessRestrictions, Variant.From((ushort)0), "None (0x0000)", (int)NodeClass.Variable);
        yield return new(Attributes.AccessRestrictions, Variant.From((ushort)15),
            "SigningRequired | EncryptionRequired | SessionRequired | ApplyRestrictionsToBrowse (0x000F)",
            (int)NodeClass.Variable);
        yield return new(Attributes.AccessRestrictions, Variant.From(3u),
            "SigningRequired | EncryptionRequired (0x0003)", (int)NodeClass.Variable);
        yield return new(Attributes.EventNotifier, Variant.From((byte)0), "None (0x00)", (int)NodeClass.Object);
        yield return new(Attributes.EventNotifier, Variant.From((byte)13),
            "SubscribeToEvents | HistoryRead | HistoryWrite (0x0D)", (int)NodeClass.Object);
        yield return new(Attributes.ValueRank, Variant.From(-3), "ScalarOrOneDimension (-3)", (int)NodeClass.Variable);
        yield return new(Attributes.ValueRank, Variant.From(-2), "Any (-2)", (int)NodeClass.Variable);
        yield return new(Attributes.ValueRank, Variant.From(0), "OneOrMoreDimensions (0)", (int)NodeClass.Variable);
        yield return new(Attributes.ValueRank, Variant.From(1), "OneDimension (1)", (int)NodeClass.Variable);
        yield return new(Attributes.ValueRank, Variant.From(2), "TwoDimensions (2)", (int)NodeClass.Variable);
        yield return new(Attributes.ValueRank, Variant.From(3), "3", (int)NodeClass.Variable);
        yield return new(Attributes.Description, Variant.Null, "(null)", (int)NodeClass.Variable);
        yield return new(Attributes.Description, Variant.From(new LocalizedText("A label")), "A label",
            (int)NodeClass.Variable);
        yield return new(Attributes.BrowseName, Variant.From(new QualifiedName("Pressure", 2)), "2:Pressure",
            (int)NodeClass.Variable);
    }

    private static ReferenceDescription Reference(string name, bool forward)
    {
        return new ReferenceDescription
        {
            NodeId = new NodeId(name, 2),
            BrowseName = new QualifiedName(name),
            DisplayName = new LocalizedText(name),
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            NodeClass = NodeClass.Variable,
            IsForward = forward
        };
    }

    private static DataValue Attribute(uint id)
    {
        return id switch
        {
            Attributes.NodeClass => new DataValue(Variant.From((int)NodeClass.Variable)),
            Attributes.NodeId => new DataValue(Variant.From(new NodeId("Pressure", 2))),
            Attributes.AccessLevel => new DataValue(Variant.From((byte)3)),
            Attributes.ValueRank => new DataValue(Variant.From(ValueRanks.Scalar)),
            Attributes.RolePermissions => DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid),
            _ => new DataValue(Variant.From("fixture"))
        };
    }

    private static readonly string[] s_directions = ["→", "←", "→"];
    private static readonly string[] s_targets = ["Child", "Other", "Last"];
}
