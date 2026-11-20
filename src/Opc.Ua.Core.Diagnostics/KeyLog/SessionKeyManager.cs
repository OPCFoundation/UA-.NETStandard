/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
