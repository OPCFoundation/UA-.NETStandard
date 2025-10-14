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
        public JsonValidationData()
        {
        }

        public JsonValidationData(BuiltInType builtInType)
        {
            BuiltInType = builtInType;
        }

        public string GetExpected(JsonEncodingType jsonEncodingType)
        {
            return jsonEncodingType switch
            {
                JsonEncodingType.Verbose => ExpectedVerbose ?? ExpectedCompact,
                JsonEncodingType.NonReversible => ExpectedNonReversible ?? ExpectedReversible,
                JsonEncodingType.Reversible => ExpectedReversible,
                JsonEncodingType.Compact => ExpectedCompact,
                _ => throw ServiceResultException.Unexpected($"Unexpected encoding type {jsonEncodingType}")
            };
        }

        public BuiltInType BuiltInType;
        public object Instance;
        public string ExpectedCompact;
        public string ExpectedVerbose;
        public string ExpectedReversible;
        public string ExpectedNonReversible;
        public bool IncludeDefaultValue;

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (BuiltInType == BuiltInType.Variant && Instance is Variant variant)
            {
                BuiltInType? builtInType = variant.TypeInfo?.BuiltInType;
                if (builtInType != null)
                {
                    return $"Variant:{builtInType}:{Instance}" +
                        (IncludeDefaultValue
                            ? ":Default"
                            : string.Empty);
                }
            }
            return $"{BuiltInType}:{Instance}" + (IncludeDefaultValue ? ":Default" : string.Empty);
        }
    }

    public class JsonValidationDataCollection : List<JsonValidationData>
    {
        public JsonValidationDataCollection()
        {
        }

        public JsonValidationDataCollection(IEnumerable<JsonValidationData> collection)
            : base(collection)
        {
        }

        public JsonValidationDataCollection(int capacity)
            : base(capacity)
        {
        }

        public static JsonValidationDataCollection ToJsonValidationDataCollection(
            JsonValidationData[] values)
        {
            return values != null ? [.. values] : [];
        }

        public void Add(
            BuiltInType builtInType,
            object instance,
            string expectedReversible,
            string expectedNonReversible)
        {
            Add(
                new JsonValidationData
                {
                    BuiltInType = builtInType,
                    Instance = instance,
                    ExpectedReversible = expectedReversible,
                    ExpectedNonReversible = expectedNonReversible,
                    ExpectedCompact = expectedReversible,
                    ExpectedVerbose = expectedNonReversible
                });
        }

        public void Add(
            BuiltInType builtInType,
            object instance,
            string expectedReversible,
            string expectedNonReversible,
            string expectedCompact,
            string expectedVerbose)
        {
            Add(
                new JsonValidationData
                {
                    BuiltInType = builtInType,
                    Instance = instance,
                    ExpectedReversible = expectedReversible,
                    ExpectedNonReversible = expectedNonReversible,
                    ExpectedCompact = expectedCompact,
                    ExpectedVerbose = expectedVerbose
                });
        }

        public void Add(
            BuiltInType builtInType,
            object instance,
            string expectedReversible,
            string expectedNonReversible,
            bool includeDefaultValue)
        {
            Add(
                new JsonValidationData
                {
                    BuiltInType = builtInType,
                    Instance = instance,
                    ExpectedReversible = expectedReversible,
                    ExpectedNonReversible = expectedNonReversible,
                    ExpectedCompact = expectedReversible,
                    ExpectedVerbose = expectedNonReversible,
                    IncludeDefaultValue = includeDefaultValue
                });
        }

        public void Add(
            BuiltInType builtInType,
            object instance,
            string expectedReversible,
            string expectedNonReversible,
            string expectedCompact,
            string expectedVerbose,
            bool includeDefaultValue)
        {
            Add(
                new JsonValidationData
                {
                    BuiltInType = builtInType,
                    Instance = instance,
                    ExpectedReversible = expectedReversible,
                    ExpectedNonReversible = expectedNonReversible,
                    ExpectedCompact = expectedCompact,
                    ExpectedVerbose = expectedVerbose,
                    IncludeDefaultValue = includeDefaultValue
                });
        }
    }

    /// <summary>
    /// Helper as value source for tests.
    /// </summary>
    public class JsonEncodingTypeCollection : List<JsonEncodingType>
    {
        public JsonEncodingTypeCollection(JsonEncodingType[] values)
        {
            AddRange(values);
        }
    }
}
