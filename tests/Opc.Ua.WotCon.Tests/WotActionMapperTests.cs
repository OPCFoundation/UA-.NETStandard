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
            Assert.That(arguments[0].DataType, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(arguments[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            // Rec 4: assert the full description text so format / framing mutations
            // (square brackets, separator spaces, "min=, max=" comma) are caught.
            Assert.That(
                arguments[0].Description.Text,
                Is.EqualTo("Target temperature [degree Celsius] (min=10, max=30)"));
            Assert.That(arguments[1].Name, Is.EqualTo("confirm"));
            Assert.That(arguments[1].DataType, Is.EqualTo(Ua.DataTypeIds.Boolean));
            Assert.That(arguments[1].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(arguments[1].Description.IsNull, Is.True,
                "Members without description/unit/bounds should have a null LocalizedText.");
        }

        /// <summary>
        /// G4: member with unit only — no min/max, no description — must produce '[unit]' only.
        /// </summary>
        [Test]
        public void BuildArgumentsForUnitOnlyMemberFormatsBracketsOnly()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["voltage"] = new WotActionMember { Type = "number", Unit = "V" }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments[0].Description.Text, Is.EqualTo("[V]"));
        }

        /// <summary>
        /// G4: minimum-only member must produce '(min=X)' without trailing comma or 'max=' text.
        /// </summary>
        [Test]
        public void BuildArgumentsForMinimumOnlyMemberFormatsMinOnly()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["throttle"] = new WotActionMember { Type = "number", Minimum = 0 }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments[0].Description.Text, Is.EqualTo("(min=0)"));
        }

        /// <summary>
        /// G4: maximum-only member must produce '(max=X)' without leading 'min=' or comma.
        /// </summary>
        [Test]
        public void BuildArgumentsForMaximumOnlyMemberFormatsMaxOnly()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["throttle"] = new WotActionMember { Type = "number", Maximum = 100 }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments[0].Description.Text, Is.EqualTo("(max=100)"));
        }

        /// <summary>
        /// G4: description + unit must include exactly one space separator between them.
        /// </summary>
        [Test]
        public void BuildArgumentsForDescriptionAndUnitInsertsSeparatorSpace()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["voltage"] = new WotActionMember
                    {
                        Type = "number",
                        Description = "Line voltage",
                        Unit = "V"
                    }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments[0].Description.Text, Is.EqualTo("Line voltage [V]"));
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
            Assert.That(arguments[0].DataType, Is.EqualTo(Ua.DataTypeIds.BaseDataType));
            Assert.That(arguments[0].Name, Is.EqualTo("rawString"));
            Assert.That(arguments[0].Description.Text, Is.EqualTo("raw payload"));
        }

        /// <summary>
        /// G5: non-object schema without a Description must produce a null LocalizedText.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void BuildArgumentsForNonObjectSchemaWithoutDescriptionEmitsNullText(string? description)
        {
            var schema = new WotActionSchema
            {
                Type = "string",
                Title = "rawString",
                Description = description
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(1));
            Assert.That(arguments[0].Description.IsNull, Is.True);
        }

        /// <summary>
        /// G3: empty Properties dictionary on a type:object schema must collapse to the
        /// same fallback as a null Properties (single BaseDataType argument).
        /// </summary>
        [Test]
        public void BuildArgumentsForEmptyPropertiesDictionaryCollapsesToBaseDataType()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Title = "empty",
                Properties = []
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(1));
            Assert.That(arguments[0].DataType, Is.EqualTo(Ua.DataTypeIds.BaseDataType));
            Assert.That(arguments[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(arguments[0].Name, Is.EqualTo("empty"));
        }

        /// <summary>
        /// G3: non-object schema without a Title must fall back to the literal "value" as Name.
        /// </summary>
        [Test]
        public void BuildArgumentsForNonObjectSchemaWithoutTitleNamesArgumentValue()
        {
            var schema = new WotActionSchema { Type = "string" };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(1));
            Assert.That(arguments[0].Name, Is.EqualTo("value"));
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
            Assert.That(arguments[0].DataType, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(arguments[0].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        /// <summary>
        /// G6: array member with Items.Type = "object" must keep ValueRank=OneDimension but
        /// fall back to BaseDataType because object has no mapping (per Spec Table 14).
        /// </summary>
        [Test]
        public void BuildArgumentsForArrayOfObjectMemberFallsBackToBaseDataType()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["records"] = new WotActionMember
                    {
                        Type = "array",
                        Items = new WotPropertyItems { Type = "object" }
                    }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(1));
            Assert.That(arguments[0].DataType, Is.EqualTo(Ua.DataTypeIds.BaseDataType));
            Assert.That(arguments[0].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        /// <summary>
        /// G6: array member without Items (Items = null) must also fall back to BaseDataType.
        /// </summary>
        [Test]
        public void BuildArgumentsForArrayMemberWithoutItemsFallsBackToBaseDataType()
        {
            var schema = new WotActionSchema
            {
                Type = "object",
                Properties = new Dictionary<string, WotActionMember>
                {
                    ["records"] = new WotActionMember { Type = "array" }
                }
            };

            IReadOnlyList<Argument> arguments = WotActionMapper.BuildArguments(schema);

            Assert.That(arguments, Has.Count.EqualTo(1));
            Assert.That(arguments[0].DataType, Is.EqualTo(Ua.DataTypeIds.BaseDataType));
            Assert.That(arguments[0].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }
    }
}
