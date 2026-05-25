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
    public void Load_WhenConfiguratorAssemblyIsLooseInBinFolder_LoadsIt()
    {
        const string looseConfiguratorAssemblyName = "QaaS.Runner.Tests.LooseConfigurator.dll";
        const string looseConfiguratorFullName =
            "QaaS.Runner.Tests.LooseConfigurator.LooseBinFolderConfigurator";
        var looseConfiguratorPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            looseConfiguratorAssemblyName);
        var logger = Mock.Of<ILogger>();

        Assert.That(looseConfiguratorPath, Does.Exist,
            "The fixture assembly must be copied beside the test host without being referenced by the test project.");

        var configurators = ExecutionBuilderConfiguratorLoader.Load(logger);

        Assert.That(
            configurators.Select(configurator => configurator.GetType().FullName),
            Does.Contain(looseConfiguratorFullName),
            "Configurator discovery should include plugin DLLs that are present in the bin folder even when " +
            "they are not listed in the dependency manifest.");
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
