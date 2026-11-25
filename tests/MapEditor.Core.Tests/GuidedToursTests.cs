using MapEditor.Core.Tours;

namespace MapEditor.Core.Tests;

public sealed class GuidedToursTests
{
    [Fact]
    public void NextBackAndClose()
    {
        var runner = new TourRunner(GuidedTours.DrawIslands, _ => false);
        Assert.False(runner.CanGoBack);
        runner.Back();
        Assert.Equal("new-world", runner.Current?.Id);

        runner.Next();
        Assert.Equal("rectangle", runner.Current?.Id);
        Assert.True(runner.CanGoBack);
        runner.Back();
        Assert.Equal("new-world", runner.Current?.Id);

        runner.Close();
        runner.Next();
        runner.Back();
        runner.Update();
        Assert.True(runner.IsFinished);
        Assert.Null(runner.Current);
    }

    [Fact]
    public void CompletingAnActionAdvancesButBackCanReviewIt()
    {
        var state = new HashSet<TourCondition> { TourCondition.WorldOpen, TourCondition.OutsideLandMode };
        var runner = new TourRunner(GuidedTours.StagesAndTools, state.Contains);
        Assert.Equal("terrain", runner.Current?.Id);
        runner.Update();
        Assert.Equal("terrain", runner.Current?.Id);

        state.Add(TourCondition.TerrainGenerated);
        runner.Update();
        Assert.Equal("raise", runner.Current?.Id);
        runner.Back();
        runner.Update();
        Assert.Equal("terrain", runner.Current?.Id);
        runner.Next();
        Assert.Equal("raise", runner.Current?.Id);
    }

    [Fact]
    public void PresetWorldSkipsLandModeSteps()
    {
        var state = new HashSet<TourCondition> { TourCondition.OutsideLandMode };
        var runner = new TourRunner(GuidedTours.DrawIslands, state.Contains);
        Assert.Equal("new-world", runner.Current?.Id);

        state.Add(TourCondition.WorldOpen);
        runner.Update();
        Assert.Equal("terrain", runner.Current?.Id);
        Assert.False(runner.CanGoBack);
        Assert.Equal("Click Generate to make the terrain.", runner.Current?.Body(state.Contains));
    }

    [Fact]
    public void TourFromOfferStartsWithRectangleAndExplainsTerrainAfterBuild()
    {
        var state = new HashSet<TourCondition> { TourCondition.WorldOpen };
        var runner = new TourRunner(GuidedTours.DrawIslands, state.Contains);
        Assert.Equal("rectangle", runner.Current?.Id);

        state.Add(TourCondition.LandBoxDrawn);
        runner.Update();
        Assert.Equal("select-boxes", runner.Current?.Id);
        runner.Next();
        Assert.Equal("build-world", runner.Current?.Id);

        state.UnionWith([TourCondition.OutsideLandMode, TourCondition.DrawnTerrainBuilt, TourCondition.TerrainGenerated]);
        runner.Update();
        Assert.Equal("terrain", runner.Current?.Id);
        Assert.Contains("Build World made the terrain.", runner.Current?.Body(state.Contains));
        runner.Next();
        Assert.Equal("town-sites", runner.Current?.Id);
    }

    [Fact]
    public void TerrainTipTextFollowsTheBuiltWorldWhileShown()
    {
        var state = new HashSet<TourCondition> { TourCondition.WorldOpen, TourCondition.OutsideLandMode };
        var runner = new TourRunner(GuidedTours.DrawIslands, state.Contains);
        Assert.Equal("terrain", runner.Current?.Id);
        var before = runner.Tip;

        state.Add(TourCondition.DrawnTerrainBuilt);
        runner.Update();
        Assert.Equal("terrain", runner.Current?.Id);
        Assert.NotEqual(before, runner.Tip);
        Assert.Equal("Build World made the terrain. Click Next.", runner.Tip?.Body);

        state.Remove(TourCondition.DrawnTerrainBuilt);
        runner.Update();
        Assert.Equal("terrain", runner.Current?.Id);
        Assert.Equal(before, runner.Tip);
    }

    [Fact]
    public void TipDoesNotChangeWhenTheStepTextIgnoresTheWorld()
    {
        var state = new HashSet<TourCondition> { TourCondition.WorldOpen };
        var runner = new TourRunner(GuidedTours.DrawIslands, state.Contains);
        Assert.Equal("rectangle", runner.Current?.Id);
        var before = runner.Tip;

        state.Add(TourCondition.OutsideLandMode);
        runner.Update();
        Assert.Equal("terrain", runner.Current?.Id);

        var terrain = runner.Tip;
        runner.Update();
        Assert.Equal(terrain, runner.Tip);
        Assert.NotNull(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastStepFinishesByNextOrExport(bool exported)
    {
        var runner = new TourRunner(GuidedTours.StagesAndTools,
            condition => condition == TourCondition.Exported && exported);
        for (int i = 1; i < GuidedTours.StagesAndTools.Steps.Count; i++) runner.Next();
        if (exported)
        {
            // Forward navigation skips an export already completed.
            Assert.True(runner.IsFinished);
        }
        else
        {
            Assert.Equal("export", runner.Current?.Id);
            runner.Next();
            Assert.True(runner.IsFinished);
        }
        Assert.Null(runner.Current);
    }

    [Fact]
    public void ExportCompletesTheCurrentStep()
    {
        var state = new HashSet<TourCondition>(Enum.GetValues<TourCondition>());
        state.Remove(TourCondition.Exported);
        var runner = new TourRunner(GuidedTours.StagesAndTools, state.Contains);
        Assert.Equal("export", runner.Current?.Id);
        state.Add(TourCondition.Exported);
        runner.Update();
        Assert.True(runner.IsFinished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StepsHaveUniqueIdsAndText(bool islands)
    {
        var tour = islands ? GuidedTours.DrawIslands : GuidedTours.StagesAndTools;
        Assert.Equal(tour.Steps.Count, tour.Steps.Select(step => step.Id).Distinct().Count());
        Assert.All(tour.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Text));
        });
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    public void OfferFollowsTheSavedChoice(bool dismissed, bool started, bool dontShowAgain, bool showAgain)
    {
        Assert.Equal(!dismissed, TourOffer.ShouldShow(dismissed));
        Assert.Equal(showAgain, TourOffer.ShouldShow(TourOffer.IsDismissed(dismissed, started, dontShowAgain)));
    }
}
