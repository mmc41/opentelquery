using OpenTel.Query.Core.Filtering;
using OpenTel.Query.Core.Model;

namespace OpenTel.Query.Cli.Commands;

internal static class FiltersEchoBuilder
{
    public static FiltersEcho? From(FilterSpec spec)
    {
        if (spec.IsEmpty) return null;

        OperationFilterEcho? operation = spec.Operation is null
            ? null
            : new OperationFilterEcho(spec.Operation.Pattern, spec.Operation.Mode.ToString());

        IReadOnlyList<AttributeFilterEcho>? attrs = spec.Attributes.Count == 0
            ? null
            : spec.Attributes.Select(a => new AttributeFilterEcho(a.Key, a.Value)).ToList();

        IReadOnlyList<HttpStatusRangeEcho>? httpStatus = spec.HttpStatus is { IsEmpty: false } httpSpec
            ? httpSpec.Ranges.Select(r => new HttpStatusRangeEcho(r.Low, r.High)).ToList()
            : null;

        return new FiltersEcho(
            Service: spec.Service,
            Operation: operation,
            Status: spec.Status,
            Attributes: attrs,
            HttpStatus: httpStatus,
            DurationGtUs: spec.DurationGtUs);
    }
}
