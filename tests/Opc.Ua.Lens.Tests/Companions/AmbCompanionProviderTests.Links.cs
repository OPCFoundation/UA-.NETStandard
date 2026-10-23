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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using AmbNames = Opc.Ua.AMB.BrowseNames;

namespace UaLens.Tests.Companions
{
    public sealed partial class AmbCompanionProviderTests
    {
        [Test]
        public async Task LinkEditPinsTheOwnedVariableAndOldUriAsync()
        {
            var test = new AssetTaskContext();
            LinkFixture link = AddDocumentation(test, true);
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "write-link",
                [new("linkId", Variant.From(link.Id)), new("uri", Variant.From("https://example.invalid/revised"))],
                CancellationToken.None).ConfigureAwait(false);
            await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "write-link", prepared, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(test.Writes, Has.Count.EqualTo(1));
            Assert.That(test.Writes[0].NodeId, Is.EqualTo(link.Id));
            Assert.That(test.Writes[0].Value.WrappedValue, Is.EqualTo(Variant.From("https://example.invalid/revised")));
        }

        [Test]
        public async Task UriChangedAfterPreparationCannotBeOverwrittenAsync()
        {
            var test = new AssetTaskContext();
            LinkFixture link = AddDocumentation(test, true);
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "write-link",
                [new("linkId", Variant.From(link.Id)), new("uri", Variant.From("https://example.invalid/revised"))],
                CancellationToken.None).ConfigureAwait(false);
            test.Session.SetValue(link.Id, Variant.From("https://example.invalid/another-edit"));
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "write-link", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
            Assert.That(test.Writes, Is.Empty);
        }

        [TestCase("foreign")]
        [TestCase("read-only")]
        [TestCase("not-user")]
        public async Task LinkOwnershipAndPermissionAreRequiredBeforeMutationAsync(string kind)
        {
            var test = new AssetTaskContext();
            LinkFixture link = AddDocumentation(test, kind != "not-user");
            string operation = kind == "not-user" ? "remove-link" : "write-link";
            NodeId target = kind == "foreign" ? test.Session.Id("another-assets-link") : link.Id;
            if (kind == "read-only")
            {
                test.Session.SetValue(link.Id, Variant.From((byte)AccessLevels.CurrentRead), Attributes.UserAccessLevel);
            }
            ArrayOf<CompanionValue> inputs = kind == "not-user"
                ? [new("linkId", Variant.From(target))]
                : [new("linkId", Variant.From(target)), new("uri", Variant.From("https://example.invalid/revised"))];
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, test.Target, operation, inputs, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(test.Writes, Is.Empty);
            Assert.That(test.Session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task RemovingAUserLinkCallsOnlyItsVerifiedAddInMethodAsync()
        {
            var test = new AssetTaskContext();
            LinkFixture link = AddDocumentation(test, true);
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "remove-link", [new("linkId", Variant.From(link.Id))], CancellationToken.None)
                .ConfigureAwait(false);
            test.Session.CallHandler = (call, _) =>
            {
                Assert.That(call.ObjectId, Is.EqualTo(link.Owner));
                Assert.That(call.MethodId, Is.EqualTo(link.RemoveMethod));
                Assert.That(call.InputArguments, Has.Count.EqualTo(1));
                Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(link.Id)));
                return new CallMethodResult();
            };
            await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "remove-link", prepared, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(test.Session.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RemovedUserMarkerRevokesAnExistingRemovalPreparationAsync()
        {
            var test = new AssetTaskContext();
            LinkFixture link = AddDocumentation(test, true);
            CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "remove-link", [new("linkId", Variant.From(link.Id))], CancellationToken.None)
                .ConfigureAwait(false);
            test.Session.SetValue(link.Marker, Variant.From(false));
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "remove-link", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            Assert.That(test.Session.Calls.IsEmpty, Is.True);
        }

        private static LinkFixture AddDocumentation(AssetTaskContext test, bool userLink)
        {
            IndustrialCompanionTestSession session = test.Session;
            NodeId addIn = session.Id("documentation");
            session.AddObject(addIn, Opc.Ua.AMB.ObjectTypeIds.DocumentationLinksType);
            session.AddChild(test.Target.NodeId, addIn, Opc.Ua.AMB.Namespaces.AMB, AmbNames.DocumentationLinks);
            NodeId link = session.AddProperty(
                addIn, Opc.Ua.AMB.Namespaces.AMB, "Manual", Variant.From("https://example.invalid/manual"));
            session.SetValue(link, Variant.From((byte)AccessLevels.CurrentReadOrWrite), Attributes.AccessLevel);
            session.SetValue(link, Variant.From((byte)AccessLevels.CurrentReadOrWrite), Attributes.UserAccessLevel);
            session.SetValue(link, Variant.From(DataTypeIds.UriString), Attributes.DataType);
            session.SetReferences(addIn, BrowseDirection.Forward, ReferenceTypeIds.HasComponent,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(link),
                    NodeClass = NodeClass.Variable,
                    BrowseName = new QualifiedName("Manual", session.InstanceNamespaceIndex),
                    DisplayName = new LocalizedText("Manual")
                }
            ]);
            NodeId marker = session.Id("user-link");
            session.SetValue(marker, Variant.From(userLink));
            session.SetReferences(link, BrowseDirection.Forward, ReferenceTypeIds.HasProperty,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(marker),
                    NodeClass = NodeClass.Variable,
                    BrowseName = new QualifiedName(
                        Opc.Ua.AMB.DocumentationLinkProperties.UserLink, session.InstanceNamespaceIndex)
                }
            ]);
            NodeId remove = session.Id("remove-link");
            session.AddObject(remove, ObjectTypeIds.BaseObjectType);
            session.AddChild(addIn, remove, Opc.Ua.AMB.Namespaces.AMB, AmbNames.RemoveLink);
            session.SetValue(remove, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
            session.SetValue(remove, Variant.From(true), Attributes.Executable);
            session.SetValue(remove, Variant.From(true), Attributes.UserExecutable);
            session.AddProperty(remove, Namespaces.OpcUa, BrowseNames.InputArguments,
                Variant.FromStructure((ArrayOf<Argument>)
                [
                    new Argument { DataType = DataTypeIds.NodeId, ValueRank = ValueRanks.Scalar }
                ]));
            return new LinkFixture(link, addIn, marker, remove);
        }

        private sealed record LinkFixture(NodeId Id, NodeId Owner, NodeId Marker, NodeId RemoveMethod);
    }
}
