using NUnit.Framework;

namespace QaaS.Runner.Sessions.Tests;

/// <summary>
/// Assembly-level teardown. Several builder tests intentionally call
/// <c>Build(...)</c> with placeholder Kafka configurations to assert builder wiring.
/// Each <c>Build</c> constructs a real <c>Confluent.Kafka.IConsumer</c>/<c>IProducer</c>,
/// which spawns librdkafka background threads at construction time. Even with
/// explicit <see cref="System.IDisposable.Dispose"/> calls and forced finalizer
/// passes, some native threads linger long enough that vstest's
/// <c>--blame-hang-timeout</c> falsely fires during testhost shutdown
/// (CI run 25622904664). After all tests have completed and assertions are
/// flushed, hard-exit the testhost so CI does not flake.
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

        System.Environment.Exit(0);
    }
}
