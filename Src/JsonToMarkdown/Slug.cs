using System.Globalization;
using System.Text;

namespace JsonToMarkdown;

/// <summary>
/// Heading Fragment allocation, reproducing github-slugger@2.0.0 so the anchors
/// the reference implementations emit are byte-identical: JS-style lowercasing,
/// removal of the code points in <see cref="SlugRanges"/>, spaces to hyphens, and
/// occurrence-counted <c>-N</c> de-duplication. Operates on Unicode scalar values.
/// </summary>
internal sealed class Slugger
{
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    /// <summary>Allocate a document-unique fragment for <paramref name="value"/> in output order.</summary>
    internal string Slug(string value)
    {
        string result = SlugBase(value);
        string original = result;
        while (_occurrences.ContainsKey(result))
        {
            _occurrences[original] = _occurrences.GetValueOrDefault(original) + 1;
            result = original + "-" + _occurrences[original].ToString(CultureInfo.InvariantCulture);
        }
        _occurrences[result] = 0;
        return result;
    }

    private static string SlugBase(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (Rune r in JsLowercase(value).EnumerateRunes())
        {
            if (r.Value == ' ')
            {
                sb.Append('-');
            }
            else if (!IsStripped(r.Value))
            {
                sb.Append(r.ToString());
            }
        }
        return sb.ToString();
    }

    /// <summary>Binary search over the sorted strip ranges.</summary>
    private static bool IsStripped(int r)
    {
        var ranges = SlugRanges.Strip;
        int lo = 0, hi = ranges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (r < ranges[mid].Lo) hi = mid - 1;
            else if (r > ranges[mid].Hi) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>
    /// Mirror JS <c>String.prototype.toLowerCase</c>: Unicode simple mappings plus the two
    /// language-insensitive deviations — the unconditional full mapping U+0130 (İ → i +
    /// combining dot above) and the contextual Final_Sigma rule (Σ at word end → ς, U+03C2).
    /// </summary>
    private static string JsLowercase(string s)
    {
        Rune[] runes = s.EnumerateRunes().ToArray();
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < runes.Length; i++)
        {
            Rune r = runes[i];
            if (r.Value == 0x0130)
            {
                sb.Append('i').Append('̇'); // combining dot above
            }
            else if (r.Value == 0x03a3 && FinalSigma(runes, i))
            {
                sb.Append('ς'); // Greek small letter final sigma
            }
            else
            {
                sb.Append(Rune.ToLowerInvariant(r).ToString());
            }
        }
        return sb.ToString();
    }

    /// <summary>Unicode Cased characters — the Final_Sigma condition's "cased letter".</summary>
    private static bool IsCased(Rune r)
    {
        UnicodeCategory cat = Rune.GetUnicodeCategory(r);
        return cat is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter;
    }

    /// <summary>
    /// Approximates Unicode Case_Ignorable: the Mn/Me/Cf/Lm/Sk categories plus the
    /// Word_Break MidLetter/MidNumLet/Single_Quote code points.
    /// </summary>
    private static bool IsCaseIgnorable(Rune r)
    {
        switch (r.Value)
        {
            case '\'':
            case '.':
            case ':':
            case 0x00b7:
            case 0x0387:
            case 0x05f4:
            case 0x2018:
            case 0x2019:
            case 0x2024:
            case 0x2027:
            case 0xfe13:
            case 0xfe52:
            case 0xfe55:
            case 0xff07:
            case 0xff0e:
            case 0xff1a:
                return true;
        }
        UnicodeCategory cat = Rune.GetUnicodeCategory(r);
        return cat is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.ModifierSymbol;
    }

    /// <summary>
    /// The Unicode Final_Sigma context for <c>runes[i]</c>: a cased letter precedes it
    /// (looking through case-ignorables) and no cased letter follows (again through
    /// case-ignorables).
    /// </summary>
    private static bool FinalSigma(Rune[] runes, int i)
    {
        bool before = false;
        for (int j = i - 1; j >= 0; j--)
        {
            if (IsCaseIgnorable(runes[j])) continue;
            before = IsCased(runes[j]);
            break;
        }
        if (!before) return false;
        for (int j = i + 1; j < runes.Length; j++)
        {
            if (IsCaseIgnorable(runes[j])) continue;
            return !IsCased(runes[j]);
        }
        return true;
    }
}
