/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// PubSub transport profile identifiers defined by the Ethernet
    /// transport, kept with the transport implementation rather than in
    /// the core profile constants.
    /// </summary>
    public static class EthProfiles
    {
        /// <summary>
        /// Uri for the "PubSub Ethernet UADP" Profile. This PubSub
        /// transport Facet combines the OPC UA Part 14 Ethernet transport
        /// protocol mapping with UADP message mapping and is used for
        /// direct Layer 2 messaging (EtherType 0xB62C).
        /// </summary>
        public const string PubSubEthUadpTransport
            = "http://opcfoundation.org/UA-Profile/Transport/pubsub-eth-uadp";
    }
}
