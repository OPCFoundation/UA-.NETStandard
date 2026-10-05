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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Adds, changes and removes a documentation link over a real client
    /// session (OPC 10000-110 §10.5.3, §10.5.4).
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbDocumentationLinksSessionTests
    {
        [TestCase(true, TestName = "AClientManagesItsLinksWhenAllowed")]
        [TestCase(false, TestName = "AnAnonymousClientIsRefusedByDefault")]
        public async Task AClientManagesItsLinksAsync(bool allowAnonymous)
        {
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AmbDocumentationLinksSessionTests),
                Guid.NewGuid().ToString("N"));
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AmbDocumentationLinksSessionTests) + allowAnonymous,
                    builder => builder.WithDocumentationLinks(links => links.AllowUserLinks()),
                    options =>
                    {
                        options.UseFileSystemStores(Path.Combine(root, "state"));
                        if (allowAnonymous)
                        {
                            options.AuthorizeLinkEdit = _ => true;
                        }
                    })
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            Opc.Ua.AMB.Server.DocumentationLinks.IDocumentationLinks links = asset.DocumentationLinks!;
            DocumentationLinksState addIn = await server
                .FindNodeAsync<DocumentationLinksState>(links.NodeId)
                .ConfigureAwait(false);

            using var clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
            await clientFixture.LoadClientConfigurationAsync(Path.Combine(root, "client-pki")).ConfigureAwait(false);
            using ISession session = await clientFixture.ConnectAsync(
                new Uri(server.EndpointUrl),
                SecurityPolicies.None).ConfigureAwait(false);
            ushort ns = (ushort)session.NamespaceUris.GetIndex(
                server.Server.NamespaceUris.GetString(server.Manager.InstanceNamespaceIndex)!);

            CallMethodResult added = await CallAsync(
                session,
                addIn.NodeId,
                addIn.AddLink!.NodeId,
                Variant.From("https://plant.example/wiring"),
                Variant.From(new QualifiedName("Wiring", ns)),
                Variant.From(new LocalizedText("Wiring diagram")),
                Variant.From(new LocalizedText("The wiring of line 2."))).ConfigureAwait(false);
            if (!allowAnonymous)
            {
                Assert.That(added.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                return;
            }

            Assert.That(StatusCode.IsGood(added.StatusCode), Is.True, added.StatusCode.ToString());
            NodeId link = added.OutputArguments[0].TryGetValue(out NodeId id) ? id : NodeId.Null;
            DataValue before = await ReadAsync(session, link).ConfigureAwait(false);
            WriteResponse write = await session.WriteAsync(
                null,
                [
                    new WriteValue
                    {
                        NodeId = link,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From("https://plant.example/wiring/v2"))
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            DataValue after = await ReadAsync(session, link).ConfigureAwait(false);
            CallMethodResult removed = await CallAsync(
                session,
                addIn.NodeId,
                addIn.RemoveLink!.NodeId,
                Variant.From(link)).ConfigureAwait(false);
            DataValue gone = await ReadAsync(session, link).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(before.WrappedValue.TryGetValue(out string first) ? first : null, Is.EqualTo(
                    "https://plant.example/wiring"));
                Assert.That(StatusCode.IsGood(write.Results[0]), Is.True, write.Results[0].ToString());
                Assert.That(after.WrappedValue.TryGetValue(out string second) ? second : null, Is.EqualTo(
                    "https://plant.example/wiring/v2"));
                Assert.That(StatusCode.IsGood(removed.StatusCode), Is.True, removed.StatusCode.ToString());
                Assert.That(gone.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(links.Links.Count, Is.Zero);
            });
        }

        private static async Task<DataValue> ReadAsync(ISession session, NodeId nodeId)
        {
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }

        private static async Task<CallMethodResult> CallAsync(
            ISession session,
            NodeId objectId,
            NodeId methodId,
            params Variant[] arguments)
        {
            CallResponse response = await session.CallAsync(
                null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = arguments.ToArrayOf()
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }
    }
}
