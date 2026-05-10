---
id: aa353d5050ef4522
created: 2026-05-07T23:50:41.6205121Z
modified: 2026-05-07T23:50:41.6205124Z
tags:
  - golden-rule
---

# LLM-first CLI design principle

memctl's primary consumer is LLMs, not humans. All CLI ergonomics, error messages, output format, and command design must be optimized for LLM callers. 'Smooth for LLM' means: error messages must state exactly what went wrong AND the correct usage in one shot (LLM cannot interactively probe); CLI patterns must be consistent and inferrable without reading docs; JSON output must be machine-parseable with no ambiguity; flag names must be obvious enough that an LLM guesses them correctly on first try. Example failure: 'memctl add' error says 'Unrecognized command or argument <content>' — LLM cannot tell whether the flag name or the value is wrong.