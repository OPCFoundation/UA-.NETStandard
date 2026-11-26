// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// A content-equatable additional file, without retaining its host snapshot.
    /// </summary>
    internal sealed class AdditionalTextSnapshot : AdditionalText, IEquatable<AdditionalTextSnapshot>
    {
        /// <summary>
        /// Captures a file's path and text once for downstream generation.
        /// </summary>
        internal AdditionalTextSnapshot(AdditionalText text, CancellationToken cancellationToken)
        {
            Path = text.Path;
            try
            {
                m_text = text.GetText(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_readFailure = ExceptionDispatchInfo.Capture(ex);
            }
        }

        /// <inheritdoc/>
        public override string Path { get; }

        /// <inheritdoc/>
        public override SourceText? GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_readFailure?.Throw();
            return m_text;
        }

        /// <inheritdoc/>
        public bool Equals(AdditionalTextSnapshot? other)
        {
            return ReferenceEquals(this, other) || (other != null &&
                m_readFailure == null && other.m_readFailure == null &&
                string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                (ReferenceEquals(m_text, other.m_text) ||
                    (m_text != null && other.m_text != null &&
                        Equals(m_text.Encoding, other.m_text.Encoding) &&
                        m_text.ChecksumAlgorithm == other.m_text.ChecksumAlgorithm &&
                        m_text.ContentEquals(other.m_text))));
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AdditionalTextSnapshot other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Path);
        }

        private readonly SourceText? m_text;
        private readonly ExceptionDispatchInfo? m_readFailure;
    }
}
