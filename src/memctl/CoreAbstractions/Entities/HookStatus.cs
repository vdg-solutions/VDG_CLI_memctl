using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record HookStatus(
    string                          LogPath,
    bool                            LogExists,
    int                             RecentSuccess,
    int                             RecentFail,
    string?                         LastError,
    string?                         LastErrorAt,
    IReadOnlyList<HookLogEntry>     LastEntries);

public sealed record HookLogEntry(
    string Timestamp,
    string Action,
    bool   Success,
    string? Error);
