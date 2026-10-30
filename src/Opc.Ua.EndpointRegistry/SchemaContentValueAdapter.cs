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
using System.Collections.Generic;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry
{
    /// <summary>
    /// Maps inline Message schema content (<c>dataschema</c>) between its generic source value and the
    /// typed schema content selected by <c>dataschemaformat</c>.
    /// </summary>
    /// <remarks>
    /// JSON Schema and Avro content use the supplied <see cref="ISchemaFormatProvider"/> with an exact
    /// generic JSON bridge on the server side; clients receive the typed content directly. An undeclared
    /// format keeps the exact source value in <see cref="ExtensionSchemaContentDataType"/>. An Apache Arrow
    /// document is a binary IPC Schema message without an inline JSON form, so inline Arrow content is
    /// rejected as an invalid argument in either direction; a declared format without a provider is not
    /// supported. No schema is fetched and no Schema Registry is required.
    /// </remarks>
    public sealed class SchemaContentValueAdapter : IRegistryNativeValueAdapter
    {
        /// <summary>
        /// Creates the adapter with the schema format providers that may map inline content.
        /// </summary>
        /// <exception cref="ArgumentException">Two providers declare the same format.</exception>
        public SchemaContentValueAdapter(ArrayOf<ISchemaFormatProvider> providers)
        {
            foreach (ISchemaFormatProvider provider in providers)
            {
                if (provider is null)
                {
                    throw new ArgumentException("A schema format provider is null.", nameof(providers));
                }
                string? contentType = ContentTypeOf(provider.Format);
                if (contentType is not null && !m_providers.TryAdd(contentType, provider))
                {
                    throw new ArgumentException(
                        "Two schema format providers declare the format " + provider.Format,
                        nameof(providers));
                }
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> DataTypes =>
        [
            nameof(JsonSchemaContentDataType),
            nameof(AvroSchemaContentDataType),
            nameof(ArrowSchemaContentDataType),
            nameof(ExtensionSchemaContentDataType),
            nameof(ArrowIpcSchemaContentDataType)
        ];

        /// <inheritdoc/>
        public IEncodeable Project(RegistryValueDataType value, string dataType, RegistryRecordMapper mapper)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            switch (dataType)
            {
                case nameof(ExtensionSchemaContentDataType):
                    RegistryValues.Validate(value);
                    return new ExtensionSchemaContentDataType
                    {
                        Format = string.Empty,
                        Root = (RegistryValueDataType)value.Clone()
                    };
                case nameof(ArrowSchemaContentDataType):
                case nameof(ArrowIpcSchemaContentDataType):
                    throw InlineArrow();
                default:
                    ISchemaFormatProvider provider = Provider(dataType);
                    return provider.Parse(RegistryValues.ToJson(value).Span) ??
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "The schema format provider returned no content for " + dataType);
            }
        }

        /// <inheritdoc/>
        public RegistryValueDataType Restore(IEncodeable value, string dataType, RegistryRecordMapper mapper)
        {
            switch (value)
            {
                case ExtensionSchemaContentDataType extension
                    when dataType == nameof(ExtensionSchemaContentDataType):
                    if (extension.Format is not { Length: 0 } || extension.Root is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidArgument,
                            "Content of an undeclared schema format has an empty Format and a generic Root.");
                    }
                    RegistryValues.Validate(extension.Root);
                    return (RegistryValueDataType)extension.Root.Clone();
                case ArrowSchemaContentDataType:
                case ArrowIpcSchemaContentDataType:
                    throw InlineArrow();
                case JsonSchemaContentDataType json when dataType == nameof(JsonSchemaContentDataType):
                    return RegistryValues.Parse(Provider(dataType).Serialize(json).Span);
                case AvroSchemaContentDataType avro when dataType == nameof(AvroSchemaContentDataType):
                    return RegistryValues.Parse(Provider(dataType).Serialize(avro).Span);
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "Typed schema content of DataType " + dataType + " is required.");
            }
        }

        private ISchemaFormatProvider Provider(string dataType)
        {
            return m_providers.TryGetValue(dataType, out ISchemaFormatProvider? provider)
                ? provider
                : throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "No schema format provider is configured for inline " + dataType);
        }

        private static string? ContentTypeOf(string? format)
        {
            if (format is null)
            {
                return null;
            }
            string selected = EndpointRegistryNativeCatalog.Catalog.SelectType(SchemaContentFamily, format);
            return selected is nameof(JsonSchemaContentDataType) or nameof(AvroSchemaContentDataType)
                ? selected
                : null;
        }

        private static ServiceResultException InlineArrow()
        {
            return new ServiceResultException(
                StatusCodes.BadInvalidArgument,
                "An Apache Arrow schema document is binary and has no inline form; " +
                "reference it through dataschemauri or dataschemaxid.");
        }

        private const string SchemaContentFamily = nameof(SchemaContentDataType);
        private readonly Dictionary<string, ISchemaFormatProvider> m_providers = new(StringComparer.Ordinal);
    }
}
