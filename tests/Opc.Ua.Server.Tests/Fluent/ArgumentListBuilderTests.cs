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
