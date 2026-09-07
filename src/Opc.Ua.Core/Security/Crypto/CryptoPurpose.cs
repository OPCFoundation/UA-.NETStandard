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

namespace Opc.Ua
{
    /// <summary>
    /// Identifies what a cryptographic operation is being performed for.
    /// </summary>
    /// <remarks>
    /// Providers are selected per purpose so that a deployment can put its
    /// application instance key in a TPM, have user identity tokens signed by a
    /// remote key service, and leave everything else to the platform, all at the
    /// same time. Selection is further refined by security policy, so the same
    /// purpose can resolve differently for different policies.
    /// <para>
    /// This is a value type with well known instances rather than an enum. The
    /// security constants in this stack are already closed enums, which is the
    /// single largest obstacle to contributing a new algorithm, and repeating
    /// that mistake here would make the provider model equally closed. A caller
    /// may define a purpose the stack does not know about.
    /// </para>
    /// </remarks>
    public readonly record struct CryptoPurpose
    {
        /// <summary>
        /// Initializes a new purpose with the given name.
        /// </summary>
        /// <param name="name">
        /// A stable identifier, used in configuration, logs and diagnostics.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is null or white space.
        /// </exception>
        public CryptoPurpose(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A purpose requires a name.", nameof(name));
            }

            Name = name;
        }

        /// <summary>
        /// The name of the purpose.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Signing and decryption with the application instance certificate,
        /// performed while a secure channel is opened.
        /// </summary>
        public static CryptoPurpose ApplicationInstanceKey { get; } = new("ApplicationInstanceKey");

        /// <summary>
        /// Signing with a user's certificate to prove possession when a session
        /// is activated.
        /// </summary>
        public static CryptoPurpose UserIdentityKey { get; } = new("UserIdentityKey");

        /// <summary>
        /// Ephemeral key agreement for the elliptic curve security policies.
        /// </summary>
        public static CryptoPurpose KeyAgreement { get; } = new("KeyAgreement");

        /// <summary>
        /// Signing certificates, certificate requests and revocation lists.
        /// </summary>
        public static CryptoPurpose CertificateIssuance { get; } = new("CertificateIssuance");

        /// <summary>
        /// Per message symmetric encryption and signing on an open channel.
        /// </summary>
        public static CryptoPurpose ChannelSymmetric { get; } = new("ChannelSymmetric");

        /// <summary>
        /// Derivation of the channel and session key material from a shared
        /// secret.
        /// </summary>
        public static CryptoPurpose KeyDerivation { get; } = new("KeyDerivation");

        /// <summary>
        /// Generation of nonces and other random material.
        /// </summary>
        public static CryptoPurpose RandomNumberGeneration { get; } = new("RandomNumberGeneration");

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name ?? string.Empty;
        }
    }
}
