using System;

namespace SimpleSlide
{
    public enum CommandSignals
    {
        MovementUnderway, MovementNotUnderway
    }

    /// <summary>
    /// Catches and holds status from command processing 
    /// </summary>
    /// <remarks>
    /// Progress/IProgress is a recommended method for inter-thread communications. This
    /// latches onto it to help the UI and Player threads share status info.
    /// </remarks>
    internal class CommandProgress<T> : Progress<T>
    {
        public CommandSignals CurrentMovement { get; set; } = CommandSignals.MovementNotUnderway;
        public CommandProgress() : base() { }
        public CommandProgress(Action<T> handler) : base(handler)
        {
        }
        protected override void OnReport(T value)
        {
            switch(value)
            {
                case CommandSignals.MovementUnderway:
                    CurrentMovement = CommandSignals.MovementUnderway;
                    break;
                case CommandSignals.MovementNotUnderway:
                    CurrentMovement = CommandSignals.MovementNotUnderway;
                    break;
                default:
                    break;
            }
            base.OnReport(value);
        }
    }
}
