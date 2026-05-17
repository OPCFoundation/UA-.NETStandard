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
using System.IO;
using System.Text;
using System.Xml;
using Opc.Ua;

namespace UaLens.Connection;

/// <summary>
/// On-wire encoding of an OPC UA value file, supported by
/// <see cref="DataValueCodec"/>.
/// </summary>
internal enum EncodingFormat
{
    Binary,
    Xml,
    Json
}

/// <summary>
/// Round-trips a <see cref="DataValue"/> or <see cref="Variant"/> through
/// one of the SDK encoders (binary / xml / json) so the value can be
/// persisted to disk and reloaded.  Used by:
/// <list type="bullet">
///   <item>The address-space <c>Export value to file…</c> context-menu
///         entry — encodes the freshly-read DataValue.</item>
///   <item>The <c>Write Value</c> dialog <c>Import…</c> button — decodes
///         a DataValue and formats its Variant back into the textbox.</item>
///   <item>The <c>Method Call</c> dialog per-argument <c>Import…</c>
///         button — decodes a single Variant.</item>
/// </list>
/// </summary>
internal static class DataValueCodec
{
    private const string FieldName = "Value";

    public static byte[] EncodeDataValue(DataValue dv, EncodingFormat fmt, IServiceMessageContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        switch (fmt)
        {
            case EncodingFormat.Binary:
            {
                using var enc = new BinaryEncoder(ctx);
                enc.WriteDataValue(FieldName, dv);
                return enc.CloseAndReturnBuffer();
            }
            case EncodingFormat.Xml:
            {
                using var enc = new XmlEncoder(ctx);
                enc.WriteDataValue(FieldName, dv);
                return Encoding.UTF8.GetBytes(enc.CloseAndReturnText());
            }
            case EncodingFormat.Json:
            {
                using var ms = new MemoryStream();
                using (var enc = new JsonEncoder(ms, ctx))
                {
                    enc.WriteDataValue(FieldName, dv);
                }
                return ms.ToArray();
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(fmt));
        }
    }

    public static byte[] EncodeVariant(Variant v, EncodingFormat fmt, IServiceMessageContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        switch (fmt)
        {
            case EncodingFormat.Binary:
            {
                using var enc = new BinaryEncoder(ctx);
                enc.WriteVariant(FieldName, v);
                return enc.CloseAndReturnBuffer();
            }
            case EncodingFormat.Xml:
            {
                using var enc = new XmlEncoder(ctx);
                enc.WriteVariant(FieldName, v);
                return Encoding.UTF8.GetBytes(enc.CloseAndReturnText());
            }
            case EncodingFormat.Json:
            {
                using var ms = new MemoryStream();
                using (var enc = new JsonEncoder(ms, ctx))
                {
                    enc.WriteVariant(FieldName, v);
                }
                return ms.ToArray();
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(fmt));
        }
    }

    public static DataValue DecodeDataValue(byte[] data, EncodingFormat fmt, IServiceMessageContext ctx)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(ctx);
        switch (fmt)
        {
            case EncodingFormat.Binary:
            {
                using var dec = new BinaryDecoder(data, ctx);
                return dec.ReadDataValue(FieldName);
            }
            case EncodingFormat.Xml:
            {
                using var stream = new MemoryStream(data);
                using var reader = XmlReader.Create(stream);
                using var dec = new XmlDecoder(reader, ctx);
                return dec.ReadDataValue(FieldName);
            }
            case EncodingFormat.Json:
            {
                string json = Encoding.UTF8.GetString(data);
                using var dec = new JsonDecoder(json, ctx);
                return dec.ReadDataValue(FieldName) ?? new DataValue();
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(fmt));
        }
    }

    public static Variant DecodeVariant(byte[] data, EncodingFormat fmt, IServiceMessageContext ctx)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(ctx);
        switch (fmt)
        {
            case EncodingFormat.Binary:
            {
                using var dec = new BinaryDecoder(data, ctx);
                return dec.ReadVariant(FieldName);
            }
            case EncodingFormat.Xml:
            {
                using var stream = new MemoryStream(data);
                using var reader = XmlReader.Create(stream);
                using var dec = new XmlDecoder(reader, ctx);
                return dec.ReadVariant(FieldName);
            }
            case EncodingFormat.Json:
            {
                string json = Encoding.UTF8.GetString(data);
                using var dec = new JsonDecoder(json, ctx);
                return dec.ReadVariant(FieldName);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(fmt));
        }
    }

    /// <summary>
    /// Returns the file extension typically associated with the given
    /// encoding format ("bin", "xml", "json").
    /// </summary>
    public static string DefaultExtension(EncodingFormat fmt) => fmt switch
    {
        EncodingFormat.Binary => "bin",
        EncodingFormat.Xml => "xml",
        EncodingFormat.Json => "json",
        _ => "dat"
    };

    /// <summary>
    /// Guess the encoding from a file extension (case-insensitive).
    /// Returns null if no obvious match — callers should prompt.
    /// </summary>
    public static EncodingFormat? GuessFromExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }
        string ext = Path.GetExtension(fileName).Trim('.').ToLowerInvariant();
        return ext switch
        {
            "bin" or "uabin" => EncodingFormat.Binary,
            "xml" => EncodingFormat.Xml,
            "json" => EncodingFormat.Json,
            _ => null
        };
    }
}
