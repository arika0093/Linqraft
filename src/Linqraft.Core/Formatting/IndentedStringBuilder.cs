using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Linqraft.Core.Formatting;

/// <summary>
/// Builds indented text for generated source output.
/// </summary>
internal sealed class IndentedStringBuilder
{
    private readonly StringBuilder _builder = new();
    private readonly string _indentToken;
    private int _indent;

    /// <summary>
    /// Initializes a new instance of the IndentedStringBuilder class.
    /// </summary>
    public IndentedStringBuilder(string indentToken = "    ")
    {
        _indentToken = indentToken;
    }

    /// <summary>
    /// Increases the current indentation level.
    /// </summary>
    public void IncreaseIndent()
    {
        _indent++;
    }

    /// <summary>
    /// Decreases the current indentation level.
    /// </summary>
    public void DecreaseIndent()
    {
        if (_indent == 0)
        {
            throw new InvalidOperationException("Indent cannot be negative.");
        }

        _indent--;
    }

    /// <summary>
    /// Appends an empty line.
    /// </summary>
    public void AppendLine()
    {
        _builder.AppendLine();
    }

    /// <summary>
    /// Appends a line using the current indentation level.
    /// </summary>
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

    /// <summary>
    /// Appends each line from the supplied text using the current indentation level.
    /// </summary>
    public void AppendLines(string value, CancellationToken cancellationToken = default)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendLine(line, cancellationToken);
        }
    }

    /// <summary>
    /// Appends text without applying indentation.
    /// </summary>
    public void Append(string value)
    {
        _builder.Append(value);
    }

    /// <summary>
    /// Appends text after writing the current indentation prefix.
    /// </summary>
    public void AppendIndented(string value)
    {
        for (var index = 0; index < _indent; index++)
        {
            _builder.Append(_indentToken);
        }

        _builder.Append(value);
    }

    /// <summary>
    /// Creates a scope that increases indentation until it is disposed.
    /// </summary>
    public IDisposable Indent()
    {
        IncreaseIndent();
        return new IndentScope(this);
    }

    /// <summary>
    /// Returns the accumulated text.
    /// </summary>
    public override string ToString()
    {
        return _builder.ToString();
    }

    /// <summary>
    /// Restores the previous indentation level when disposed.
    /// </summary>
    private sealed class IndentScope : IDisposable
    {
        private readonly IndentedStringBuilder _builder;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the IndentScope class.
        /// </summary>
        public IndentScope(IndentedStringBuilder builder)
        {
            _builder = builder;
        }

        /// <summary>
        /// Restores the previous indentation level.
        /// </summary>
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
