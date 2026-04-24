using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenTel.Query.Core.Filtering;

namespace OpenTel.Query.Backends.OpenObserve;

public static class OpenObserveSqlTranslator
{
    private static readonly Regex AttributeKeyPattern = new("^[A-Za-z0-9_.]+$", RegexOptions.Compiled);

    public static string EscapeLiteral(string value) => value.Replace("'", "''");

    public static string ValidateIdentifier(string key, string optionName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"{optionName} key cannot be empty.");
        if (!AttributeKeyPattern.IsMatch(key))
            throw new InvalidOperationException(
                $"{optionName} key '{key}' must match [A-Za-z0-9_.]+.");
        return key.Replace('.', '_');
    }

    public static string? ToTracePredicate(FilterSpec spec)
    {
        if (spec.IsEmpty) return null;

        var parts = new List<string>();

        if (spec.Service is not null)
            parts.Add($"service_name = '{EscapeLiteral(spec.Service)}'");

        if (spec.Operation is not null)
            parts.Add(BuildPatternPredicate("operation_name", spec.Operation));

        if (spec.Status is not null)
            parts.Add($"span_status = '{EscapeLiteral(spec.Status)}'");

        foreach (var attr in spec.Attributes)
        {
            var column = ValidateIdentifier(attr.Key, "--attr");
            parts.Add($"{column} = '{EscapeLiteral(attr.Value)}'");
        }

        if (spec.HttpStatus is { IsEmpty: false } httpStatus)
            parts.Add(BuildHttpStatusPredicate(httpStatus));

        if (spec.DurationGtUs is long durationUs)
            parts.Add($"duration > {durationUs.ToString(CultureInfo.InvariantCulture)}");

        return string.Join(" AND ", parts);
    }

    public static (string? WherePredicate, string? MatchAll) ToLogsPredicate(LogsFilterSpec spec)
    {
        var clauses = new List<string>();

        if (spec.TraceId is not null)
            clauses.Add($"trace_id = '{EscapeLiteral(spec.TraceId)}'");
        if (spec.Service is not null)
            clauses.Add($"service_name = '{EscapeLiteral(spec.Service)}'");
        if (spec.Level is not null)
            clauses.Add($"severity = '{EscapeLiteral(spec.Level)}'");

        if (spec.MatchField is not null && spec.FieldMatch is not null)
        {
            var column = ValidateIdentifier(spec.MatchField, "--match-field");
            clauses.Add(BuildPatternPredicate(column, spec.FieldMatch));
        }

        var where = clauses.Count == 0 ? null : string.Join(" AND ", clauses);
        return (where, spec.Match);
    }

    public static string BuildPatternPredicate(string column, OperationPattern pattern)
    {
        return pattern.Mode switch
        {
            PatternMode.Like => $"{column} LIKE '{EscapeLiteral(pattern.Pattern)}'",
            PatternMode.Regex => $"re_match({column}, '{EscapeLiteral(pattern.Pattern)}')",
            PatternMode.Glob => $"{column} LIKE '{EscapeLiteral(GlobToLike(pattern.Pattern))}'",
            _ => throw new InvalidOperationException(
                $"Pattern mode must be specified for column '{column}'."),
        };
    }

    private static string BuildHttpStatusPredicate(HttpStatusSpec spec)
    {
        const string column = "http_response_status_code";
        var clauses = new List<string>();
        foreach (var (low, high) in spec.Ranges)
        {
            clauses.Add(low == high
                ? $"{column} = {low.ToString(CultureInfo.InvariantCulture)}"
                : $"({column} >= {low.ToString(CultureInfo.InvariantCulture)} AND {column} <= {high.ToString(CultureInfo.InvariantCulture)})");
        }
        return clauses.Count == 1 ? clauses[0] : "(" + string.Join(" OR ", clauses) + ")";
    }

    public static string GlobToLike(string glob)
    {
        var sb = new StringBuilder(glob.Length + 4);
        foreach (var c in glob)
        {
            switch (c)
            {
                case '*': sb.Append('%'); break;
                case '?': sb.Append('_'); break;
                case '%': sb.Append(@"\%"); break;
                case '_': sb.Append(@"\_"); break;
                case '\\': sb.Append(@"\\"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
