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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.DocumentationLinks;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Gives registered assets the <c>DocumentationLinks</c> AddIn
    /// (OPC 10000-110 §10.5) and changes, adds and removes links in a hosted
    /// server, across restarts.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class DocumentationLinksTests
    {
        private const string ManualUri = "https://acme.example/sensor/manual.pdf";

        private static readonly string[] s_declaredNames = ["Manual", "OperatorHandbook"];

        private static readonly QualifiedName[] s_editingUnits =
        [
            new("AMB DocumentationLinks Base"),
            new("AMB DocumentationLinks Edit Base"),
            new("AMB DocumentationLinks Edit Advanced")
        ];

        [Test]
        public async Task TheAddInListsTheDeclaredLinksAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(TheAddInListsTheDeclaredLinksAsync),
                    builder => builder.WithDocumentationLinks(links => links
                        .Add("Manual", ManualUri, new LocalizedText("The operating manual."))
                        .AddEditable("OperatorHandbook", "https://acme.example/handbook")))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IDocumentationLinks links = asset.DocumentationLinks!;
            DocumentationLinksState addIn = await server.FindNodeAsync<DocumentationLinksState>(links.NodeId)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> addIns = await server
                .BrowseAsync(device.NodeId, Ua.ReferenceTypeIds.HasAddIn)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> components = await server
                .BrowseAsync(addIn.NodeId, Ua.ReferenceTypeIds.HasComponent)
                .ConfigureAwait(false);
            DocumentationLink manual = Single(links.Links, link => link.BrowseName.Name == "Manual");
            BaseVariableState manualVariable = await server.FindNodeAsync<BaseVariableState>(manual.NodeId)
                .ConfigureAwait(false);
            StatusCode readOnly = await server.WriteAsync(manual.NodeId, Variant.From("https://other"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(addIns.Select(reference => reference.NodeId), Does.Contain((ExpandedNodeId)addIn.NodeId));
                Assert.That(
                    addIn.BrowseName,
                    Is.EqualTo(new QualifiedName("DocumentationLinks", server.Manager.AmbNamespaceIndex)));
                Assert.That(addIn.TypeDefinitionId, Is.EqualTo(server.ToNodeId(ObjectTypeIds.DocumentationLinksType)));
                Assert.That(addIn.AddLink, Is.Null, "no user links were allowed");
                Assert.That(links.AllowsUserLinks, Is.False);
                Assert.That(links.Links.Count, Is.EqualTo(2));
                Assert.That(
                    components.Select(reference => reference.BrowseName.Name),
                    Is.EquivalentTo(s_declaredNames));
                Assert.That(manual.Uri, Is.EqualTo(ManualUri));
                Assert.That(manual.IsEditable, Is.False);
                Assert.That(manual.IsUserLink, Is.False);
                Assert.That(manualVariable.DataType, Is.EqualTo(Ua.DataTypeIds.UriString));
                Assert.That(manualVariable.Description.Text, Is.EqualTo("The operating manual."));
                Assert.That(StatusCode.IsBad(readOnly), Is.True, "a manufacturer link is read only");
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB DocumentationLinks Base")));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB DocumentationLinks Edit Base")),
                    "the memory store does not keep what clients write");
            });
        }

        [Test]
        public async Task AnonymousUsersCannotChangeLinksByDefaultAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AnonymousUsersCannotChangeLinksByDefaultAsync),
                    builder => builder.WithDocumentationLinks(links => links
                        .AddEditable("OperatorHandbook")
                        .AllowUserLinks()),
                    options => options.UseFileSystemStores(StateDirectory()))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IDocumentationLinks links = asset.DocumentationLinks!;
            DocumentationLinksState addIn = await server.FindNodeAsync<DocumentationLinksState>(links.NodeId)
                .ConfigureAwait(false);

            StatusCode write = await server.WriteAsync(links.Links[0].NodeId, Variant.From("https://mine"))
                .ConfigureAwait(false);
            CallMethodResult add = await AddLinkAsync(server, addIn, ManualUri, "Mine").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(write, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(add.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(links.Links.Count, Is.EqualTo(1));
                Assert.That(links.Links[0].Uri, Is.Empty);
            });
        }

        [Test]
        public async Task LinksUsersChangeAndAddSurviveARestartAsync()
        {
            string state = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(DocumentationLinksTests),
                Guid.NewGuid().ToString("N"));
            const string name = nameof(LinksUsersChangeAndAddSurviveARestartAsync);
            NodeId added;
            NodeId removable;
            await using (AmbHostedServer first = await StartAsync(name, state).ConfigureAwait(false))
            {
                IDocumentationLinks links = Asset(first).DocumentationLinks!;
                DocumentationLinksState addIn = await first.FindNodeAsync<DocumentationLinksState>(links.NodeId)
                    .ConfigureAwait(false);
                NodeId handbook = Single(links.Links, link => link.IsEditable).NodeId;

                StatusCode written = await first.WriteAsync(handbook, Variant.From("https://plant.example/handbook"))
                    .ConfigureAwait(false);
                StatusCode tooLong = await first.WriteAsync(handbook, Variant.From(new string('x', 2049)))
                    .ConfigureAwait(false);
                StatusCode wrongType = await first.WriteAsync(handbook, Variant.From(42)).ConfigureAwait(false);

                CallMethodResult result = await AddLinkAsync(first, addIn, "https://plant.example/wiring", "Wiring")
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, result.StatusCode.ToString());
                added = result.OutputArguments[0].TryGetValue(out NodeId linkVariable) ? linkVariable : NodeId.Null;
                CallMethodResult second = await AddLinkAsync(first, addIn, "https://plant.example/spares", "Spares")
                    .ConfigureAwait(false);
                removable = second.OutputArguments[0].TryGetValue(out NodeId spares) ? spares : NodeId.Null;

                CallMethodResult duplicate = await AddLinkAsync(first, addIn, "https://other", "Wiring")
                    .ConfigureAwait(false);
                CallMethodResult declaredName = await AddLinkAsync(first, addIn, "https://other", "Manual")
                    .ConfigureAwait(false);
                CallMethodResult empty = await AddLinkAsync(first, addIn, string.Empty, "Empty").ConfigureAwait(false);
                CallMethodResult beyondLimit = await AddLinkAsync(first, addIn, "https://other", "Third")
                    .ConfigureAwait(false);
                CallMethodResult removeManual = await RemoveLinkAsync(
                    first,
                    addIn,
                    Single(links.Links, link => link.BrowseName.Name == "Manual").NodeId).ConfigureAwait(false);
                CallMethodResult removeUnknown = await RemoveLinkAsync(first, addIn, new NodeId(4711u, 1))
                    .ConfigureAwait(false);
                DataValue addedValue = await first.ReadAsync(added).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(written, Is.EqualTo(StatusCodes.Good));
                    Assert.That(tooLong, Is.EqualTo(StatusCodes.BadOutOfRange));
                    Assert.That(wrongType, Is.EqualTo(StatusCodes.BadTypeMismatch));
                    Assert.That(added.IsNull, Is.False);
                    Assert.That(addedValue.WrappedValue.TryGetValue(out string uri) ? uri : null, Is.EqualTo(
                        "https://plant.example/wiring"));
                    Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(declaredName.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(empty.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(beyondLimit.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument), "two per asset");
                    Assert.That(removeManual.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(removeUnknown.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(links.Links.Count, Is.EqualTo(4));
                    Assert.That(
                        first.Manager.ConformanceUnits.ToArray(),
                        Is.SupersetOf(s_editingUnits));
                });
            }

            await using (AmbHostedServer restarted = await StartAsync(name, state).ConfigureAwait(false))
            {
                IDocumentationLinks links = Asset(restarted).DocumentationLinks!;
                DocumentationLinksState addIn = await restarted.FindNodeAsync<DocumentationLinksState>(links.NodeId)
                    .ConfigureAwait(false);
                DataValue handbook = await restarted
                    .ReadAsync(Single(links.Links, link => link.BrowseName.Name == "OperatorHandbook").NodeId)
                    .ConfigureAwait(false);
                DataValue wiring = await restarted.ReadAsync(added).ConfigureAwait(false);
                StatusCode rewritten = await restarted.WriteAsync(added, Variant.From("https://plant.example/wiring2"))
                    .ConfigureAwait(false);
                CallMethodResult removed = await RemoveLinkAsync(restarted, addIn, removable).ConfigureAwait(false);
                IReadOnlyList<ReferenceDescription> components = await restarted
                    .BrowseAsync(addIn.NodeId, Ua.ReferenceTypeIds.HasComponent)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        handbook.WrappedValue.TryGetValue(out string text) ? text : null,
                        Is.EqualTo("https://plant.example/handbook"));
                    Assert.That(
                        wiring.WrappedValue.TryGetValue(out string link) ? link : null,
                        Is.EqualTo("https://plant.example/wiring"),
                        "the added link keeps its NodeId across the restart");
                    Assert.That(rewritten, Is.EqualTo(StatusCodes.Good));
                    Assert.That(StatusCode.IsGood(removed.StatusCode), Is.True, removed.StatusCode.ToString());
                    Assert.That(
                        components.Select(reference => reference.NodeId),
                        Does.Not.Contain((ExpandedNodeId)removable));
                    Assert.That(links.Links.Count, Is.EqualTo(3));
                });
            }

            await using AmbHostedServer third = await StartAsync(name, state).ConfigureAwait(false);
            IDocumentationLinks restored = Asset(third).DocumentationLinks!;
            DocumentationLink wiringLink = Single(restored.Links, link => link.IsUserLink);
            Assert.Multiple(() =>
            {
                Assert.That(restored.Links.Count, Is.EqualTo(3), "the removed link stays removed");
                Assert.That(wiringLink.NodeId, Is.EqualTo(added));
                Assert.That(wiringLink.Uri, Is.EqualTo("https://plant.example/wiring2"));
            });
        }

        [Test]
        public async Task OnlyAddedLinksCarryTheUserLinkPropertyAsync()
        {
            string state = StateDirectory();
            const string name = nameof(OnlyAddedLinksCarryTheUserLinkPropertyAsync);
            NodeId added;
            NodeId marker;
            await using (AmbHostedServer first = await StartAsync(name, state).ConfigureAwait(false))
            {
                IDocumentationLinks links = Asset(first).DocumentationLinks!;
                DocumentationLinksState addIn = await first.FindNodeAsync<DocumentationLinksState>(links.NodeId)
                    .ConfigureAwait(false);
                CallMethodResult result = await AddLinkAsync(first, addIn, "https://plant.example/wiring", "Wiring")
                    .ConfigureAwait(false);
                added = result.OutputArguments[0].TryGetValue(out NodeId linkVariable) ? linkVariable : NodeId.Null;

                IReadOnlyList<ReferenceDescription> properties = await first
                    .BrowseAsync(added, Ua.ReferenceTypeIds.HasProperty)
                    .ConfigureAwait(false);
                ReferenceDescription property = properties.Single(
                    reference => reference.BrowseName.Name == DocumentationLinkProperties.UserLink);
                marker = first.ToNodeId(property.NodeId);
                DataValue value = await first.ReadAsync(marker).ConfigureAwait(false);
                DataValue dataType = await first.ReadAsync(marker, Attributes.DataType).ConfigureAwait(false);
                var declared = new List<IReadOnlyList<ReferenceDescription>>();
                foreach (DocumentationLink link in links.Links.ToArray()!)
                {
                    if (!link.IsUserLink)
                    {
                        declared.Add(await first.BrowseAsync(link.NodeId, Ua.ReferenceTypeIds.HasProperty)
                            .ConfigureAwait(false));
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(
                        property.BrowseName.NamespaceIndex,
                        Is.EqualTo(first.Manager.TypeNamespaceIndex),
                        "not an AMB name: the server qualifies it with its namespace of server-specific types");
                    Assert.That(property.TypeDefinition, Is.EqualTo((ExpandedNodeId)VariableTypeIds.PropertyType));
                    Assert.That(value.WrappedValue.TryGetValue(out bool isUserLink) && isUserLink, Is.True);
                    Assert.That(dataType.WrappedValue.TryGetValue(out NodeId type) ? type : NodeId.Null, Is.EqualTo(
                        Ua.DataTypeIds.Boolean));
                    Assert.That(declared, Has.Count.EqualTo(2), "the manual and the editable handbook");
                    Assert.That(
                        declared.SelectMany(references => references)
                            .Select(reference => reference.BrowseName.Name),
                        Has.None.EqualTo(DocumentationLinkProperties.UserLink),
                        "the links of the manufacturer are not removable and carry no marker");
                });
            }

            await using AmbHostedServer restarted = await StartAsync(name, state).ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> restored = await restarted
                .BrowseAsync(added, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false);
            DocumentationLinksState restartedAddIn = await restarted
                .FindNodeAsync<DocumentationLinksState>(Asset(restarted).DocumentationLinks!.NodeId)
                .ConfigureAwait(false);
            CallMethodResult removed = await RemoveLinkAsync(restarted, restartedAddIn, added).ConfigureAwait(false);
            DataValue gone = await restarted.ReadAsync(marker).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    restored.Select(reference => restarted.ToNodeId(reference.NodeId)),
                    Does.Contain(marker),
                    "the restored link carries the marker under the same NodeId");
                Assert.That(StatusCode.IsGood(removed.StatusCode), Is.True, removed.StatusCode.ToString());
                Assert.That(
                    gone.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                    "RemoveLink takes the marker along");
            });
        }

        [Test]
        public async Task TheTextsOfAddedLinksAreBoundedAsync()
        {
            string state = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(DocumentationLinksTests),
                Guid.NewGuid().ToString("N"));
            const string name = nameof(TheTextsOfAddedLinksAreBoundedAsync);
            string longDisplayName = new('d', 100);
            string longDescription = new('e', 300);
            NodeId added;
            await using (AmbHostedServer first = await StartAsync(name, state).ConfigureAwait(false))
            {
                DocumentationLinksState addIn = await first
                    .FindNodeAsync<DocumentationLinksState>(Asset(first).DocumentationLinks!.NodeId)
                    .ConfigureAwait(false);
                ushort ns = first.Manager.InstanceNamespaceIndex;

                CallMethodResult longName = await AddLinkAsync(
                    first,
                    addIn,
                    "https://plant.example/a",
                    new QualifiedName(new string('n', 129), ns),
                    new LocalizedText("Name"),
                    default).ConfigureAwait(false);
                CallMethodResult longTitle = await AddLinkAsync(
                    first,
                    addIn,
                    "https://plant.example/b",
                    new QualifiedName("Title", ns),
                    new LocalizedText(new string('t', 129)),
                    default).ConfigureAwait(false);
                CallMethodResult longText = await AddLinkAsync(
                    first,
                    addIn,
                    "https://plant.example/c",
                    new QualifiedName("Text", ns),
                    new LocalizedText("Text"),
                    new LocalizedText(new string('x', 1025))).ConfigureAwait(false);
                CallMethodResult longLocale = await AddLinkAsync(
                    first,
                    addIn,
                    "https://plant.example/d",
                    new QualifiedName("Locale", ns),
                    new LocalizedText(new string('l', 65), "Locale"),
                    default).ConfigureAwait(false);
                CallMethodResult fits = await AddLinkAsync(
                    first,
                    addIn,
                    "https://plant.example/wiring",
                    new QualifiedName("Wiring", ns),
                    new LocalizedText(longDisplayName),
                    new LocalizedText(longDescription)).ConfigureAwait(false);
                added = fits.OutputArguments[0].TryGetValue(out NodeId link) ? link : NodeId.Null;

                Assert.Multiple(() =>
                {
                    Assert.That(longName.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(longTitle.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(longText.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(longLocale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(StatusCode.IsGood(fits.StatusCode), Is.True, fits.StatusCode.ToString());
                });
            }

            // Lower limits than the link was added with: it comes back cut.
            await using AmbHostedServer restarted = await StartAsync(
                name,
                state,
                options =>
                {
                    options.MaxDocumentationLinkNameLength = 20;
                    options.MaxDocumentationLinkDescriptionLength = 50;
                }).ConfigureAwait(false);
            BaseVariableState restored = await restarted.FindNodeAsync<BaseVariableState>(added).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(restored.DisplayName.Text, Is.EqualTo(longDisplayName.Substring(0, 20)));
                Assert.That(restored.Description.Text, Is.EqualTo(longDescription.Substring(0, 50)));
                Assert.That(
                    () => new AssetManagement(new AmbServerOptions { MaxDocumentationLinkNameLength = 0 }),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void TheBuilderAndTheOptionsRejectInvalidLinks()
        {
            var builder = new AssetBuilder();
            builder.WithDocumentationLinks(links => links.Add("Manual", ManualUri));

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => builder.WithDocumentationLinks(null!));
                Assert.Throws<ArgumentException>(
                    () => builder.WithDocumentationLinks(links => links.Add("Manual", "https://other")));
                Assert.Throws<ArgumentException>(
                    () => builder.WithDocumentationLinks(links => links.AddEditable("Manual")));
                Assert.Throws<ArgumentException>(
                    () => builder.WithDocumentationLinks(links => links.Add("Empty", string.Empty)));
                Assert.Throws<ArgumentException>(
                    () => builder.WithDocumentationLinks(links => links.AddEditable(string.Empty)));
                Assert.That(builder.DocumentationLinks!.Links, Has.Count.EqualTo(1));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new AssetManagement(new AmbServerOptions { MaxDocumentationLinkLength = 254 }));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new AssetManagement(new AmbServerOptions { MaxUserLinksPerAsset = 1 }));
            });
        }

        [Test]
        public async Task AnAssetWithoutProductInstanceUriCannotKeepUserLinksAsync()
        {
            ServiceResultException? refused = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnAssetWithoutProductInstanceUriCannotKeepUserLinksAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement(options =>
                    {
                        options.RequireProductInstanceUri = false;
                        options.UseFileSystemStores(StateDirectory());
                    })
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device =
                            await AssetRegistrationTests.CreateDeviceAsync(context, "Anonymous", null).ConfigureAwait(false);
                        try
                        {
                            await device.RegisterAsAssetAsync(
                                context.GetRequiredService<IAssetManagement>(),
                                asset => asset.WithDocumentationLinks(links => links.AllowUserLinks())).ConfigureAwait(false);
                        }
                        catch (ServiceResultException ex)
                        {
                            refused = ex;
                        }
                    })).ConfigureAwait(false);

            Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task UserLinksNeedAPersistentStoreAsync()
        {
            ServiceResultException? refused = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(UserLinksNeedAPersistentStoreAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device =
                            await AssetRegistrationTests.CreateDeviceAsync(context, "Sensor", "urn:acme:sensor:1").ConfigureAwait(false);
                        try
                        {
                            await device.RegisterAsAssetAsync(
                                context.GetRequiredService<IAssetManagement>(),
                                asset => asset.WithDocumentationLinks(links => links.AllowUserLinks())).ConfigureAwait(false);
                        }
                        catch (ServiceResultException ex)
                        {
                            refused = ex;
                        }
                    })).ConfigureAwait(false);

            Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError), "§10.5.3");
        }

        [Test]
        public async Task AnUnsetEditableLinkIsNullAndCountsNotAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AnUnsetEditableLinkIsNullAndCountsNotAsync),
                    builder => builder.WithDocumentationLinks(links => links.AddEditable("OperatorHandbook")),
                    options =>
                    {
                        options.UseFileSystemStores(StateDirectory());
                        options.AuthorizeLinkEdit = _ => true;
                    })
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            NodeId handbook = asset.DocumentationLinks!.Links[0].NodeId;
            DataValue unset = await server.ReadAsync(handbook).ConfigureAwait(false);
            QualifiedName[] before = server.Manager.ConformanceUnits.ToArray()!;

            StatusCode written = await server.WriteAsync(handbook, Variant.From("https://plant.example/handbook"))
                .ConfigureAwait(false);
            DataValue published = await server
                .ReadAsync(Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(unset.WrappedValue.IsNull, Is.True, "an unset link is unknown, not empty");
                Assert.That(before, Does.Not.Contain(new QualifiedName("AMB DocumentationLinks Base")));
                Assert.That(StatusCode.IsGood(written), Is.True, written.ToString());
                Assert.That(
                    published.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units) ? units.ToArray() : null,
                    Does.Contain(new QualifiedName("AMB DocumentationLinks Base")),
                    "a link that got a value is published again");
            });
        }

        [Test]
        public async Task TheUserAccessShowsWhoMayChangeLinksAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(TheUserAccessShowsWhoMayChangeLinksAsync),
                    builder => builder.WithDocumentationLinks(links => links
                        .AddEditable("OperatorHandbook")
                        .AllowUserLinks()),
                    options => options.UseFileSystemStores(StateDirectory()))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IDocumentationLinks links = asset.DocumentationLinks!;
            DocumentationLinksState addIn = await server.FindNodeAsync<DocumentationLinksState>(links.NodeId)
                .ConfigureAwait(false);
            BaseVariableState handbook = await server.FindNodeAsync<BaseVariableState>(links.Links[0].NodeId)
                .ConfigureAwait(false);

            DataValue userAccess = await server.ReadAsync(handbook.NodeId, Attributes.UserAccessLevel)
                .ConfigureAwait(false);
            DataValue userExecutable = await server.ReadAsync(addIn.AddLink!.NodeId, Attributes.UserExecutable)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(handbook.AccessLevel & AccessLevels.CurrentWrite, Is.Not.Zero, "the link is writable");
                Assert.That(
                    userAccess.WrappedValue.TryGetValue(out byte level) ? level & AccessLevels.CurrentWrite : -1,
                    Is.Zero,
                    "but not for an anonymous user");
                Assert.That(
                    userExecutable.WrappedValue.TryGetValue(out bool executable) && !executable,
                    Is.True,
                    "nor AddLink");
            });
        }

        private static string StateDirectory()
        {
            return Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(DocumentationLinksTests),
                Guid.NewGuid().ToString("N"));
        }

        internal static Task<CallMethodResult> AddLinkAsync(
            AmbHostedServer server,
            DocumentationLinksState addIn,
            string uri,
            string name)
        {
            return server.CallAsync(
                addIn.NodeId,
                addIn.AddLink!.NodeId,
                Variant.From(uri),
                Variant.From(new QualifiedName(name, server.Manager.InstanceNamespaceIndex)),
                Variant.From(new LocalizedText(name)),
                Variant.From(new LocalizedText("Added by a user.")));
        }

        internal static Task<CallMethodResult> AddLinkAsync(
            AmbHostedServer server,
            DocumentationLinksState addIn,
            string uri,
            QualifiedName browseName,
            LocalizedText displayName,
            LocalizedText description)
        {
            return server.CallAsync(
                addIn.NodeId,
                addIn.AddLink!.NodeId,
                Variant.From(uri),
                Variant.From(browseName),
                Variant.From(displayName),
                Variant.From(description));
        }

        internal static Task<CallMethodResult> RemoveLinkAsync(
            AmbHostedServer server,
            DocumentationLinksState addIn,
            NodeId link)
        {
            return server.CallAsync(addIn.NodeId, addIn.RemoveLink!.NodeId, Variant.From(link));
        }

        private static DocumentationLink Single(
            ArrayOf<DocumentationLink> links,
            Func<DocumentationLink, bool> predicate)
        {
            DocumentationLink? found = null;
            foreach (DocumentationLink link in links)
            {
                if (predicate(link))
                {
                    Assert.That(found, Is.Null, "one link matches");
                    found = link;
                }
            }
            Assert.That(found, Is.Not.Null, "a link matches");
            return found!;
        }

        private static IAssetHandle Asset(AmbHostedServer server)
        {
            return server.AssetManagement.Assets[0];
        }

        /// <summary>
        /// Starts the server of the restart test; every run has the same
        /// application identity, so the namespaces of the links are the same.
        /// </summary>
        private static Task<AmbHostedServer> StartAsync(
            string name,
            string state,
            Action<AmbServerOptions>? configure = null)
        {
            return AmbHostedServer.StartAsync(
                name,
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement(options =>
                    {
                        options.UseFileSystemStores(state);
                        options.MaxUserLinksPerAsset = 2;
                        options.AuthorizeLinkEdit = _ => true;
                        configure?.Invoke(options);
                    })
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device =
                            await AssetRegistrationTests.CreateDeviceAsync(context, "Sensor", "urn:acme:sensor:4711").ConfigureAwait(false);
                        await device.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            asset => asset.WithDocumentationLinks(links => links
                                .Add("Manual", ManualUri)
                                .AddEditable("OperatorHandbook")
                                .AllowUserLinks()),
                            context.CancellationToken).ConfigureAwait(false);
                    }));
        }
    }
}
