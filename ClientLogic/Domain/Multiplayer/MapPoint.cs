using System;

namespace DTAClient.Domain.Multiplayer
{
    /// <summary>
    /// A point in map or map preview coordinates.
    /// </summary>
    /// <remarks>
    /// Has the same public fields as the XNA Point it replaces, so the custom map cache
    /// (serialized with IncludeFields) keeps the same format.
    /// </remarks>
    public struct MapPoint : IEquatable<MapPoint>
    {
        public int X;
        public int Y;

        public MapPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(MapPoint other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is MapPoint other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public static bool operator ==(MapPoint left, MapPoint right) => left.Equals(right);

        public static bool operator !=(MapPoint left, MapPoint right) => !left.Equals(right);

        public override string ToString() => $"{{X:{X} Y:{Y}}}";
    }
}
