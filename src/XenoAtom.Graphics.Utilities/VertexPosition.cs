using System.Numerics;

namespace XenoAtom.Graphics.Utilities
{
    public struct VertexPosition
    {
        public const byte SizeInBytes = 12;
        public const byte ElementCount = 1;

        public readonly Vector3 Position;

        public VertexPosition(Vector3 position)
        {
            Position = position;
        }
    }
}
