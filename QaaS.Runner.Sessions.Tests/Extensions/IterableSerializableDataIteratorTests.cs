using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using QaaS.Framework.Policies.Exceptions;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Tests.Extensions;

[TestFixture]
public class IterableSerializableDataIteratorTests
{
    [Test]
    public void ApplyToAll_Parallel_UnwrapsStopActionExceptionFromAggregate()
    {
        // Regression: Parallel.ForEach wraps worker exceptions in AggregateException.
        // Callers catch StopActionException directly, so the iterator must unwrap it.
        var iterator = new IterableSerializableDataIterator(null, null);
        var data = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.Throws<StopActionException>(() =>
            iterator.ApplyToAll<int>(
                data,
                _ => throw new StopActionException("stop"),
                parallel: true
            )
        );
    }

    [Test]
    public void ApplyToAll_Parallel_RethrowsAggregateForOtherExceptions()
    {
        var iterator = new IterableSerializableDataIterator(null, null);
        var data = new List<int> { 1, 2, 3 };

        Assert.Throws<AggregateException>(() =>
            iterator.ApplyToAll<int>(
                data,
                _ => throw new InvalidOperationException("boom"),
                parallel: true
            )
        );
    }

    [Test]
    public void ApplyToAll_Sequential_PropagatesStopActionExceptionDirectly()
    {
        var iterator = new IterableSerializableDataIterator(null, null);
        var data = new List<int> { 1, 2, 3 };

        Assert.Throws<StopActionException>(() =>
            iterator.ApplyToAll<int>(
                data,
                _ => throw new StopActionException("stop"),
                parallel: false
            )
        );
    }

    // R-1: MaxDegreeOfParallelism is respected
    [Test, TestCase(1, 20), TestCase(2, 20), TestCase(4, 20)]
    public void ApplyToAll_Parallel_WithMaxDegreeOfParallelism_BoundsConcurrency(
        int maxDop,
        int itemCount
    )
    {
        var iterator = new IterableSerializableDataIterator(null, null);
        var data = Enumerable.Range(0, itemCount).ToList();
        var activeThreads = 0;
        var maxObservedConcurrency = 0;

        iterator.ApplyToAll<int>(
            data,
            _ =>
            {
                var current = Interlocked.Increment(ref activeThreads);
                // Record peak concurrency atomically (best-effort; benign race acceptable in a test)
                int observed;
                do
                {
                    observed = maxObservedConcurrency;
                } while (
                    observed < current
                    && Interlocked.CompareExchange(ref maxObservedConcurrency, current, observed)
                        != observed
                );
                Thread.Sleep(10);
                Interlocked.Decrement(ref activeThreads);
            },
            parallel: true,
            maxDegreeOfParallelism: maxDop
        );

        Assert.That(
            maxObservedConcurrency,
            Is.InRange(1, maxDop),
            $"Observed concurrency {maxObservedConcurrency} exceeded configured maxDop {maxDop}"
        );
    }

    // R-9: GetDataBeforeSerialization throws on empty IteratedData
    [Test]
    public void GetDataBeforeSerialization_WhenIteratedDataIsEmpty_ThrowsInvalidOperationException()
    {
        var iterator = new IterableSerializableDataIterator(null, null);

        Assert.Throws<InvalidOperationException>(() => iterator.GetDataBeforeSerialization(0));
    }
}
