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
 *
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
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Guards the live materialization path: what the registry converter hands
    /// back has to survive being written, read and imported.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotMaterializationImportTests
    {
        [Test]
        public async Task ConvertedRegistryDocumentSurvivesSerializeReadImportAsync()
        {
            // IWotDocumentConverter produces a UANodeSet, the coordinator
            // writes it to XML, and the runtime NodeSet node manager reads and
            // imports that XML. A reference written as a bare name the document
            // never declares fails the last step with BadNodeIdInvalid, and
            // nothing before it notices.
            var converter = new WotNodeSetDocumentConverter();
            byte[] content = TestMaterialization.Tm("urn:test:materialization");

            var version = new WotResourceVersion(
                versionId: "v1",
                digest: WotContentDigest.Compute(content),
                contentLength: content.Length,
                contentType: "application/tm+json",
                format: "WoT-TM/1.0",
                createdAt: default,
                modifiedAt: default);
            var resource = new WotResource(
                groupId: WotRegistryGroups.ThingModels,
                resourceId: "materialization-tm",
                kind: WoTDocumentKindEnum.ThingModel,
                versions: ImmutableArray.Create(version),
                defaultVersionId: "v1");

            using var service = new WotRegistryService();

            WotConversionOutput output = await converter
                .ConvertAsync(
                    resource,
                    ByteString.From(content),
                    service.Current,
                    new Dictionary<string, ByteString>
                    {
                        [version.DigestHex] = ByteString.From(content)
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(
                output.Succeeded,
                Is.True,
                string.Join("; ", output.Errors));

            byte[] xml;
            using (var buffer = new MemoryStream())
            {
                output.NodeSet!.Write(buffer);
                xml = buffer.ToArray();
            }

            UANodeSet reread;
            using (var buffer = new MemoryStream(xml, writable: false))
            {
                reread = UANodeSet.Read(buffer)!;
            }
            Assert.That(reread, Is.Not.Null);

            var namespaces = new NamespaceTable();
            foreach (string namespaceUri in reread.NamespaceUris ?? [])
            {
                namespaces.GetIndexOrAppend(namespaceUri);
            }
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var nodes = new NodeStateCollection();

            Assert.DoesNotThrow(
                () => reread.Import(context, nodes),
                "A converted registry document has to be importable:" +
                Environment.NewLine + Encoding.UTF8.GetString(xml));
            Assert.That(nodes, Is.Not.Empty);
        }
    }
}
