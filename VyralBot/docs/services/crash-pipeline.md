# Crash pipeline (global exception handling, secret scrubbing, crash dialog)

Owned files: `src/VyralBot.UI/App.xaml.cs` (boot/exception sections), `src/VyralBot.UI/Services/CrashReport.cs`, `src/VyralBot.UI/Views/ErrorDialog.xaml(.cs)`.

## Wiring (all in the `App` constructor, right after Serilog init)

| Handler | Log | UI | Process outcome |
|---|---|---|---|
| `Application.DispatcherUnhandledException` | Fatal, scrubbed | ErrorDialog (modal) | `e.Handled = true` when the dialog was shown — app keeps running |
| `AppDomain.CurrentDomain.UnhandledException` | Fatal, scrubbed, then `Log.CloseAndFlush()` (OnExit does NOT run here) | ErrorDialog best-effort from any thread; MessageBox fallback | Process usually terminates (runtime decision) |
| `TaskScheduler.UnobservedTaskException` | Error, scrubbed | none (fires on the finalizer thread for background work) | `e.SetObserved()` — background noise never kills the process |

Handlers log the **scrubbed report string**, never the raw `Exception` object (Serilog would render its unscrubbed message/stack).

## SecretScrubber (`Services/CrashReport.cs`)

`CrashReport.Format(Exception)` builds type + message + stack + inner chain (aggregates unwrapped) and runs `CrashReport.Scrub` over every string. Patterns (regex, case-insensitive, compiled):

- named assignments: `access_token|refresh_token|client_secret|bot_token|api_key|stream_key|auth_token|webhook_token|password|secret|token|authorization` `= / :` value → `name=***REDACTED***` (query-string `?name=`/`&name=` forms keep their separator)
- `Bearer <token>` headers, Twitch IRC `oauth:<token>` passwords
- RTMP(S) URLs: `rtmp://host/app/<stream-key>` — host/app kept for diagnosis, key redacted

## ErrorDialog reentrancy

`ErrorDialog.ShowReport(title, report)` is the only supported entry point (UI thread): if a dialog is already open, the new report is **appended** under a `========== Another error occurred … ==========` separator instead of stacking a second modal. `App.ShowCrashDialog` marshals to the UI dispatcher with a **2 s bounded** `Dispatcher.Invoke` (a wedged UI thread must not deadlock a crashing background thread) and falls back to a native `MessageBox` when WPF cannot serve a dialog (returns `false` so `DispatcherUnhandledException` can leave `Handled = false`).

## Serilog caps (App constructor)

`logs\vyralbot-.txt` next to the exe (`App.LogDirectory` — correct for dev bin AND single-file publish), day rolling, `fileSizeLimitBytes` 25 MB with `rollOnFileSizeLimit`, `retainedFileCountLimit` 10 → ~250 MB worst-case disk on a crash-looping 24/7 box. **No async sink wrapper** is used, so every write is synchronously flushed; fatal paths additionally call `Log.CloseAndFlush()`.

## Boot failure UX

- DI host build failure (`App` ctor): `Log.Fatal` + `CloseAndFlush` + last-resort MessageBox (scrubbed report + log path) + `Environment.Exit(1)` — previously a silent crash with no window.
- `OnStartup` catch: scrubbed report MessageBox + `LogDirectory`.
- Profile-init failure (`InitializeProfileDataAsync`): message now shows the **full** log folder path (`App.LogDirectory`), not the dev-relative `logs/vyralbot-*.txt`.
