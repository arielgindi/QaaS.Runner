using System;
using System.Collections.Generic;
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
            iterator.ApplyToAll<int>(data, _ => throw new StopActionException("stop"), parallel: true));
    }

    [Test]
    public void ApplyToAll_Parallel_RethrowsAggregateForOtherExceptions()
    {
        var iterator = new IterableSerializableDataIterator(null, null);
        var data = new List<int> { 1, 2, 3 };

        Assert.Throws<AggregateException>(() =>
            iterator.ApplyToAll<int>(data, _ => throw new InvalidOperationException("boom"), parallel: true));
    }

    [Test]
    public void ApplyToAll_Sequential_PropagatesStopActionExceptionDirectly()
    {
        var iterator = new IterableSerializableDataIterator(null, null);
        var data = new List<int> { 1, 2, 3 };

        Assert.Throws<StopActionException>(() =>
            iterator.ApplyToAll<int>(data, _ => throw new StopActionException("stop"), parallel: false));
    }
}
