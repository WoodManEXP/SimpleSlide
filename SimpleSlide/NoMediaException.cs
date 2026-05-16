using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleSlide
{
    /// <summary>
    /// Custom exception raised in event of no media available
    /// </summary>
    internal class NoMediaException : System.Exception
    {
        public NoMediaException()
        {
        }
        public NoMediaException(string message)
    : base(message)
        {
        }

        public NoMediaException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
