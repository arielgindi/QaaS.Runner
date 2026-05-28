using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.Transactions;

namespace QaaS.Runner.Sessions.Tests.Actions.Transactions;

[TestFixture]
public class TransactionParallelismTests
{
    [Test]
    public void Act_WithParallelismOne_TransactsEachItemWithoutConcurrency()
    {
        var data = Enumerable.Range(0, 8)
            .Select(index => new Data<object> { Body = $"item-{index}" })
            .ToArray();
        var activeTransacts = 0;
        var maxConcurrentTransacts = 0;
        var transactor = new Mock<ITransactor>();
        transactor.Setup(instance => instance.Transact(It.IsAny<Data<object>>()))
            .Returns<Data<object>>(item =>
            {
                var active = Interlocked.Increment(ref activeTransacts);
                RecordMax(ref maxConcurrentTransacts, active);

                try
                {
                    return new Tuple<DetailedData<object>, DetailedData<object>?>(
                        new DetailedData<object> { Body = item.Body },
                        new DetailedData<object> { Body = $"response-{item.Body}" });
                }
                finally
                {
                    Interlocked.Decrement(ref activeTransacts);
                }
            });
        var transaction = new Transaction(
            "parallel-one",
            transactor.Object,
            0,
            new DataFilter { Body = true, Timestamp = true, MetaData = true },
            new DataFilter { Body = true, Timestamp = true, MetaData = true },
            null,
            false,
            1,
            1,
            0,
            null,
            null,
            null,
            null,
            ["source"],
            Globals.Logger);
        var dataSource = new DataSource
        {
            Name = "source",
            DataSourceList = ImmutableList<DataSource>.Empty,
            Generator = new StaticGenerator(data)
        };

        transaction.InitializeIterableSerializableSaveIterator([], [dataSource]);
        var actData = transaction.Act();

        Assert.Multiple(() =>
        {
            transactor.Verify(
                instance => instance.Transact(It.IsAny<Data<object>>()),
                Times.Exactly(data.Length));
            Assert.That(maxConcurrentTransacts, Is.EqualTo(1));
            Assert.That(Volatile.Read(ref activeTransacts), Is.EqualTo(0));
            Assert.That(actData.Input, Has.Count.EqualTo(data.Length));
            Assert.That(actData.Output, Has.Count.EqualTo(data.Length));
        });
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

    private sealed class StaticGenerator(IEnumerable<Data<object>> data) : IGenerator
    {
        public Context Context { get; set; } = null!;

        public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration) => [];

        public IEnumerable<Data<object>> Generate(
            IImmutableList<SessionData> sessionDataList,
            IImmutableList<DataSource> dataSourceList) => data;
    }
}
