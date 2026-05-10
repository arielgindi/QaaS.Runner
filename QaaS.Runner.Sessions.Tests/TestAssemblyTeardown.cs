using NUnit.Framework;

namespace QaaS.Runner.Sessions.Tests;

/// <summary>
/// Assembly-level teardown. Several builder tests intentionally call
/// <c>Build(...)</c> with placeholder Kafka configurations to assert builder wiring.
/// Each <c>Build</c> constructs a real <c>Confluent.Kafka.IConsumer</c>/<c>IProducer</c>,
/// which spawns <c>librdkafka</c> background threads at construction time.
/// Even after the action classes <c>Dispose</c> their underlying readers/senders,
/// <c>librdkafka</c> keeps DNS-resolution and broker-bootstrap threads alive past
/// the assertion phase, which causes vstest's <c>--blame-hang-timeout</c> to fire
/// after all tests have already passed (CI run 25632684123: 377 tests passed in
/// 21s, blame fired 5 min later, Test Run Aborted with exit code 1).
///
/// Production assemblies are unaffected by this teardown: the runner shuts down
/// via its own dispose chain. Only the <c>Sessions.Tests</c> testhost is short-
/// circuited so CI accepts the already-emitted coverage file.
/// </summary>
[SetUpFixture]
public sealed class TestAssemblyTeardown
{
    [OneTimeTearDown]
    public void Cleanup()
    {
        for (var i = 0; i < 3; i++)
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        }

        // librdkafka native threads cannot be forcibly joined from managed code
        // and keep the process alive long after every NUnit assertion has flushed.
        // Environment.Exit() runs finalizers — librdkafka's native finalizers
        // themselves block on those threads, so the hang just moves. Use
        // Process.Kill() which skips finalizers and terminates immediately.
        // Coverage data has already been written to disk by dotnet-coverage's
        // per-testhost data sink before this teardown runs.
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
        catch
        {
            // Fallback if Kill is denied for any reason — let the runtime exit
            // normally; the only consequence is a slow shutdown.
        }
    }
}
