/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Xml;
using Opc.Ua;

/// <summary>
/// Fuzzing code for the Xml decoder and encoder.
/// </summary>
public static partial class FuzzableCode
{
    /// <summary>
    /// The Xml decoder fuzz target for afl-fuzz.
    /// </summary>
    public static void AflfuzzXmlDecoder(Stream stream)
    {
        _ = FuzzXmlDecoderCore(stream);
    }

    /// <summary>
    /// The Xml encoder fuzz target for afl-fuzz.
    /// </summary>
    public static void AflfuzzXmlEncoder(Stream stream)
    {
        IEncodeable encodeable = null;
        try
        {
            encodeable = FuzzXmlDecoderCore(stream);
        }
        catch
        {
            return;
        }

        // encode the fuzzed object and see if it crashes
        if (encodeable != null)
        {
            using (var encoder = new JsonEncoder(messageContext, true))
            {
                encoder.EncodeMessage(encodeable);
                encoder.Close();
            }
        }
    }

    /// <summary>
    /// The Xml decoder fuzz target for libfuzzer.
    /// </summary>
    public static void LibfuzzXmlDecoder(ReadOnlySpan<byte> input)
    {
        using (var memoryStream = new MemoryStream(input.ToArray()))
        {
            _ = FuzzXmlDecoderCore(memoryStream);
        }
    }

    /// <summary>
    /// The Xml encoder fuzz target for afl-fuzz.
    /// </summary>
    public static void LibfuzzXmlEncoder(ReadOnlySpan<byte> input)
    {
        IEncodeable encodeable;
        try
        {
            using (var memoryStream = new MemoryStream(input.ToArray()))
            {
                encodeable = FuzzXmlDecoderCore(memoryStream);
            }
        }
        catch
        {
            return;
        }

        // encode the fuzzed object and see if it crashes
        if (encodeable != null)
        {
            using (var encoder = new XmlEncoder(messageContext))
            {
                encoder.EncodeMessage(encodeable);
                encoder.Close();
            }
        }
    }

    /// <summary>
    /// The fuzz target for the XmlDecoder.
    /// </summary>
    /// <param name="stream">A stream with fuzz content.</param>
    internal static IEncodeable FuzzXmlDecoderCore(Stream stream, bool throwAll = false)
    {
        XmlReader reader = null;
        try
        {
            Type systemType = null;
            try
            {
                reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
                reader.MoveToContent();
                string typeName = reader.LocalName;
                string namespaceUri = reader.NamespaceURI;
                systemType = messageContext.Factory.EncodeableTypes
                    .Where(entry => entry.Value.Name == typeName/* && entry.Key.NamespaceUri == namespaceUri*/)
                    .Select(entry => entry.Value)
                    .FirstOrDefault();
            }
            catch (XmlException ex)
            {
                if (!throwAll)
                {
                    return null;
                }
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, ex.Message);
            }

            if (systemType == null)
            {
                if (!throwAll)
                {
                    return null;
                }
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not find type for decoding.");
            }

            // TODO: match ns GetEncodeableFactory(typeName, namespaceUri, out IEncodeable encodeable, out _);
            using (var decoder = new XmlDecoder(reader, messageContext))
            {
                return decoder.DecodeMessage(systemType);
            }
        }
        catch (ServiceResultException sre)
        {
            switch (sre.StatusCode)
            {
                case StatusCodes.BadEncodingLimitsExceeded:
                case StatusCodes.BadDecodingError:
                    if (!throwAll)
                    {
                        return null;
                    }
                    break;
            }

            Console.WriteLine("Unexpected ServiceResultException: {0} {1}", (StatusCode)sre.StatusCode, sre.Message);

            throw;
        }
        finally
        {
            reader?.Dispose();
        }
    }
}

