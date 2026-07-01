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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Opc.Ua.Redundancy.Kubernetes
{
    internal sealed class KubernetesObjectMetadata
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("namespace")]
        public string? Namespace { get; set; }

        [JsonPropertyName("resourceVersion")]
        public string? ResourceVersion { get; set; }

        [JsonPropertyName("labels")]
        public Dictionary<string, string>? Labels { get; set; }
    }

    internal sealed class KubernetesLease
    {
        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = "coordination.k8s.io/v1";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "Lease";

        [JsonPropertyName("metadata")]
        public KubernetesObjectMetadata Metadata { get; set; } = new();

        [JsonPropertyName("spec")]
        public KubernetesLeaseSpec Spec { get; set; } = new();
    }

    internal sealed class KubernetesLeaseSpec
    {
        [JsonPropertyName("holderIdentity")]
        public string? HolderIdentity { get; set; }

        [JsonPropertyName("leaseDurationSeconds")]
        public int LeaseDurationSeconds { get; set; }

        [JsonPropertyName("acquireTime")]
        public string? AcquireTime { get; set; }

        [JsonPropertyName("renewTime")]
        public string? RenewTime { get; set; }

        [JsonPropertyName("leaseTransitions")]
        public int? LeaseTransitions { get; set; }
    }

    internal sealed class KubernetesEndpointSliceList
    {
        [JsonPropertyName("items")]
        public List<KubernetesEndpointSlice> Items { get; set; } = [];
    }

    internal sealed class KubernetesEndpointSlice
    {
        [JsonPropertyName("metadata")]
        public KubernetesObjectMetadata Metadata { get; set; } = new();

        [JsonPropertyName("endpoints")]
        public List<KubernetesEndpoint> Endpoints { get; set; } = [];

        [JsonPropertyName("ports")]
        public List<KubernetesEndpointPort> Ports { get; set; } = [];
    }

    internal sealed class KubernetesEndpoint
    {
        [JsonPropertyName("addresses")]
        public List<string> Addresses { get; set; } = [];

        [JsonPropertyName("conditions")]
        public KubernetesEndpointConditions? Conditions { get; set; }
    }

    internal sealed class KubernetesEndpointConditions
    {
        [JsonPropertyName("ready")]
        public bool? Ready { get; set; }
    }

    internal sealed class KubernetesEndpointPort
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("port")]
        public int? Port { get; set; }
    }
}