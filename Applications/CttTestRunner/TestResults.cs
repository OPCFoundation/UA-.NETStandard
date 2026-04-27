/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Opc.Ua.CttTestRunner
{
    /// <summary>
    /// Status of a test execution.
    /// </summary>
    public enum TestStatus
    {
        Passed,
        Failed,
        Skipped,
        Error
    }

    /// <summary>
    /// Result of a single test execution.
    /// </summary>
    public sealed class TestResult
    {
        public string TestFile { get; set; } = "";
        public TestStatus Status { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Logs { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Collection of test results with summary and export capabilities.
    /// </summary>
    public sealed class TestResultCollection
    {
        private readonly List<TestResult> _results = new();

        public void Add(TestResult result) => _results.Add(result);

        public int Total => _results.Count;
        public int Passed => _results.Count(r => r.Status == TestStatus.Passed);
        public int Failed => _results.Count(r => r.Status == TestStatus.Failed);
        public int Skipped => _results.Count(r => r.Status == TestStatus.Skipped);
        public int Errors => _results.Count(r => r.Status == TestStatus.Error);

        public void PrintSummary(Microsoft.Extensions.Logging.ILogger logger)
        {
            logger.LogInformation("");
            logger.LogInformation("═══════════════════════════════════════");
            logger.LogInformation("  Test Results Summary");
            logger.LogInformation("═══════════════════════════════════════");
            logger.LogInformation("  Total:   {Total}", Total);
            logger.LogInformation("  Passed:  {Passed} ✅", Passed);
            logger.LogInformation("  Failed:  {Failed} ❌", Failed);
            logger.LogInformation("  Skipped: {Skipped} ⏭️", Skipped);
            logger.LogInformation("  Errors:  {Errors} 💥", Errors);
            logger.LogInformation("═══════════════════════════════════════");

            if (Failed > 0 || Errors > 0)
            {
                logger.LogInformation("");
                logger.LogInformation("Failed/Error tests:");
                foreach (var r in _results.Where(r => r.Status is TestStatus.Failed or TestStatus.Error))
                {
                    string name = Path.GetFileNameWithoutExtension(r.TestFile);
                    logger.LogInformation("  {Status} {Name}: {Error}",
                        r.Status == TestStatus.Failed ? "❌" : "💥",
                        name,
                        r.ErrorMessage ?? string.Join("; ", r.Errors));
                }
            }
        }

        public void WriteXml(string path)
        {
            var settings = new XmlWriterSettings { Indent = true };
            using var writer = XmlWriter.Create(path, settings);

            writer.WriteStartDocument();
            writer.WriteStartElement("CttTestResults");
            writer.WriteAttributeString("timestamp", DateTime.UtcNow.ToString("o"));
            writer.WriteAttributeString("total", Total.ToString());
            writer.WriteAttributeString("passed", Passed.ToString());
            writer.WriteAttributeString("failed", Failed.ToString());
            writer.WriteAttributeString("skipped", Skipped.ToString());
            writer.WriteAttributeString("errors", Errors.ToString());

            foreach (var r in _results)
            {
                writer.WriteStartElement("Test");
                writer.WriteAttributeString("file", r.TestFile);
                writer.WriteAttributeString("status", r.Status.ToString());
                writer.WriteAttributeString("duration", r.Duration.TotalMilliseconds.ToString("F1"));
                if (r.ErrorMessage != null)
                {
                    writer.WriteElementString("Error", r.ErrorMessage);
                }
                foreach (var log in r.Logs)
                {
                    writer.WriteElementString("Log", log);
                }
                foreach (var warn in r.Warnings)
                {
                    writer.WriteElementString("Warning", warn);
                }
                foreach (var err in r.Errors)
                {
                    writer.WriteElementString("Error", err);
                }
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
    }
}
