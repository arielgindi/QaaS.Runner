using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Loaders;

namespace QaaS.Runner.Tests.LoadersTests;

[TestFixture]
public class ExecutionBuilderConfiguratorLoaderTests
{
    [Test]
    public void Load_WhenEntryAssemblyContainsInternalConfigurator_LoadsIt()
    {
        var logger = Mock.Of<ILogger>();

        var configurators = ExecutionBuilderConfiguratorLoader.Load(
            logger,
            typeof(InternalEntryAssemblyConfigurator).Assembly,
            [typeof(InternalEntryAssemblyConfigurator)]);

        Assert.That(
            configurators.Any(configurator => configurator.GetType() == typeof(InternalEntryAssemblyConfigurator)),
            Is.True);
    }

    [Test]
    public void Load_WhenDependencyConfiguratorCannotBeActivated_SkipsIt()
    {
        var logger = new Mock<ILogger>();

        Assert.DoesNotThrow(() => ExecutionBuilderConfiguratorLoader.Load(
            logger.Object,
            typeof(Runner).Assembly,
            [typeof(PublicDependencyConfiguratorWithoutDefaultConstructor)]));

        var configurators = ExecutionBuilderConfiguratorLoader.Load(
            logger.Object,
            typeof(Runner).Assembly,
            [typeof(PublicDependencyConfiguratorWithoutDefaultConstructor)]);

        Assert.That(
            configurators.Any(configurator =>
                configurator.GetType() == typeof(PublicDependencyConfiguratorWithoutDefaultConstructor)),
            Is.False);
    }

    [Test]
    public void Load_WhenConfiguratorAssemblyIsLooseInBinFolder_DoesNotLoadIt()
    {
        const string looseConfiguratorAssemblyName = "QaaS.Runner.Tests.LooseConfigurator.dll";
        const string looseConfiguratorFullName =
            "QaaS.Runner.Tests.LooseConfigurator.LooseBinFolderConfigurator";
        var looseConfiguratorPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            looseConfiguratorAssemblyName);
        var logger = Mock.Of<ILogger>();

        Assert.That(looseConfiguratorPath, Does.Exist,
            "The fixture assembly must be copied beside the test host without being referenced by the test project " +
            "so the test exercises the real loose-DLL scenario.");

        var configurators = ExecutionBuilderConfiguratorLoader.Load(logger);

        // By design: PluginAssemblyDiscovery walks the dependency manifest only and runs the bin-folder
        // scan only as a fallback when the manifest is unusable. Antivirus scans every file open, so
        // reading the PE header of every DLL beside the entry assembly on every startup is unacceptable
        // for deployments with many unrelated DLLs. Plugins are expected to be ProjectReferences or
        // NuGet packages so they appear in deps.json. This test guards against a regression where the
        // bin scan is made always-on again.
        Assert.That(
            configurators.Select(configurator => configurator.GetType().FullName),
            Does.Not.Contain(looseConfiguratorFullName),
            "Loose DLLs in the bin folder must NOT be picked up on the fast path; consumers should reference "
            + "their plugin assembly explicitly so it appears in the dependency manifest.");
    }

    internal sealed class InternalEntryAssemblyConfigurator : IExecutionBuilderConfigurator
    {
        public void Configure(ExecutionBuilder executionBuilder)
        {
        }
    }

    public sealed class PublicDependencyConfiguratorWithoutDefaultConstructor : IExecutionBuilderConfigurator
    {
        public PublicDependencyConfiguratorWithoutDefaultConstructor(string requiredValue)
        {
            _ = requiredValue;
        }

        public void Configure(ExecutionBuilder executionBuilder)
        {
        }
    }
}
