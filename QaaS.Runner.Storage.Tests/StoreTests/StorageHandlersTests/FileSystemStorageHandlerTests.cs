using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Storage.Tests.StoreTests.StorageHandlersTests;

public class FileSystemStorageHandlerTests
{
    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(100)]
    public void TestStore_CallFunctionWithMockFileSystem_ShouldCreateSameNumberOfFilesAsNumberOfItemsToStoreGiven(
        int numberOfItemsToStore
    )
    {
        // Arrange — R-13 fix: CreateDirectory is always called (idempotent); Exists is never called.
        var mockDirectory = new Mock<IDirectory>();
        mockDirectory.Setup(m => m.CreateDirectory(It.IsAny<string>()));
        var mockFile = new Mock<IFile>();
        mockFile.Setup(m => m.WriteAllBytes(It.IsAny<string>(), It.IsAny<byte[]>()));

        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(m => m.Directory).Returns(mockDirectory.Object);
        mockFileSystem.Setup(m => m.File).Returns(mockFile.Object);

        var itemsToStore = new List<SessionData>();
        for (var i = 0; i < numberOfItemsToStore; i++)
            itemsToStore.Add(new SessionData { Name = $"session-{i}" });

        var storageHandler = new FileSystemStorage(
            new FilesInFileSystemConfig { Path = "somePath" },
            mockFileSystem.Object,
            Formatting.Indented
        )
        {
            _context = Globals.Context,
        };

        // Act
        storageHandler.Store(itemsToStore.ToImmutableList()!, null);

        // Assert — no Exists check; CreateDirectory is always called
        mockDirectory.Verify(
            m => m.Exists(It.IsAny<string>()),
            Times.Never,
            "Directory.Exists must not be called after R-13 fix (CreateDirectory is idempotent)"
        );
        mockDirectory.Verify(
            m => m.CreateDirectory(It.IsAny<string>()),
            Times.Exactly(numberOfItemsToStore)
        );
        mockFile.Verify(
            m => m.WriteAllBytes(It.IsAny<string>(), It.IsAny<byte[]>()),
            Times.Exactly(numberOfItemsToStore)
        );
    }

    // R-13: RetrieveSerialized must materialize the bytes while storage is alive (ToList).
    [Test]
    public void RetrieveSerialized_MaterializesFileBytesEagerly()
    {
        // Return a valid SessionData JSON so Retrieve can fully deserialize it
        var sessionData = new SessionData { Name = "test-session" };
        var expectedBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(sessionData);

        var mockDirectory = new Mock<IDirectory>();
        mockDirectory.Setup(m => m.Exists(It.IsAny<string>())).Returns(true);
        mockDirectory
            .Setup(m =>
                m.GetFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>())
            )
            .Returns(["file1.json"]);
        var mockFile = new Mock<IFile>();
        mockFile.Setup(m => m.ReadAllBytes("file1.json")).Returns(expectedBytes);

        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(m => m.Directory).Returns(mockDirectory.Object);
        mockFileSystem.Setup(m => m.File).Returns(mockFile.Object);

        var storageHandler = new FileSystemStorage(
            new FilesInFileSystemConfig { Path = "somePath", SearchPattern = "*.json" },
            mockFileSystem.Object,
            Formatting.Indented
        )
        {
            _context = Globals.Context,
        };

        // Call RetrieveSerialized via reflection to bypass JSON deserialization
        var method = typeof(FileSystemStorage).GetMethod(
            "RetrieveSerialized",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        )!;
        var result = ((IEnumerable<byte[]>)method.Invoke(storageHandler, [null])!).ToList();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(expectedBytes));
        // Verify that ReadAllBytes was called eagerly during retrieval (materialized, not deferred)
        mockFile.Verify(
            m => m.ReadAllBytes("file1.json"),
            Times.Once,
            "ReadAllBytes must be called eagerly (materialized) during RetrieveSerialized (R-13 fix)"
        );
    }
}
