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
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Alarms;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class AmbCompanionProvider
    {
        private static CompanionOperation FindTask(string operationId)
        {
            foreach (CompanionOperation operation in s_tasks)
            {
                if (operation.Id == operationId)
                {
                    return operation;
                }
            }
            throw new ServiceResultException(StatusCodes.BadNotSupported, "Select a supported prepared AMB task.");
        }

        private static MutationInput ParseTaskInput(
            CompanionContext context, CompanionTarget target, CompanionOperation operation,
            ArrayOf<CompanionValue> inputs)
        {
            CellCompanionSupport.ValidateTarget(context, target, "amb");
            CompanionInputContract.ValidateShape(operation, inputs);
            foreach (CompanionValue field in inputs)
            {
                if (!StatusCode.IsGood(field.Quality))
                {
                    throw new ArgumentException("Task inputs must have Good quality.", nameof(inputs));
                }
            }
            var payload = new MutationInput(
                operation, CompanionInputContract.Snapshot(inputs, context.Session.MessageContext));
            switch (operation.Id)
            {
                case "set-asset-id":
                    ValidateText(payload.Text("assetId"), 256, allowEmpty: true);
                    break;
                case "add-link":
                    ValidateLinkUri(payload.Text("uri"));
                    QualifiedName name = payload.Name;
                    ValidateText(name.Name, 128, allowEmpty: false);
                    if (name.NamespaceIndex == 0 ||
                        name.NamespaceIndex >= context.Session.NamespaceUris.Count ||
                        name.Name is "." or ".." ||
                        name.Name!.IndexOfAny(['/', '\\', ':']) >= 0 ||
                        name.Name.Contains('%', StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Use one local, namespace-qualified browse name, not a path.");
                    }
                    ValidateLocalizedText(payload.DisplayName, 256);
                    ValidateLocalizedText(payload.Description, 2048);
                    break;
                case "write-link":
                    ValidateNodeId(context, payload.Node("linkId"));
                    ValidateLinkUri(payload.Text("uri"));
                    break;
                case "remove-link":
                    ValidateNodeId(context, payload.Node("linkId"));
                    break;
                case "acknowledge-health":
                case "acknowledge-maintenance":
                    ValidateNodeId(context, payload.Node("conditionId"));
                    if (payload.EventId.IsNull || payload.EventId.Length is 0 or > AlarmLimits.EventIdLength)
                    {
                        throw new ArgumentException("Use this condition's exact, bounded current EventId.");
                    }
                    ValidateText(payload.Text("comment"), AlarmLimits.TextLength, allowEmpty: true);
                    break;
            }
            if (payload.Review.Length > 7600)
            {
                throw new ArgumentException("The AMB request exceeds the bounded review size.", nameof(inputs));
            }
            return payload;
        }

        private static void ValidateLinkUri(string text)
        {
            ValidateText(text, 2048, allowEmpty: false);
            if (text.Contains('\\', StringComparison.Ordinal) ||
                !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("http" or "https") ||
                string.IsNullOrEmpty(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                uri.IsFile ||
                uri.IsUnc)
            {
                throw new ArgumentException(
                    "Use an absolute HTTP(S) documentation URI without credentials, query text or local paths.");
            }
            foreach (char character in Uri.UnescapeDataString(text))
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException("Documentation URIs cannot contain encoded control characters.");
                }
            }
        }

        private static void ValidateNodeId(CompanionContext context, NodeId nodeId)
        {
            if (nodeId.IsNull ||
                nodeId.NamespaceIndex >= context.Session.NamespaceUris.Count ||
                nodeId.ToString().Length > 1024)
            {
                throw new ArgumentException("A bounded local node identifier in the current session is required.");
            }
        }

        private static void ValidateLocalizedText(LocalizedText value, int maximum)
        {
            ValidateText(value.Text ?? string.Empty, maximum, allowEmpty: true);
            ValidateText(value.Locale ?? string.Empty, 64, allowEmpty: true);
        }

        private static void ValidateText(string? text, int maximum, bool allowEmpty)
        {
            if (text is null ||
                text.Length > maximum ||
                (!allowEmpty && text.Length == 0) ||
                !string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("The AMB field is missing, too long or has surrounding whitespace.");
            }
            foreach (char character in text)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException("AMB task fields cannot contain control characters.");
                }
            }
        }

        /// <summary>
        /// Typed scalar snapshots. Neither the caller's input collection nor mutable byte storage is retained.
        /// </summary>
        private sealed class MutationInput(CompanionOperation operation, ArrayOf<CompanionValue> inputs)
            : CompanionTaskInput
        {
            /// <inheritdoc/>
            public override string Review => operation.Id switch
            {
                "set-asset-id" => Text("assetId").Length == 0 ? "Clear the asset's assigned AssetId." :
                    "Assign AssetId: " + Text("assetId"),
                "add-link" => $"Add documentation link {Name}: {Text("uri")}\n" +
                    $"Display name: {DisplayName}\nDescription: {Description}\nStore metadata only; never fetch the URI.",
                "write-link" => $"Replace the URI of {Node("linkId")}: {Text("uri")}\n" +
                    "Store metadata only; never fetch the URI.",
                "remove-link" => $"Remove the verified user-created documentation link {Node("linkId")}.",
                _ => $"Acknowledge only {Node("conditionId")} EventId {EventId.ToBase64()}.\n" +
                    $"Comment: {Text("comment")}\nNo acknowledgement of a newer event or another branch."
            };

            /// <summary>
            /// Gets the privately owned inputs for the policy's additional snapshot.
            /// </summary>
            public ArrayOf<CompanionValue> Inputs { get; } = inputs;

            /// <summary>
            /// Gets the exact browse name of a new link.
            /// </summary>
            public QualifiedName Name => Value("browseName").TryGetValue(out QualifiedName value) ? value : default;

            /// <summary>
            /// Gets the new link's display name.
            /// </summary>
            public LocalizedText DisplayName =>
                Value("displayName").TryGetValue(out LocalizedText value) ? value : default;

            /// <summary>
            /// Gets the new link's description.
            /// </summary>
            public LocalizedText Description =>
                Value("description").TryGetValue(out LocalizedText value) ? value : default;

            /// <summary>
            /// Gets the copied event identity.
            /// </summary>
            public ByteString EventId => Value("eventId").TryGetValue(out ByteString value) ? value : default;

            /// <summary>
            /// Gets an exactly typed text field.
            /// </summary>
            /// <exception cref="ArgumentException"></exception>
            public string Text(string name)
            {
                return Value(name).TryGetValue(out string? value) && value is not null ? value :
                    throw new ArgumentException("A string task field is required.");
            }

            /// <summary>
            /// Gets an exactly typed node field.
            /// </summary>
            /// <exception cref="ArgumentException"></exception>
            public NodeId Node(string name)
            {
                return Value(name).TryGetValue(out NodeId value) ? value :
                    throw new ArgumentException("A NodeId task field is required.");
            }

            private Variant Value(string name)
            {
                foreach (CompanionValue input in Inputs)
                {
                    if (input.Name == name)
                    {
                        return input.Value;
                    }
                }
                return Variant.Null;
            }
        }

        /// <summary>
        /// Extends the shared session/identity/namespace stamp with borrowed-session and peer identity.
        /// </summary>
        private sealed class MutationSession
        {
            /// <summary>
            /// Captures the exact borrowed session and its non-secret peer evidence.
            /// </summary>
            public MutationSession(CompanionContext context, CompanionOperationDraft draft)
            {
                m_session = context.Session;
                m_messageContext = context.Session.MessageContext;
                m_certificate = context.Session.Endpoint.ServerCertificate.Copy();
                m_draft = draft;
            }

            /// <summary>
            /// Rejects replacement sessions, remapping, identity changes, peer changes and expiry.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            public void RequireCurrent(CompanionContext context, TimeProvider timeProvider)
            {
                if (!ReferenceEquals(context.Session, m_session) ||
                    !ReferenceEquals(context.Session.MessageContext, m_messageContext) ||
                    context.Session.Endpoint.ServerCertificate != m_certificate ||
                    !m_draft.Matches(context.Session, timeProvider.GetUtcNow()))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The AMB session, peer, identity or namespace metadata changed, or preparation expired.");
                }
            }

            private readonly ISession m_session;
            private readonly IServiceMessageContext m_messageContext;
            private readonly ByteString m_certificate;
            private readonly CompanionOperationDraft m_draft;
        }

        /// <summary>
        /// One provider-bound preparation, consumed before cancellation or any asynchronous execution work.
        /// </summary>
        private sealed class PreparedMutation(
            AmbCompanionProvider owner, CompanionOperationDraft draft, MutationSession session,
            MutationInput payload, TaskEvidence evidence) : CompanionTaskInput
        {
            /// <inheritdoc/>
            public override string Review => Payload.Review +
                "\nExact asset/link/event identity and old metadata are pinned. " +
                "Both alias versions must remain available. Checks are not an atomic compare-and-swap. " +
                "Run consumes this preparation once; never replay an unknown outcome.";

            /// <summary>
            /// Gets the shared immutable policy and session evidence.
            /// </summary>
            public CompanionOperationDraft Draft { get; } = draft;

            /// <summary>
            /// Gets the copied typed input.
            /// </summary>
            public MutationInput Payload { get; } = payload;

            /// <summary>
            /// Gets the exact source and old-value evidence.
            /// </summary>
            public TaskEvidence Evidence { get; } = evidence;

            /// <summary>
            /// Consumes even a canceled, mismatched or subsequently failed execution.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            /// <exception cref="ArgumentException"></exception>
            public void Consume(AmbCompanionProvider provider, CompanionTarget target, string operationId)
            {
                if (Interlocked.Exchange(ref m_consumed, 1) != 0)
                {
                    throw new InvalidOperationException("This AMB preparation was consumed. Do not replay it.");
                }
                if (!ReferenceEquals(provider, owner) || target != Draft.Target || operationId != Draft.Operation.Id)
                {
                    throw new ArgumentException("The preparation belongs to another AMB provider, target or task.");
                }
            }

            /// <summary>
            /// Checks both the session stamp and the granted, potentially shorter expiry.
            /// </summary>
            /// <exception cref="UnauthorizedAccessException"></exception>
            public void RequireCurrent(CompanionContext context, TimeProvider timeProvider)
            {
                session.RequireCurrent(context, timeProvider);
                if (!Draft.Matches(context.Session, timeProvider.GetUtcNow()))
                {
                    throw new UnauthorizedAccessException("The prepared AMB deployment grant expired.");
                }
            }

            private int m_consumed;
        }

        private static readonly ArrayOf<CompanionOperation> s_tasks =
        [
            new("set-asset-id", "Set or clear AssetId", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs =
                [
                    new("assetId", "AssetId", BuiltInType.String,
                        "At most 256 characters; empty clears the assigned ID.", Required: false)
                ]
            },
            new("add-link", "Add user documentation link", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs =
                [
                    new("uri", "Documentation URI", BuiltInType.String,
                        "Absolute HTTP(S), no credentials or query text. Stored, never fetched."),
                    new("browseName", "Link browse name", BuiltInType.QualifiedName,
                        "One nonempty name in a current nonzero namespace, not a path."),
                    new("displayName", "Display name", BuiltInType.LocalizedText,
                        "Optional localized label, at most 256 characters.", Required: false),
                    new("description", "Description", BuiltInType.LocalizedText,
                        "Optional localized description, at most 2048 characters.", Required: false)
                ]
            },
            new("write-link", "Edit documentation URI", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs =
                [
                    new("linkId", "Link NodeId", BuiltInType.NodeId, "Select a writable link of this asset's AddIn."),
                    new("uri", "Documentation URI", BuiltInType.String,
                        "Absolute HTTP(S), no credentials or query text. Stored, never fetched.")
                ]
            },
            new("remove-link", "Remove user documentation link", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs =
                [
                    new("linkId", "Link NodeId", BuiltInType.NodeId,
                        "Select this asset's link with a current true user-link marker.")
                ]
            },
            new("acknowledge-health", "Acknowledge current health event", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = AcknowledgementInputs()
            },
            new("acknowledge-maintenance", "Acknowledge finished maintenance", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = AcknowledgementInputs()
            }
        ];

        private static ArrayOf<CompanionInputDefinition> AcknowledgementInputs()
        {
            return
            [
                new("conditionId", "Condition NodeId", BuiltInType.NodeId,
                    "A retained, unacknowledged current condition owned by the selected asset."),
                new("eventId", "Exact EventId", BuiltInType.ByteString,
                    "Copy the current event's ByteString. A newer event requires a new preparation."),
                new("comment", "Acknowledgement comment", BuiltInType.String,
                    "At most 2048 characters.", Required: false)
            ];
        }
    }
}
