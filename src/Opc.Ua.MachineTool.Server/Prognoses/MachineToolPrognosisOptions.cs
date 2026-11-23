// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
