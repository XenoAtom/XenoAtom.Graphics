using System;

namespace XenoAtom.Graphics
{
    /// <summary>
    /// Represents errors that occur in the XenoAtom.Graphics library.
    /// </summary>
    public class GraphicsException : Exception
    {
        /// <summary>
        /// Constructs a new VeldridException.
        /// </summary>
        public GraphicsException()
        {
        }

        /// <summary>
        /// Constructs a new Veldridexception with the given message.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public GraphicsException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructs a new Veldridexception with the given message and inner exception.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception.</param>
        public GraphicsException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
