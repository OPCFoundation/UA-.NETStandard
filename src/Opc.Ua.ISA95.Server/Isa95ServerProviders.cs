// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Server
{
    /// <summary>
    /// Provider set projected by an <see cref="Isa95NodeManager"/>.
    /// </summary>
    public sealed class Isa95ServerProviders
    {
        public IIsa95JobOrderReceiverV1? JobOrderReceiverV1 { get; init; }
        public IIsa95JobResponseProviderV1? JobResponseProviderV1 { get; init; }
        public IIsa95JobResponseReceiverV1? JobResponseReceiverV1 { get; init; }
        public IIsa95JobOrderReceiverV2? JobOrderReceiverV2 { get; init; }
        public IIsa95JobResponseProviderV2? JobResponseProviderV2 { get; init; }
        public IIsa95JobResponseReceiverV2? JobResponseReceiverV2 { get; init; }
        public IIsa95JobStatusSourceV2? JobStatusSourceV2 { get; init; }
        public IIsa95JobOrderCatalog? JobOrderCatalog { get; init; }
        public IIsa95JobOrderCatalogChangeSource? JobOrderCatalogChangeSource { get; init; }
    }
}
