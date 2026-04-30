using Memctl.CoreAbstractions.Entities;

namespace Memctl.Boundary.Requests;

public static class RequestValidator
{
    public static MemctlOutcome? Validate(object request, string action)
    {
        var errs = request switch
        {
            SetWeightRequest s => s.Validate(),
            DecayRequest     d => d.Validate(),
            AddNoteRequest   a => a.Validate(),
            _ => throw new InvalidOperationException($"No validator for {request.GetType().Name}"),
        };

        if (errs.Count == 0) return null;
        return MemctlOutcome.Fail(action, $"Invalid input — {string.Join("; ", errs)}");
    }
}
