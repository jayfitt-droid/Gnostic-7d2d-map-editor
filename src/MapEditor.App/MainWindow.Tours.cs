using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MapEditor.App.Editing;
using MapEditor.Core.Generate;
using MapEditor.Core.Ops;
using MapEditor.Core.Tours;

namespace MapEditor.App;

public partial class MainWindow
{
    private TourRunner? _tour;
    private bool _showingTourOffer;
    private bool _tourUpdateQueued;
    private readonly HashSet<TourCondition> _tourActions = [];
    private FrameworkElement? _lastTownTarget;

    private void InitializeTours()
    {
        TourTip.BackRequested += (_, _) =>
        {
            _tour?.Back();
            ShowTourStep();
        };
        TourTip.NextRequested += (_, _) =>
        {
            if (_showingTourOffer)
            {
                SaveTourOfferChoice(started: true);
                StartTour(GuidedTours.DrawIslands);
                return;
            }
            _tour?.Next();
            ShowTourStep();
        };
        TourTip.CloseRequested += (_, _) =>
        {
            if (_showingTourOffer) SaveTourOfferChoice(started: false);
            EndTour();
        };
        _edits.Committed += (_, _) => QueueTourUpdate();
        _landTools.HistoryChanged += (_, _) => QueueTourUpdate();
        _tools.ToolEditCommitted += (_, tool) => RecordTourAction(tool switch
        {
            EditorTool.Raise => TourCondition.Raised,
            EditorTool.Smooth => TourCondition.Smoothed,
            EditorTool.Road => TourCondition.RoadDrawn,
            _ => TourCondition.None,
        });
        _townTools.TownBuilt += (_, _) => RecordTourAction(TourCondition.TownBuilt);
        _vm.PropertyChanged += TourViewChanged;
        _vm.Generate.PropertyChanged += TourViewChanged;
        RootDock.LayoutUpdated += (_, _) =>
        {
            if (TourTip.IsVisible) PositionTourTip();
        };
    }

    private void DrawIslandsTour_Click(object sender, RoutedEventArgs e) => StartTour(GuidedTours.DrawIslands);
    private void StagesAndToolsTour_Click(object sender, RoutedEventArgs e) => StartTour(GuidedTours.StagesAndTools);

    private void StartTour(Tour tour)
    {
        if (_vm.Generate.IsRunning) return;
        EndTour();
        _tour = new TourRunner(tour, TourConditionHolds);
        ShowTourStep();
    }

    private void EndTour()
    {
        _tour?.Close();
        _tour = null;
        _showingTourOffer = false;
        _tourActions.Clear();
        HideTourTip();
    }

    private void HideTourTip()
    {
        bool tipHadFocus = TourTip.IsKeyboardFocusWithin;
        TourTip.Visibility = Visibility.Collapsed;
        if (tipHadFocus) Map.Focus();
    }

    private void TourWorldChanging(bool creating)
    {
        if (!creating || _tour?.Current?.Target != TourTarget.NewWorld) EndTour();
    }

    private void TourWorldCreated()
    {
        if (_world is null) return;
        if (_tour is not null)
        {
            UpdateTour();
        }
        else if (TourOffer.ShouldShow(_settings.TourOfferDismissed))
        {
            _showingTourOffer = true;
            TourTip.ShowOffer();
            PositionTourTip();
        }
    }

    private void SaveTourOfferChoice(bool started)
    {
        bool dismissed = TourOffer.IsDismissed(_settings.TourOfferDismissed, started, TourTip.SuppressOffer);
        if (dismissed == _settings.TourOfferDismissed) return;
        _settings.TourOfferDismissed = dismissed;
        if (!_settings.Save()) _vm.Status = "Couldn't save the tour setting. Details are in crash.log.";
    }

    private void RecordTourAction(TourCondition action)
    {
        if (_tour is not null && action != TourCondition.None && _tourActions.Add(action)) QueueTourUpdate();
    }

    private bool TourConditionHolds(TourCondition condition) => condition switch
    {
        TourCondition.WorldOpen => _world is not null,
        TourCondition.OutsideLandMode => !_vm.IsLandMode,
        TourCondition.LandBoxDrawn => _vm.IsLandMode && _landTools.BoxNames.Count > 0,
        TourCondition.DrawnTerrainBuilt => !_vm.IsLandMode && _vm.Generate.TerrainDone &&
            _projectSettings.Generator?.Terrain.Shape is { Kind: LandShapeKind.Drawn, Drawing: not null },
        TourCondition.TerrainGenerated => _vm.Generate.TerrainDone,
        TourCondition.TownSitesGenerated => _vm.Generate.TownDone,
        TourCondition.FeaturesGenerated => _vm.Generate.FeatureDone,
        TourCondition.HighwaysGenerated => _vm.Generate.HighwayDone,
        TourCondition.WildernessGenerated => _vm.Generate.WildernessDone,
        TourCondition.SpawnsGenerated => _vm.Generate.SpawnDone,
        _ => _tourActions.Contains(condition),
    };

    private void TourViewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == _vm.Generate && e.PropertyName == nameof(GenerateViewModel.IsRunning))
            TourTip.SetNavigationEnabled(!_vm.Generate.IsRunning);
        if (e.PropertyName is nameof(MainViewModel.IsLandMode) or nameof(MainViewModel.IsBusy)
            or nameof(GenerateViewModel.TerrainDone) or nameof(GenerateViewModel.TownDone)
            or nameof(GenerateViewModel.FeatureDone) or nameof(GenerateViewModel.HighwayDone)
            or nameof(GenerateViewModel.WildernessDone) or nameof(GenerateViewModel.SpawnDone)
            or nameof(GenerateViewModel.HasPlan))
            QueueTourUpdate();
    }

    private void QueueTourUpdate()
    {
        if (_tour is null || _tourUpdateQueued) return;
        _tourUpdateQueued = true;
        // Read the stage and Land state after the command finishes updating its view model.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _tourUpdateQueued = false;
            UpdateTour();
        });
    }

    private void UpdateTour()
    {
        if (_tour is null || _vm.IsBusy) return;
        var previous = _tour.Current;
        _tour.Update();
        if (_tour.Current != previous) ShowTourStep();
        else if (previous?.Target == TourTarget.TownSites && TownsTourTarget() != _lastTownTarget)
            RevealTourTarget(previous);
    }

    private void ShowTourStep()
    {
        if (_tour?.Current is not { } step)
        {
            EndTour();
            return;
        }

        int stage = TourStage(step.Target);
        if (stage > 0 && !_vm.IsLandMode && _world is not null)
        {
            _vm.ShowSidePanel = true;
            _vm.SidePanelTab = MainViewModel.GenerateTab;
            _vm.Generate.OpenStage = stage;
        }
        TourTip.ShowStep(step.Title, step.Body(TourConditionHolds), _tour.CanGoBack);
        RevealTourTarget(step);
        PositionTourTip();
    }

    private void RevealTourTarget(TourStep step)
    {
        if (step.Target == TourTarget.TownSites) _lastTownTarget = TownsTourTarget();
        if (TourStage(step.Target) == 0) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_tour?.Current == step && FindTourTarget(step.Target) is { IsVisible: true } target)
                target.BringIntoView();
        });
    }

    private static int TourStage(TourTarget target) => target switch
    {
        TourTarget.Terrain => 1,
        TourTarget.TownSites => 2,
        TourTarget.Features => 3,
        TourTarget.Highways => 4,
        TourTarget.Wilderness => 5,
        TourTarget.Spawns => 6,
        _ => 0,
    };

    private FrameworkElement TownsTourTarget() => _vm.Generate.HasPlan ? BuildTownsTarget : PlanTownsTarget;

    private FrameworkElement? FindTourTarget(TourTarget target) => target switch
    {
        TourTarget.NewWorld => NewWorldTarget,
        TourTarget.Rectangle => RectangleTarget,
        TourTarget.SelectBoxes => SelectBoxesTarget,
        TourTarget.BuildWorld => BuildWorldTarget,
        TourTarget.Terrain => GenerateTerrainTarget,
        TourTarget.TownSites => TownsTourTarget(),
        TourTarget.Features => GenerateFeaturesTarget,
        TourTarget.Highways => GenerateHighwaysTarget,
        TourTarget.Wilderness => GenerateWildernessTarget,
        TourTarget.Spawns => GenerateSpawnsTarget,
        TourTarget.Raise => ToolTarget(TerrainGroup, EditorTool.Raise),
        TourTarget.Smooth => ToolTarget(TerrainGroup, EditorTool.Smooth),
        TourTarget.Town => ToolTarget(BuildGroup, EditorTool.Town),
        TourTarget.Road => ToolTarget(BuildGroup, EditorTool.Road),
        TourTarget.Validate => ValidateAction,
        TourTarget.Export => ExportAction,
        _ => null,
    };

    private static FrameworkElement? ToolTarget(ListBox group, EditorTool tool) =>
        group.Items.OfType<ToolItem>().FirstOrDefault(item => item.Tool == tool) is { } item
            ? group.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement
            : null;

    private void PositionTourTip()
    {
        if (TourLayer.RenderSize.Width == 0 || TourLayer.RenderSize.Height == 0) return;
        var bounds = RootDock.TransformToVisual(TourLayer).TransformBounds(new Rect(RootDock.RenderSize));
        bounds.Intersect(new Rect(TourLayer.RenderSize));
        if (_showingTourOffer)
        {
            var anchor = _vm.IsLandMode ? (FrameworkElement)RectangleTarget : GenerateAction;
            TourTip.Place(bounds, VisibleTourTarget(anchor, bounds), preferSide: false);
        }
        else if (_tour?.Current?.Target is { } key)
            TourTip.Place(bounds, VisibleTourTarget(FindTourTarget(key), bounds), preferSide: TourStage(key) > 0);
    }

    private Rect VisibleTourTarget(FrameworkElement? target, Rect bounds)
    {
        if (target is not { IsVisible: true } || target.RenderSize.Width == 0 || target.RenderSize.Height == 0
            || !RootDock.IsAncestorOf(target)) return Rect.Empty;

        var visible = target.TransformToVisual(TourLayer).TransformBounds(new Rect(target.RenderSize));
        visible.Intersect(bounds);
        for (DependencyObject? parent = target; parent is Visual visual && !visible.IsEmpty; parent = VisualTreeHelper.GetParent(parent))
        {
            if (VisualTreeHelper.GetClip(visual) is { } clip)
                visible.Intersect(visual.TransformToVisual(TourLayer).TransformBounds(clip.Bounds));
            if (visual == RootDock) break;
        }
        return visible.Width > 0 && visible.Height > 0 ? visible : Rect.Empty;
    }
}
