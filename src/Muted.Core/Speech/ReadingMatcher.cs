using System.Text;

namespace Muted.Core.Speech;

/// <summary>How far a reader got through the expected text.</summary>
/// <param name="Fraction">0..1, the position of the last word recognised.</param>
/// <param name="WordsMatched">How many expected words were recognised in order.</param>
/// <param name="Finished">True once the end of the text was heard.</param>
public readonly record struct ReadingProgress(double Fraction, int WordsMatched, bool Finished);

/// <summary>
/// Follows what someone reads aloud by comparing a transcript with the text they were
/// asked to read. Speech recognition is never perfect, so it is forgiving: words may be
/// skipped, slightly misheard, or written as one ("seashells" for "sea shells").
/// </summary>
public sealed class ReadingMatcher
{
    // A transcript may skip this many expected words before a match no longer counts.
    private const int Lookahead = 4;

    private readonly string[] _expected;

    public ReadingMatcher(string expectedText)
    {
        _expected = Words(expectedText);
        if (_expected.Length == 0)
        {
            throw new ArgumentException("There is nothing to read.", nameof(expectedText));
        }
    }

    public int ExpectedWords => _expected.Length;

    public ReadingProgress Match(string? heard)
    {
        var words = Words(heard ?? string.Empty);
        var position = 0;
        var matched = 0;
        foreach (var word in words)
        {
            for (var candidate = position; candidate < Math.Min(_expected.Length, position + Lookahead + 1); candidate++)
            {
                if (Similar(word, _expected[candidate]))
                {
                    matched++;
                    position = candidate + 1;
                    break;
                }

                if (candidate + 1 < _expected.Length && word == _expected[candidate] + _expected[candidate + 1])
                {
                    matched += 2;
                    position = candidate + 2;
                    break;
                }
            }
        }

        // The end counts as heard once the last or second-to-last word was recognised and
        // enough of the rest came before it, so one misheard final word cannot stall it.
        var finished = position >= _expected.Length - 1 && matched >= Math.Ceiling(_expected.Length * 0.5);
        return new ReadingProgress(position / (double)_expected.Length, matched, finished);
    }

    /// <summary>Lowercase words with everything but letters and digits removed.</summary>
    public static string[] Words(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool Similar(string heard, string expected)
    {
        if (heard == expected)
        {
            return true;
        }

        // One wrong, missing or extra letter is fine in longer words ("shore"/"shores").
        return expected.Length >= 4 && Math.Abs(heard.Length - expected.Length) <= 1 && EditDistance(heard, expected) <= 1;
    }

    private static int EditDistance(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];
        for (var column = 0; column <= second.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= first.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= second.Length; column++)
            {
                var cost = first[row - 1] == second[column - 1] ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }
}
