using System.Runtime.ExceptionServices;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization.Serializers;

namespace QaaS.Runner.Sessions.Extensions;

/// <summary>
///     Wraps a data enumerable so when iterating over it its saved as is to a list and returns the data serialized
/// </summary>
public sealed class IterableSerializableDataIterator
{
    private readonly IEnumerable<Data<object>>? _iterableData;
    private readonly ISerializer? _serializer;

    public IterableSerializableDataIterator(IEnumerable<Data<object>>? iterableData, ISerializer? serializer)
    {
        IteratedData = new List<Data<object>>();
        _iterableData = iterableData;
        _serializer = serializer;
    }

    /// <summary>
    ///     Contains the iterated data before serialization
    /// </summary>
    public IList<Data<object>> IteratedData { get; }

    /// <summary>
    ///     Iterate over the iterable data loaded into this iterator while saving the iterated data as is in `IteratedData`
    ///     and returning the serialized data
    /// </summary>
    /// <returns> The iterable enumerable with its items serialized </returns>
    public IEnumerable<Data<object>> IterateEnumerable()
    {
        foreach (var (_, serialized) in IterateWithOriginal())
            yield return serialized;
    }

    /// <summary>
    ///     Iterate over the iterable data while returning both the original value and the serialized value.
    /// </summary>
    public IEnumerable<(Data<object> Original, Data<object> Serialized)> IterateWithOriginal()
    {
        foreach (var item in _iterableData ?? [])
        {
            IteratedData.Add(item);
            yield return (item, Serialize(item));
        }
    }

    /// <summary>
    ///     Applies <paramref name="methodToApply"/> to every item in <paramref name="iterator"/>.
    ///     When <paramref name="parallelism"/> is <c>null</c> the iteration runs sequentially.
    ///     Otherwise <see cref="Parallel.ForEach"/> is bounded by
    ///     <see cref="ParallelOptions.MaxDegreeOfParallelism"/> so the worker count matches the
    ///     configured parallelism — no extra throttling primitive is needed at the call site.
    ///     Exceptions from the parallel body are unwrapped from <see cref="AggregateException"/>
    ///     when all inner exceptions share the same type, so callers see the same exception type
    ///     they would in sequential mode (e.g. <c>StopActionException</c>). Heterogeneous parallel
    ///     failures propagate as <see cref="AggregateException"/> so no information is lost.
    /// </summary>
    public void ApplyToAll<TData>(IEnumerable<TData>? iterator, Action<TData> methodToApply,
        int? parallelism = null)
    {
        iterator ??= IterateEnumerable().Cast<TData>();

        if (parallelism is null)
        {
            foreach (var data in iterator) methodToApply(data);
            return;
        }

        try
        {
            Parallel.ForEach(
                iterator,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism.Value },
                methodToApply);
        }
        catch (AggregateException ex) when (AllInnerExceptionsAreSameType(ex))
        {
            // Either a single inner or many of the same type (e.g. several workers each
            // throwing StopActionException). Rethrow the first so callers see the real
            // type instead of the parallel wrapper. Heterogeneous failures (multiple
            // distinct types) propagate as AggregateException — picking one would hide
            // the others.
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
    }

    private static bool AllInnerExceptionsAreSameType(AggregateException aggregate)
    {
        if (aggregate.InnerExceptions.Count == 0) return false;
        var firstType = aggregate.InnerExceptions[0].GetType();
        for (var i = 1; i < aggregate.InnerExceptions.Count; i++)
            if (aggregate.InnerExceptions[i].GetType() != firstType)
                return false;
        return true;
    }

    /// <summary>
    /// Returns the data contained under the IteratedData list before it was serialized.
    /// </summary>
    /// <param name="indexToFetch"> The index of the data you wish to fetch </param>
    public Data<object> GetDataBeforeSerialization(int indexToFetch)
        => IteratedData[indexToFetch % IteratedData.Count];

    private Data<object> Serialize(Data<object> item)
    {
        if (_serializer == null)
            return item;

        return new Data<object>
        {
            Body = _serializer.Serialize(item.Body),
            MetaData = item.MetaData
        };
    }
}
