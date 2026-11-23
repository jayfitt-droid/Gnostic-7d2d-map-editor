using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class PrefabGroupTests
{
    private static readonly PrefabInfo Tile = new("rwg_tile_test", "RWGTiles", new Int3(150, 20, 150), -12, 0, 0, ["streettile"], null);
    private static readonly PrefabInfo Poi = new("cabin", "POIs", new Int3(10, 20, 20), -1, 0, 1, [], null);
    private static readonly PrefabInfo Trader = Poi with { Name = "trader_test" };
    private static readonly PrefabInfo Part = Poi with { Name = "part_light", Size = new Int3(2, 3, 2), Tags = ["part"] };
    private static readonly PrefabInfo Bridge = Poi with { Name = "bridge_test" };
    private static readonly PrefabLibrary Library = PrefabLibrary.Of([Tile, Poi, Trader, Part, Bridge]);

    [Fact]
    public void Box_includes_only_whole_footprints_and_spawn_positions()
    {
        var world = Flat();
        var tile = Add(world, Tile, -200, -200);
        var rider = Add(world, Poi, -190, -180);
        var trader = Add(world, Trader, -60, -130);
        var turned = Add(world, Poi, -70, -150, rotation: 1);
        Add(world, Poi, -56, -120); // Its centre fits, but its east side crosses the box.
        Add(world, Poi, -210, -190);
        Add(world, Part, -185, -185);
        var bridgePiece = Add(world, Bridge, -150, -180);
        var unknown = new PrefabInstance { Name = "missing", Position = new Int3(-65, 40, -90) };
        world.Prefabs.Add(unknown);
        var edge = new SpawnPoint { X = -50, Z = -70 };
        world.Spawns.AddRange([edge, new SpawnPoint { X = -49.75f, Z = -70 }]);

        var hits = PrefabGroup.InBox(world, Library, 462, 462, 312, 312, p => p == bridgePiece);

        Assert.Equal([tile, rider, trader, turned, unknown], hits.Prefabs);
        Assert.Equal([edge], hits.Spawns);
    }

    [Fact]
    public void Move_carries_each_rider_once_levels_the_tiles_and_undoes_everything_together()
    {
        var world = Flat();
        var first = Add(world, Tile, -350, -200, y: 19);
        var second = Add(world, Tile, -200, -200, y: 24);
        var rider = Add(world, Poi, -340, -180, y: 33);
        var part = Add(world, Part, -338, -178, y: 34);
        var otherRider = Add(world, Trader, -180, -170, y: 37);
        var free = Add(world, Poi, -30, -200);
        var spawn = new SpawnPoint { X = -24.25f, Y = 41, Z = -190.5f, RotationY = 120 };
        world.Spawns.Add(spawn);
        SetGround(world, 0, -200, 150, 150, 60);
        SetGround(world, 150, -200, 150, 150, 100);
        SetGround(world, 320, -200, 10, 20, 90);
        var before = world.Prefabs.Select(p => p.Position).ToArray();
        var group = new PrefabGroup(world, Library, [rider, first, second, free], [spawn]);
        var undo = new UndoStack();
        var edits = new EditRunner(undo);

        var move = edits.Begin(world, "Move selection");
        Assert.True(edits.Step(move, s => Assert.Null(group.Apply(350, 0, 0, true, true, s))));
        Assert.True(edits.Commit(move));

        Assert.Equal(new Int3(0, 69, -200), first.Position);
        Assert.Equal(new Int3(150, 69, -200), second.Position);
        Assert.Equal(new Int3(10, 83, -180), rider.Position);
        Assert.Equal(new Int3(12, 84, -178), part.Position);
        Assert.Equal(new Int3(170, 82, -170), otherRider.Position);
        Assert.Equal(new Int3(320, 90, -200), free.Position);
        Assert.Equal((325.75f, 91f, -190.5f, 120f), (spawn.X, spawn.Y, spawn.Z, spawn.RotationY));
        Assert.Equal(80, Ground(world, 149, -200));
        Assert.Equal(80, Ground(world, 150, -200));
        Assert.Equal(90, Ground(world, 320, -200));

        Assert.Equal("Move selection", undo.Undo()!.Name);
        Assert.False(undo.CanUndo);
        Assert.Equal(before, world.Prefabs.Select(p => p.Position));
        var restoredSpawn = Assert.Single(world.Spawns);
        Assert.Equal((-24.25f, 41f, -190.5f, 120f), (restoredSpawn.X, restoredSpawn.Y, restoredSpawn.Z, restoredSpawn.RotationY));
        Assert.Equal(60, Ground(world, 149, -200));
        Assert.Equal(100, Ground(world, 150, -200));

        Assert.NotNull(undo.Redo());
        Assert.Equal(new Int3(10, 83, -180), world.Prefabs[2].Position);
        Assert.Equal(80, Ground(world, 149, -200));
        Assert.Equal(91, Assert.Single(world.Spawns).Y);
    }

    [Fact]
    public void Rotation_turns_all_members_about_one_centre()
    {
        var world = Flat();
        var a = Add(world, Poi, 0, 0);
        var b = Add(world, Trader, 30, 11);
        var spawn = new SpawnPoint { X = 20, Z = 15.5f, Y = 50, RotationY = 90 };
        world.Spawns.Add(spawn);
        var edits = new EditRunner(new UndoStack());

        Assert.True(edits.Run(world, "Rotate selection", s =>
            Assert.Null(new PrefabGroup(world, Library, [a, b], [spawn]).Apply(0, 0, 1, true, false, s))));

        Assert.Equal(new Int3(16, 40, -5), a.Position);
        Assert.Equal(new Int3(5, 40, 25), b.Position);
        Assert.Equal(1, a.Rotation);
        Assert.Equal(1, b.Rotation);
        Assert.Equal((20.5f, 41f, 15f, 0f), (spawn.X, spawn.Y, spawn.Z, spawn.RotationY));

        Assert.True(edits.Run(world, "Rotate selection", s =>
            Assert.Null(new PrefabGroup(world, Library, [a, b], [spawn]).Apply(0, 0, -1, true, false, s))));

        Assert.Equal(new Int3(0, 40, 0), a.Position);
        Assert.Equal(new Int3(30, 40, 11), b.Position);
        Assert.Equal(0, a.Rotation);
        Assert.Equal(0, b.Rotation);
        Assert.Equal((20f, 15.5f, 90f), (spawn.X, spawn.Z, spawn.RotationY));
    }

    [Fact]
    public void Rotation_refuses_to_overlap_an_unselected_poi()
    {
        var world = Flat();
        var a = Add(world, Poi, 0, 0);
        var b = Add(world, Trader, 30, 11);
        var blocker = Add(world, Poi, 34, -12); // Only its edge overlaps the turned footprint.
        var before = world.Prefabs.Select(p => (p.Position, p.Rotation)).ToArray();
        var group = new PrefabGroup(world, Library, [a, b], []);
        var undo = new UndoStack();
        string? problem = null;

        Assert.True(new EditRunner(undo).Run(world, "Rotate selection",
            s => problem = group.Apply(0, 0, 1, true, false, s)));

        Assert.Equal("The selection would turn onto cabin.", problem);
        Assert.Equal([a, b, blocker], world.Prefabs);
        Assert.Equal(before, world.Prefabs.Select(p => (p.Position, p.Rotation)));
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Rotation_carries_a_poi_on_its_selected_tile()
    {
        var world = Flat();
        var tile = Add(world, Tile, 0, 0);
        var rider = Add(world, Poi, 10, 20);
        var group = new PrefabGroup(world, Library, [tile], []);

        Assert.True(new EditRunner(new UndoStack()).Run(world, "Rotate selection",
            s => Assert.Null(group.Apply(0, 0, 1, false, false, s))));

        Assert.Equal([tile, rider], world.Prefabs);
        Assert.Equal((110, 10, 1), (rider.Position.X, rider.Position.Z, rider.Rotation));
        Assert.Equal(1, tile.Rotation);
    }

    [Fact]
    public void Rotation_allows_a_poi_to_turn_on_an_unselected_tile()
    {
        var world = Flat();
        var tile = Add(world, Tile, 0, 0);
        var poi = Add(world, Poi, 20, 20);
        var group = new PrefabGroup(world, Library, [poi], []);

        Assert.True(new EditRunner(new UndoStack()).Run(world, "Rotate selection",
            s => Assert.Null(group.Apply(0, 0, 1, false, false, s))));

        Assert.Equal([tile, poi], world.Prefabs);
        Assert.Equal((15, 25, 1), (poi.Position.X, poi.Position.Z, poi.Rotation));
        Assert.Equal((0, 0, 0), (tile.Position.X, tile.Position.Z, tile.Rotation));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Opposite_turns_restore_a_prefab_group_with_a_fractional_spawn(int step)
    {
        var world = Flat();
        var poi = Add(world, Poi, 0, 0);
        var spawn = new SpawnPoint { X = 20.5f, Y = 41, Z = 30.5f, RotationY = 90 };
        world.Spawns.Add(spawn);
        var edits = new EditRunner(new UndoStack());
        string? problem = null;

        foreach (int turn in new[] { step, -step })
        {
            Assert.True(edits.Run(world, "Rotate selection", s =>
                problem = new PrefabGroup(world, Library, [poi], [spawn]).Apply(0, 0, turn, false, false, s)));
            Assert.Null(problem);
        }

        Assert.Equal(new Int3(0, 40, 0), poi.Position);
        Assert.Equal(0, poi.Rotation);
        Assert.Equal((20.5f, 41f, 30.5f, 90f), (spawn.X, spawn.Y, spawn.Z, spawn.RotationY));
    }

    [Fact]
    public void Spawn_only_selection_turns_in_45_degree_steps()
    {
        var world = Flat();
        var a = new SpawnPoint { X = -10, Y = 41 };
        var b = new SpawnPoint { X = 10, Y = 41 };
        world.Spawns.AddRange([a, b]);

        Assert.True(new EditRunner(new UndoStack()).Run(world, "Rotate selection", s =>
            Assert.Null(new PrefabGroup(world, Library, [], [a, b]).Apply(0, 0, 1, true, false, s))));

        Assert.Equal(-7.071f, a.X, 3);
        Assert.Equal(-7.071f, a.Z, 3);
        Assert.Equal(7.071f, b.X, 3);
        Assert.Equal(7.071f, b.Z, 3);
        Assert.Equal(315, a.RotationY);
        Assert.Equal(315, b.RotationY);
    }

    [Fact]
    public void Opposite_turns_restore_an_uneven_spawn_only_group()
    {
        var world = Flat();
        var a = new SpawnPoint { Y = 41 };
        var b = new SpawnPoint { X = 80, Y = 41, RotationY = 90 };
        var c = new SpawnPoint { Z = 40, Y = 41, RotationY = 270 };
        world.Spawns.AddRange([a, b, c]);
        var before = world.Spawns.Select(s => (s.X, s.Y, s.Z, s.RotationY)).ToArray();
        var edits = new EditRunner(new UndoStack());
        string? problem = null;

        foreach (int turn in new[] { 1, -1 })
        {
            Assert.True(edits.Run(world, "Rotate selection", s =>
                problem = new PrefabGroup(world, Library, [], [a, b, c]).Apply(0, 0, turn, false, false, s)));
            Assert.Null(problem);
        }

        Assert.All(world.Spawns, (spawn, index) =>
        {
            Assert.Equal(before[index].X, spawn.X, 3);
            Assert.Equal(before[index].Y, spawn.Y);
            Assert.Equal(before[index].Z, spawn.Z, 3);
            Assert.Equal(before[index].RotationY, spawn.RotationY);
        });
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(10, 0)]
    [InlineData(0, 1)]
    public void A_prefab_footprint_outside_the_map_blocks_the_whole_edit(int dx, int steps)
    {
        var world = Flat();
        var edge = Add(world, Poi, 502, -20, y: 68);
        var other = Add(world, Trader, 490, 15, y: 52);
        var spawn = new SpawnPoint { X = 500, Y = 55, Z = 10, RotationY = 90 };
        world.Spawns.Add(spawn);
        SetGround(world, 490, -20, 20, 20, 80);
        var heights = world.Height.Data.ToArray();
        var group = new PrefabGroup(world, Library, [edge, other], [spawn]);
        var undo = new UndoStack();
        string? problem = null;

        Assert.True(new EditRunner(undo).Run(world, "Move selection",
            s => problem = group.Apply(dx, 0, steps, true, true, s)));

        Assert.Equal("The selection would move a prefab outside the map.", problem);
        Assert.Equal([edge, other], world.Prefabs);
        Assert.Equal(new Int3(502, 68, -20), edge.Position);
        Assert.Equal(new Int3(490, 52, 15), other.Position);
        Assert.Equal(0, edge.Rotation);
        Assert.Equal(0, other.Rotation);
        Assert.Equal((500f, 55f, 10f, 90f), (spawn.X, spawn.Y, spawn.Z, spawn.RotationY));
        Assert.Equal(heights, world.Height.Data);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Drop_replaces_covered_prefabs_and_their_contents()
    {
        var world = Flat();
        var moving = Add(world, Tile, -350, -200, y: 29);
        var rider = Add(world, Poi, -340, -180);
        var free = Add(world, Trader, -30, -200);
        Add(world, Tile, 0, -200);
        Add(world, Poi, 80, -150);
        Add(world, Poi, 316, -200); // Its centre is inside the free prefab's destination.
        var outside = Add(world, Poi, 329, -180);
        var group = new PrefabGroup(world, Library, [moving, free], []);
        var undo = new UndoStack();

        Assert.True(new EditRunner(undo).Run(world, "Move selection",
            s => Assert.Null(group.Apply(350, 0, 0, true, true, s))));

        Assert.Equal([moving, rider, free, outside], world.Prefabs);
        Assert.Equal(new Int3(10, 40, -180), rider.Position);
        Assert.NotNull(undo.Undo());
        Assert.Equal(7, world.Prefabs.Count);
    }

    [Fact]
    public void Part_overlap_with_a_tile_blocks_the_whole_drop()
    {
        var world = Flat();
        var moving = Add(world, Tile, -350, -200);
        var free = Add(world, Poi, -30, -200);
        var blocked = Add(world, Tile, 100, -200);
        var covered = Add(world, Poi, 320, -200);
        var group = new PrefabGroup(world, Library, [moving, free], []);
        var undo = new UndoStack();

        Assert.True(new EditRunner(undo).Run(world, "Move selection",
            s => Assert.Contains("partly overlap", group.Apply(350, 0, 0, true, true, s))));

        Assert.Equal([moving, free, blocked, covered], world.Prefabs);
        Assert.Equal(-350, moving.Position.X);
        Assert.Equal(-30, free.Position.X);
        Assert.False(undo.CanUndo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Part_overlap_with_a_bridge_blocks_the_drop(bool recorded)
    {
        var world = Flat();
        var moving = Add(world, Poi, -350, -200);
        var bridge = Add(world, recorded ? Part : Bridge, 9, -200);
        var group = new PrefabGroup(world, Library, [moving], [], p => recorded && p == bridge);
        var undo = new UndoStack();

        Assert.True(new EditRunner(undo).Run(world, "Move selection",
            s => Assert.Contains("overlap a bridge", group.Apply(350, 0, 0, true, true, s))));

        Assert.Equal([moving, bridge], world.Prefabs);
        Assert.Equal(-350, moving.Position.X);
        Assert.False(undo.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_drop_can_replace_a_wholly_covered_bridge_prefab_without_a_bridge_record(bool recorded)
    {
        var world = Flat();
        var moving = Add(world, Poi, -350, -200);
        var bridge = Add(world, Bridge, 0, -200);
        var group = new PrefabGroup(world, Library, [moving], [], p => recorded && p == bridge);
        var undo = new UndoStack();
        string? problem = null;

        Assert.True(new EditRunner(undo).Run(world, "Move selection",
            s => problem = group.Apply(350, 0, 0, true, true, s)));

        if (recorded)
        {
            Assert.Contains("overlap a bridge", problem);
            Assert.Equal([moving, bridge], world.Prefabs);
            Assert.False(undo.CanUndo);
        }
        else
        {
            Assert.Null(problem);
            Assert.Equal(moving, Assert.Single(world.Prefabs));
            Assert.True(undo.CanUndo);
        }
    }

    [Fact]
    public void Delete_removes_the_group_and_restores_it_with_one_undo()
    {
        var world = Flat();
        var tile = Add(world, Tile, -250, -150);
        var rider = Add(world, Poi, -230, -120);
        Add(world, Part, -225, -110);
        var selected = Add(world, Trader, 10, 20);
        var outside = Add(world, Poi, 80, 90);
        var spawn = new SpawnPoint { X = 144, Y = 50, Z = 160 };
        world.Spawns.Add(spawn);
        var before = world.Prefabs.Select(p => p.Name).ToArray();
        var group = new PrefabGroup(world, Library, [tile, rider, selected], [spawn]);
        var undo = new UndoStack();

        Assert.True(new EditRunner(undo).Run(world, "Delete selection", s => Assert.Null(group.Delete(s))));
        Assert.Equal(outside, Assert.Single(world.Prefabs));
        Assert.Empty(world.Spawns);

        Assert.Equal("Delete selection", undo.Undo()!.Name);
        Assert.False(undo.CanUndo);
        Assert.Equal(before, world.Prefabs.Select(p => p.Name));
        Assert.Equal(144, Assert.Single(world.Spawns).X);
    }

    [Theory]
    [InlineData(1, false, "Raised 1 prefab 1 block.")]
    [InlineData(1, true, "Raised 2 prefabs 1 block.")]
    [InlineData(-1, false, "Lowered 1 prefab 1 block.")]
    [InlineData(-1, true, "Lowered 2 prefabs 1 block.")]
    [InlineData(-2, true, "Lowered 2 prefabs 2 blocks.")]
    public void Height_nudge_moves_prefabs_and_riders_and_reports_the_selected_prefabs(
        int blocks, bool selectRider, string expectedStatus)
    {
        var world = Flat();
        var tile = Add(world, Tile, -250, -150, y: 29);
        var rider = Add(world, Poi, -230, -120);
        var otherRider = Add(world, Poi, -210, -120);
        var spawn = new SpawnPoint { X = 20, Y = 41, Z = 20 };
        world.Spawns.Add(spawn);
        var group = new PrefabGroup(world, Library, selectRider ? [tile, rider] : [tile], [spawn]);
        var undo = new UndoStack();
        string name = blocks > 0 ? "Raise selection" : "Lower selection";
        string? status = null;

        Assert.True(new EditRunner(undo).Run(world, name, s => status = group.NudgeHeight(blocks, s)));

        Assert.Equal(29 + blocks, tile.Position.Y);
        Assert.Equal(40 + blocks, rider.Position.Y);
        Assert.Equal(40 + blocks, otherRider.Position.Y);
        Assert.Equal(41, spawn.Y);
        Assert.Equal(40, Ground(world, -250, -150));
        Assert.Equal(name, undo.Undo()!.Name);
        Assert.False(undo.CanUndo);
        Assert.Equal(29, world.Prefabs[0].Position.Y);
        Assert.Equal(40, world.Prefabs[1].Position.Y);
        Assert.Equal(40, world.Prefabs[2].Position.Y);
        Assert.Equal(expectedStatus, status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Height_nudge_reports_a_problem_when_only_spawn_points_are_selected(int blocks)
    {
        var world = Flat();
        var spawn = new SpawnPoint { X = 20, Y = 41, Z = 20 };
        world.Spawns.Add(spawn);
        var group = new PrefabGroup(world, Library, [], [spawn]);
        var undo = new UndoStack();
        string? problem = null;

        Assert.True(new EditRunner(undo).Run(world, "Nudge height", s => problem = group.NudgeHeight(blocks, s)));

        Assert.Equal(41, spawn.Y);
        Assert.False(undo.CanUndo);
        Assert.Equal("Spawn points can't be raised or lowered. Select a prefab.", problem);
    }

    [Fact]
    public void Clipboard_round_trip_keeps_tiles_riders_spawns_and_height_offsets()
    {
        var source = Flat();
        var tile = Add(source, Tile, -150, -150, y: 31, rotation: 1);
        var poi = Add(source, Poi, -140, -120, y: 45, rotation: 3);
        poi.ExtraAttributes.Add(new("custom", "one & two"));
        var trader = Add(source, Trader, 20, -120, y: 38, rotation: 2);
        var spawn = new SpawnPoint { X = 36.25f, Z = -104.5f, Y = 44, RotationY = 135 };
        source.Spawns.Add(spawn);

        var copy = RoundTrip(source, [tile, trader], [spawn]);
        var target = Flat();
        Array.Fill(target.Height.Data, World.MetresToHeight(60));
        var targetLibrary = PrefabLibrary.Of([Tile, Poi with { YOffset = -3 }, Trader]);
        var group = copy.PreparePaste(target, targetLibrary, _ => false, out int skipped);

        var pasted = Paste(target, group, 0, 0);

        Assert.Null(pasted.Problem);
        Assert.Equal(0, skipped);
        Assert.Equal(4, copy.Count);
        var pastedTile = Assert.Single(pasted.Prefabs, p => p.Name == Tile.Name);
        var pastedPoi = Assert.Single(pasted.Prefabs, p => p.Name == Poi.Name);
        var pastedTrader = Assert.Single(pasted.Prefabs, p => p.Name == Trader.Name);
        var pastedSpawn = Assert.Single(pasted.Spawns);
        Assert.Equal((1, 3, 2), (pastedTile.Rotation, pastedPoi.Rotation, pastedTrader.Rotation));
        Assert.Equal((10, 30), (pastedPoi.Position.X - pastedTile.Position.X, pastedPoi.Position.Z - pastedTile.Position.Z));
        Assert.Equal((170, 30), (pastedTrader.Position.X - pastedTile.Position.X, pastedTrader.Position.Z - pastedTile.Position.Z));
        Assert.Equal((186.25f, 45.5f), (pastedSpawn.X - pastedTile.Position.X, pastedSpawn.Z - pastedTile.Position.Z));
        Assert.Equal((51, 63, 58, 64f, 135f),
            (pastedTile.Position.Y, pastedPoi.Position.Y, pastedTrader.Position.Y, pastedSpawn.Y, pastedSpawn.RotationY));
        Assert.True(pastedPoi.GroundLevel);
        Assert.Equal(-3, pastedPoi.GroundLevelOffset);
        Assert.Equal(poi.ExtraAttributes, pastedPoi.ExtraAttributes);
    }

    [Fact]
    public void Clipboard_round_trip_of_a_single_spawn_preserves_its_fractional_position_and_allows_a_turn()
    {
        var world = Flat();
        var spawn = new SpawnPoint { X = 11.25f, Z = -4.5f, Y = 43, RotationY = 0 };
        world.Spawns.Add(spawn);
        var group = RoundTrip(world, [], [spawn]).PreparePaste(world, Library, _ => false, out int skipped);
        var (dx, dz) = group.DeltaTo(31.25, 20.5);

        var pasted = Paste(world, group, dx, dz, steps: -1);

        Assert.Equal(0, skipped);
        Assert.Null(pasted.Problem);
        Assert.Empty(pasted.Prefabs);
        var added = Assert.Single(pasted.Spawns);
        Assert.NotSame(spawn, added);
        Assert.Equal((31.25f, 43f, 20.5f, 45f), (added.X, added.Y, added.Z, added.RotationY));
    }

    [Fact]
    public void Clipboard_skips_an_unknown_tile_but_pastes_its_known_poi()
    {
        var source = Flat();
        var tile = Add(source, Tile, 0, 0);
        var poi = Add(source, Poi, 20, 30, y: 47);
        var target = Flat();
        var group = RoundTrip(source, [tile], []).PreparePaste(target, PrefabLibrary.Of([Poi]), _ => false, out int skipped);

        var pasted = Paste(target, group, 0, 0);

        Assert.Null(pasted.Problem);
        Assert.Equal(1, skipped);
        var added = Assert.Single(target.Prefabs);
        Assert.Equal(poi.Name, added.Name);
        Assert.Equal(poi.Position.Y, added.Position.Y);
    }

    [Theory]
    [InlineData("ordinary clipboard text")]
    [InlineData("{\"Version\":1,\"Prefabs\":[],\"Spawns\":[]}")]
    [InlineData("Gnostic map selection\n{")]
    [InlineData("Gnostic map selection\n{\"Version\":1,\"Prefabs\":null,\"Spawns\":[]}")]
    [InlineData("Gnostic map selection\n{\"Version\":1,\"Prefabs\":[],\"Spawns\":[null]}")]
    [InlineData("Gnostic map selection\n{\"Version\":1,\"Prefabs\":[],\"Spawns\":[{\"X\":3}]}")]
    [InlineData("Gnostic map selection\n{\"Version\":1,\"Prefabs\":[],\"Spawns\":[{\"X\":1e99,\"Z\":0,\"Rotation\":0,\"HeightOffset\":0}]}")]
    [InlineData("Gnostic map selection\n{\"Version\":2,\"Prefabs\":[],\"Spawns\":[{\"X\":0,\"Z\":0,\"Rotation\":0,\"HeightOffset\":0}]}")]
    public void Clipboard_ignores_foreign_or_invalid_payloads(string text) => Assert.Null(PrefabClipboard.Parse(text));

    [Fact]
    public void Clipboard_ignores_oversized_payloads() =>
        Assert.Null(PrefabClipboard.Parse("Gnostic map selection\n" + new string(' ', 2_000_000)));

    [Fact]
    public void Paste_uses_the_rotated_preview_and_one_undo_restores_a_covered_poi_and_the_ground()
    {
        var source = Flat();
        var poi = Add(source, Poi, -20, -20, rotation: 1);
        var trader = Add(source, Trader, 30, 20);
        var spawn = new SpawnPoint { X = 35.5f, Y = 41, Z = 40, RotationY = 90 };
        source.Spawns.Add(spawn);
        var target = Flat();
        var group = RoundTrip(source, [poi, trader], [spawn]).PreparePaste(target, Library, _ => false, out _);
        var preview = group.Layout(150, 160, 1);
        var spot = Assert.Single(preview.Prefabs, p => p.Prefab.Name == Poi.Name).Spot;
        var covered = Add(target, Poi, spot.X, spot.Z);
        // A low cell in the footprint must be restored even when the median stays at 40.
        target.Height[target.WorldToGrid(spot.X), target.WorldToGrid(spot.Z)] = World.MetresToHeight(15);
        var undo = new UndoStack();

        var pasted = Paste(target, group, 150, 160, 1, flatten: true, undo: undo);

        Assert.Null(pasted.Problem);
        Assert.Equal(preview.Prefabs.Select(t => (t.Spot.X, t.Spot.Z, t.Spot.Rotation)),
            pasted.Prefabs.Select(p => (p.Position.X, p.Position.Z, p.Rotation)));
        Assert.Equal(2, pasted.Prefabs[0].Rotation);
        var turnedSpawn = Assert.Single(preview.Spawns);
        var addedSpawn = Assert.Single(target.Spawns);
        Assert.Equal(((float)turnedSpawn.X, (float)turnedSpawn.Z, 0f), (addedSpawn.X, addedSpawn.Z, addedSpawn.RotationY));
        Assert.DoesNotContain(covered, target.Prefabs);
        Assert.Equal(40f, Ground(target, spot.X, spot.Z));
        Assert.Equal("Paste selection", undo.Undo()!.Name);
        Assert.False(undo.CanUndo);
        Assert.Equal(covered.Position, Assert.Single(target.Prefabs).Position);
        Assert.Empty(target.Spawns);
        Assert.Equal(15f, Ground(target, spot.X, spot.Z));
        Assert.NotNull(undo.Redo());
        Assert.Equal(2, target.Prefabs.Count);
        Assert.Single(target.Spawns);
    }

    [Fact]
    public void A_paste_partly_outside_the_map_keeps_the_whole_destination_unchanged()
    {
        var source = Flat();
        var left = Add(source, Poi, -40, 0);
        var right = Add(source, Poi, 40, 0);
        var target = Flat();
        var group = RoundTrip(source, [left, right], []).PreparePaste(target, Library, _ => false, out _);
        var undo = new UndoStack();

        var pasted = Paste(target, group, 475, 0, flatten: true, undo: undo);

        Assert.Contains("outside the map", pasted.Problem);
        Assert.Empty(pasted.Prefabs);
        Assert.Empty(target.Prefabs);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Paste_snaps_the_whole_group_to_the_nearby_street_grid()
    {
        var source = Flat();
        var tile = Add(source, Tile, -150, 0);
        var poi = Add(source, Poi, -140, 20);
        var target = Flat();
        Add(target, Tile, 0, 0);
        var group = RoundTrip(source, [tile], []).PreparePaste(target, Library, _ => false, out _);
        var (dx, dz) = group.DeltaTo(228, 79);

        var pasted = Paste(target, group, dx, dz, snap: 10);

        Assert.Null(pasted.Problem);
        var addedTile = Assert.Single(pasted.Prefabs, p => p.Name == Tile.Name);
        var addedPoi = Assert.Single(pasted.Prefabs, p => p.Name == Poi.Name);
        Assert.Equal((150, 0), (addedTile.Position.X, addedTile.Position.Z));
        Assert.Equal((poi.Position.X - tile.Position.X, poi.Position.Z - tile.Position.Z),
            (addedPoi.Position.X - addedTile.Position.X, addedPoi.Position.Z - addedTile.Position.Z));
    }

    [Fact]
    public void Clipboard_excludes_protected_bridge_pieces_and_paste_cannot_replace_a_bridge()
    {
        var source = Flat();
        var tile = Add(source, Tile, 0, 0);
        var manual = Add(source, Bridge, 10, 20);
        var stage = Add(source, Bridge, 30, 10);
        var copy = PrefabClipboard.Capture(source, Library, [tile, stage], [], p => p == manual || p == stage, out int skipped);
        Assert.Equal(2, skipped);
        Assert.Equal(1, copy.Count);
        var target = Flat();
        var existing = Add(target, Bridge, 0, 0);
        var group = copy.PreparePaste(target, Library, p => p == existing, out _);

        var pasted = Paste(target, group, 0, 0);

        Assert.Contains("overlap a bridge", pasted.Problem);
        Assert.Same(existing, Assert.Single(target.Prefabs));
    }

    private static PrefabClipboard RoundTrip(World world, PrefabInstance[] prefabs, SpawnPoint[] spawns)
    {
        var copy = PrefabClipboard.Capture(world, Library, prefabs, spawns, _ => false, out _);
        return Assert.IsType<PrefabClipboard>(PrefabClipboard.Parse(copy.Serialize()));
    }

    private static GroupPasteResult Paste(World world, PrefabGroup group, int dx, int dz, int steps = 0,
        bool flatten = false, int snap = 0, UndoStack? undo = null)
    {
        GroupPasteResult? pasted = null;
        Assert.True(new EditRunner(undo ?? new UndoStack()).Run(world, "Paste selection",
            s => pasted = group.Paste(dx, dz, steps, flatten, s, snap)));
        return Assert.IsType<GroupPasteResult>(pasted);
    }

    private static World Flat()
    {
        var world = new World { Name = "Group", Size = 1024, Height = new Grid<ushort>(1024, 1024) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        return world;
    }

    private static PrefabInstance Add(World world, PrefabInfo info, int x, int z, int y = 40, int rotation = 0)
    {
        var prefab = new PrefabInstance { Name = info.Name, Position = new Int3(x, y, z), Rotation = rotation };
        world.Prefabs.Add(prefab);
        return prefab;
    }

    private static void SetGround(World world, int x, int z, int sx, int sz, int height)
    {
        int gx = world.WorldToGrid(x), gz = world.WorldToGrid(z);
        for (int row = gz; row < gz + sz; row++)
            world.Height.Row(row).Slice(gx, sx).Fill(World.MetresToHeight(height));
    }

    private static float Ground(World world, int x, int z) =>
        World.HeightToMetres(world.Height[world.WorldToGrid(x), world.WorldToGrid(z)]);
}
