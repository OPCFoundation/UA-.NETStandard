// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Stores the configuration for the Global Discovery Client.
    /// Serialized via source-generated IEncodeable implementation.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public sealed partial class GlobalDiscoveryClientConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public GlobalDiscoveryClientConfiguration()
        {
        }

        /// <summary>
        /// The URL of the Global Discovery Server.
        /// </summary>
        [DataTypeField(Order = 0)]
        public string? GlobalDiscoveryServerUrl { get; set; }

        /// <summary>
        /// The path to the external editor application.
        /// </summary>
        [DataTypeField(Order = 1)]
        public string? ExternalEditor { get; set; }
    }
}
