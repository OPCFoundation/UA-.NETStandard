/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for the schema generation dependency injection registration.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class SchemaServiceCollectionExtensionsTests
    {
        [Test]
        public void AddSchemaGenerationResolvesProviderAndResolvesRegisteredType()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            var registry = serviceProvider.GetRequiredService<DataTypeDefinitionRegistry>();
            UaTypeDescription type = SchemaTestData.Structure(
                3001,
                "SampleType",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            registry.Add(type);

            var provider = serviceProvider.GetRequiredService<ISchemaProvider>();
            bool resolved = provider.TryGetSchema(
                type.TypeId,
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(schema, Is.Not.Null);
                Assert.That(schema!.Format, Is.EqualTo(UaSchemaFormat.JsonCompact));
            });
        }

        [Test]
        public void AddSchemaGenerationThrowsForNullBuilder()
        {
            Assert.That(
                () => SchemaServiceCollectionExtensions.AddSchemaGeneration(null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("builder"));
        }

        [Test]
        public void TryGetSchemaReturnsFalseForUnknownType()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            var provider = serviceProvider.GetRequiredService<ISchemaProvider>();
            bool resolved = provider.TryGetSchema(
                new ExpandedNodeId(new NodeId(9999, 1)),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.False);
                Assert.That(schema, Is.Null);
            });
        }
    }
}
