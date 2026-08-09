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
using System.Globalization;
using System.Text;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Identifies the token to open and, optionally, the object on it.
    /// </summary>
    /// <remarks>
    /// This is the configuration a PKCS#11 store needs. It can be written out
    /// directly or parsed from an RFC 7512 <c>pkcs11:</c> URI, which is what the
    /// store path in an OPC UA configuration file contains.
    /// </remarks>
    public sealed class Pkcs11TokenOptions
    {
        /// <summary>
        /// The scheme of an RFC 7512 PKCS#11 URI, including the colon.
        /// </summary>
        public const string UriScheme = "pkcs11:";

        /// <summary>
        /// The file system path of the PKCS#11 module to load, for example
        /// <c>/usr/lib/softhsm/libsofthsm2.so</c>.
        /// </summary>
        /// <remarks>
        /// Required. There is no meaningful default: the module is supplied by
        /// the token vendor.
        /// </remarks>
        public string? ModulePath { get; set; }

        /// <summary>
        /// The label of the token to use, or <c>null</c> to accept any token.
        /// </summary>
        public string? TokenLabel { get; set; }

        /// <summary>
        /// The serial number of the token to use, or <c>null</c> to accept any.
        /// </summary>
        public string? TokenSerial { get; set; }

        /// <summary>
        /// The slot to use, or <c>null</c> to search every slot with a token.
        /// </summary>
        public ulong? SlotId { get; set; }

        /// <summary>
        /// The label of the certificate object to use, or <c>null</c> for all.
        /// </summary>
        public string? ObjectLabel { get; set; }

        /// <summary>
        /// The CKA_ID of the objects to use, or an empty value for all.
        /// </summary>
        public ByteString ObjectId { get; set; }

        /// <summary>
        /// The user PIN, or <c>null</c> when <see cref="PinProvider"/> supplies it.
        /// </summary>
        /// <remarks>
        /// Prefer <see cref="PinProvider"/> so the PIN can be read from a secret
        /// store instead of being held in configuration.
        /// </remarks>
        public string? Pin { get; set; }

        /// <summary>
        /// Supplies the user PIN on demand, for example from a secret store.
        /// </summary>
        /// <remarks>
        /// Takes precedence over <see cref="Pin"/> when both are set.
        /// </remarks>
        public Func<string?>? PinProvider { get; set; }

        /// <summary>
        /// Returns the PIN to log in with, or <c>null</c> for a public session.
        /// </summary>
        /// <returns>The PIN, or <c>null</c>.</returns>
        public string? GetPin()
        {
            return PinProvider?.Invoke() ?? Pin;
        }

        /// <summary>
        /// Removes the PIN from a PKCS#11 URI so it can be retained and shown.
        /// </summary>
        /// <param name="uri">The URI to redact.</param>
        /// <returns>
        /// The URI with any <c>pin-value</c> replaced, or the input unchanged
        /// when it carries none.
        /// </returns>
        /// <remarks>
        /// A store path is surfaced in configuration, diagnostics and the address
        /// space, none of which should carry the credential that unlocks the
        /// token's private keys. The PIN is kept in
        /// <see cref="Pin"/> instead, which is not part of any of those.
        /// </remarks>
        public static string RedactPin(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return uri;
            }

            int query = uri.IndexOf('?', StringComparison.Ordinal);

            if (query < 0)
            {
                return uri;
            }

            var redacted = new System.Text.StringBuilder(uri.Substring(0, query + 1));
            bool first = true;

            foreach (string pair in uri.Substring(query + 1).Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                if (!first)
                {
                    redacted.Append('&');
                }

                first = false;

                int equals = pair.IndexOf('=', StringComparison.Ordinal);
                string key = equals > 0 ? pair.Substring(0, equals).Trim().ToLowerInvariant() : pair;

                redacted.Append(key == "pin-value" ? "pin-value=<redacted>" : pair);
            }

            return redacted.ToString();
        }

        /// <summary>
        /// Whether a store path looks like an RFC 7512 PKCS#11 URI.
        /// </summary>
        /// <param name="storePath">The store path to test.</param>
        /// <returns><c>true</c> when the path uses the <c>pkcs11:</c> scheme.</returns>
        public static bool IsPkcs11Uri(string? storePath)
        {
            return storePath != null &&
                storePath.StartsWith(UriScheme, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses an RFC 7512 <c>pkcs11:</c> URI.
        /// </summary>
        /// <param name="uri">
        /// The URI, for example
        /// <c>pkcs11:token=my-token;object=server?module-path=/usr/lib/libsofthsm2.so&amp;pin-value=1234</c>.
        /// </param>
        /// <returns>The options the URI describes.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="uri"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="uri"/> does not use the <c>pkcs11:</c> scheme.
        /// </exception>
        /// <remarks>
        /// Only the attributes this stack acts on are interpreted; anything else
        /// is ignored rather than rejected, so a URI produced for another tool
        /// still works here.
        /// </remarks>
        public static Pkcs11TokenOptions Parse(string uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (!IsPkcs11Uri(uri))
            {
                throw new ArgumentException(
                    $"'{uri}' is not a PKCS#11 URI; it must start with '{UriScheme}'.",
                    nameof(uri));
            }

            string remainder = uri.Substring(UriScheme.Length);
            string pathPart = remainder;
            string queryPart = string.Empty;

            int query = remainder.IndexOf('?', StringComparison.Ordinal);

            if (query >= 0)
            {
                pathPart = remainder.Substring(0, query);
                queryPart = remainder.Substring(query + 1);
            }

            var options = new Pkcs11TokenOptions();

            foreach (KeyValuePair<string, string> attribute in SplitAttributes(pathPart, ';'))
            {
                switch (attribute.Key)
                {
                    case "token":
                        options.TokenLabel = PercentDecode(attribute.Value);
                        break;
                    case "serial":
                        options.TokenSerial = PercentDecode(attribute.Value);
                        break;
                    case "object":
                        options.ObjectLabel = PercentDecode(attribute.Value);
                        break;
                    case "id":
                        // Decoded straight to bytes: CKA_ID is binary and is not
                        // required to be valid UTF-8, so decoding it through a
                        // string would corrupt it.
                        options.ObjectId = new ByteString(PercentDecodeToBytes(attribute.Value));
                        break;
                    case "slot-id":
                        if (ulong.TryParse(
                                attribute.Value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out ulong slotId))
                        {
                            options.SlotId = slotId;
                        }
                        break;
                    default:
                        break;
                }
            }

            foreach (KeyValuePair<string, string> attribute in SplitAttributes(queryPart, '&'))
            {
                switch (attribute.Key)
                {
                    case "module-path":
                    case "module-name":
                        options.ModulePath = PercentDecode(attribute.Value);
                        break;
                    case "pin-value":
                        options.Pin = PercentDecode(attribute.Value);
                        break;
                    default:
                        break;
                }
            }

            return options;
        }

        /// <summary>
        /// Splits attributes into raw, still encoded, key and value pairs.
        /// </summary>
        /// <remarks>
        /// The value is deliberately left encoded. Decoding it here would force
        /// every attribute through a string, and <c>id</c> carries arbitrary
        /// bytes that need not form valid UTF-8.
        /// </remarks>
        private static IEnumerable<KeyValuePair<string, string>> SplitAttributes(
            string text,
            char separator)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }

            foreach (string pair in text.Split(separator))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                int equals = pair.IndexOf('=', StringComparison.Ordinal);

                if (equals <= 0)
                {
                    continue;
                }

                yield return new KeyValuePair<string, string>(
                    pair.Substring(0, equals).Trim().ToLowerInvariant(),
                    pair.Substring(equals + 1));
            }
        }

        private static string PercentDecode(string value)
        {
            return Encoding.UTF8.GetString(PercentDecodeToBytes(value));
        }

        private static byte[] PercentDecodeToBytes(string value)
        {
            var bytes = new List<byte>(value.Length);

            for (int ii = 0; ii < value.Length; ii++)
            {
                if (value[ii] == '%' &&
                    ii + 2 < value.Length &&
                    TryParseHex(value[ii + 1], value[ii + 2], out byte decoded))
                {
                    bytes.Add(decoded);
                    ii += 2;
                    continue;
                }

                bytes.AddRange(Encoding.UTF8.GetBytes(value[ii].ToString()));
            }

            return [.. bytes];
        }

        private static bool TryParseHex(char high, char low, out byte value)
        {
            value = 0;

            if (!TryParseNibble(high, out int highValue) ||
                !TryParseNibble(low, out int lowValue))
            {
                return false;
            }

            value = (byte)((highValue << 4) | lowValue);
            return true;
        }

        private static bool TryParseNibble(char character, out int value)
        {
            if (character >= '0' && character <= '9')
            {
                value = character - '0';
                return true;
            }

            if (character >= 'a' && character <= 'f')
            {
                value = character - 'a' + 10;
                return true;
            }

            if (character >= 'A' && character <= 'F')
            {
                value = character - 'A' + 10;
                return true;
            }

            value = 0;
            return false;
        }
    }
}
