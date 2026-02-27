using BitScatter.Application.Interfaces;
using BitScatter.Application.Strategies;
using BitScatter.Domain.Enums;
using FluentAssertions;
using Moq;

namespace BitScatter.Application.Tests;

public class RoundRobinScatteringStrategyTests
{
    private static IStorageProvider MakeProvider(StorageProviderType type)
    {
        var mock = new Mock<IStorageProvider>();
        mock.SetupGet(p => p.ProviderType).Returns(type);
        return mock.Object;
    }

    private readonly RoundRobinScatteringStrategy _sut = new();

    [Fact]
    public void SelectProvider_SingleProvider_AlwaysReturnsSameProvider()
    {
        var provider = MakeProvider(StorageProviderType.FileSystem);
        IReadOnlyList<IStorageProvider> providers = [provider];

        _sut.SelectProvider(0, providers).Should().BeSameAs(provider);
        _sut.SelectProvider(1, providers).Should().BeSameAs(provider);
        _sut.SelectProvider(99, providers).Should().BeSameAs(provider);
    }

    [Fact]
    public void SelectProvider_TwoProviders_AlternatesCorrectly()
    {
        var fs = MakeProvider(StorageProviderType.FileSystem);
        var db = MakeProvider(StorageProviderType.Database);
        IReadOnlyList<IStorageProvider> providers = [fs, db];

        _sut.SelectProvider(0, providers).Should().BeSameAs(fs);
        _sut.SelectProvider(1, providers).Should().BeSameAs(db);
        _sut.SelectProvider(2, providers).Should().BeSameAs(fs);
        _sut.SelectProvider(3, providers).Should().BeSameAs(db);
    }

    [Fact]
    public void SelectProvider_ManyChunks_DistributesEvenly()
    {
        var fs = MakeProvider(StorageProviderType.FileSystem);
        var db = MakeProvider(StorageProviderType.Database);
        IReadOnlyList<IStorageProvider> providers = [fs, db];

        var selections = Enumerable.Range(0, 10)
            .Select(i => _sut.SelectProvider(i, providers))
            .ToList();

        selections.Count(p => p == fs).Should().Be(5);
        selections.Count(p => p == db).Should().Be(5);
    }

    [Fact]
    public void SelectProvider_EmptyProviderList_Throws()
    {
        IReadOnlyList<IStorageProvider> providers = [];
        var act = () => _sut.SelectProvider(0, providers);
        act.Should().Throw<InvalidOperationException>();
    }
}
