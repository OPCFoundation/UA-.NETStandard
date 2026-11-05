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
 *
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

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Detaches provider-supplied measurement values and metadata from caller-owned mutable state.
    /// </summary>
    /// <remarks>
    /// Localized text uses <see cref="LocalizedText.Copy"/>, which rejects unsupported formatting arguments and
    /// translation dictionaries before any formatting or publication. Structures are copied field by field;
    /// no arbitrary <c>Clone</c> is invoked and custom <see cref="IEncodeable"/> values are rejected.
    /// Statistic values are detached by the shared IA statistics runtime when it publishes them.
    /// </remarks>
    internal static class EcmOwnership
    {
        /// <summary>
        /// Detaches localized text, rejecting state that cannot be copied safely.
        /// </summary>
        /// <param name="text">The text to detach.</param>
        /// <param name="parameterName">The name reported when the text is unsupported.</param>
        /// <exception cref="ArgumentException">The text holds unsupported mutable state.</exception>
        internal static LocalizedText Own(LocalizedText text, string parameterName)
        {
            try
            {
                return text.Copy();
            }
            catch (ServiceResultException exception)
            {
                throw new ArgumentException(
                    "Localized text contains formatting arguments or translations that cannot be copied safely.",
                    parameterName,
                    exception);
            }
        }

        /// <summary>
        /// Detaches an opaque NodeId from caller-owned byte storage.
        /// </summary>
        /// <param name="nodeId">The NodeId to detach.</param>
        internal static NodeId Own(NodeId nodeId)
        {
            return nodeId.TryGetValue(out ByteString identifier)
                ? new NodeId(ByteString.From(identifier.Span), nodeId.NamespaceIndex)
                : nodeId;
        }

        /// <summary>
        /// Creates a detached field-wise copy of engineering-unit metadata.
        /// </summary>
        /// <param name="units">The units to detach, or null.</param>
        /// <param name="parameterName">The name reported when the units are unsupported.</param>
        /// <exception cref="ArgumentException">The units hold unsupported mutable text.</exception>
        internal static EUInformation? Own(EUInformation? units, string parameterName)
        {
            if (units is null)
            {
                return null;
            }
            LocalizedText displayName = Own(units.DisplayName, parameterName);
            LocalizedText description = Own(units.Description, parameterName);
            return new EUInformation
            {
                NamespaceUri = units.NamespaceUri,
                UnitId = units.UnitId,
                DisplayName = displayName,
                Description = description
            };
        }

        /// <summary>
        /// Creates a detached field-wise copy of one accuracy-class value.
        /// </summary>
        /// <param name="value">The class to detach.</param>
        /// <param name="parameterName">The name reported when the class is unsupported.</param>
        /// <exception cref="ArgumentException">The class holds unsupported mutable text.</exception>
        internal static EnumValueType Own(EnumValueType value, string parameterName)
        {
            LocalizedText displayName = Own(value.DisplayName, parameterName);
            LocalizedText description = Own(value.Description, parameterName);
            return new EnumValueType
            {
                Value = value.Value,
                DisplayName = displayName,
                Description = description
            };
        }

        /// <summary>
        /// Detaches a scalar measurement value.
        /// </summary>
        /// <param name="value">The value to detach.</param>
        /// <exception cref="ArgumentException">
        /// The value is not a supported immutable scalar or an exact AcPe or AcPp structure.
        /// </exception>
        internal static Variant Own(Variant value)
        {
            if (value.IsNull)
            {
                return value;
            }
            if (value.TryGetStructure(out AcPeDataType? phases))
            {
                if (phases is null || phases.GetType() != typeof(AcPeDataType))
                {
                    throw Unsupported();
                }
                return Variant.FromStructure(new AcPeDataType { L1 = phases.L1, L2 = phases.L2, L3 = phases.L3 });
            }
            if (value.TryGetStructure(out AcPpDataType? lines))
            {
                if (lines is null || lines.GetType() != typeof(AcPpDataType))
                {
                    throw Unsupported();
                }
                return Variant.FromStructure(
                    new AcPpDataType { L1L2 = lines.L1L2, L2L3 = lines.L2L3, L3L1 = lines.L3L1 });
            }
            if (value.TypeInfo.IsScalar && value.TypeInfo.BuiltInType is
                BuiltInType.Boolean or BuiltInType.SByte or BuiltInType.Byte or BuiltInType.Int16 or
                BuiltInType.UInt16 or BuiltInType.Int32 or BuiltInType.UInt32 or BuiltInType.Int64 or
                BuiltInType.UInt64 or BuiltInType.Float or BuiltInType.Double or BuiltInType.String or
                BuiltInType.DateTime or BuiltInType.Guid or BuiltInType.StatusCode)
            {
                return value;
            }
            throw Unsupported();
        }

        /// <summary>
        /// Creates the rejection for values that cannot be detached field by field.
        /// </summary>
        private static ArgumentException Unsupported()
        {
            return new ArgumentException(
                "Measurement values must be immutable scalars or the exact AcPe or AcPp structure.", "value");
        }
    }
}
