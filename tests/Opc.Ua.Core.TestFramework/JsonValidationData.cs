/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Core.TestFramework
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
                JsonEncodingType.Compact => ExpectedCompact,
                _ => throw ServiceResultException.Unexpected($"Unexpected encoding type {jsonEncodingType}")
            };
        }

        public BuiltInType BuiltInType { get; set; }
        public Variant Instance { get; set; }
        public string ExpectedCompact { get; set; } = null!;
        public string ExpectedVerbose { get; set; } = null!;

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (BuiltInType == BuiltInType.Variant)
            {
                return $"Variant:{Instance.TypeInfo.BuiltInType}:{Instance}";
            }
            return $"{BuiltInType}:{Instance}";
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
            Variant instance,
            string expectedCompact,
            string expectedVerbose)
        {
            Add(new JsonValidationData
            {
                BuiltInType = builtInType,
                Instance = instance,
                ExpectedCompact = expectedCompact,
                ExpectedVerbose = expectedVerbose
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
