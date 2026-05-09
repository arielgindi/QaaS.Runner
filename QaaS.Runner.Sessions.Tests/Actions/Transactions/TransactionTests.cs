using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.Tests.Actions.Utils;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace QaaS.Runner.Sessions.Tests.Actions.Transactions;

[TestFixture]
public class TransactionTests
{
    private const int DefaultTestMessagesPerSecond = 100_000;
    private static Mock<ITransactor> _transactor = null!;
    private static List<Data<object>> _infoSent = null!;

    [
        Test,
        TestCaseSource(
            typeof(TestResourceDataSources),
            nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters)
        )
    ]
    public void TestTransactAndInitializeIterableSerializableSaveIterator_ReceivesValidDataSourceNamesAndAppropriateFiltersAndLoopPolicy_SendsTheProperDataToTheSenderObject(
        List<string> names,
        List<string> patterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedData
    )
    {
        // Arrange
        var amountOfDataToSend = expectedData.Count * 2;
        var transactor = CreationalFunctions.CreateTransactorWithLoop(
            ref _transactor!,
            "test data",
            ref _infoSent,
            patterns.ToArray(),
            names.ToArray(),
            amountOfDataToSend
        );

        // Act
        transactor.InitializeIterableSerializableSaveIterator([], dataSources);
        transactor.Act();

        // Assert
        var orderedExpectedData = expectedData.SelectMany(d => new[] { d.Body, d.Body }).Order();
        var orderedReceivedData = _infoSent.Select(d => d.Body).Order();
        CollectionAssert.AreEqual(orderedExpectedData, orderedReceivedData);
    }

    private static Sessions.Actions.Transactions.Transaction InitTransactorWithIterations(
        string[]? dsPatterns,
        string[]? dsNames,
        int numberOfIterations,
        string dataToGet,
        int msgPerSec = DefaultTestMessagesPerSecond
    )
    {
        _infoSent = CreationalFunctions.InitTransactor(ref _transactor!, dataToGet);

        return new Sessions.Actions.Transactions.Transaction(
            "TestPub",
            _transactor!.Object,
            0,
            new DataFilter()
            {
                Body = true,
                Timestamp = true,
                MetaData = false,
            },
            new DataFilter()
            {
                Body = true,
                Timestamp = true,
                MetaData = false,
            },
            new LoadBalancePolicy(msgPerSec, 1000),
            false,
            numberOfIterations,
            0,
            null,
            null,
            null,
            dsPatterns,
            dsNames,
            new SerilogLoggerFactory(
                new LoggerConfiguration()
                    .MinimumLevel.Is(LogEventLevel.Information)
                    .WriteTo.Console()
                    .CreateLogger()
            ).CreateLogger("DefaultLogger")
        );
    }

    [
        Test,
        TestCaseSource(
            typeof(TestResourceDataSources),
            nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters)
        )
    ]
    public void TestTransact_ReceivesValidDataSourceNamesAndAppropriateFiltersAndIterationsPolicy_SendsTheProperDataToTheSenderObject(
        List<string> names,
        List<string> patterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedData
    )
    {
        // Arrange
        const int iterationNumber = 2;
        var transactor = InitTransactorWithIterations(
            patterns.ToArray(),
            names.ToArray(),
            iterationNumber,
            "test data"
        );

        // Act
        transactor.InitializeIterableSerializableSaveIterator([], dataSources);
        transactor.Act();

        // Assert
        _transactor!.Verify(
            t => t.Transact(It.IsAny<Data<object>>()),
            Times.Exactly(expectedData.Count * iterationNumber)
        );
    }

    [
        Test,
        TestCaseSource(
            typeof(TestResourceDataSources),
            nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters)
        )
    ]
    public void TestExportRunningCommunicationData_ReceivesRcdToExport_ExportTheGivenDataToTheRcd(
        List<string> names,
        List<string> patterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedData
    )
    {
        // Arrange
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, dataSources);
        var transactor = InitTransactorWithIterations(
            patterns.ToArray(),
            names.ToArray(),
            1,
            "test data"
        );

        // Act
        transactor.ExportRunningCommunicationData(context, sessionName);
        transactor.InitializeIterableSerializableSaveIterator([], dataSources);
        transactor.Act();

        // Arrange
        var expectedSentData = expectedData.Select(d => d.Body).Order().ToList();
        var expectedReceivedData = Enumerable.Repeat("test data", expectedData.Count).ToList();
        var receivedData = context
            .InternalRunningSessions.RunningSessionsDict[sessionName]
            .Outputs![0]
            .Data.Select(d => d!.Body)
            .Order()
            .ToList();
        var sentData = context
            .InternalRunningSessions.RunningSessionsDict[sessionName]
            .Inputs![0]
            .Data.Select(d => d!.Body)
            .Order()
            .ToList();
        CollectionAssert.AreEqual(expectedReceivedData, receivedData);
        CollectionAssert.AreEqual(expectedSentData, sentData);
    }

    [Test]
    public void TestExportRunningCommunicationData_PreservesTransactionMetadataOnExportedOutputs()
    {
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var transaction = InitTransactorWithIterations([], [], 1, "test data");

        transaction.ExportRunningCommunicationData(context, sessionName);

        var inputRcd = context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs![0];
        var outputRcd = context.InternalRunningSessions.RunningSessionsDict[sessionName].Outputs![
            0
        ];

        Assert.That(inputRcd.Name, Is.EqualTo("TestPub"));
        Assert.That(outputRcd.Name, Is.EqualTo("TestPub"));
        Assert.That(
            outputRcd.SerializationType,
            Is.EqualTo(
                transaction
                    .GetType()
                    .GetMethod(
                        "GetOutputCommunicationSerializationType",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )!
                    .Invoke(transaction, null)
            )
        );
    }

    [Test]
    public void Act_WithBinaryOutputDeserializerAndNoSpecificType_DeserializesToOriginalPayloadType()
    {
        var payload = new BinaryPayload { Value = "transaction-output" };
        var serializer = SerializerFactory.BuildSerializer(SerializationType.Binary)!;
        var transactor = new Mock<ITransactor>();
        transactor
            .Setup(instance => instance.Transact(It.IsAny<Data<object>>()))
            .Returns(
                new Tuple<DetailedData<object>, DetailedData<object>?>(
                    new DetailedData<object> { Body = "request" },
                    new DetailedData<object> { Body = serializer.Serialize(payload) }
                )
            );
        var transaction = new Sessions.Actions.Transactions.Transaction(
            "BinaryTransaction",
            transactor.Object,
            0,
            new DataFilter
            {
                Body = true,
                Timestamp = true,
                MetaData = true,
            },
            new DataFilter
            {
                Body = true,
                Timestamp = true,
                MetaData = true,
            },
            null,
            false,
            1,
            0,
            null,
            SerializationType.Binary,
            null,
            null,
            ["source-a"],
            Globals.Logger
        );
        var dataSource = new DataSource
        {
            Name = "source-a",
            DataSourceList = ImmutableList<DataSource>.Empty,
            Generator = new SingleItemGenerator(),
        };

        transaction.InitializeIterableSerializableSaveIterator([], [dataSource]);
        var actData = transaction.Act();

        var body = actData.Output!.Single()!.Body;
        Assert.Multiple(() =>
        {
            Assert.That(body, Is.TypeOf<BinaryPayload>());
            Assert.That(((BinaryPayload)body!).Value, Is.EqualTo(payload.Value));
        });
    }

    [Serializable]
    private sealed class BinaryPayload
    {
        public string Value { get; set; } = string.Empty;
    }

    private const int TimeoutMsForWork = 10;

    private readonly System.Reflection.FieldInfo _iterableSerializableSaveIteratorField =
        typeof(Sessions.Actions.Transactions.Transaction).GetField(
            "_iterableSerializableSaveIterator",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        )!;

    [
        Test,
        TestCase(1, 5),
        TestCase(5, 5),
        TestCase(10, 5),
        TestCase(100, 5),
        TestCase(10, 50),
        TestCase(10, 100),
        TestCase(2, 5)
    ]
    public void TestTransact_CallTransactionWithDifferentParallelism_ExpectToSendAllItemsInMatchingParallelism(
        int parallelism,
        int numberOfItems
    )
    {
        var activeThreads = 0;
        var maxActiveThreads = 0;
        var dataIterated = 0;
        var dataToPublish = Enumerable
            .Range(0, numberOfItems)
            .Select(_ => new Data<object>
            {
                MetaData = new MetaData(),
                Body = "A body of a message that is being published in chunks",
            })
            .ToArray();
        var transactorMock = new Mock<ITransactor>();
        transactorMock
            .Setup(sender => sender.Transact(It.IsAny<Data<object>>()))
            .Callback(() =>
            {
                System.Threading.Interlocked.Increment(ref activeThreads);
                if (maxActiveThreads < activeThreads)
                    System.Threading.Interlocked.Exchange(ref maxActiveThreads, activeThreads);
                System.Threading.Thread.Sleep(TimeoutMsForWork);
                System.Threading.Interlocked.Decrement(ref activeThreads);
            })
            .Returns(() =>
            {
                var inputData = dataToPublish[
                    System.Threading.Interlocked.Increment(ref dataIterated) - 1
                ]
                    .CloneDetailed();
                var outputData = new DetailedData<object>
                {
                    Body = "response",
                    MetaData = inputData.MetaData,
                    Timestamp = inputData.Timestamp,
                };
                return new Tuple<DetailedData<object>, DetailedData<object>?>(
                    inputData,
                    outputData
                );
            })
            .Verifiable();

        var transaction = new Sessions.Actions.Transactions.Transaction(
            "test",
            transactorMock.Object,
            0,
            new DataFilter(),
            new DataFilter(),
            null,
            false,
            1,
            0,
            null,
            null,
            null,
            [],
            [],
            Globals.Logger,
            parallelism
        );

        var testActData = new Sessions.Actions.InternalCommunicationData<object>
        {
            Input = [],
            InputSerializationType = SerializationType.Json,
            Output = [],
            OutputSerializationType = SerializationType.Json,
        };
        _iterableSerializableSaveIteratorField.SetValue(
            transaction,
            new IterableSerializableDataIterator(
                dataToPublish,
                SerializerFactory.BuildSerializer(SerializationType.Json)
            )
        );

        typeof(Sessions.Actions.Transactions.Transaction)
            .GetMethod(
                "Transact",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            )!
            .Invoke(transaction, [testActData]);

        transactorMock.Verify(
            sender => sender.Transact(It.IsAny<Data<object>>()),
            Times.Exactly(numberOfItems)
        );
        Assert.That(testActData.Input!.Count, Is.EqualTo(numberOfItems));
        Assert.That(testActData.Output!.Count, Is.EqualTo(numberOfItems));
        var expectedMaxConcurrency = Math.Min(numberOfItems, parallelism);
        Assert.That(maxActiveThreads, Is.InRange(1, expectedMaxConcurrency));
    }

    [Test]
    public void TestTransact_WithParallelism_PreservesReturnedTimestampPerBody()
    {
        var baseTime = System.DateTime.UtcNow;
        var dataToPublish = Enumerable
            .Range(0, 6)
            .Select(index => new Data<object> { Body = $"body-{index}", MetaData = new MetaData() })
            .ToArray();

        var expectedTimestamps = dataToPublish.ToDictionary(
            item => (string)item.Body!,
            item => baseTime.AddMilliseconds(int.Parse(item.Body!.ToString()!.Split('-')[1]))
        );

        var transactorMock = new Mock<ITransactor>();
        transactorMock
            .Setup(sender => sender.Transact(It.IsAny<Data<object>>()))
            .Returns(
                (Data<object> sentData) =>
                {
                    var body = sentData.Body!.ToString()!;
                    var index = int.Parse(body.Split('-')[1]);
                    System.Threading.Thread.Sleep((dataToPublish.Length - index) * 2);
                    var inputDetail = new DetailedData<object>
                    {
                        Body = sentData.Body,
                        MetaData = sentData.MetaData,
                        Timestamp = expectedTimestamps[body],
                    };
                    var outputDetail = new DetailedData<object>
                    {
                        Body = "response",
                        MetaData = sentData.MetaData,
                        Timestamp = expectedTimestamps[body].AddMilliseconds(1),
                    };
                    return new Tuple<DetailedData<object>, DetailedData<object>?>(
                        inputDetail,
                        outputDetail
                    );
                }
            );

        var transaction = new Sessions.Actions.Transactions.Transaction(
            "test",
            transactorMock.Object,
            0,
            new DataFilter(),
            new DataFilter(),
            null,
            false,
            1,
            0,
            null,
            null,
            null,
            [],
            [],
            Globals.Logger,
            4
        );
        var testActData = new Sessions.Actions.InternalCommunicationData<object>
        {
            Input = [],
            InputSerializationType = SerializationType.Json,
            Output = [],
            OutputSerializationType = SerializationType.Json,
        };
        _iterableSerializableSaveIteratorField.SetValue(
            transaction,
            new IterableSerializableDataIterator(dataToPublish, null)
        );

        typeof(Sessions.Actions.Transactions.Transaction)
            .GetMethod(
                "Transact",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            )!
            .Invoke(transaction, [testActData]);

        Assert.That(testActData.Input, Has.Count.EqualTo(dataToPublish.Length));
        Assert.That(
            testActData.Input!.All(item =>
                item.Timestamp == expectedTimestamps[item.Body!.ToString()!]
            ),
            Is.True
        );
        Assert.That(
            testActData.Output!.All(item =>
                item!.Timestamp
                == expectedTimestamps[$"body-{item.MetaData!.IoMatchIndex}"].AddMilliseconds(1)
            ),
            Is.True
        );
    }

    private sealed class SingleItemGenerator : IGenerator
    {
        public Context Context { get; set; } = null!;

        public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration) =>
            [];

        public IEnumerable<Data<object>> Generate(
            IImmutableList<SessionData> sessionDataList,
            IImmutableList<DataSource> dataSourceList
        )
        {
            return [new Data<object> { Body = "request" }];
        }
    }

    // R-6: CompleteAdding must be called even when the Transact loop throws.
    [Test]
    public void Act_WhenTransactLoopThrows_CompleteAddingIsStillCalledOnBothRcds()
    {
        var transactor = new Mock<ITransactor>();
        transactor
            .Setup(t => t.Transact(It.IsAny<Data<object>>()))
            .Throws(new InvalidOperationException("transactor failed"));

        const string sessionName = "R6session";
        var context = CreationalFunctions.CreateContext(sessionName, []);
        var transaction = new Sessions.Actions.Transactions.Transaction(
            "R6Tx",
            transactor.Object,
            0,
            new DataFilter { Body = true },
            new DataFilter { Body = true },
            null,
            false,
            1,
            0,
            null,
            null,
            null,
            null,
            ["R6src"],
            Globals.Logger
        );

        var dataSource = new DataSource
        {
            Name = "R6src",
            DataSourceList = ImmutableList<DataSource>.Empty,
            Generator = new SingleItemGenerator(),
        };

        transaction.ExportRunningCommunicationData(context, sessionName);
        transaction.InitializeIterableSerializableSaveIterator([], [dataSource]);

        Assert.Throws<InvalidOperationException>(() => transaction.Act());

        var inputs = context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs!;
        var outputs = context.InternalRunningSessions.RunningSessionsDict[sessionName].Outputs!;
        Assert.That(
            inputs.First().Data.IsAddingCompleted,
            Is.True,
            "Sent RCD BlockingCollection must be completed even when Transact throws (R-6 fix)"
        );
        Assert.That(
            outputs.First().Data.IsAddingCompleted,
            Is.True,
            "Received RCD BlockingCollection must be completed even when Transact throws (R-6 fix)"
        );
    }

    // R-10: pairIndex must always use Interlocked.Increment, even in sequential mode.
    [Test]
    public void TestTransact_SequentialMode_IoMatchIndexIsCorrectlyOrdered()
    {
        const int itemCount = 5;
        var expectedIndices = Enumerable.Range(0, itemCount).ToList();
        var observedIndices = new List<int>();

        var transactor = new Mock<ITransactor>();
        transactor
            .Setup(t => t.Transact(It.IsAny<Data<object>>()))
            .Returns(
                (Data<object> d) =>
                {
                    var input = new DetailedData<object> { Body = d.Body };
                    var output = new DetailedData<object> { Body = "resp" };
                    return new Tuple<DetailedData<object>, DetailedData<object>?>(input, output);
                }
            );

        // sequential mode: parallelism = null
        var transaction = new Sessions.Actions.Transactions.Transaction(
            "R10Tx",
            transactor.Object,
            0,
            new DataFilter { Body = true },
            new DataFilter { Body = true },
            null,
            false,
            1,
            0,
            null,
            null,
            null,
            null,
            ["R10src"],
            Globals.Logger
        );

        var data = Enumerable
            .Range(0, itemCount)
            .Select(i => new Data<object> { Body = $"item-{i}" })
            .ToArray();
        var dataSource = new DataSource
        {
            Name = "R10src",
            DataSourceList = ImmutableList<DataSource>.Empty,
            Generator = new FixedDataGenerator(data),
        };

        var testActData = new Sessions.Actions.InternalCommunicationData<object>
        {
            Input = [],
            InputSerializationType = SerializationType.Json,
            Output = [],
            OutputSerializationType = SerializationType.Json,
        };

        _iterableSerializableSaveIteratorField.SetValue(
            transaction,
            new IterableSerializableDataIterator(data, null)
        );

        typeof(Sessions.Actions.Transactions.Transaction)
            .GetMethod(
                "Transact",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            )!
            .Invoke(transaction, [testActData]);

        var inputIndices = testActData
            .Input!.Select(d => d.MetaData!.IoMatchIndex)
            .OrderBy(i => i)
            .ToList();
        CollectionAssert.AreEqual(
            expectedIndices,
            inputIndices,
            "Sequential Transact must assign consecutive IoMatchIndex values (R-10 fix)"
        );
    }

    private sealed class FixedDataGenerator(IEnumerable<Data<object>> data) : IGenerator
    {
        public Context Context { get; set; } = null!;

        public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration) =>
            [];

        public IEnumerable<Data<object>> Generate(
            IImmutableList<SessionData> sessionDataList,
            IImmutableList<DataSource> dataSourceList
        ) => data;
    }
}
