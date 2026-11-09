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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Selects actual prediction capabilities and finite per-instance bounds without inventing equipment forecasts.
    /// </summary>
    public sealed record MachineToolPrognosisOptions
    {
        /// <summary>
        /// Gets the explicitly selected prognosis surfaces.
        /// </summary>
        public MachineToolFeatures Features { get; init; }

        /// <summary>
        /// Gets the kinds the configured provider can report. An enabled surface requires an explicit selection.
        /// </summary>
        public ArrayOf<MachineToolPrognosisKind> SupportedKinds { get; init; }

        /// <summary>
        /// Gets the maximum number of simultaneous forecasts, between 1 and 65536.
        /// </summary>
        public int MaximumPrognoses { get; init; } = 128;

        /// <summary>
        /// Gets the maximum combined tool, job and part references examined for one coherent observation.
        /// </summary>
        public int MaximumReferencedEntities { get; init; } = 8192;

        /// <summary>
        /// Gets the maximum length of each key, identifier, name and localized description.
        /// </summary>
        public int MaximumTextLength { get; init; } = 1024;

        /// <summary>
        /// Gets an optional typed factory preserving actual generated companion prognosis subtypes.
        /// The actual type must derive from the selected standard kind or the explicitly supplied CompanionTypeId.
        /// </summary>
        public Func<ISystemContext, NodeState, QualifiedName, MachineToolPrognosis, CancellationToken,
            ValueTask<MachineToolEquipmentNode<PrognosisState>>>? CreatePrognosisAsync { get; init; }

        /// <summary>
        /// Gets the actual server-publication barrier before dynamic removal.
        /// The builder supplies the selected namespace owner's PublishResponse barrier when omitted.
        /// </summary>
        public Func<ArrayOf<NodeState>, CancellationToken, ValueTask>? PublishFinalObservationsAsync { get; init; }
    }
}
