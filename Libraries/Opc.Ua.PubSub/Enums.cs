/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// The possible values for the FieldType encoding byte.
    /// </summary>
    [Flags]
    internal enum FieldTypeEncodingMask : byte
    {
        Variant = 0,
        RawData = 1,
        DataValue = 2,
        Reserved = 3
    }

    /// <summary>
    /// The possible values for the NetworkMessage DataSetFlags1 encoding byte.
    /// </summary>
    [Flags]
    public enum DataSetFlags1EncodingMask : byte
    {
        /// <summary>
        /// No dataset flags usage.
        /// </summary>
        None = 0,
        /// <summary>
        /// Dataset flag set as message is valid.
        /// </summary>
        MessageIsValid = 1,
        // Field type options (FieldTypeEncodingMask)
        /// <summary>
        /// Dataset flag SequenceNumber is set.
        /// </summary>
        SequenceNumber = 8,
        /// <summary>
        /// Dataset flag Status is set.
        /// </summary>
        Status = 16,
        /// <summary>
        /// Dataset flag ConfigurationVersionMajorVersion is set.
        /// </summary>
        ConfigurationVersionMajorVersion = 32,
        /// <summary>
        /// Dataset flags ConfigurationVersionMinorVersion is set.
        /// </summary>
        ConfigurationVersionMinorVersion = 64,
        /// <summary>
        /// DataSetFlags2 option is set.
        /// </summary>
        DataSetFlags2 = 128
    }

    /// <summary>
    /// The possible values for the NetworkMessage DataSetFlags2 encoding byte.
    /// </summary>
    [Flags]
    public enum DataSetFlags2EncodingMask : byte
    {
        /// <summary>
        /// No dataset flag usage.
        /// </summary>
        None = 0,
        /// <summary>
        /// Dataset flag Timestamp is set.
        /// </summary>
        Timestamp = 16,
        /// <summary>
        /// Dataset flag PicoSeconds is set.
        /// </summary>
        PicoSeconds = 32,
        /// <summary>
        /// Dataset flag is reserved.
        /// </summary>
        Reserved = 64,
        /// <summary>
        /// Dataset flag is reserved for extended flags.
        /// </summary>
        ReservedForExtendedFlags = 128
    }

    /// <summary>
    /// The possible values for the NetworkMessage UADPFlags encoding byte.
    /// </summary>
    [Flags]
    public enum UADPFlagsEncodingMask : byte
    {
        /// <summary>
        /// No UADP flag usage.
        /// </summary>
        None = 0,
        /// <summary>
        /// UADP PublisherId option is used.
        /// </summary>
        PublisherId = 16,
        /// <summary>
        /// UADP GroupHeader option is used.
        /// </summary>
        GroupHeader = 32,
        /// <summary>
        /// UADP PayloadHeader option is used.
        /// </summary>
        PayloadHeader = 64,
        /// <summary>
        /// UADP ExtendedFlags1 option is used.
        /// </summary>
        ExtendedFlags1 = 128
    }

    /// <summary>
    /// The possible types of UADP network messages
    /// </summary>
    [Flags]
    public enum UADPNetworkMessageType
    {
        /// <summary>
        /// DataSet message
        /// </summary>
        DataSetMessage = 0, 
        /// <summary>
        /// Discovery Request message
        /// </summary>
        DiscoveryRequest = 4, 
        /// <summary>
        /// Discovery Response message
        /// </summary>
        DiscoveryResponse = 8
    }

    /// <summary>
    /// The possible values for the NetworkMessage ExtendedFlags1 encoding byte.
    /// </summary>
    [Flags]
    public enum ExtendedFlags1EncodingMask : byte
    {
        /// <summary>
        /// No ExtendedFlags1 usage.
        /// </summary>
        None = 0,
        // PublishedId type merge
        /// <summary>
        /// UADP DataSetClassId option is used.
        /// </summary>
        DataSetClassId = 8,
        /// <summary>
        /// UADP Security option is used.
        /// </summary>
        Security = 16,
        /// <summary>
        /// UADP Timestamp option is used.
        /// </summary>
        Timestamp = 32,
        /// <summary>
        /// UADP PicoSeconds option is used.
        /// </summary>
        PicoSeconds = 64,
        /// <summary>
        /// UADP ExtendedFlags2 options are used.
        /// </summary>
        ExtendedFlags2 = 128
    }

    /// <summary>
    /// The possible values for the NetworkMessage ExtendedFlags2 encoding byte.
    /// </summary>
    [Flags]
    public enum ExtendedFlags2EncodingMask : byte
    {
        /// <summary>
        /// No ExtendedFlags2 usage.
        /// </summary>
        None = 0,
        /// <summary>
        /// UADP ChunkMessage type is used.
        /// </summary>
        ChunkMessage = 1,
        /// <summary>
        /// UADP PromotedFields type are used.
        /// </summary>
        PromotedFields = 2,
        /// <summary>
        /// UADP NetworkMessageWithDiscoveryRequest type is used.
        /// </summary>
        NetworkMessageWithDiscoveryRequest = 4,
        /// <summary>
        /// UADP NetworkMessageWithDiscoveryResponse type is used.
        /// </summary>
        NetworkMessageWithDiscoveryResponse = 8,
        /// <summary>
        /// UADP ExtendedFlags2 type is reserved.
        /// </summary>
        Reserved = 16
    }

    /// <summary>
    /// The possible values for the NetworkMessage PublisherIdType encoding byte.
    /// </summary>
    [Flags]
    internal enum PublisherIdTypeEncodingMask : byte
    {
        Byte = 0,
        UInt16 = 1,
        UInt32 = 2,
        UInt64 = 3,
        String = 4,
        Reserved = 5
    }

    /// <summary>
    /// The possible values for the NetworkMessage GroupFlags encoding byte.
    /// </summary>
    [Flags]
    public enum GroupFlagsEncodingMask : byte
    {
        /// <summary>
        /// No ExtendedFlags2 usage.
        /// </summary>
        None = 0,
        /// <summary>
        /// UADP GroupFlags WriterGroupId is used.
        /// </summary>
        WriterGroupId = 1,
        /// <summary>
        /// UADP GroupFlags GroupVersion is used.
        /// </summary>
        GroupVersion = 2,
        /// <summary>
        /// UADP GroupFlags NetworkMessageNumber is used.
        /// </summary>
        NetworkMessageNumber = 4,
        /// <summary>
        /// UADP GroupFlags SequenceNumber is used.
        /// </summary>
        SequenceNumber = 8
    }

    /// <summary>
    /// The possible values for the NetworkMessage SecurityFlags encoding byte.
    /// </summary>
    [Flags]
    public enum SecurityFlagsEncodingMask : byte
    {
        /// <summary>
        /// No SecurityFlags usage.
        /// </summary>
        None = 0,
        /// <summary>
        /// UADP SecurityFlags NetworkMessageSigned is used.
        /// </summary>
        NetworkMessageSigned = 1,
        /// <summary>
        /// UADP SecurityFlags NetworkMessageEncrypted is used.
        /// </summary>
        NetworkMessageEncrypted = 2,
        /// <summary>
        /// UADP SecurityFlags SecurityFooter is used.
        /// </summary>
        SecurityFooter = 4,
        /// <summary>
        /// UADP SecurityFlags ForceKeyReset is used.
        /// </summary>
        ForceKeyReset = 8,
        /// <summary>
        /// UADP SecurityFlags is reserved.
        /// </summary>
        Reserved = 16
    }

    /// <summary>
    /// Enumeration for possible transport protocols used with PubSub
    /// </summary>
    public enum TransportProtocol
    {
        /// <summary>
        /// Not available.
        /// </summary>
        NotAvailable,
        /// <summary>
        /// UADP protocol.
        /// </summary>
        UADP,
        /// <summary>
        /// MQTT protocol.
        /// </summary>
        MQTT,
        /// <summary>
        /// AMQP protocol.
        /// </summary>
        AMQP
    }
}
