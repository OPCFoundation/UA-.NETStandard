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
using System.IO;
using System.Security.Cryptography;

namespace Opc.Ua.Pcap.KeyLog
{
    /// <summary>
    /// Manages per-session encryption keys for key-log files.
    /// </summary>
    internal static class SessionKeyManager
    {
        /// <summary>
        /// The AES-256-GCM session key size in bytes.
        /// </summary>
        public const int KeySizeInBytes = 32;

        /// <summary>
        /// Creates a new session key and persists it to the sibling key file.
        /// </summary>
        public static byte[] CreateAndPersistKey(string keylogFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keylogFilePath);

            byte[] sessionKey = RandomNumberGenerator.GetBytes(KeySizeInBytes);
            string keyFilePath = GetKeyFilePath(keylogFilePath);
            using FileStream stream = new(
                keyFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: KeySizeInBytes,
                FileOptions.None);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            stream.Write(sessionKey);
            return sessionKey;
        }

        /// <summary>
        /// Loads the session key from the sibling key file.
        /// </summary>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        public static byte[] LoadKey(string keylogFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keylogFilePath);

            string keyFilePath = GetKeyFilePath(keylogFilePath);
            if (!File.Exists(keyFilePath))
            {
                throw new FileNotFoundException("The key-log session key file was not found.", keyFilePath);
            }

            byte[] sessionKey = File.ReadAllBytes(keyFilePath);
            if (sessionKey.Length != KeySizeInBytes)
            {
                throw new InvalidDataException("The key-log session key file must contain a 32 byte key.");
            }

            return sessionKey;
        }

        private static string GetKeyFilePath(string keylogFilePath)
        {
            return keylogFilePath + ".key";
        }
    }
}
