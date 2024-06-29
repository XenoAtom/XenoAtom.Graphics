
namespace XenoAtom.Graphics;

public readonly record struct GraphicsVersion(int Major, int Minor, int Subminor, int Patch)
{
    public static GraphicsVersion Unknown => default;

    public bool IsKnown => Major != 0 && Minor != 0 && Subminor != 0 && Patch != 0;

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Subminor}.{Patch}";
    }
}