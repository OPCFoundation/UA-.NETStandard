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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public class WotActionMapperTests
    {
        [Test]
        public void BuildArgumentsForFlatObjectMapsEachPropertyToOneArgument()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["target"] = new WotActionMember
                    {
                        Type = "number",
                        Unit = "degree Celsius",
                        Minimum = 10,
                        Maximum = 30,
                        Description = "Target temperature"
                    },
                    ["confirm"] = new WotActionMember { Type = "boolean" }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(2));
            Assert.That(arguments[0].Name, Is.EqualTo("target"));
            Assert.That(arguments[0].DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(arguments[0].Description.Text, Does.Contain("Target temperature"));
            Assert.That(arguments[0].Description.Text, Does.Contain("degree Celsius"));
            Assert.That(arguments[0].Description.Text, Does.Contain("min=10"));
            Assert.That(arguments[0].Description.Text, Does.Contain("max=30"));
            Assert.That(arguments[1].Name, Is.EqualTo("confirm"));
            Assert.That(arguments[1].DataType, Is.EqualTo(DataTypeIds.Boolean));
        }

        [Test]
        public void BuildArgumentsForNullSchemaReturnsEmpty()
        {
            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(null);

            Assert.That(arguments, Is.Empty);
        }

        [Test]
        public void BuildArgumentsForNonObjectSchemaCollapsesToBaseDataType()
        {
            var schema = new WotActionSchema
            {
                Type = "string",
                Title = "rawString",
                Description = "raw payload"
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(1));
            Assert.That(arguments[0].DataType, Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(arguments[0].Name, Is.EqualTo("rawString"));
            Assert.That(arguments[0].Description.Text, Is.EqualTo("raw payload"));
        }

        [Test]
        public void BuildArgumentsForArrayMemberMarksOneDimensional()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["samples"] = new WotActionMember
                    {
                        Type = "array",
                        Items = new WotPropertyItems { Type = "number" }
                    }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(1));
            Assert.That(arguments[0].DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(arguments[0].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }
    }
}
