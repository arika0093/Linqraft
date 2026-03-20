using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Linqraft.Core.Formatting;

internal sealed class IndentedStringBuilder
{
    private readonly StringBuilder _builder = new();
    private readonly string _indentToken;
    private int _indent;

    public IndentedStringBuilder(string indentToken = "    ")
    {
        _indentToken = indentToken;
    }

    public void IncreaseIndent()
    {
        _indent++;
    }

    public void DecreaseIndent()
    {
        if (_indent == 0)
        {
            throw new InvalidOperationException("Indent cannot be negative.");
        }

        _indent--;
    }

    public void AppendLine()
    {
        _builder.AppendLine();
    }

    public void AppendLine(string line, CancellationToken cancellationToken = default)
    {
        if (line.Length != 0)
        {
            for (var index = 0; index < _indent; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _builder.Append(_indentToken);
            }
        }

        _builder.AppendLine(line);
    }

    public void AppendLines(string value, CancellationToken cancellationToken = default)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendLine(line, cancellationToken);
        }
    }

    public void Append(string value)
    {
        _builder.Append(value);
    }

    public void AppendIndented(string value)
    {
        for (var index = 0; index < _indent; index++)
        {
            _builder.Append(_indentToken);
        }

        _builder.Append(value);
    }

    public IDisposable Indent()
    {
        IncreaseIndent();
        return new IndentScope(this);
    }

    public override string ToString()
    {
        return _builder.ToString();
    }

    private sealed class IndentScope : IDisposable
    {
        private readonly IndentedStringBuilder _builder;
        private bool _disposed;

        public IndentScope(IndentedStringBuilder builder)
        {
            _builder = builder;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _builder.DecreaseIndent();
        }
    }
}
