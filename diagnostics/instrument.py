import hashlib
import pathlib
import subprocess

HEAD = "fa65ac80dcb8bff2a1789569f482a64ba928a4f9"
assert subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip() == HEAD


def edit(path, changes):
    original = subprocess.check_output(["git", "show", HEAD + ":" + path])
    file = pathlib.Path(path)
    assert file.read_bytes() == original, f"Preimage mismatch: {path}"
    text = original.decode().replace("\r\n", "\n")
    for before, after in changes:
        assert text.count(before) == 1, f"Ambiguous insertion: {path}: {before[:90]}"
        text = text.replace(before, after)
    file.write_bytes(text.replace("\n", "\r\n").encode())
    print(path, hashlib.sha256(original).hexdigest(), hashlib.sha256(file.read_bytes()).hexdigest())


edit("tests/Opc.Ua.Test.Common/Logging.cs", [
    ("using var provider = new NUnitLoggerProvider(context, logLevel);\n"
     "#pragma warning disable CA2000 // ownership transfers to NUnitTelemetryContext via TelemetryContextBase\n"
     "            ILoggerFactory factory = Microsoft.Extensions.Logging.LoggerFactory\n"
     "                .Create(builder => builder.AddProvider(provider));",
     "using var provider = new NUnitLoggerProvider(context, logLevel);\n"
     "#pragma warning disable CA2000 // ownership transfers to NUnitTelemetryContext via TelemetryContextBase\n"
     "            ILoggerFactory factory = Microsoft.Extensions.Logging.LoggerFactory\n"
     "                .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(provider));"),
])

edit("src/Opc.Ua.Client/Subscription/SubscriptionManager.cs", [
    ("                        publishPending = true;\n",
     "                        publishPending = true;\n"
     "                        m_logger.QuietDiagnosticRequest(Index, handle, requestSequence, timeoutHint,\n"
     "                            acks.Count, Stopwatch.GetTimestamp());\n"
     "                        for (int i = 0; i < acks.Count; i++)\n"
     "                        {\n"
     "                            m_logger.QuietDiagnosticAck(\"send\", Index, handle,\n"
     "                                acks[i].SubscriptionId, acks[i].SequenceNumber, StatusCodes.Good);\n"
     "                        }\n"),
    ("                        publishPending = false;\n\n                        moreNotifications = response.MoreNotifications;",
     "                        publishPending = false;\n"
     "                        m_logger.QuietDiagnosticResponse(Index, handle, response.ResponseHeader.RequestHandle,\n"
     "                            response.SubscriptionId, response.NotificationMessage.SequenceNumber,\n"
     "                            Stopwatch.GetTimestamp());\n"
     "                        for (int i = 0; i < Math.Min(acks.Count, response.Results.Count); i++)\n"
     "                        {\n"
     "                            m_logger.QuietDiagnosticAck(\"result\", Index, handle,\n"
     "                                acks[i].SubscriptionId, acks[i].SequenceNumber, response.Results[i]);\n"
     "                        }\n\n"
     "                        moreNotifications = response.MoreNotifications;"),
    ("                                Interlocked.Increment(ref m_outer.m_badPublishRequestCount);\n",
     "                                int unknownCount = Interlocked.Increment(ref m_outer.m_badPublishRequestCount);\n"
     "                                m_logger.QuietDiagnosticUnknown(Index, handle, subscriptionId, unknownCount);\n"),
    ("                        Interlocked.Increment(ref m_outer.m_badPublishRequestCount);\n",
     "                        int badCount = Interlocked.Increment(ref m_outer.m_badPublishRequestCount);\n"
     "                        m_logger.QuietDiagnosticFailure(e, Index, handle, requestSequence,\n"
     "                            error.StatusCode, publishPending, badCount, Stopwatch.GetTimestamp());\n"),
    ("    internal static partial class SubscriptionManagerLog\n    {\n",
     """    internal static partial class SubscriptionManagerLog
    {
        /// <summary>
        /// Records diagnostic-only Publish dispatch correlation.
        /// </summary>
        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 40, Level = LogLevel.Information,
            Message = "QUIET_DIAG SEND Worker={Worker} Handle={Handle} Request={Request} Hint={Hint} " +
                "Acks={Acks} Ticks={Ticks}")]
        public static partial void QuietDiagnosticRequest(this ILogger logger,
            int worker, uint handle, long request, uint hint, int acks, long ticks);

        /// <summary>
        /// Records diagnostic-only Publish response correlation.
        /// </summary>
        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 41, Level = LogLevel.Information,
            Message = "QUIET_DIAG RESPONSE Worker={Worker} Handle={Handle} Echo={Echo} " +
                "Subscription={Subscription} Sequence={Sequence} Ticks={Ticks}")]
        public static partial void QuietDiagnosticResponse(this ILogger logger,
            int worker, uint handle, uint echo, uint subscription, uint sequence, long ticks);

        /// <summary>
        /// Records diagnostic-only acknowledgement request and result correlation.
        /// </summary>
        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 42, Level = LogLevel.Information,
            Message = "QUIET_DIAG ACK Phase={Phase} Worker={Worker} Handle={Handle} " +
                "Subscription={Subscription} Sequence={Sequence} Status={Status}")]
        public static partial void QuietDiagnosticAck(this ILogger logger,
            string phase, int worker, uint handle, uint subscription, uint sequence, StatusCode status);

        /// <summary>
        /// Records the diagnostic-only exception associated with a counter increment.
        /// </summary>
        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 43, Level = LogLevel.Error,
            Message = "QUIET_DIAG FAILURE Worker={Worker} Handle={Handle} Request={Request} " +
                "Status={Status} Pending={Pending} BadCount={BadCount} Ticks={Ticks}")]
        public static partial void QuietDiagnosticFailure(this ILogger logger, Exception exception,
            int worker, uint handle, long request, StatusCode status, bool pending, int badCount, long ticks);

        /// <summary>
        /// Records the diagnostic-only unknown-subscription counter increment.
        /// </summary>
        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 44, Level = LogLevel.Information,
            Message = "QUIET_DIAG UNKNOWN Worker={Worker} Handle={Handle} " +
                "Subscription={Subscription} BadCount={BadCount}")]
        public static partial void QuietDiagnosticUnknown(this ILogger logger,
            int worker, uint handle, uint subscription, int badCount);
"""),
])

edit("src/Opc.Ua.Core/Stack/Server/EndpointBase.cs", [
    ("            fault.ResponseHeader.ServiceResult = result.Code;\n",
     "            fault.ResponseHeader.ServiceResult = result.Code;\n"
     "            logger.QuietDiagnosticFault(exception, fault.ResponseHeader.RequestHandle, result.StatusCode);\n"),
    ("    internal static partial class EndpointBaseLog\n    {\n",
     """    internal static partial class EndpointBaseLog
    {
        /// <summary>
        /// Records a diagnostic-only service fault with the echoed request handle and original exception.
        /// </summary>
        [LoggerMessage(EventId = CoreEventIds.EndpointBase + 4, Level = LogLevel.Error,
            Message = "QUIET_DIAG SERVER_FAULT Handle={Handle} Status={Status}")]
        public static partial void QuietDiagnosticFault(
            this ILogger logger, Exception exception, uint handle, StatusCode status);
"""),
])

edit("src/Opc.Ua.Server/Subscription/SubscriptionManager.cs", [
    ("                    ISubscriptionPublishPipeline subscription = await queue.PublishAsync(\n",
     "                    m_logger.QuietDiagnosticPark(\"park\", context.ClientHandle,\n"
     "                        context.OperationDeadline, 0);\n"
     "                    ISubscriptionPublishPipeline subscription = await queue.PublishAsync(\n"),
    ("                        parkSink,\n                        cancellationToken).ConfigureAwait(false);\n",
     "                        parkSink,\n                        cancellationToken).ConfigureAwait(false);\n"
     "                    m_logger.QuietDiagnosticPark(\"assigned\", context.ClientHandle,\n"
     "                        context.OperationDeadline, subscription?.Id ?? 0);\n"),
    ("    internal static partial class SubscriptionManagerLog\n    {\n",
     """    internal static partial class SubscriptionManagerLog
    {
        /// <summary>
        /// Records diagnostic-only request parking and assignment against the operation deadline.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 34, Level = LogLevel.Information,
            Message = "QUIET_DIAG SERVER_PARK Phase={Phase} Handle={Handle} Deadline={Deadline:O} " +
                "Subscription={Subscription}")]
        public static partial void QuietDiagnosticPark(
            this ILogger logger, string phase, uint handle, DateTime deadline, uint subscription);
"""),
])

edit("tests/Opc.Ua.Subscriptions.Tests/AcknowledgementProviderIntegrationTests.cs", [
    ("                session.Confirm(handle);\n                await WaitForAsync(\n",
     "                TestContext.Progress.WriteLine($\"QUIET_DIAG CONFIRM Handle={handle} Workers={workers}\");\n"
     "                session.Confirm(handle);\n                await WaitForAsync(\n"),
    ("                Assert.That(observer.Outcomes.Count(outcome => outcome.Handle == handle), Is.EqualTo(1));\n"
     "                Assert.That(engine.BadPublishRequestCount, Is.Zero);",
     "                foreach (AcknowledgementOutcome outcome in observer.Outcomes)\n"
     "                {\n"
     "                    TestContext.Progress.WriteLine(\n"
     "                        $\"QUIET_DIAG OUTCOME Handle={outcome.Handle} Disposition={outcome.Disposition}\");\n"
     "                }\n"
     "                TestContext.Progress.WriteLine(\n"
     "                    $\"QUIET_DIAG ASSERT BadCount={engine.BadPublishRequestCount} KeepAlives={handler.KeepAliveCount}\");\n"
     "                Assert.That(observer.Outcomes.Count(outcome => outcome.Handle == handle), Is.EqualTo(1));\n"
     "                Assert.That(engine.BadPublishRequestCount, Is.Zero);"),
])
