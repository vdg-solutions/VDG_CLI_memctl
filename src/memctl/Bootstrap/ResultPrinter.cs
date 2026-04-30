using System.Text.Json;
using Memctl.CoreAbstractions.Entities;
using Memctl.Operators.Mapping;

namespace Memctl.Bootstrap;

public static class ResultPrinter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void Print(MemctlOutcome outcome)
    {
        var result = MemctlResultMapper.ToResult(outcome);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
    }
}
