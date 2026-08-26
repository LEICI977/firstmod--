using System.Globalization;
using System.Text;

namespace VivantValley.Services;

/// <summary>Single-line Unicode text storage with a caret kept on text-element boundaries.</summary>
public sealed class SingleLineTextBuffer
{
    private string text = string.Empty;

    public SingleLineTextBuffer(int maximumLength)
    {
        MaximumLength = Math.Max(1, maximumLength);
    }

    public int MaximumLength { get; }

    public string Text
    {
        get => text;
        set
        {
            text = NormalizeAndLimit(value);
            CaretIndex = text.Length;
        }
    }

    /// <summary>UTF-16 index which is always aligned to a complete text element.</summary>
    public int CaretIndex { get; private set; }

    public bool Insert(string? value)
    {
        string insertion = NormalizeAndLimit(value, MaximumLength - text.Length);
        if (insertion.Length == 0)
            return false;

        text = text.Insert(CaretIndex, insertion);
        CaretIndex += insertion.Length;
        return true;
    }

    public bool Backspace()
    {
        int previous = PreviousBoundary(CaretIndex);
        if (previous == CaretIndex)
            return false;

        text = text.Remove(previous, CaretIndex - previous);
        CaretIndex = previous;
        return true;
    }

    public bool Delete()
    {
        int next = NextBoundary(CaretIndex);
        if (next == CaretIndex)
            return false;

        text = text.Remove(CaretIndex, next - CaretIndex);
        return true;
    }

    public bool MoveLeft()
    {
        int next = PreviousBoundary(CaretIndex);
        if (next == CaretIndex)
            return false;

        CaretIndex = next;
        return true;
    }

    public bool MoveRight()
    {
        int next = NextBoundary(CaretIndex);
        if (next == CaretIndex)
            return false;

        CaretIndex = next;
        return true;
    }

    public void MoveHome() => CaretIndex = 0;

    public void MoveEnd() => CaretIndex = text.Length;

    public void SetCaret(int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        int[] boundaries = GetBoundaries();
        CaretIndex = boundaries
            .OrderBy(boundary => Math.Abs(boundary - index))
            .ThenBy(boundary => boundary)
            .First();
    }

    public int[] GetBoundaries()
    {
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        var boundaries = new int[starts.Length + 1];
        starts.CopyTo(boundaries, 0);
        boundaries[^1] = text.Length;
        return boundaries;
    }

    private int PreviousBoundary(int index)
    {
        int[] boundaries = GetBoundaries();
        for (int position = boundaries.Length - 1; position >= 0; position--)
        {
            if (boundaries[position] < index)
                return boundaries[position];
        }

        return index;
    }

    private int NextBoundary(int index)
    {
        foreach (int boundary in GetBoundaries())
        {
            if (boundary > index)
                return boundary;
        }

        return index;
    }

    private string NormalizeAndLimit(string? value, int? availableLength = null)
    {
        int remaining = Math.Clamp(availableLength ?? MaximumLength, 0, MaximumLength);
        if (remaining == 0 || string.IsNullOrEmpty(value))
            return string.Empty;

        string normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        int[] starts = StringInfo.ParseCombiningCharacters(normalized);
        var result = new StringBuilder(Math.Min(normalized.Length, remaining));
        for (int index = 0; index < starts.Length; index++)
        {
            int start = starts[index];
            int end = index + 1 < starts.Length ? starts[index + 1] : normalized.Length;
            string element = normalized[start..end];
            if (element.All(char.IsControl) || result.Length + element.Length > remaining)
                continue;

            result.Append(element);
        }

        return result.ToString();
    }
}
