// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="ArgumentBuilder"/>.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class ArgumentBuilderTests
    {
        [Test]
        public void ConstructorCreatesNewArgument()
        {
            ArgumentBuilder builder = new();

            Assert.That(builder.Item, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithArgumentUsesProvidedInstance()
        {
            var argument = new Argument("A", DataTypeIds.Int32, ValueRanks.Scalar, "desc");

            ArgumentBuilder builder = new(argument);

            Assert.That(builder.Item, Is.SameAs(argument));
        }

        [Test]
        public void ConstructorWithArgumentThrowsWhenNull()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => _ = new ArgumentBuilder(arg: null!))!;

            Assert.That(ex.ParamName, Is.EqualTo("arg"));
        }

        [Test]
        public void ConstructorWithValuesAssignsProperties()
        {
            ArgumentBuilder builder = new("Input", DataTypeIds.String, ValueRanks.OneDimension, "text");

            Assert.That(builder.Item.Name, Is.EqualTo("Input"));
            Assert.That(builder.Item.DataType, Is.EqualTo(DataTypeIds.String));
            Assert.That(builder.Item.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(builder.Item.Description.Text, Is.EqualTo("text"));
        }

        [Test]
        public void WithMethodsMutateItemAndReturnBuilder()
        {
            ArgumentBuilder builder = new();
            LocalizedText description = new("localized");

            IArgumentBuilder chain = builder
                .WithName("B")
                .WithDataType(DataTypeIds.Int64)
                .WithValueRank(ValueRanks.Scalar)
                .WithDescription("desc")
                .WithDescription(description);

            Assert.That(chain, Is.SameAs(builder));
            Assert.That(builder.Item.Name, Is.EqualTo("B"));
            Assert.That(builder.Item.DataType, Is.EqualTo(DataTypeIds.Int64));
            Assert.That(builder.Item.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(builder.Item.Description, Is.EqualTo(description));
        }

        [Test]
        public void WithDataTypeGenericAssignsMappedDataType()
        {
            NamespaceTable namespaceUris = new();
            namespaceUris.Append(Namespaces.Roles);
            var context = new SystemContext(telemetry: null!)
            {
                NamespaceUris = namespaceUris
            };

            ArgumentBuilder builder = new();

            builder.WithDataType<int>(context);

            Assert.That(builder.Item.DataType, Is.EqualTo(DataTypeIds.Int32));
        }

        [Test]
        public void StaticCreateOverloadsReturnConfiguredBuilders()
        {
            var source = new Argument("S", DataTypeIds.Double, ValueRanks.Scalar, "d");

            IArgumentBuilder defaultBuilder = ArgumentBuilder.Create();
            IArgumentBuilder fromArgument = ArgumentBuilder.Create(source);
            IArgumentBuilder fromValues = ArgumentBuilder.Create("N", DataTypeIds.Boolean);

            Assert.That(defaultBuilder.Item, Is.Not.Null);
            Assert.That(fromArgument.Item, Is.SameAs(source));
            Assert.That(fromValues.Item.Name, Is.EqualTo("N"));
            Assert.That(fromValues.Item.DataType, Is.EqualTo(DataTypeIds.Boolean));
        }
    }
}
