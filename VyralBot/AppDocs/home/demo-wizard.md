# Demo Wizard

> A 4-step Google OAuth walk-through in the sidebar's bottom group that demonstrates the app's reusable OAuth wizard engine — it runs a real Google sign-in flow, then hands out dummy tokens, connects nothing, and keeps its "credentials" in process-only environment variables.

## Overview

The **Demo Wizard** exists to show off [BaseOAuthSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs) — the 638-line wizard engine describing a four-step OAuth setup — without touching any real platform. It takes you through a genuine Google sign-in, developer-console and client-ID flow, and a real Google OAuth consent page whose `localhost:3000` redirect is intercepted by the app. Then, instead of exchanging the authorization code, it returns hard-coded dummy tokens, "sends" a fake test message after a one-second delay, and navigates you Home. Nothing is connected when it finishes.

It is the reference implementation other wizard UIs can be built against; the production Twitch/Kick/YouTube wizards currently duplicate their own logic rather than inherit this engine (see [Platforms Overview](../platforms/overview.md) for that caveat).

You open it from **Demo Wizard** in the sidebar's bottom group (`MainWindow.xaml:477`) — it is not on the Home page.

## User Guide

The wizard is a single page with a four-step indicator and one panel per step.

1. **Step 1 — Sign in.** Click **SIGN IN WITH GOOGLE**. An embedded browser opens `accounts.google.com`; sign in with any Google account, then continue. (Any account works — the demo never validates it.)
2. **Step 2 — Create application.** Click **OPEN DEVELOPER CONSOLE** to open `console.cloud.google.com` in the same embedded browser. The step shows the redirect URI to register — `http://localhost:3000` — with a **copy button**. Use the **Save Client ID** and **Save Client Secret** buttons to paste the values from the console into popup modals. Both are required to continue.
3. **Step 3 — Authorize.** Click **AUTHORIZE WITH GOOGLE**. The wizard builds the real Google consent URL from your client ID and opens it. When you approve, the browser tries to navigate to `http://localhost:3000` and the wizard silently intercepts it. If the embedded browser errors instead, the page offers a manual fallback: paste the full callback URL (`http://localhost:3000/?code=...`) into the box provided. Either way the wizard reports "Authorized as DemoUser" and advances.
4. **Step 4 — Test.** Enter a channel name and click send. After roughly a second the status reads "Test message sent to #…! (Demo mode)" — a `Task.Delay`, no message goes anywhere. Click finish.

**Finishing navigates Home and connects nothing.** No platform row is created, no chat joins, no service starts.

One side effect to know — a small one since the 2026-08-15 fix: anything you enter as Client ID/Secret in Step 2 is written to `VYRALBOT_DEMO_CLIENT_ID` / `VYRALBOT_DEMO_CLIENT_SECRET` environment variables at **process scope only** (see below). They vanish when the app exits and never touch the persistent Windows user environment.

## How It Works (Developer)

`NavigateToDemoWizardAsync` (`MainViewModel.cs:1263-1295`) is a stripped-down navigation command: no wizard-in-progress guard, no `CurrentViewModel = null`, no dispose sweep. It resolves [GoogleDemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs) — registered **transient** (`App.xaml.cs:244`), one of the few non-singleton VMs — caches it in `_demoSetupViewModel`, and hooks `SetupCompleted` to run `NavigateToHomeCommand`. Because the instance is cached, leaving mid-wizard and returning resumes where you left off.

The demo VM is a thin branding + hooks layer over the engine; everything mechanical (step switching, WebView2 lifecycle via `WebView2InitializeRequested`/`WebView2DisposeRequested` events, the client-ID/secret modals, redirect-URI clipboard copy, OAuth interception, manual callback fallback) lives in the base class. The interception is the engine's core trick (`BaseOAuthSetupViewModel.cs:371-391`): cancel any `NavigationStarting` to `http://localhost:3000` and parse the code from the URL — no local web server.

The demo's stubs are honest and total: `HandleOAuthCallbackAsync` (`GoogleDemoSetupViewModel.cs:75-80`) returns hard-coded dummy values — `("demo_access_token", "demo_refresh_token", "DemoUser")` — without any token exchange, and `SendTestMessageAsync` (`82-111`) just sets "⏳ Connecting…", waits `Task.Delay(1000)`, and writes "✓ Test message sent to #{ChannelName}! (Demo mode)". No network call is made at either step.

The one place the demo *does* keep state is credential storage — process-scope only since 2026-08-15:

```csharp
// GoogleDemoSetupViewModel.cs — process scope only; never persisted to the Windows user environment
Environment.SetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_ID", clientId);
Environment.SetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_SECRET", clientSecret);
```

Everything else in Vyral persists under the portable per-profile `appdata/` folder ([Profiles](../getting-started/profiles.md)); these variables live and die with the app process. `LoadCredentialsAsync` reads them back within the same session, which is why Step 2 can appear pre-populated after navigating away and back. (An earlier build wrote them to the persistent User scope — KI-22, resolved.)

## Files & Code

| File | Role |
|---|---|
| [GoogleDemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs) | Demo wizard VM — branding overrides, URL config, dummy callback/test, env-var storage |
| [BaseOAuthSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs) | The reusable engine: steps, modals, WebView2 lifecycle, `localhost:3000` interception, manual fallback |
| [GoogleDemoSetupView.xaml](../../src/VyralBot.UI/Views/GoogleDemoSetupView.xaml) | 1059-line view: step indicator, embedded WebView2, modals, manual-callback fallback UI |
| [GoogleDemoSetupView.xaml.cs](../../src/VyralBot.UI/Views/GoogleDemoSetupView.xaml.cs) | Code-behind: creates/disposes the WebView2 control on the VM's events |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `NavigateToDemoWizardAsync` (`1263-1295`) + `SetupCompleted` → Home wiring |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Sidebar Demo Wizard button (`477`); VM→View DataTemplate (`878-879`) |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | Registers the VM transient (`244`) |
| [DemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs) / [DemoSetupView.xaml](../../src/VyralBot.UI/Views/DemoSetupView.xaml) | Dead code — an older demo (on `BaseWebSetupViewModel`, opening github.com/google.com), registered nowhere |

## Data & Settings

- **Process-scope environment variables** `VYRALBOT_DEMO_CLIENT_ID` / `VYRALBOT_DEMO_CLIENT_SECRET` — the only thing the wizard "persists", and only for the current app process (`GoogleDemoSetupViewModel.cs`). They are gone at exit; nothing is written to the user/machine environment or the per-profile `appdata/` model.
- **No database rows, no files.** The wizard never touches the `Platforms` table or the profile's credentials folder — unlike the real platform wizards (see [Data Layer](../architecture/data-layer.md)).
- **In-memory wizard state** (current step, saved client ID/secret mask, callback input) lives in the cached `_demoSetupViewModel` instance and survives navigating away and back; it resets when you finish or when the app exits.

## Status & Known Issues

- **Works as a demo**: real Google sign-in and consent pages render in WebView2; the redirect interception, manual callback fallback, modals, step navigation, and finish → Home flow all function.
- **Credentials stay in-process** (`VYRALBOT_DEMO_CLIENT_ID/SECRET`, process scope only) — no persistent user/machine environment writes since the 2026-08-15 fix (KI-22 resolved in the [known issues ledger](../reference/known-issues.md)).
- **No wizard-in-progress guard** — unlike every other destination, navigating away from the Demo Wizard mid-flow triggers no confirmation (it doesn't need one, but it also silently keeps its WebView2 alive until the VM is disposed; the dispose sweep logs it under the wrong name, "DemoSetupViewModel" — see [App Shell & Navigation](app-shell.md)).
- **Older dead pair** — [DemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs) / [DemoSetupView.xaml](../../src/VyralBot.UI/Views/DemoSetupView.xaml) are unregistered orphans (KI-18 partial); only the Google pair above is live.
