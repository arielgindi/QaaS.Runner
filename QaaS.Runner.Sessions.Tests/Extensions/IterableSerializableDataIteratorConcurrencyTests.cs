using QaaS.Framework.Policies.Exceptions;
using QaaS.Runner.Sessions.Extensions;
using NUnit.Framework;

namespace QaaS.Runner.Sessions.Tests.Extensions;

[TestFixture]
public class IterableSerializableDataIteratorConcurrencyTests
{
    [Test]
    public void ApplyToAll_WithParallelism_BoundsConcurrentBodyExecutions()
    {
        const int parallelism = 4;
        const int itemCount = 16;
        var activeBodies = 0;
        var maxObservedConcurrency = 0;
        using var releaseBodies = new SemaphoreSlim(0, itemCount);
        var ceilingReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var iterator = new IterableSerializableDataIterator(null, null);

        var applyTask = Task.Run(() =>
            iterator.ApplyToAll(
                Enumerable.Range(0, itemCount),
                _ =>
                {
                    var active = Interlocked.Increment(ref activeBodies);
                    RecordMax(ref maxObservedConcurrency, active);

                    if (active == parallelism)
                        ceilingReached.TrySetResult();

                    releaseBodies.Wait();
                    Interlocked.Decrement(ref activeBodies);
                },
                parallelism));

        var reachedCeiling = ceilingReached.Task.Wait(TimeSpan.FromSeconds(10));
        releaseBodies.Release(itemCount);
        applyTask.GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(reachedCeiling, Is.True);
            Assert.That(maxObservedConcurrency, Is.EqualTo(parallelism));
            Assert.That(maxObservedConcurrency, Is.LessThanOrEqualTo(parallelism));
        });
    }

    [Test]
    public void ApplyToAll_WithSingleParallelStopActionException_ThrowsStopActionException()
    {
        var expected = new StopActionException("stop");
        var iterator = new IterableSerializableDataIterator(null, null);

        var exception = Assert.Throws<StopActionException>(() =>
            iterator.ApplyToAll([0], _ => throw expected, parallelism: 4));

        Assert.That(exception, Is.SameAs(expected));
    }

    [Test]
    public void ApplyToAll_WithMultipleParallelExceptions_ThrowsAggregateExceptionWithAllInners()
    {
        var items = Enumerable.Range(0, 4).ToArray();
        using var entered = new CountdownEvent(items.Length);
        using var releaseBodies = new ManualResetEventSlim(false);
        var iterator = new IterableSerializableDataIterator(null, null);
        AggregateException? exception = null;

        var applyTask = Task.Run(() =>
        {
            exception = Assert.Throws<AggregateException>(() =>
                iterator.ApplyToAll(
                    items,
                    item =>
                    {
                        entered.Signal();
                        releaseBodies.Wait();
                        // Heterogeneous exception types: AggregateException must be preserved so
                        // none of the distinct failures is silently dropped.
                        throw item % 2 == 0
                            ? new InvalidOperationException($"failure-{item}")
                            : new TimeoutException($"failure-{item}");
                    },
                    parallelism: items.Length));
        });

        var allBodiesEntered = entered.Wait(TimeSpan.FromSeconds(10));
        releaseBodies.Set();
        applyTask.GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(allBodiesEntered, Is.True);
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(items.Length));
            Assert.That(
                exception.InnerExceptions.Select(inner => inner.Message),
                Is.EquivalentTo(items.Select(item => $"failure-{item}")));
            Assert.That(exception.InnerExceptions.OfType<InvalidOperationException>().Count(), Is.GreaterThan(0));
            Assert.That(exception.InnerExceptions.OfType<TimeoutException>().Count(), Is.GreaterThan(0));
        });
    }

    [Test]
    public void ApplyToAll_WithMultipleParallelExceptionsOfSameType_UnwrapsToSingleException()
    {
        var items = Enumerable.Range(0, 4).ToArray();
        using var entered = new CountdownEvent(items.Length);
        using var releaseBodies = new ManualResetEventSlim(false);
        var iterator = new IterableSerializableDataIterator(null, null);

        var exception = Assert.Throws<StopActionException>(() =>
        {
            var applyTask = Task.Run(() => iterator.ApplyToAll(
                items,
                item =>
                {
                    entered.Signal();
                    releaseBodies.Wait();
                    throw new StopActionException($"stop-{item}");
                },
                parallelism: items.Length));

            entered.Wait(TimeSpan.FromSeconds(10));
            releaseBodies.Set();
            applyTask.GetAwaiter().GetResult();
        });

        // All workers threw the same exception type, so callers see the real type
        // instead of an AggregateException wrapper they didn't ask for.
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.StartWith("stop-"));
    }

    [Test]
    public void ApplyToAll_WithNullParallelism_ThrowsOriginalExceptionDirectly()
    {
        var expected = new InvalidOperationException("sequential failure");
        var iterator = new IterableSerializableDataIterator(null, null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            iterator.ApplyToAll([0, 1], item => throw expected));

        Assert.That(exception, Is.SameAs(expected));
    }

    private static void RecordMax(ref int maxValue, int value)
    {
        while (true)
        {
            var currentMax = Volatile.Read(ref maxValue);
            if (value <= currentMax)
                return;

            if (Interlocked.CompareExchange(ref maxValue, value, currentMax) == currentMax)
                return;
        }
    }
}
