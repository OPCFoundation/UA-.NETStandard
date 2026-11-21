// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Types
{
#if !INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public
#else
    internal
#endif
        static class ReferenceTypes
    {
        public const uint References = 31;

        public const uint NonHierarchicalReferences = 32;

        public const uint HierarchicalReferences = 33;

        public const uint Organizes = 35;

        public const uint HasEventSource = 36;

        public const uint HasModellingRule = 37;

        public const uint HasEncoding = 38;

        public const uint HasDescription = 39;

        public const uint HasTypeDefinition = 40;

        public const uint GeneratesEvent = 41;

        public const uint AlwaysGeneratesEvent = 3065;

        public const uint Aggregates = 44;

        public const uint HasSubtype = 45;

        public const uint HasProperty = 46;

        public const uint HasComponent = 47;

        public const uint HasNotifier = 48;

        public const uint HasOrderedComponent = 49;

        public const uint FromState = 51;

        public const uint ToState = 52;

        public const uint HasCause = 53;

        public const uint HasEffect = 54;

        public const uint HasGuard = 15112;

        public const uint HasDictionaryEntry = 17597;

        public const uint HasInterface = 17603;

        public const uint HasAddIn = 17604;

        public const uint HasTrueSubState = 9004;

        public const uint HasFalseSubState = 9005;

        public const uint HasAlarmSuppressionGroup = 16361;

        public const uint AlarmGroupMember = 16362;

        public const uint AlarmSuppressionGroupMember = 32059;

        public const uint HasCondition = 9006;
    }
}
