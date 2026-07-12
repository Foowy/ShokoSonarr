using Moq;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using ShokoSonarr.Services;
using Xunit;

namespace ShokoSonarr.Tests;

public class RelatedSeriesFinderTests
{
    private static Mock<ISeries> MakeRelatedSeries(int id, string title, IReadOnlyList<int> shokoSeriesIds, AnimeType type = AnimeType.TV)
    {
        var related = new Mock<ISeries>();
        related.Setup(s => s.ID).Returns(id);
        related.Setup(s => s.Title).Returns(title);
        related.Setup(s => s.ShokoSeriesIDs).Returns(shokoSeriesIds);
        related.Setup(s => s.Type).Returns(type);
        return related;
    }

    private static Mock<IRelatedMetadata<ISeries, ISeries>> MakeRelation(RelationType type, ISeries related)
    {
        var relation = new Mock<IRelatedMetadata<ISeries, ISeries>>();
        relation.Setup(r => r.RelationType).Returns(type);
        relation.Setup(r => r.Related).Returns(related);
        return relation;
    }

    [Fact]
    public void FindSuggestions_UnownedSequel_IsSuggested()
    {
        var relatedSeries = MakeRelatedSeries(200, "Sequel Show", []);
        var relation = MakeRelation(RelationType.Sequel, relatedSeries.Object);

        var owned = new Mock<IShokoSeries>();
        owned.Setup(s => s.ID).Returns(100);
        owned.Setup(s => s.Title).Returns("Owned Show");
        owned.Setup(s => s.RelatedSeries).Returns([relation.Object]);

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([owned.Object]);

        var finder = new RelatedSeriesFinder(metadataService.Object);
        var suggestions = finder.FindSuggestions();

        Assert.Single(suggestions);
        Assert.Equal(100, suggestions[0].OwningShokoSeriesId);
        Assert.Equal("Owned Show", suggestions[0].OwningSeriesTitle);
        Assert.Equal("Sequel", suggestions[0].RelationType);
        Assert.Equal("Sequel Show", suggestions[0].RelatedTitle);
    }

    [Fact]
    public void FindSuggestions_OwnedRelatedSeries_IsExcluded()
    {
        var relatedSeries = MakeRelatedSeries(201, "Owned Sequel", [500]);
        var relation = MakeRelation(RelationType.Sequel, relatedSeries.Object);

        var owned = new Mock<IShokoSeries>();
        owned.Setup(s => s.ID).Returns(101);
        owned.Setup(s => s.Title).Returns("Owned Show 2");
        owned.Setup(s => s.RelatedSeries).Returns([relation.Object]);

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([owned.Object]);

        var finder = new RelatedSeriesFinder(metadataService.Object);
        var suggestions = finder.FindSuggestions();

        Assert.Empty(suggestions);
    }

    [Fact]
    public void FindSuggestions_NoisyRelationType_IsExcluded()
    {
        var relatedSeries = MakeRelatedSeries(202, "Same Setting Show", []);
        var relation = MakeRelation(RelationType.SameSetting, relatedSeries.Object);

        var owned = new Mock<IShokoSeries>();
        owned.Setup(s => s.ID).Returns(102);
        owned.Setup(s => s.Title).Returns("Owned Show 3");
        owned.Setup(s => s.RelatedSeries).Returns([relation.Object]);

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([owned.Object]);

        var finder = new RelatedSeriesFinder(metadataService.Object);
        var suggestions = finder.FindSuggestions();

        Assert.Empty(suggestions);
    }

    [Fact]
    public void FindSuggestions_SameRelatedSeriesFromTwoOwnedSeries_IsDeduped()
    {
        var relatedSeries = MakeRelatedSeries(203, "Shared Sequel", []);
        var relationA = MakeRelation(RelationType.Sequel, relatedSeries.Object);
        var relationB = MakeRelation(RelationType.Sequel, relatedSeries.Object);

        var ownedA = new Mock<IShokoSeries>();
        ownedA.Setup(s => s.ID).Returns(103);
        ownedA.Setup(s => s.Title).Returns("Owned Show 4A");
        ownedA.Setup(s => s.RelatedSeries).Returns([relationA.Object]);

        var ownedB = new Mock<IShokoSeries>();
        ownedB.Setup(s => s.ID).Returns(104);
        ownedB.Setup(s => s.Title).Returns("Owned Show 4B");
        ownedB.Setup(s => s.RelatedSeries).Returns([relationB.Object]);

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([ownedA.Object, ownedB.Object]);

        var finder = new RelatedSeriesFinder(metadataService.Object);
        var suggestions = finder.FindSuggestions();

        Assert.Single(suggestions);
    }

    [Fact]
    public void FindSuggestions_MovieTypeRelated_PopulatesRelatedType()
    {
        var relatedSeries = MakeRelatedSeries(300, "Movie Sequel", [], type: AnimeType.Movie);
        var relation = MakeRelation(RelationType.Sequel, relatedSeries.Object);

        var owned = new Mock<IShokoSeries>();
        owned.Setup(s => s.ID).Returns(200);
        owned.Setup(s => s.Title).Returns("Owned Show 6");
        owned.Setup(s => s.RelatedSeries).Returns([relation.Object]);

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([owned.Object]);

        var finder = new RelatedSeriesFinder(metadataService.Object);
        var suggestions = finder.FindSuggestions();

        Assert.Single(suggestions);
        Assert.Equal("Movie", suggestions[0].RelatedType);
    }

    [Fact]
    public void FindSuggestions_NullRelated_IsSkipped()
    {
        var relation = MakeRelation(RelationType.Sequel, null!);

        var owned = new Mock<IShokoSeries>();
        owned.Setup(s => s.ID).Returns(105);
        owned.Setup(s => s.Title).Returns("Owned Show 5");
        owned.Setup(s => s.RelatedSeries).Returns([relation.Object]);

        var metadataService = new Mock<IMetadataService>();
        metadataService.Setup(m => m.GetAllShokoSeries()).Returns([owned.Object]);

        var finder = new RelatedSeriesFinder(metadataService.Object);
        var suggestions = finder.FindSuggestions();

        Assert.Empty(suggestions);
    }
}
