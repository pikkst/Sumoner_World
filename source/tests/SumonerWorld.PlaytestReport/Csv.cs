using System.Text;

namespace SumonerWorld.PlaytestReport;

/// <summary>A small RFC 4180 reader (quoted fields, doubled quotes, commas and line breaks in quotes), so raters can fill the sheets in any spreadsheet.</summary>
public static class Csv
{
    /// <summary>Reads rows as header name → value. Header names are trimmed and lower-cased; blank rows are skipped.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        List<List<string>> rows = Parse(reader.ReadToEnd());
        if (rows.Count == 0)
        {
            return [];
        }

        string[] header = [.. rows[0].Select(name => name.Trim().ToLowerInvariant())];
        var result = new List<IReadOnlyDictionary<string, string>>();
        foreach (List<string> row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < header.Length; i++)
            {
                fields[header[i]] = i < row.Count ? row[i].Trim() : string.Empty;
            }

            result.Add(fields);
        }

        return result;
    }

    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().TrimEnd('\r'));
            rows.Add(row);
        }

        return rows;
    }
}
