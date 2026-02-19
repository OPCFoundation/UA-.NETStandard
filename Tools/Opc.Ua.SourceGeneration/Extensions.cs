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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    internal static class Extensions
    {
        /// <summary>
        /// Get options from file options
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        public static NodesetFileOptions ToNodeSetOptions(this AnalyzerConfigOptions options)
        {
            if (options == null)
            {
                return new NodesetFileOptions();
            }
            return new NodesetFileOptions
            {
                Ignore = options.GetBool(nameof(NodesetFileOptions.Ignore), false),
                Prefix = options.GetString(nameof(NodesetFileOptions.Prefix), false),
                Version = options.GetString(nameof(NodesetFileOptions.Version), false),
                Name = options.GetString(nameof(NodesetFileOptions.Name), false),
                ModelUri = options.GetString(nameof(NodesetFileOptions.ModelUri), false)
            };
        }

        /// <summary>
        /// Create collection
        /// </summary>
        public static NodesetFileCollection ToNodeSetFileCollection(
            this ImmutableArray<(AdditionalText, NodesetFileOptions)> nodeset2Files,
            IFileSystem fileSystem,
            ITelemetryContext telemetry)
        {
            return new NodesetFileCollection(
                [.. nodeset2Files.Select(f => (f.Item1.Path, f.Item2))],
                fileSystem,
                telemetry);
        }

        /// <summary>
        /// Design files end in xml but are not nodeset2.xml files.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static bool IsDesignOrNodeset2File(this AdditionalText text)
        {
            return text.HasFileExtension("xml");
        }

        /// <summary>
        /// Identifer files are csv files
        /// </summary>
        public static bool IsIdentifierFile(this AdditionalText text)
        {
            return text.HasFileExtension("csv");
        }

        /// <summary>
        /// Has file extension check
        /// </summary>
        public static bool HasFileExtension(this AdditionalText text, string extension)
        {
            return text.Path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static T GetValue<T>(
            this AnalyzerConfigOptions options,
            string propertyName,
            Func<string, T> converter,
            bool buildProperty = true)
        {
            string prefix = buildProperty ? "build_property" : "build_metadata.AdditionalFiles";
            options.TryGetValue(
                $"{prefix}.{SourceGenerator.Name}{propertyName}".ToLowerInvariant(),
                out string value);
            return converter(value);
        }

        /// <summary>
        /// Get integer value from build properties
        /// </summary>
        public static int GetInteger(
            this AnalyzerConfigOptions config,
            string propertyName,
            bool buildProperty = true)
        {
            return config.GetValue(propertyName,
                s => s != null && int.TryParse(s, out int integer) ?
                    integer :
                    0,
                buildProperty);
        }

        /// <summary>
        /// Get boolean option from options
        /// </summary>
        public static bool GetBool(
            this AnalyzerConfigOptions config,
            string propertyName,
            bool buildProperty = true)
        {
            return config.GetValue(propertyName,
                s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase),
                buildProperty);
        }

        /// <summary>
        /// Get string option from options
        /// </summary>
        public static string GetString(
            this AnalyzerConfigOptions config,
            string propertyName,
            bool buildProperty = true)
        {
            return config.GetValue(propertyName, s => s, buildProperty)
                ?? string.Empty;
        }

        /// <summary>
        /// Get string option from options
        /// </summary>
        public static List<string> GetStrings(
            this AnalyzerConfigOptions config,
            string propertyName,
            bool buildProperty = true)
        {
            return config.GetValue(propertyName, Split, buildProperty);

            static List<string> Split(string s)
            {
                return s == null ? [] : [.. s
                    .Split(';', ',', '+')
                    .Select(e => e.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))];
            }
        }
    }
}
