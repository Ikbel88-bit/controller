using System;

namespace RoboClashController
{
    public sealed class ControllerIpEntry
    {
        public string Value { get; private set; } = string.Empty;
        public event Action Changed;

        public void Set(string value)
        {
            Value = value ?? string.Empty;
            Changed?.Invoke();
        }

        public void Append(char character)
        {
            if (Value.Length >= 15 || !(char.IsDigit(character) || character == '.'))
                return;

            Value += character;
            Changed?.Invoke();
        }

        public void Backspace()
        {
            if (Value.Length == 0)
                return;

            Value = Value.Substring(0, Value.Length - 1);
            Changed?.Invoke();
        }

        public void Clear()
        {
            Value = string.Empty;
            Changed?.Invoke();
        }
    }
}
