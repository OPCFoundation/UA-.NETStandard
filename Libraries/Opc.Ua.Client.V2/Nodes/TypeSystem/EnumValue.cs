// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    /// <summary>
    /// <para>
    /// This is an object that works around the limits of the encoder
    /// decoder api today which uses the .net Enum type to represent
    /// enumerations.
    /// </para>
    /// <para>
    /// EnumValue is a enum value of type <see cref="EnumValueType"/>
    /// with both symbol and value which can be decoded and encoded
    /// using the enum description.
    /// </para>
    /// <para>
    /// There are only 2 cases where a custom enum can occur:
    /// Inside a custom structure and inside a Variant.
    /// </para>
    /// <para>
    /// The first case is covered by the custom structure encoder/decoder
    /// where we have special casing for this type.
    /// </para>
    /// <para>
    /// The second case is handled by the encoder/decoder in that any
    /// enumeration value in a Variant is encoded as a 32 bit integer
    /// and thus will not even hit us here. This needs to be validated!!
    /// </para>
    /// </summary>
    public sealed record class EnumValue
    {
        /// <summary>
        /// Symbol
        /// </summary>
        public string Symbol { get; }

        /// <summary>
        /// Value
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// Empty enum value
        /// </summary>
        public static EnumValue Null { get; } = new EnumValue("0", 0);

        /// <summary>
        /// Create enum value
        /// </summary>
        /// <param name="displayName"></param>
        /// <param name="value"></param>
        public EnumValue(string displayName, long value)
        {
            Symbol = displayName;
            Value = value;
        }

        /// <summary>
        /// Create enum value from field
        /// </summary>
        /// <param name="field"></param>
        public EnumValue(EnumField field)
        {
            Symbol = field.Name;
            Value = field.Value;
        }
    }
}
