// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Describes how to connect to an endpoint.
    /// </summary>
    public partial class EndpointConfiguration
    {
        /// <summary>
        /// The maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        public int MaxEncodingNestingLevels
        {
            get =>
                m_maxEncodingNestingLevels <= 0
                    ? DefaultEncodingLimits.MaxEncodingNestingLevels
                    : m_maxEncodingNestingLevels;
            set => m_maxEncodingNestingLevels = value;
        }

        /// <summary>
        /// The number of times the decoder can recover from an error
        /// caused by an encoded ExtensionObject before throwing a decoder error.
        /// </summary>
        public int MaxDecoderRecoveries { get; set; }

        private int m_maxEncodingNestingLevels;
    }
}
