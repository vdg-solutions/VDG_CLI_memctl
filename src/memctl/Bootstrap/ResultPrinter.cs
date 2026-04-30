using System.Text.Json;
using Memctl.Boundary;
using Memctl.CoreAbstractions.Entities;
using Memctl.Operators.Mapping;

namespace Memctl.Bootstrap;

public static class ResultPrinter
{
    public static void Print(MemctlOutcome outcome)
    {
        var result = MemctlResultMapper.ToResult(outcome);
        Console.WriteLine(JsonSerializer.Serialize(result, MemctlJsonContext.Default.MemctlResult));
    }
}
