/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Helper class to create list of encoding validation items.
    /// </summary>
    public class JsonValidationData : IFormattable
    {
        public JsonValidationData() { }

        public JsonValidationData(BuiltInType builtInType)
        {
            BuiltInType = builtInType;
        }

        public BuiltInType BuiltInType;
        public object Instance;
        public string ExpectedReversible;
        public string ExpectedNonReversible;
        public bool IncludeDefaultValue;

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"{BuiltInType}:{Instance}" + (IncludeDefaultValue ? ":Default" : "");
        }
    };

    public class JsonValidationDataCollection : List<JsonValidationData>
    {
        public JsonValidationDataCollection() { }
        public JsonValidationDataCollection(IEnumerable<JsonValidationData> collection) : base(collection) { }
        public JsonValidationDataCollection(int capacity) : base(capacity) { }
        public static JsonValidationDataCollection ToJsonValidationDataCollection(JsonValidationData[] values)
        {
            return values != null ? new JsonValidationDataCollection(values) : new JsonValidationDataCollection();
        }

        public void Add(
            BuiltInType builtInType,
            object instance,
            string expectedReversible,
            string expectedNonReversible)
        {
            Add(new JsonValidationData() {
                BuiltInType = builtInType,
                Instance = instance,
                ExpectedReversible = expectedReversible,
                ExpectedNonReversible = expectedNonReversible
            });
        }

        public void Add(
            BuiltInType builtInType,
            object instance,
            string expectedReversible,
            string expectedNonReversible,
            bool includeDefaultValue)
        {
            Add(new JsonValidationData() {
                BuiltInType = builtInType,
                Instance = instance,
                ExpectedReversible = expectedReversible,
                ExpectedNonReversible = expectedNonReversible,
                IncludeDefaultValue = includeDefaultValue
            });
        }
    }
}
