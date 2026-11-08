# Process Management

## Overview
External process management services. The ProcessManagement directory exists but details should be verified against current implementation.

## Source Files

### ProcessManagement Directory
- **Path:** `src/VyralBot.Core/Services/ProcessManagement/`

## Note
This service area may contain utilities for managing external processes (e.g., ngrok tunnel). Check the directory for current files.

The ngrok tunnel agent (`NgrokService`, VyralBot.Api) is started through
`IProcessManager.StartProcessAsync` and killed via `KillProcessAsync`, so it is
tracked and covered by the shutdown kill-all. Only the short-lived
`ngrok version` install probe still uses a raw, self-terminating
`Process.Start` (exits in milliseconds, never orphaned).
