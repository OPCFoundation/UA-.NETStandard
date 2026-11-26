// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="ArgumentListBuilder"/>.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class ArgumentListBuilderTests
    {
        [Test]
        public void AddBuilderLambdaCreatesArgument()
        {
            ArgumentListBuilder builder = new();

            builder.Add(argument => argument
                .WithName("Input")
                .WithDataType(DataTypeIds.Int32)
                .WithValueRank(ValueRanks.Scalar)
                .WithDescription("Input value"));

            Assert.That(builder.Items, Has.Length.EqualTo(1));
            Assert.That(builder.Items[0].Name, Is.EqualTo("Input"));
            Assert.That(builder.Items[0].DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(builder.Items[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(builder.Items[0].Description.Text, Is.EqualTo("Input value"));
        }

        [Test]
        public void AddBuilderLambdasAddsEachArgument()
        {
            ArgumentListBuilder builder = new();

            builder.Add(
                argument => argument.WithName("A").WithDataType(DataTypeIds.Int32),
                argument => argument.WithName("B").WithDataType(DataTypeIds.String));

            Assert.That(builder.Items, Has.Length.EqualTo(2));
            Assert.That(builder.Items[0].Name, Is.EqualTo("A"));
            Assert.That(builder.Items[1].Name, Is.EqualTo("B"));
        }

        [Test]
        public void AddBuilderLambdaThrowsWhenNull()
        {
            ArgumentListBuilder builder = new();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => builder.Add(configureArgument: null!))!;

            Assert.That(ex.ParamName, Is.EqualTo("configureArgument"));
        }

        [Test]
        public void AddBuilderLambdasThrowsWhenArrayContainsNull()
        {
            ArgumentListBuilder builder = new();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => builder.Add(
                    argument => argument.WithName("A"),
                    null!))!;

            Assert.That(ex.ParamName, Is.EqualTo("configureArgument"));
        }
    }
}
