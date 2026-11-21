// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.WotCon
{
    /// <summary>
    /// Centralizes the supported WoT document-kind identifiers.
    /// </summary>
    internal static class WotDocumentKinds
    {
        /// <summary>
        /// Determines whether the kind denotes a Thing Description or Thing Model document.
        /// </summary>
        /// <param name="kind">The wo t document kind enum.</param>
        public static bool IsDocument(WoTDocumentKindEnum kind)
        {
            return kind is WoTDocumentKindEnum.ThingDescription or WoTDocumentKindEnum.ThingModel;
        }

        /// <summary>
        /// Returns the kind if it denotes a document, otherwise throws.
        /// </summary>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="parameterName">The parameter name.</param>
        public static WoTDocumentKindEnum RequireDocument(WoTDocumentKindEnum kind, string parameterName)
        {
            if (!IsDocument(kind))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, kind, "A document kind must be ThingDescription or ThingModel, not a selector.");
            }
            return kind;
        }
    }
}
