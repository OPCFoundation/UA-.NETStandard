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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Stores various configuration parameters used by the channel.
    /// </summary>
    public class ChannelQuotas
    {
        /// <summary>
        /// Creates an object with default values.
        /// </summary>
        public ChannelQuotas(ServiceMessageContext messageContext)
        {
            MessageContext = messageContext;
            MaxMessageSize = TcpMessageLimits.DefaultMaxMessageSize;
            MaxBufferSize = TcpMessageLimits.DefaultMaxBufferSize;
            ChannelLifetime = TcpMessageLimits.DefaultChannelLifetime;
            SecurityTokenLifetime = TcpMessageLimits.DefaultSecurityTokenLifeTime;
        }

        /// <summary>
        /// The context to use when encoding/decoding messages.
        /// </summary>
        public IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Validator to use when handling certificates.
        /// </summary>
        public ICertificateValidatorEx? CertificateValidator { get; set; }

        /// <summary>
        /// Selects the crypto provider for operations performed on the channel,
        /// or <c>null</c> to use platform cryptography.
        /// </summary>
        /// <remarks>
        /// The channel resolves a provider once, when it is opened, and holds the
        /// result for its lifetime, in the same way it caches the security policy
        /// on its token. Nothing on a per message path consults this.
        /// </remarks>
        public ICryptoProviderRegistry? CryptoProviders { get; set; }

        /// <summary>
        /// The security policies the channel negotiates against, or <c>null</c>
        /// to use <see cref="SecurityPolicies.Default"/>.
        /// </summary>
        /// <remarks>
        /// Set this from the registry the application configured so a policy it
        /// contributed through <c>AddSecurityPolicy</c> is reachable by a peer.
        /// Leaving it <c>null</c> keeps the built-in policy set, which is what a
        /// caller that configures nothing gets.
        /// </remarks>
        public ISecurityPolicyRegistry? SecurityPolicyRegistry { get; set; }

        /// <summary>
        /// The maximum size for a message sent or received.
        /// </summary>
        public int MaxMessageSize { get; set; }

        /// <summary>
        /// The maximum size for the send or receive buffers.
        /// </summary>
        public int MaxBufferSize { get; set; }

        /// <summary>
        /// The default lifetime for the channel in milliseconds.
        /// </summary>
        public int ChannelLifetime { get; set; }

        /// <summary>
        /// The default lifetime for a security token in milliseconds.
        /// </summary>
        public int SecurityTokenLifetime { get; set; }
    }
}
