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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Shared explanations for equivalent workflow parameters, including format, intent and concrete examples.
    /// </summary>
    internal static class WorkflowParameterDescriptions
    {
        /// <summary>Explains exact local or namespace-qualified node identity.</summary>
        internal const string NodeId =
            "UA NodeId, e.g. 'nsu=urn:plant;s=Temp'.";

        /// <summary>Explains registered-session selection.</summary>
        internal const string SessionName =
            "Session alias; omit for a sole connection.";

        /// <summary>Explains page and observation capacity.</summary>
        internal const string MaxResults =
            "Item limit; host caps apply.";

        /// <summary>Explains live browse offsets rather than retained history cursors.</summary>
        internal const string Offset =
            "Zero-based browse offset; not a cursor.";

        /// <summary>Explains discovery roots.</summary>
        internal const string RootNodeId =
            "Browse root NodeId, e.g. 'i=85' for Objects. Discovery examines only the selected bounded hierarchy.";

        /// <summary>Explains discovery traversal limits.</summary>
        internal const string MaxDepth =
            "Maximum descendant browse depth, e.g. 3; range 0..6. Does not grant access outside the chosen root.";

        /// <summary>Explains directory ownership.</summary>
        internal const string DirectoryId =
            "Actual parent FileDirectoryType NodeId, e.g. 'ns=2;s=Reports'; discover it with files_roots/files_list.";

        /// <summary>Explains a verified immediate child.</summary>
        internal const string SourceId =
            "Existing immediate child NodeId of directoryId, e.g. 'ns=2;s=Report'; unrelated nodes are refused.";

        /// <summary>Explains an explicit destination.</summary>
        internal const string TargetDirectoryId =
            "Existing destination FileDirectoryType NodeId, e.g. 'ns=2;s=Archive'; no parents are created implicitly.";

        /// <summary>Explains FileType identity.</summary>
        internal const string FileId =
            "Existing FileType NodeId, e.g. 'ns=2;s=Report'; protected configuration files require dedicated tools.";

        /// <summary>Explains host-confined artifact paths.</summary>
        internal const string FilePath =
            "Artifact path beneath host TransferRoot, e.g. 'reports/result.bin'; links and path escapes are refused.";

        /// <summary>Explains temporary-transfer identity.</summary>
        internal const string TransferId =
            "Existing TemporaryFileTransferType NodeId, e.g. 'ns=2;s=ReportTransfer'; never a raw file handle.";

        /// <summary>Explains completion inspection.</summary>
        internal const string CompletionId =
            "FileTransferStateMachine NodeId returned by generation/commit, e.g. 'ns=2;s=TransferCompletion'.";

        /// <summary>Explains unqualified creation names.</summary>
        internal const string Name =
            "One unqualified leaf name, e.g. 'report.bin'; no path separators, namespace prefixes or overwrite.";

        /// <summary>Explains explicit rename destinations.</summary>
        internal const string NewName =
            "New unqualified destination leaf, e.g. 'archived.bin'; it must not already exist.";

        /// <summary>Explains deliberate content replacement.</summary>
        internal const string ReplaceExistingContent =
            "Set true only to explicitly replace the existing remote file content; false refuses before truncation.";

        /// <summary>Explains explicit subtree scope for copy/move.</summary>
        internal const string IncludeChildren =
            "Set true to include a nonempty directory's subtree; default false refuses implicit recursive scope.";

        /// <summary>Explains explicit subtree deletion.</summary>
        internal const string Recursive =
            "Set true only to delete the selected nonempty directory and its descendants; default false.";

        /// <summary>Explains generation-option discrimination.</summary>
        internal const string OptionType =
            "UA built-in type of generation options, e.g. 'String'; default 'Null' means no generation options.";

        /// <summary>Explains actual JSON option values without stringified documents.</summary>
        internal const string OptionValue =
            "Actual JSON scalar or one-dimensional scalar array matching optionType, e.g. 0 or [1,2]. " +
            "Objects and JSON-encoded documents are not accepted; omit for Null options.";

        /// <summary>Explains condition identity rather than a browse-name guess.</summary>
        internal const string ConditionId =
            "ConditionId NodeId from the actual event, e.g. 'ns=2;s=HighTemperature'; not an alarm display name.";

        /// <summary>Explains occurrence identity.</summary>
        internal const string EventId =
            "Exact occurrence EventId as base64, e.g. 'AQIDBA=='; use the current id after acknowledgement.";

        /// <summary>Explains branch identity.</summary>
        internal const string BranchId =
            "Selected branch NodeId, e.g. 'ns=2;s=Branch1'; default 'i=0' selects the condition's current branch.";

        /// <summary>Explains a required event comment.</summary>
        internal const string Comment =
            "Occurrence comment, e.g. 'Sensor inspected'; maximum 4096 characters, empty text permitted.";

        /// <summary>Explains the distinction between no comment and an explicitly empty comment.</summary>
        internal const string OptionalComment =
            "Optional text, e.g. 'Maintenance'; omission uses the no-comment method, while '' explicitly sends " +
            "an empty comment. Maximum 4096 characters.";

        /// <summary>Explains localization.</summary>
        internal const string Locale =
            "Optional locale for the comment, e.g. 'en-US'; omission sends no locale rather than guessing one.";

        /// <summary>Explains event-notifier identity.</summary>
        internal const string NotifierId =
            "Event-notifier NodeId, e.g. 'i=2253' for Server; events are observed only from this selected source.";

        /// <summary>Explains finite observation windows.</summary>
        internal const string DurationMilliseconds =
            "Observation window in milliseconds, e.g. 1000; range 1..30000, subject to the host's lower ceiling.";

        /// <summary>Explains finite shelving duration.</summary>
        internal const string ShelvingTimeMilliseconds =
            "Positive finite shelving duration in milliseconds, e.g. 60000; the server's MaxTimeShelved applies.";

        /// <summary>Explains dialog response indexes.</summary>
        internal const string SelectedResponse =
            "Nonnegative index into the dialog's actual ResponseOptionSet, e.g. 0 for the first advertised option.";

        /// <summary>Explains explicit time-zone-bearing start instants.</summary>
        internal const string StartTime =
            "ISO 8601 start; Z/offset required.";

        /// <summary>Explains explicit time-zone-bearing end instants.</summary>
        internal const string EndTime =
            "ISO 8601 end; Z/offset required.";

        /// <summary>Explains raw-history bounding samples.</summary>
        internal const string ReturnBounds =
            "Include raw-history bounding samples.";

        /// <summary>Explains explicit retained-cursor ownership.</summary>
        internal const string RetainCursor =
            "Retain caller/session cursor (trusted caller/quota required); false releases continuation.";

        /// <summary>Explains at-time interpolation bounds.</summary>
        internal const string UseSimpleBounds =
            "Use simple ReadAtTime bounding values.";

        /// <summary>Explains aggregate capability selection.</summary>
        internal const string AggregateId =
            "Advertised aggregate NodeId, e.g. 'i=2342' from history_capabilities.";

        /// <summary>Explains bounded processed-history periods.</summary>
        internal const string ProcessingIntervalMilliseconds =
            "Finite positive interval in ms; host period limit applies.";

        /// <summary>Explains raw versus modified-history deletion.</summary>
        internal const string DeleteModified =
            "True deletes modified history; false deletes raw samples.";

        /// <summary>Explains opaque historical handles.</summary>
        internal const string Cursor =
            "Owned history cursor; not a server continuation point.";

        /// <summary>Explains flat DataValue declarations.</summary>
        internal const string Values =
            "Samples, e.g. [{type:'Int32',value:1,sourceTimestamp:'2026-01-01T00:00:00Z'}]. " +
            "Arrays use arrayLength/arrayElements.";

        /// <summary>Explains explicitly indexed primitive array elements.</summary>
        internal const string ArrayElements =
            "Cells, e.g. [{valueIndex:0,elementIndex:0,value:1}]; fill arrayLength. " +
            "valueIndex indexes values/events.";

        /// <summary>Explains flat namespace-qualified event selectors.</summary>
        internal const string Select =
            "Fields, e.g. [{browseName:'Time'}]; exact namespaceUri; paths for multi-segment names.";

        /// <summary>Explains flat multi-segment browse paths.</summary>
        internal const string Paths =
            "Segments, e.g. [{selectIndex:0,segmentIndex:0,name:'EnabledState'}]; " +
            "contiguous zero-based path indexes.";

        /// <summary>Explains flat event value cells and status ordering.</summary>
        internal const string Events =
            "Cells, e.g. [{eventIndex:0,fieldIndex:0,type:'ByteString',value:'AQ=='}]; " +
            "fill each select index once per event.";

        /// <summary>Explains standard annotation batches.</summary>
        internal const string Annotations =
            "Annotations, e.g. [{annotationTime:'2026-01-01T00:00:00Z',message:'Calibrated'}].";

        /// <summary>Explains explicit permission replacement.</summary>
        internal const string Permissions =
            "Declared role permissions, e.g. [{roleId:'i=15644',permissions:1}]. " +
            "An explicit [] clears them; omission is invalid and effective UserRolePermissions are not writable.";

        /// <summary>Explains ordered timestamp batches.</summary>
        internal const string Timestamps =
            "ISO 8601 instants with offsets, e.g. ['2026-01-01T00:00:00Z'].";

        /// <summary>Explains binary historical-event identities.</summary>
        internal const string EventIds =
            "Base64 EventIds, e.g. ['AQID']; not event names.";

        /// <summary>Explains role-instance selection.</summary>
        internal const string RoleId =
            "Existing RoleType NodeId from roles_list, e.g. 'ns=2;s=Maintenance'; namespace identity is preserved.";

        /// <summary>Explains creation names.</summary>
        internal const string RoleName =
            "New role's browse-name text, e.g. 'Maintenance'; maximum 256 characters, no implicit role mappings.";

        /// <summary>Explains explicit role namespaces.</summary>
        internal const string NamespaceUri =
            "Exact namespace URI for the new role, e.g. 'urn:plant:roles'; not a namespace index or display label.";

        /// <summary>Explains standard identity-rule discriminators.</summary>
        internal const string CriteriaType =
            "Part 18 identity criterion, e.g. 'UserName'; determines how the exact criteria text is interpreted.";

        /// <summary>Explains identity-mapping values.</summary>
        internal const string Criteria =
            "Exact identity-mapping value, e.g. 'alice' for UserName; maximum 2048 characters, not caller authority.";

        /// <summary>Explains application rule identity.</summary>
        internal const string ApplicationUri =
            "Exact application URI to add/remove, e.g. 'urn:plant:client'; this mapping does not connect to it.";

        /// <summary>Explains endpoint rule identity.</summary>
        internal const string EndpointUrl =
            "Exact URL in the endpoint rule, e.g. 'opc.tcp://server:4840'; no connection is opened by this mapping.";

        /// <summary>Explains endpoint rule modes.</summary>
        internal const string SecurityMode =
            "Security mode in the endpoint mapping, e.g. 'SignAndEncrypt'; does not change the active UA channel.";

        /// <summary>Explains endpoint rule policy URIs.</summary>
        internal const string SecurityPolicyUri =
            "Exact UA security-policy URI, e.g. 'http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256'.";

        /// <summary>Explains optional application exclusion updates.</summary>
        internal const string ApplicationsExclude =
            "Optional explicit exclusion flag for application rules, e.g. false; omission leaves it unchanged.";

        /// <summary>Explains optional endpoint exclusion updates.</summary>
        internal const string EndpointsExclude =
            "Optional explicit exclusion flag for endpoint rules, e.g. true; omission leaves it unchanged.";

        /// <summary>Explains optional custom configuration updates.</summary>
        internal const string CustomConfiguration =
            "Optional vendor custom-configuration flag, e.g. false; omission leaves the current value unchanged.";

        /// <summary>Explains the target user, distinct from the MCP caller.</summary>
        internal const string UserName =
            "Server user account to administer, e.g. 'alice'; this is the target account, never the MCP caller id.";

        /// <summary>Explains secret-safe password references.</summary>
        internal const string PasswordReference =
            "Authorized secret reference, e.g. 'alias:initial-password'; bound to UserManagement. Never plaintext.";

        /// <summary>Explains password-only optional modifications.</summary>
        internal const string NewPasswordReference =
            "Optional authorized replacement reference, e.g. 'alias:new-password'; omission leaves password unchanged.";

        /// <summary>Explains self-service current passwords.</summary>
        internal const string OldPasswordReference =
            "Authorized current-password secret reference, e.g. 'alias:current-password'; never a plaintext password.";

        /// <summary>Explains self-service replacement passwords.</summary>
        internal const string ReplacementPasswordReference =
            "Authorized new-password secret reference, e.g. 'alias:replacement-password'; never plaintext.";

        /// <summary>Explains exact account configuration bits.</summary>
        internal const string UserConfiguration =
            "Part 18 UserConfigurationMask bits, e.g. 0. For modification, omission is unchanged and 0 is an update.";

        /// <summary>Explains absent versus cleared descriptions.</summary>
        internal const string UserDescription =
            "Account description, e.g. 'Maintenance operator'; for modification omission is unchanged and '' clears.";
    }
}
