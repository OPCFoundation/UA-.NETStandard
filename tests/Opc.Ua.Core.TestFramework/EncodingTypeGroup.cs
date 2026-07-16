/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

namespace Opc.Ua.Core.TestFramework
{
    /// <summary>
    /// A group of encoder types.
    /// </summary>
    public class EncodingTypeGroup : IFormattable
    {
        public EncodingTypeGroup(
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType = JsonEncodingType.Verbose,
            bool useXmlParser = false)
        {
            EncoderType = encoderType;
            JsonEncodingType = jsonEncodingType;
            UseXmlParser = useXmlParser;
        }

        public EncodingType EncoderType { get; }

        public JsonEncodingType JsonEncodingType { get; }

        public bool UseXmlParser { get; }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (EncoderType == EncodingType.Json)
            {
                return Utils.Format("{0}:{1}", EncoderType, JsonEncodingType);
            }
            if (EncoderType == EncodingType.Xml)
            {
                return Utils.Format("{0}:{1}", EncoderType, UseXmlParser ? "Parser" : "Reader");
            }
            return Utils.Format("{0}", EncoderType);
        }
    }
}
