/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Text.Json.Serialization;

namespace Opc.Ua.Redundancy.Kubernetes
{
    [JsonSerializable(typeof(KubernetesLease))]
    [JsonSerializable(typeof(KubernetesEndpointSliceList))]
    internal sealed partial class KubernetesJsonContext : JsonSerializerContext;
}
