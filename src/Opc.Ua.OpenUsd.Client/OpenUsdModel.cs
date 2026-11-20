// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// The small slice of the OPC UA - OpenUSD Bindings companion model the client
    /// needs. A connector is a client: it does not need the server-side generated
    /// NodeState model, only the namespace URI, the well-known type NodeIds, and the
    /// meaning of the <c>RenderTargetKind</c> enumeration (values mirror
    /// <c>Opc.Ua.OpenUsdBinding.NodeSet2.xml</c>, DataType i=3002).
    /// </summary>
    internal static class OpenUsdModel
    {
        public const string NamespaceUri = "http://opcfoundation.org/UA/OpenUSD/";
        public const uint RepresentationTypeId = 1003;
        public const uint LiveBindingTypeId = 1004;       // abstract base
        public const uint ValueChangeBindingTypeId = 1007;  // : OpenUsdLiveBindingType
        public const uint AlarmBindingTypeId = 1008;
        public const uint HistoryBindingTypeId = 1009;
        public const uint CommandBindingTypeId = 1011;
        public const uint ComponentBindingTypeId = 1005;
        public const uint AssetTypeId = 1006;
    }

    /// <summary>
    /// Role of a served USD asset within a stage's closure (NodeSet i=3010).
    /// </summary>
    public enum OpenUsdAssetKind
    {
        RootLayer = 0,
        SubLayer = 1,
        Reference = 2,
        Payload = 3,
        Texture = 4,
        Package = 5
    }

    /// <summary>
    /// Cardinality of a component binding (NodeSet i=3008).
    /// </summary>
    public enum OpenUsdCardinality
    {
        One = 0,
        Many = 1
    }

    /// <summary>
    /// USD composition arc for a component prim (NodeSet i=3009).
    /// </summary>
    public enum OpenUsdCompositionArc
    {
        Child = 0,
        Reference = 1,
        Payload = 2,
        Instance = 3
    }

    /// <summary>
    /// How a bound value drives the target USD attribute (NodeSet i=3002).
    /// </summary>
    public enum OpenUsdRenderTargetKind
    {
        Translation = 0,
        Rotation = 1,
        Scale = 2,
        Transform = 3,
        Visibility = 4,
        DisplayColor = 5,
        EmissiveColor = 6,
        Opacity = 7,
        Custom = 8,
        Georeference = 9
    }

    /// <summary>Binding intent/direction — a connector-internal runtime discriminator
    /// derived from the concrete binding subtype (OpenUsdValueChange/Alarm/History/CommandBindingType,
    /// §5.4). The former NodeSet enum (i=3001) was removed in favour of the subtype hierarchy.</summary>
    public enum OpenUsdIntentProfile
    {
        UaToUsdTelemetry = 0,
        UaAlarmToUsd = 1,
        UaHistoryToUsd = 2,
        UsdToUaCommand = 3
    }

    /// <summary>
    /// Role of the bound signal (NodeSet i=3005).
    /// </summary>
    public enum OpenUsdSignalRole
    {
        Observable = 0,
        Controllable = 1
    }

    /// <summary>
    /// A&amp;C condition aspect a UaAlarmToUsd binding drives (NodeSet i=3006).
    /// </summary>
    public enum OpenUsdAlarmAspect
    {
        ActiveState = 0,
        Severity = 1,
        AckedState = 2,
        EnabledState = 3
    }

    /// <summary>
    /// Digest algorithm for OpenUsdStageType.RootLayerDigest (NodeSet i=3007).
    /// </summary>
    public enum OpenUsdDigestAlgorithm
    {
        None = 0,
        Sha256 = 1,
        Sha384 = 2,
        Sha512 = 3
    }
}
