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

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the actual schema document providers under trimming and NativeAOT.
    /// </summary>
    public sealed class RegistryFormatsAotTests
    {
        [Test]
        public async Task SchemaProvidersPreserveValuesAndKnownFingerprints()
        {
            using ServiceProvider services = new ServiceCollection().AddSchemaRegistryFormats().BuildServiceProvider();
            ISchemaFormatProvider[] formats = services.GetServices<ISchemaFormatProvider>().ToArray();
            await Assert.That(formats.Length).IsEqualTo(3);
            var json = new JsonSchemaFormatProvider();
            SchemaContentDataType jsonSchema = json.Parse(
                Encoding.UTF8.GetBytes("{\"type\":\"object\",\"properties\":{\"x\":{\"default\":1.00}}}"));
            await Assert.That(RegistryValues.Identical(
                RegistryValues.Parse(json.Serialize(jsonSchema).Span),
                RegistryValues.Parse(Encoding.UTF8.GetBytes(
                    "{\"type\":\"object\",\"properties\":{\"x\":{\"default\":1.00}}}")))).IsTrue();
            var avro = new AvroSchemaFormatProvider();
            await Assert.That(avro.ComputeSchemaId(Encoding.UTF8.GetBytes("\"string\"")))
                .IsEqualTo(ByteString.FromHexString("C70345637248018F"));
            var arrow = new ArrowSchemaFormatProvider();
            ByteString raw = ByteString.FromHexString(
                "FFFFFFFF800000001000000000000A000C000600050008000A000000000104000C000000080008000000040008000000" +
                "040000000100000014000000100014000800000007000C00000010001000000000000002100000001C00000004000000" +
                "00000000020000006964000008000C00080007000800000000000001400000000000000000000000");
            SchemaContentDataType schema = arrow.Parse(raw.Span);
            await Assert.That(schema.IsEqual(arrow.Parse(arrow.Serialize(schema).Span))).IsTrue();
            await Assert.That(arrow.ComputeSchemaId(raw.Span))
                .IsEqualTo(ByteString.FromHexString("9972E47DBCA6850A"));
            foreach (string fixture in new[] { "arrow-metadata.ipc", "arrow-families.ipc" })
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RegistryFixtures", fixture));
                SchemaContentDataType complete = arrow.Parse(bytes);
                await Assert.That(complete.IsEqual(arrow.Parse(arrow.Serialize(complete).Span))).IsTrue();
            }
        }
    }
}
