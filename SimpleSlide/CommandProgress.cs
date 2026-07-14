using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleSlide
{
    /// <summary>
    /// https://jeremybytes.blogspot.com/2016/12/simple-progress-reporting-with-task.html
    /// </summary>
    internal class CommandProgress<T> : IProgress<T>
    {
        public enum CommandSignals
        {
            MovementUnderway, MovementCleared
        }

        public Boolean MovementUderway { get; set; }

        public CommandProgress()
        {
        }

        public CommandProgress(Action<T> handler) : base(handler)
        {
        }

        public void Report(T value)
        {
            throw new NotImplementedException();
        }
    }
}
