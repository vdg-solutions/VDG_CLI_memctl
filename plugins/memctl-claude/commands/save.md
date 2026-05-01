---
description: Save current decision/finding/insight to vault as new note
argument-hint: "<title> | <content>"
---
Parse `$ARGUMENTS` as `<title> | <content>`. If `$ARGUMENTS` is empty, ask the user what to save. Run `memctl add --title "<title>" --content "<content>"` and echo the resulting note id.
