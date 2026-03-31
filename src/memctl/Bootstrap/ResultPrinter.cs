using System.Text.Json;
using Memctl.CoreAbstractions.Entities;

namespace Memctl.Bootstrap;

public static class ResultPrinter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void Print(MemctlOutcome outcome) =>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            success = outcome.Success,
            action  = outcome.Action,
            message = outcome.Message,
            data    = outcome.Data,
        }, JsonOpts));
}
