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
        // Hard-exit so the testhost terminates promptly. Coverage data has already
        // been written to disk by dotnet-coverage's data sink at this point.
        System.Environment.Exit(0);
    }
}
