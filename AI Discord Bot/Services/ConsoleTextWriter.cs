using System.IO;
using System.Text;

namespace AI_Discord_Bot.Services;

public class ConsoleTextWriter : TextWriter
{
    private readonly Action<string> _append;
    private readonly StringBuilder _lineBuffer = new();
    private readonly object _lock = new();

    public ConsoleTextWriter(Action<string> append)
    {
        _append = append;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_lock)
        {
            if (value == '\n')
            {
                _append(_lineBuffer.ToString());
                _lineBuffer.Clear();
                return;
            }

            if (value == '\r')
                return;

            _lineBuffer.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        lock (_lock)
        {
            foreach (var c in value)
            {
                if (c == '\n')
                {
                    _append(_lineBuffer.ToString());
                    _lineBuffer.Clear();
                }
                else if (c == '\r')
                {
                    // skip
                }
                else
                {
                    _lineBuffer.Append(c);
                }
            }
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_lock)
        {
            _append(_lineBuffer.ToString() + (value ?? ""));
            _lineBuffer.Clear();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _lineBuffer.Length > 0)
        {
            _append(_lineBuffer.ToString());
            _lineBuffer.Clear();
        }
        base.Dispose(disposing);
    }
}
