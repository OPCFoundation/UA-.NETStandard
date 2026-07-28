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

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// A version-neutral job response tracked by the in-memory engine. Every
    /// standard Job Control V1/V2 job-response field is captured, including the
    /// personnel, equipment, physical-asset and material actual collections, so
    /// that responses round-trip losslessly within a version.
    /// </summary>
    internal sealed record Isa95JobResponse
    {
        public required string Id { get; init; }
        public required string JobOrderId { get; init; }
        public ArrayOf<LocalizedText> Description { get; init; } = [];
        public DateTimeUtc StartTime { get; init; }
        public DateTimeUtc EndTime { get; init; }
        public Isa95JobCanonicalState State { get; init; }
        public ArrayOf<Isa95Parameter> ResponseData { get; init; } = [];
        public ArrayOf<Isa95ResourceRequirement> PersonnelActuals { get; init; } = [];
        public ArrayOf<Isa95ResourceRequirement> EquipmentActuals { get; init; } = [];
        public ArrayOf<Isa95ResourceRequirement> PhysicalAssetActuals { get; init; } = [];
        public ArrayOf<Isa95Material> MaterialActuals { get; init; } = [];
        public DateTimeUtc ReceivedAt { get; init; }
    }
}
