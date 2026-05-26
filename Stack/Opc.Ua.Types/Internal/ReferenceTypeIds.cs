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

namespace Opc.Ua.Types
{
#if !INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public
#else
    internal
#endif
        static class ReferenceTypeIds
    {
        public static readonly NodeId References = new(ReferenceTypes.References);

        public static readonly NodeId NonHierarchicalReferences = new(ReferenceTypes.NonHierarchicalReferences);

        public static readonly NodeId HierarchicalReferences = new(ReferenceTypes.HierarchicalReferences);

        public static readonly NodeId Organizes = new(ReferenceTypes.Organizes);

        public static readonly NodeId HasEventSource = new(ReferenceTypes.HasEventSource);

        public static readonly NodeId HasModellingRule = new(ReferenceTypes.HasModellingRule);

        public static readonly NodeId HasEncoding = new(ReferenceTypes.HasEncoding);

        public static readonly NodeId HasDescription = new(ReferenceTypes.HasDescription);

        public static readonly NodeId HasTypeDefinition = new(ReferenceTypes.HasTypeDefinition);

        public static readonly NodeId GeneratesEvent = new(ReferenceTypes.GeneratesEvent);

        public static readonly NodeId AlwaysGeneratesEvent = new(ReferenceTypes.AlwaysGeneratesEvent);

        public static readonly NodeId Aggregates = new(ReferenceTypes.Aggregates);

        public static readonly NodeId HasSubtype = new(ReferenceTypes.HasSubtype);

        public static readonly NodeId HasProperty = new(ReferenceTypes.HasProperty);

        public static readonly NodeId HasComponent = new(ReferenceTypes.HasComponent);

        public static readonly NodeId HasNotifier = new(ReferenceTypes.HasNotifier);

        public static readonly NodeId HasOrderedComponent = new(ReferenceTypes.HasOrderedComponent);

        public static readonly NodeId FromState = new(ReferenceTypes.FromState);

        public static readonly NodeId ToState = new(ReferenceTypes.ToState);

        public static readonly NodeId HasCause = new(ReferenceTypes.HasCause);

        public static readonly NodeId HasEffect = new(ReferenceTypes.HasEffect);

        public static readonly NodeId HasGuard = new(ReferenceTypes.HasGuard);

        public static readonly NodeId HasDictionaryEntry = new(ReferenceTypes.HasDictionaryEntry);

        public static readonly NodeId HasInterface = new(ReferenceTypes.HasInterface);

        public static readonly NodeId HasAddIn = new(ReferenceTypes.HasAddIn);

        public static readonly NodeId HasTrueSubState = new(ReferenceTypes.HasTrueSubState);

        public static readonly NodeId HasFalseSubState = new(ReferenceTypes.HasFalseSubState);

        public static readonly NodeId HasAlarmSuppressionGroup = new(ReferenceTypes.HasAlarmSuppressionGroup);

        public static readonly NodeId AlarmGroupMember = new(ReferenceTypes.AlarmGroupMember);

        public static readonly NodeId AlarmSuppressionGroupMember = new(ReferenceTypes.AlarmSuppressionGroupMember);

        public static readonly NodeId HasCondition = new(ReferenceTypes.HasCondition);
    }
}
