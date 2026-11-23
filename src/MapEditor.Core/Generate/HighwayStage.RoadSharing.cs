using System.Numerics;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

public static partial class HighwayStage
{
    /// <summary>Distance in grid cells at which a new highway starts to avoid running beside another road.</summary>
    private const int RoadProximity = 5;

    /// <summary>Movement cost on a connected stretch of existing road, as a share of the cost on open land.</summary>
    private const float SharedRoadCost = 0.25f;

    /// <summary>Extra movement cost beside a road, scaled down to zero at the edge of its proximity.</summary>
    private const float BesideRoadCost = 2.5f;

    /// <summary>Octile distance weight for the road-sharing search. The usual 1.4 weight favors going straight.</summary>
    private const float SharingHeuristic = 1.25f;

    /// <summary>Share of the beside-road surcharge paid on a step that approaches or leaves the road.</summary>
    private const float RoadApproachCost = 0.1f;

    /// <summary>Maximum distance in metres from the new centreline to a road it can join.</summary>
    private const float JoinReach = 40;

    /// <summary>Minimum centreline length in metres before a stretch beside a road is replaced by it.</summary>
    private const float JoinLength = 150;

    /// <summary>A shorter stretch can be reused if the route already follows the existing pavement.</summary>
    private const float OnRoadJoinLength = 30;

    /// <summary>Distance along an approach to look for an earlier crossing of the road it joins.</summary>
    private const float JunctionApproach = 150;

    /// <summary>Maximum length of an existing connection that can replace a short turn at a junction.</summary>
    private const float JunctionConnection = 225;

    private static bool IsRoad(byte paint) => paint is (byte)RoadType.Asphalt or (byte)RoadType.Gravel;

    internal sealed partial class PathGrid
    {
        private const byte JunctionObstacles = Blocked | NoJoin | HighwayClearance;
        private readonly Vector2[] _roadPoints;
        private readonly byte[] _roadEdges;
        private readonly HashSet<Vector2> _roadEnds = [];
        // One-based index of the closest road cell, or zero outside the proximity band.
        private readonly int[] _nearRoad;
        private readonly byte[] _roadDistance;
        public bool[] LegacyRoad { get; }
        public bool HasRoads { get; private set; }

        public Vector2 RoadPoint(int cell) => _roadPoints[cell];
        public bool HasRoadEnd(Vector2 point) => _roadEnds.Contains(point);

        public float RoadCost(int from, int to, int direction)
        {
            byte flags = Flags[to];
            if ((flags & Road) != 0)
            {
                if ((flags & NoJoin) != 0) return 1;
                return (Flags[from] & Road) == 0 || (_roadEdges[from] & (1 << direction)) != 0 ? SharedRoadCost : 1;
            }
            int near = _nearRoad[to] - 1;
            if (near < 0 || (Flags[near] & JunctionObstacles) != 0) return 1;
            int distance = _roadDistance[to];
            float crossing = _roadDistance[from] == distance ? 1 : RoadApproachCost;
            return 1 + BesideRoadCost * crossing * (1 - MathF.Sqrt(distance - 1) / RoadProximity);
        }

        private void IndexExistingRoads(Grid<byte> roads)
        {
            for (int cell = 0; cell < Flags.Length; cell++)
            {
                var centre = Centre(cell);
                int x = (int)centre.X, z = (int)centre.Y;
                LegacyRoad[cell] = IsRoad(roads[x, z]);
                if ((Flags[cell] & (Blocked | Water | NoJoin)) != 0) continue;
                if (LegacyRoad[cell])
                {
                    AddRoadCell(cell, new Vector2(x + 0.5f, z + 0.5f));
                    continue;
                }
                float closest = float.MaxValue;
                Vector2 point = default;
                for (int dz = -Step / 2; dz < Step / 2; dz++)
                {
                    var row = roads.Row(z + dz).Slice(x - Step / 2, Step);
                    for (int dx = 0; dx < Step; dx++)
                    {
                        if (!IsRoad(row[dx])) continue;
                        var paint = new Vector2(x + dx - Step / 2 + 0.5f, z + dz + 0.5f);
                        float distance = Vector2.DistanceSquared(centre, paint);
                        if (distance >= closest) continue;
                        closest = distance;
                        point = paint;
                    }
                }
                if (closest < float.MaxValue) AddRoadCell(cell, point);
            }

            // Adjacent grid cells can contain separate roads. Link them only across continuous paint.
            for (int cell = 0; cell < Flags.Length; cell++)
            {
                if ((Flags[cell] & Road) == 0) continue;
                for (int direction = 0; direction < 4; direction++)
                {
                    int next = RoadNeighbour(cell, direction);
                    if ((Flags[next] & Road) == 0) continue;
                    Vector2 a = RoadPoint(cell), b = RoadPoint(next);
                    int steps = (int)MathF.Ceiling(Vector2.Distance(a, b) * 2);
                    bool continuous = true;
                    for (int i = 1; i < steps && continuous; i++)
                    {
                        var p = Vector2.Lerp(a, b, (float)i / steps);
                        continuous = IsRoad(roads[(int)p.X, (int)p.Y]);
                    }
                    if (continuous) LinkRoadCells(cell, next, direction);
                }
            }
        }

        /// <summary>Indexes the actual drawn centreline, including its bends and its junction endpoints.</summary>
        public List<int> IndexRoad(List<Vector2> line)
        {
            _roadEnds.Add(line[0]);
            _roadEnds.Add(line[^1]);
            var cells = new List<int>();
            int previous = -1;
            foreach (var point in line)
            {
                int cell = CellOf(point);
                AddRoadCell(cell, point);
                if (cell == previous) continue;
                cells.Add(cell);
                if (previous >= 0)
                    for (int direction = 0; direction < Neighbours8.Length; direction++)
                        if (RoadNeighbour(previous, direction) == cell)
                        {
                            LinkRoadCells(previous, cell, direction);
                            break;
                        }
                previous = cell;
            }
            return cells;
        }

        private void AddRoadCell(int cell, Vector2 point)
        {
            if ((Flags[cell] & Road) != 0) return;
            HasRoads = true;
            Flags[cell] |= Road;
            _roadPoints[cell] = point;
            int x = cell % Width, z = cell / Width;
            for (int nz = Math.Max(0, z - RoadProximity); nz <= Math.Min(Width - 1, z + RoadProximity); nz++)
                for (int nx = Math.Max(0, x - RoadProximity); nx <= Math.Min(Width - 1, x + RoadProximity); nx++)
                {
                    int distance = (nx - x) * (nx - x) + (nz - z) * (nz - z);
                    if (distance > RoadProximity * RoadProximity) continue;
                    int i = nx + nz * Width;
                    if (_roadDistance[i] != 0 && distance + 1 >= _roadDistance[i]) continue;
                    _nearRoad[i] = cell + 1;
                    _roadDistance[i] = (byte)(distance + 1);
                }
        }

        private int RoadNeighbour(int cell, int direction)
        {
            var (dx, dz) = Neighbours8[direction];
            return cell + dx + dz * Width;
        }

        private void LinkRoadCells(int a, int b, int direction)
        {
            _roadEdges[a] |= (byte)(1 << direction);
            _roadEdges[b] |= (byte)(1 << ((direction + 4) % Neighbours8.Length));
        }

        public int RoadNear(Vector2 point)
        {
            int cell = _nearRoad[CellOf(point)] - 1;
            return cell >= 0 && (Flags[cell] & JunctionObstacles) == 0 &&
                Vector2.DistanceSquared(point, RoadPoint(cell)) <= JoinReach * JoinReach ? cell : -1;
        }

        /// <summary>Earlier road cells connected within the distance of a local junction.</summary>
        public HashSet<int> LocalRoads(int start, int townExit = -1)
        {
            var distances = new Dictionary<int, float> { [start] = 0 };
            var open = new PriorityQueue<int, float>();
            open.Enqueue(start, 0);
            while (open.TryDequeue(out int cell, out float distance))
            {
                if (distance > distances[cell]) continue;
                for (int k = 0; k < Neighbours8.Length; k++)
                {
                    if ((_roadEdges[cell] & (1 << k)) == 0) continue;
                    int next = RoadNeighbour(cell, k);
                    // Existing pavement can go through the clearance zone to its own street exit.
                    if ((Flags[next] & NoJoin) != 0 || (next != townExit && (Flags[next] & Blocked) != 0)) continue;
                    float length = distance + ((k & 1) == 0 ? Step : Step * MathF.Sqrt(2));
                    if (length > JunctionConnection || length >= distances.GetValueOrDefault(next, float.MaxValue)) continue;
                    distances[next] = length;
                    if (next != townExit) open.Enqueue(next, length);
                }
            }
            return distances.Keys.ToHashSet();
        }

        /// <summary>Successive projections must follow connected pavement in the same part of the corridor.</summary>
        public bool FollowsRoad(int from, int to, int previous = -1, int steps = 3)
        {
            if (from == to) return true;
            if (steps == 0) return false;
            for (int k = 0; k < Neighbours8.Length; k++)
            {
                if ((_roadEdges[from] & (1 << k)) == 0) continue;
                int next = RoadNeighbour(from, k);
                if (next != previous && (Flags[next] & JunctionObstacles) == 0 && FollowsRoad(next, to, from, steps - 1)) return true;
            }
            return false;
        }

        /// <summary>A junction must be reachable on the highway grid, including diagonal and street clearance.</summary>
        public bool CanJoin(List<Vector2> connector, Exit from, Exit? to, bool country = false)
        {
            int obstacles = Blocked | NoJoin | (country ? CountryClearance : HighwayClearance);
            int last = CellOf(connector[0]);
            if ((Flags[last] & obstacles) != 0) return false;
            foreach (var p in connector)
            {
                int next = CellOf(p);
                if (next == last) continue;
                if ((Flags[next] & obstacles) != 0 || MathF.Abs(Height[next] - Height[last]) > (country ? 12 : 11)) return false;
                int x = next % Width, z = next / Width, lx = last % Width, lz = last / Width;
                if (x != lx && z != lz && ((Flags[x + lz * Width] | Flags[lx + z * Width]) & obstacles) != 0) return false;
                last = next;
            }
            return ClearsStreets(connector, country, from, to);
        }
    }

    /// <summary>Replaces continuous stretches along earlier roads with pieces that end on that pavement.</summary>
    private static List<Planned> JoinExistingRoads(PathGrid grid, Planned road)
    {
        // The two approaches to a bridge must still meet its ramps. Country roads already end on a highway.
        if (!grid.HasRoads || road.FromExit is not { } from) return [road];
        if (road.Country)
        {
            var approach = TrimRoadPiece(grid, road.Line, from, null, country: true, joinsStart: false, joinsEnd: true);
            if (approach is null) return [];
            return approach == road.Line ? [road] : [JoinedPiece(approach)];
        }
        if (road.ToExit is not { } to) return [road];
        var line = road.Line;
        var pieces = new List<Planned>();
        int start = 0;
        List<Vector2>? departure = null;
        var nearby = line.Select(grid.RoadNear).ToArray();
        for (int i = 1; i < line.Count - 1;)
        {
            if (nearby[i] < 0)
            {
                i++;
                continue;
            }
            int end = i;
            float length = 0, onRoad = 0;
            while (end + 1 < line.Count - 1 && nearby[end + 1] >= 0 && grid.FollowsRoad(nearby[end], nearby[end + 1]))
            {
                float step = Vector2.Distance(line[end], line[end + 1]);
                length += step;
                if (Vector2.DistanceSquared(line[end], grid.RoadPoint(nearby[end])) <= HighwayRadius * HighwayRadius)
                    onRoad += step;
                end++;
            }
            float minimum = onRoad >= length * 0.8f ? OnRoadJoinLength : JoinLength;
            if (length >= minimum)
            {
                int enter = i, leave = end;
                while (enter < leave && Connector(enter) is null) enter++;
                while (leave > enter && Connector(leave) is null) leave--;
                if (Vector2.Distance(grid.RoadPoint(nearby[enter]), grid.RoadPoint(nearby[leave])) >= minimum * 0.6f)
                {
                    var arrival = Connector(enter)!;
                    var piece = Piece(start, enter);
                    piece.AddRange(arrival.Skip(1));
                    AddPiece(piece, departure is not null, joinsEnd: true);
                    departure = Connector(leave)!;
                    departure.Reverse();
                    start = leave;
                }
            }
            i = end + 1;
        }
        if (departure is null) return [road];
        AddPiece(Piece(start, line.Count - 1), joinsStart: true, joinsEnd: false);
        return pieces;

        List<Vector2>? Connector(int at)
        {
            var connector = RoadOp.Centreline([line[at], grid.RoadPoint(nearby[at])], 0);
            return grid.CanJoin(connector, from, to) ? connector : null;
        }

        List<Vector2> Piece(int first, int last)
        {
            List<Vector2> piece = departure is null ? [] : [.. departure.Take(departure.Count - 1)];
            piece.AddRange(line.GetRange(first, last - first + 1));
            return piece;
        }

        void AddPiece(List<Vector2> piece, bool joinsStart, bool joinsEnd)
        {
            if (TrimRoadPiece(grid, piece, from, to, country: false, joinsStart, joinsEnd) is { } approach)
                pieces.Add(JoinedPiece(approach));
        }

        Planned JoinedPiece(List<Vector2> piece) =>
            new(road.Kind, new RoadRoute(piece, piece, piece.Select(grid.CellOf).ToList(), Length(piece), from, road.ToExit), road.From, road.To)
            {
                JoinsRoad = true,
            };
    }

    /// <summary>Keeps only the new pavement needed to meet a connected road or to reach a town exit.</summary>
    internal static List<Vector2>? TrimRoadPiece(PathGrid grid, List<Vector2> piece, Exit from, Exit? to,
        bool country, bool joinsStart, bool joinsEnd)
    {
        var result = piece;
        if (joinsEnd)
        {
            if (Trim(result, joinsStart) is not { } end) return null;
            result = end;
        }
        if (joinsStart)
        {
            var reversed = result.AsEnumerable().Reverse().ToList();
            if (Trim(reversed, joinsEnd) is not { } start) return null;
            result = start;
            result.Reverse();
        }
        return result;

        List<Vector2>? Trim(List<Vector2> approach, bool otherEndJoinsRoad)
        {
            int start = grid.CellOf(approach[0]);
            bool townAlreadyReached = !otherEndJoinsRoad && grid.HasRoadEnd(approach[0]);
            var local = grid.LocalRoads(grid.CellOf(approach[^1]), townAlreadyReached ? start : -1);
            if ((otherEndJoinsRoad || townAlreadyReached) && local.Contains(start) && Length(approach) <= JunctionApproach)
                return null;
            // A route may cross connected pavement before it gets to the planned junction. End at that
            // crossing so the tail does not cut the corner between two roads that already meet.
            float distance = 0;
            int trim = -1;
            List<Vector2>? connection = null;
            for (int i = approach.Count - 2; i > 0; i--)
            {
                distance += Vector2.Distance(approach[i], approach[i + 1]);
                if (distance > JunctionApproach) break;
                int near = grid.RoadNear(approach[i]);
                if (!local.Contains(near) ||
                    Vector2.DistanceSquared(approach[i], grid.RoadPoint(near)) > (HighwayRadius - 1) * (HighwayRadius - 1)) continue;
                var candidate = RoadOp.Centreline([approach[i], grid.RoadPoint(near)], 0);
                if (!grid.CanJoin(candidate, from, to, country)) continue;
                trim = i;
                connection = candidate;
            }
            return connection is null ? approach : [.. approach.Take(trim + 1), .. connection.Skip(1)];
        }
    }
}
