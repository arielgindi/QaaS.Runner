using NUnit.Framework;

namespace QaaS.Runner.Sessions.Tests;

/// <summary>
/// Assembly-level teardown. Several builder tests intentionally call
/// <c>Build(...)</c> with placeholder Kafka configurations to assert builder wiring.
/// Each <c>Build</c> constructs a real <c>Confluent.Kafka.IConsumer</c>/<c>IProducer</c>,
/// which spawns librdkafka background threads at construction time. The action
/// classes now dispose the underlying reader/sender, and the builder tests now
/// dispose the constructed actions, so the testhost should shut down cleanly. This
/// teardown forces a couple of finalizer passes to release any remaining native
/// handles before the runtime tears down.
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
    }
}
