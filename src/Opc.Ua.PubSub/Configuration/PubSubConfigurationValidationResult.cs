// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Aggregate result of running
    /// <see cref="PubSubConfigurationValidator.Validate(PubSubConfigurationDataType)"/>.
    /// </summary>
    public sealed class PubSubConfigurationValidationResult
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="PubSubConfigurationValidationResult"/>.
        /// </summary>
        /// <param name="issues">All issues discovered.</param>
        public PubSubConfigurationValidationResult(
            IEnumerable<PubSubConfigurationIssue> issues)
        {
            if (issues is null)
            {
                throw new ArgumentNullException(nameof(issues));
            }
            Issues = issues.ToArrayOf();
        }

        /// <summary>
        /// Discovered issues. Never <see langword="null"/>.
        /// </summary>
        public ArrayOf<PubSubConfigurationIssue> Issues { get; }

        /// <summary>
        /// <see langword="true"/> when no error-severity issue was
        /// raised. Info and warning issues are tolerated.
        /// </summary>
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity == PubSubConfigurationIssueSeverity.Error)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Throws a <see cref="PubSubConfigurationException"/> if any
        /// error-severity issue is present.
        /// </summary>
        /// <exception cref="PubSubConfigurationException">
        /// At least one error-severity issue was raised.
        /// </exception>
        public void ThrowIfInvalid()
        {
            if (!IsValid)
            {
                throw new PubSubConfigurationException([.. Issues]);
            }
        }
    }
}
