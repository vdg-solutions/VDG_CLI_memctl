using System.ComponentModel.DataAnnotations;
using Memctl.CoreAbstractions.Entities;

namespace Memctl.Boundary.Requests;

public static class RequestValidator
{
    /// Validate a Boundary Request DTO using DataAnnotations.
    /// Returns null on success; on failure returns a Fail outcome whose
    /// message lists every offending field with its error message.
    public static MemctlOutcome? Validate(object request, string action)
    {
        var ctx     = new ValidationContext(request);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, ctx, results, validateAllProperties: true))
            return null;

        var formatted = string.Join("; ",
            results.Select(r =>
            {
                var member = string.Join(",", r.MemberNames);
                return string.IsNullOrEmpty(member)
                    ? r.ErrorMessage ?? "validation failed"
                    : $"{member}: {r.ErrorMessage}";
            }));
        return MemctlOutcome.Fail(action, $"Invalid input — {formatted}");
    }
}
