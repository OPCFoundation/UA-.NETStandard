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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Samples
{
    internal enum RepositorySampleOutputStream
    {
        StandardOutput,
        StandardError
    }

    internal sealed record RepositorySampleOutputLine(RepositorySampleOutputStream Stream, string Text);

    /// <summary>
    /// Bounded framing before redaction: even a child that never emits a newline
    /// cannot grow the buffer. Raw output, credentials and configuration paths
    /// are never written to the host log or to a workspace file.
    /// </summary>
    internal sealed class RepositorySampleOutput
    {
        public RepositorySampleOutput(ArrayOf<string> privatePaths)
        {
            m_privatePaths = privatePaths;
        }

        public ArrayOf<RepositorySampleOutputLine> Snapshot
        {
            get
            {
                lock (m_gate)
                {
                    return m_lines.ToArrayOf();
                }
            }
        }

        public Task CaptureFailure => m_captureFailure.Task;

        public void Append(RepositorySampleOutputStream stream, ReadOnlySpan<char> chunk)
        {
            lock (m_gate)
            {
                Frame frame = GetFrame(stream);
                foreach (char character in chunk)
                {
                    if (character == '\n')
                    {
                        Flush(stream, frame);
                    }
                    else if (character != '\r')
                    {
                        frame.ObservePemMarker(character);
                        if (frame.Text.Length == MaximumLineLength)
                        {
                            frame.Oversized = true;
                        }
                        else
                        {
                            frame.Text.Append(char.IsControl(character) ? ' ' : character);
                        }
                    }
                }
            }
        }

        public void Complete(RepositorySampleOutputStream stream)
        {
            lock (m_gate)
            {
                Frame frame = GetFrame(stream);
                if (frame.Text.Length != 0 || frame.Oversized)
                {
                    Flush(stream, frame);
                }
            }
        }

        public void ReportCaptureFailure(RepositorySampleOutputStream stream)
        {
            Append(stream, "\n[sample output capture failed]\n");
            m_captureFailure.TrySetResult();
        }

        private Frame GetFrame(RepositorySampleOutputStream stream)
        {
            return stream switch
            {
                RepositorySampleOutputStream.StandardOutput => m_stdout,
                RepositorySampleOutputStream.StandardError => m_stderr,
                _ => throw new ArgumentOutOfRangeException(nameof(stream))
            };
        }

        private void Flush(RepositorySampleOutputStream stream, Frame frame)
        {
            string text = frame.Text.ToString();
            bool beginsPem = frame.BeginsPem;
            bool endsPem = frame.EndsPem;
            bool sensitive = frame.InPem ||
                beginsPem ||
                (text.Contains("://", StringComparison.Ordinal) && text.Contains('@', StringComparison.Ordinal));
            frame.InPem = (frame.InPem || beginsPem) && !endsPem;
            foreach (string keyword in s_sensitiveWords)
            {
                sensitive |= text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            }
            foreach (string path in m_privatePaths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    sensitive |= text.Contains(path, StringComparison.OrdinalIgnoreCase) ||
                        text.Contains(path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
                }
            }
            text = frame.Oversized
                ? "[oversized sample output omitted]"
                : sensitive ? "[sensitive sample output omitted]" : text;
            if (m_lines.Count == MaximumLines)
            {
                m_lines.Dequeue();
            }
            m_lines.Enqueue(new RepositorySampleOutputLine(stream, text));
            frame.Text.Clear();
            frame.Oversized = false;
            frame.ResetPemMarker();
        }

        internal const int MaximumLines = 128;
        internal const int MaximumLineLength = 512;

        private static readonly ArrayOf<string> s_sensitiveWords =
        [
            "password", "secret", "token", "credential", "authorization", "bearer",
            "private key", "privatekey", "thumbprint", "certificate", "pki", "connectionstring"
        ];

        private readonly ArrayOf<string> m_privatePaths;
        private readonly Queue<RepositorySampleOutputLine> m_lines = new();
        private readonly Frame m_stdout = new();
        private readonly Frame m_stderr = new();
        private readonly Lock m_gate = new();

        private readonly TaskCompletionSource m_captureFailure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class Frame
        {
            public StringBuilder Text { get; } = new(MaximumLineLength);

            public bool Oversized { get; set; }

            public bool InPem { get; set; }

            public bool BeginsPem { get; private set; }

            public bool EndsPem { get; private set; }

            public void ObservePemMarker(char character)
            {
                char upper = char.ToUpperInvariant(character);
                m_pemWindow[m_cursor] = upper;
                m_cursor = (m_cursor + 1) % m_pemWindow.Length;
                BeginsPem |= upper == 'N' && Matches("-----BEGIN");
                EndsPem |= upper == 'D' && Matches("-----END");
            }

            public void ResetPemMarker()
            {
                Array.Clear(m_pemWindow);
                m_cursor = 0;
                BeginsPem = false;
                EndsPem = false;
            }

            private bool Matches(string marker)
            {
                int start = (m_cursor + m_pemWindow.Length - marker.Length) % m_pemWindow.Length;
                for (int index = 0; index < marker.Length; index++)
                {
                    if (m_pemWindow[(start + index) % m_pemWindow.Length] != marker[index])
                    {
                        return false;
                    }
                }
                return true;
            }

            private readonly char[] m_pemWindow = new char[10];
            private int m_cursor;
        }
    }
}
