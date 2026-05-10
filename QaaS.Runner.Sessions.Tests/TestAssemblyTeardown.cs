using NUnit.Framework;

namespace QaaS.Runner.Sessions.Tests;

/// <summary>
/// Assembly-level teardown. Several builder tests intentionally call
/// <c>Build(...)</c> with placeholder Kafka configurations to assert builder wiring.
/// Each <c>Build</c> constructs a real <c>Confluent.Kafka.IConsumer</c>/<c>IProducer</c>,
/// which spawns <c>librdkafka</c> background threads at construction time.
/// Even after the action classes <c>Dispose</c> their underlying readers/senders,
/// <c>librdkafka</c> keeps DNS-resolution and broker-bootstrap threads alive past
/// the assertion phase. Tried strategies:
/// <list type="bullet">
/// <item><description><c>Environment.Exit(0)</c> — runs managed finalizers; librdkafka SafeHandle
/// finalizers themselves block on the native threads, so the hang moves rather than resolving.</description></item>
/// <item><description><c>Process.Kill()</c> — skips finalizers and terminates immediately. Exit
/// code is non-zero but the cobertura artifact is already flushed by then. The CI workflow
/// tolerates non-zero exit when the coverage XML is present.</description></item>
/// </list>
///
/// Production assemblies are unaffected: the runner shuts down via its own dispose chain.
/// Only the <c>Sessions.Tests</c> testhost is short-circuited so CI can complete.
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

        try
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
        catch
        {
            // Fallback if Kill is denied — let the runtime exit normally.
        }
    }
}
