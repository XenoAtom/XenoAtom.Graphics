using System;

namespace XenoAtom.Graphics
{
    internal static class Illegal
    {
        internal static Exception Value<T>()
        {
            return new IllegalValueException<T>();
        }

        internal class IllegalValueException<T> : GraphicsException
        {
        }
    }
}
