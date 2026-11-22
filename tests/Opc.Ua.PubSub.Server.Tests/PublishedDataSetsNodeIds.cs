// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// The standard NodeIds of the methods of <c>PublishSubscribe.PublishedDataSets</c>
    /// (i=17371) and of their argument variables, as declared by the OPC UA NodeSet.
    /// The tests keep their own copy so that they check the NodeIds the node manager
    /// assigns against the standard, not against its own constants. The
    /// <c>Placeholder*</c> ids are the method declarations of the DataSetFolderType
    /// <c>&lt;DataSetFolderName&gt;</c> placeholder, which must not be bound.
    /// </summary>
    internal static class PublishedDataSetsNodeIds
    {
        public const uint AddPublishedDataItems = 17372;
        public const uint AddPublishedDataItemsInputArguments = 17373;
        public const uint AddPublishedDataItemsOutputArguments = 17374;
        public const uint AddPublishedEvents = 17375;
        public const uint AddPublishedEventsInputArguments = 17376;
        public const uint AddPublishedEventsOutputArguments = 17377;
        public const uint AddPublishedDataItemsTemplate = 17378;
        public const uint AddPublishedDataItemsTemplateInputArguments = 17379;
        public const uint AddPublishedDataItemsTemplateOutputArguments = 17380;
        public const uint RemovePublishedDataSet = 17384;
        public const uint RemovePublishedDataSetInputArguments = 17385;
        public const uint AddDataSetFolder = 17398;
        public const uint AddDataSetFolderInputArguments = 17401;
        public const uint AddDataSetFolderOutputArguments = 17402;
        public const uint RemoveDataSetFolder = 17403;
        public const uint RemoveDataSetFolderInputArguments = 17404;
        public const uint PlaceholderAddPublishedDataItems = 14479;
        public const uint PlaceholderAddDataSetFolder = 16884;
    }
}
