# Automation (Workflows)

> Visual automation: every trigger starts a workflow; every workflow runs a chain of nodes. Built per the roadmap in `Context/BotGhostLikeIntegration.md`.

## Visual Flow Editor

A full-screen, drag-and-drop **node canvas** for building command workflows — Unreal Blueprints / n8n style: drag blocks from a palette, wire them into a graph, and the engine executes the wires. It edits the exact same workflows as the ordered list above (same storage, same triggers, same History).

### Opening the editor

- **Automation page → 🎨 VISUAL EDITOR** — opens the editor (the selected workflow, or a fresh one).
- **🎨 CANVAS** on a workflow's row — opens that workflow directly.
- **Chat Features → Commands** — **NEW COMMAND** and every **EDIT** (built-in or custom, plus double-click) open the same editor in an overlay, seeded from a flow converted out of the command; **SAVE** adopts the command (see [Commands — From command to flow](commands.md#from-command-to-flow)).
- **Discord bot detail page → Commands card → 🎨 VISUAL COMMAND BUILDER** — the same editor for that bot's command flows (the card's moderation and custom command rows each carry an **EDIT** button too).

### Layout

Three panes, restyled after BotGhost's command builder (`Context/BotGhostBuilderReference.md`):

- **Left — the side rail**: a dark 56 px **icon rail** with six rounded-square buttons — 🧊 **Blocks** (blue), `</>` **Variables** (pink), ⚠️ **Error Logs** (red), 🟢 **Commands** (green), 🕐 **Timed Events** (yellow), 🧱 **Block Templates** (purple) — beside a swappable panel whose content changes per icon. The **Blocks** panel holds the palette: three segmented tabs — purple **OPTIONS**, blue **ACTIONS**, green **CONDITIONS** — under a color-coded intro line ("Drag and drop **Options**, **Actions** and **Conditions** … Connect the corresponding colors…"). The purple tab holds two bold groups — **COMMAND PARAMETERS** (typed slash-command arguments) and **COMMAND OPTIONS** (the six command constraints). A search box filters all three tabs. Blocks list under bold group headers (each block's `Section`: **Messaging**, **Threads & Channels**, **Roles**, **Moderation**, **Variables & Math**, **Economy**, **Loops**, **Utility** — the conditions tab is a flat list), each row a ⠿ drag handle + colored icon chip + bold title + grey description. The other five panels are described under [The side rail panels](#the-side-rail-panels) below.
- **Collapse**: the indigo ☰ in the panel header collapses the **entire left side** (rail + panel) to zero width; a floating ☰ over the canvas reopens it. When the flow has unsaved edits, a red **"Please save your changes!"** bar with a white **Save Changes** button (`Ctrl+S`) sits at the bottom of the panel until you save.
- **Center — canvas**: a dark, dot-grid surface holding the nodes and their wires. A pinned amber ⚡ **trigger card** floats above the flow showing `When: <trigger>` — click it to open Command Settings on the right, and drag its blue output port onto a block to pin the main-flow entry. The flow's typed parameters render as small **purple parameter cards** in a row above the trigger card — a purple chip with the type's initial, the bold name (a dim *(unnamed)* with an orange **!** badge when blank), a grey type subtitle ("Text option", "Number option", …) and a single grey dot — with grey wires dropping from the cards and merging into the trigger card's top input dot. The cards are display-only (no dragging, no deleting); clicking one opens its Parameter editor on the right. A pinned red ⚠️ **Error Handler** card sits beside the trigger card (draggable) — wire its grey output port to the first block of an error chain. Node cards are an icon chip + bold label + grey two-line description (208 px wide); the selected one gets a dashed indigo outline, and the canvas supports **rubber-band multi-select** (see [Building a flow](#building-a-flow)). White fit/reset/organize buttons sit in the canvas's bottom-left corner — fit ⤢, reset zoom ⌂, and ⤓ **Auto-organize layout** (see [Building a flow](#building-a-flow)) — with a floating − / % / + / **FIT** zoom row above them.
- **Right — settings**: one host panel with four modes that swap live as you click through the canvas, closed by the header's red ✕ in every mode. **Block** — the selected node's settings: the parameter fields of the block (text, number, checkbox, dropdown, role/channel pickers, color, embed builder with live preview, rule builder, comparison rules list — depending on the block); the ✕ deselects the node. **Command Settings** — an amber ⚡ header over the flow name, an **Enabled** checkbox, the full trigger editor, and the command's OPTIONS and PARAMETERS lists (opened by the trigger card, the toolbar TRIGGER button, or the purple OPTIONS tab). **Parameter** — a violet ◈ header over one typed parameter's Name, read-only Type, Description, Required, and Remove Parameter (opened by clicking a canvas parameter card). With nothing selected the panel gives way to a help card. Field labels carry a grey ❓ whose tooltip is the field's hint; embed blocks get a **Message Builder ✅** section.

### The side rail panels

The five rail icons besides Blocks swap the panel's content (each with its own search where it applies):

- **Variables** (`</>`, pink) — everything this flow can use as a `{token}`, auto-collected and badge-tagged: the flow's typed **Parameters**, names stored by **Set Variable** blocks (**Flow**), the block outputs `{response}` `{http_response}` `{http_status}` `{random}` `{builtin_response}` (**Built-in**), and the run-context tokens like `{username}`, `{args}`, `{buttonId}` (**Context**). **+ Create New Variable** places a **Set Variable** block pre-named with the popup's name and selects it.
- **Error Logs** (⚠️, red) — the recent failed executions **for this workflow** (falling back to the most recent failures across all workflows when this one has none yet), newest first: a relative stamp ("just now", "5m ago"), the error message, and the trigger that started the run. **⟳ REFRESH** re-queries.
- **Commands** (🟢, green) — your command flows: **COMMANDS** lists command/slash-triggered flows and **EVENTS** lists event-triggered ones (the flow you have open and component child workflows are excluded from both). Each row can be ✏️ edited (loads it into this editor), toggled enabled/disabled, or 🗑️ deleted (removes the workflow row only, after a confirmation). **+ New Command** starts a brand-new flow with a prefix-command trigger preset.
- **Timed Events** (🕐, yellow) — the timer-triggered flows with an **ACTIVE TIMERS (N)** count, same edit/toggle/delete rows, plus **Add New Timed Event** — a fresh flow with the timer trigger preset (5-minute default).
- **Block Templates** (🧱, purple) — **Save Selected Block** stores the block currently selected on the canvas (name, description and its configured settings) as a JSON file under `%LocalAppData%\Vyral\BlockTemplates`; clicking a template places a configured copy of the block on the canvas, and the row's ✕ deletes the file.

### Building a flow

- **Add blocks**: drag an entry from the **ACTIONS** or **CONDITIONS** tab onto the canvas, or double-click it; entries in the purple **OPTIONS** tab add on a single click (see **The purple OPTIONS tab** below).
- **Pin the entry**: drag the amber trigger card's blue output port onto a block to make that block the main-flow entry (rendered as a locked blue wire; the pin is cleared if the target is deleted, or by clicking the wire's ⊗ marker — see below). Without a pin the runtime falls back to its classic detection — the first node nothing routes into.
- **Wire blocks**: every regular block has exactly **one input dot** (top) and **one output dot** (bottom); drag from an output dot to another node's input. Wire and port colors follow one rule — **blue = the main flow, grey = settings-attached**:
  - **Blue** (`#3B82F6`, the action color) — every normal block's **Next** port and its wires, plus the trigger card's entry wire and the attachment cards' onward exits. This is the main flow.
  - **Green** — both **True** and **False** of the condition family (the port labels tell the branches apart). The **Comparison Condition** is the exception once it carries a rules list: its True/False pair is replaced by one grey port per rule plus a grey **Else** port (legacy comparisons keep the green pair until their rules are edited).
  - **Grey** (`#9CA3AF`) — only the settings-attached exits: **Button**/**Menu** component drop dots, a comparison's per-rule and **Else** ports, the **loop** block's **Loop times** and **After loop** ports (always wired to its two attached cards), and the **Error Handler** wire. Grey wires render as **right-angle elbows** (BotGhost's attachment look); colored wires stay curved beziers.
  - **Amber** — error/default ports such as Wait for Response's **Error**.

  Conditions are the **only** branching blocks: one wire in, and the branch split decides where the flow goes — True/False for the plain conditions, one chain per rule (first match wins) plus **Else** for the Comparison Condition. (The old 🔀 **Switch** block was removed from the palette — flows saved with one still run and render, but you can't place a new one.)
- **Attachment cards** — the loop and comparison blocks carry BotGhost-style **attached cards** hung below them by grey elbow wires. They're part of the block's shape, not separate blocks you manage:
  - **Run a Loop** gets its two cards the moment it's placed or loaded — an **After loop** card ("Run when the loop has finished") and a **Loop # times** card ("Connected Actions & Conditions will be executed # times."). The loop's two grey dots stay wired to them; wire your after-loop chain onward from the **After loop** card's blue dot, and the loop body from the **Loop # times** card's blue dot. The iteration count still lives in the loop block's settings.
  - A **Comparison Condition** with rules drops a column of cards below the node — one green **?** card per rule (titled like "10 is equal to 5") plus a red **?** **Else** card — each fed by a grey elbow from the block's grey dots.
  - The cards can't be deleted individually (the Delete key never removes one), follow their parent around, vanish with it, and are saved as part of the flow — at run time they execute as instant no-ops the walk simply passes through. The component sub-cards (see [Buttons & menus](#buttons--menus)) follow the same pattern.
- **Click the ⊗ to disconnect** — deletable wires (blue main lines, including the trigger–entry pin, and green True branches) carry exactly **one ⊗ at the line's midpoint**; clicking it breaks that connection. Grey attachment lines (loop/comparison/button-menu elbows, Error Handler) and amber lines carry **no ⊗** — they're permanent structure; rewire them by dragging instead. Delete never removes wires.
- **One wire per input** — an input port holds at most one incoming wire; a new wire landing on an occupied input **displaces** the old one (its source's routing entry is cleared). Wires are **locked**: clicking one highlights it, but `Delete` never removes it — disconnect with the ⊗ marker (or rewire by dragging again), and deleting a node clears its wires.
- **Edit a node**: click it to edit its settings on the right; double-click its label to rename it.
- **Invalid wires are rejected** — self-connections, duplicates, and cycles are refused (rejected wires flash red briefly).
- **Keyboard**: `Delete` removes **every selected node** (never a wire; one change for the whole batch, so one undo step); `Ctrl+D` duplicates the selection (+24/+24 offset, fresh ids, no wires); `Ctrl+C` / `Ctrl+V` copy/paste the selection through the same duplicate machinery (canvas-local clipboard, pastes cascade +24/+24); `Ctrl+Z` / `Ctrl+Shift+Z` undo/redo; `Ctrl+S` saves.
- **Multi-select (rubber band)** — **left-drag on empty canvas** draws a dashed selection rectangle; on release every node whose bounds intersect it is selected, **additively** (union) while Ctrl/Shift is held, replacing the selection otherwise (a plain left click with no drag still deselects). **Ctrl/Shift+click** toggles a single node in or out of the multi-selection. Dragging **any node inside a multi-selection moves the whole group** (wires follow live; one save-worthy change at drag end), and `Delete`, `Ctrl+D` and `Ctrl+C` all operate on the full set.
- **Mouse**: the wheel zooms (25–200% around the cursor; Shift+wheel scrolls horizontally); **middle-drag** (or the scrollbars) pans; the floating zoom row (− / % / + / **FIT**) hovers above the white fit/reset/organize buttons in the canvas's bottom-left corner.
- **Auto-organize layout** — the white ⤓ button in the bottom-left group (with fit ⤢ and reset ⌂) snaps the whole graph into a tidy top-down layout computed from the connections: the entry block goes topmost, every connected block lands one level below the block that leads into it (longest-path depth), branches fan out sideways per level, and disconnected pieces — button/menu sub-flow chains, the Error Handler chain, lone blocks — get their own columns to the right. The whole rearrangement is one undo step (`Ctrl+Z` brings the old layout back), and the view auto-fits afterwards.

### Toolbar

- **Flow name** — the workflow's display name.
- **Trigger…** — opens **Command Settings** on the right (the old popup is retired): the trigger type — **Prefix command** / **Slash command (Discord)** / **Button click** / **Select menu** / **Event** / **Timer** / **Manual run** — plus the per-type fields (command text, event type, interval, …), an **Enabled** checkbox, and the command's OPTIONS and PARAMETERS lists. The pinned amber ⚡ trigger card on the canvas mirrors the current trigger (`When: !roll`, `When: every 15 min`, …) and opens the same panel when clicked.
- **Options…** — popup with **cooldown seconds**, **require moderator**, **channel restriction**, **DM only**, **guild only**, and **NSFW flag**. The same six options are listed in the palette's COMMAND OPTIONS group and in Command Settings' COMMAND OPTIONS list.

### The purple OPTIONS tab

The purple tab holds two bold groups, BotGhost-style:

- **COMMAND PARAMETERS** — typed slash-command arguments: **Text**, **Number**, **User**, **Channel**, **Role**, **Choice** (a true/false option), **Attachment**, and **Music Smart Search** (a text parameter pre-named `search`). Clicking one appends a typed parameter to the flow and opens **Command Settings** on the right — parameters render as **cards on the canvas** (below) and are edited there or in the panel.
- **COMMAND OPTIONS** — the six command-level constraints: **Cooldown**, **Require Moderator**, **Channel Restriction**, **DM Only**, **Guild Only**, and **NSFW Flag**. Clicking one adds it to the command; enabled options are listed in Command Settings' **COMMAND OPTIONS** list — each row's ✕ removes it, and **+ ADD OPTION** appends a blank one. These are the same options the runtime enforced before the restyle.

**Parameter cards & the Parameter editor** — every typed parameter renders as a small purple card in a row above the amber trigger card (purple type-initial chip, bold name — a dim *(unnamed)* with an orange **!** badge when the name is blank — a grey "Text option"-style subtitle, one grey dot; grey wires merge the row into the trigger card's input). Clicking a card opens the **Parameter** editor on the right: **Name** (normalized into the lowercase identifier Discord expects — "My Value" becomes `my_value`, with a hint when normalization changed it), **Type** (read-only — fixed when created), **Description**, **Required**, and **Remove Parameter**. The same fields sit inline in Command Settings' **PARAMETERS** list, where each row's ✕ removes the parameter. Names are free text; Discord only ever sees the normalized form. (The old parameters popup and the violet chips bar above the canvas are gone — cards and panel replaced them.)

For what parameters do at run time — real typed Discord slash options and `{parameterName}` variables — see [Discord specifics](#discord-specifics) below. The parameters apply to **slash** triggers only; prefix commands still hand everything over as `{args}` (KI-44).

### Error Handler

The pinned red ⚠️ **Error Handler** card is a reaction chain for failures: wire its grey output port to the first block of a chain (click the wire's ⊗ marker to un-pin the chain), and whenever a node fails **without an error branch of its own**, that chain runs before the run ends. The handler's steps count toward the 200-step budget and show up in **History**; the run still ends **Failed** with the original error (the handler reacts, it does not rescue the status). A node whose own amber error port is wired keeps taking that branch — the handler only catches nodes with nowhere else to go. Errors inside the handler chain itself just end the chain.

### Buttons & menus

**Send Message** and **Send Embed** blocks have a **Buttons & Menus** settings section that attaches real Discord components to the message (Discord):

- **Buttons** — up to 5 per message, each with a **Label**, **Style** (Primary / Secondary / Success / Danger / Link), and optional **Emoji**.
- **Select menu** — one per message, with a **Placeholder** and up to 25 **Label/Value** options.

Nodes that carry components show two light **⊕ Button** / **⊕ Menu** chips hanging outside below the card's corners — **⊕ Button** under the bottom-left, **⊕ Menu** under the bottom-right. Clicking a chip adds that component (the settings section edits or removes one — the chips themselves carry no ✕), and the chip **dims** once the message is at its cap (5 buttons / 1 menu). The ⊕ Menu chip stays clickable whenever the block has no wired menu card — clicking it adds (or re-adds) the Basic Select Menu card after you removed it from the settings. Every component also derives a small **grey drop dot** just under the card's bottom edge, and adding one spawns its **sub-node card directly below the node**, wired by a grey elbow line from that dot — a red **Start / Button** or blue **Basic Select Menu / Select Menu** card, one per button slot. Each component's sub-flow wires onward from its sub-card's blue dot.

On **SAVE**, every non-empty sub-chain is materialized as a **component child workflow**: a deterministic row (Category **Component**, trigger **Button click** / **Select menu**, Platform Discord) whose id derives from the component's custom id (`vyralwf:{parentWorkflowId}:{nodeId}:btn{i}` for a button, `…:menu` for a menu). Re-saving updates the same row; emptying a chain deletes its child. At run time a button or menu click dispatches by that key straight to the child flow — the child sees `{customId}`, `{buttonId}` (the id tail, e.g. `…:btn0`) and `{values}` (comma-joined menu selections) as variables. Components whose key lacks the valid prefix degrade to a plain text/embed delivery. The button ids are **positional** — reordering or removing buttons renumbers them, so any `{buttonId}` comparisons in the sub-flows need updating (KI-46).

> Button click / Select menu **triggers** (the trigger types in Command Settings) receive clicks addressed at the workflow's own `vyralwf:{workflowId}` prefix — the child-workflow rows above are how clicks on a message's components find their sub-flows.

### Bottom bar

**SAVE** · **TEST RUN** (runs the current graph without saving — check **History** for the result) · **EXPORT** / **IMPORT** JSON (portable `.flow.json` files) · unsaved-changes indicator · **Close**.

### Drafts

Unsaved edits **auto-save every 30 seconds** to `%LocalAppData%\Vyral\FlowDrafts` and are restored when the editor reopens — closing by accident loses nothing.

### Block library

Every placeable block, from the catalog in [BlockCatalog.cs](../../src/VyralBot.Workflows/Abstractions/BlockCatalog.cs). The ACTIONS tab lists blocks under bold group headers (each block's `Section`); the CONDITIONS tab is a flat list. Action blocks have a single blue **Next** output (blue is the main-flow color — see [Building a flow](#building-a-flow)); condition blocks — the whole green family — have green **True** / green **False** outputs (the port labels tell the branches apart). Two exceptions: the **loop** block exposes grey **After loop** + grey **Loop times** ports that stay wired to its two attached cards, and a **comparison** with a rules list swaps True/False for grey per-rule + **Else** ports (legacy single-rule comparisons keep the green pair). Message blocks with components derive one grey drop dot per button/menu slot. Settings-attached exits stay grey.

**Actions — Messaging**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 📨 `send_message` — Send Message | Sends a text message to the origin channel (or an override channel). | `text` (supports `{variables}`), **Ephemeral** (Discord slash/button runs: reply visible only to the invoking user; ignored for plain sends, Buttons/Menu messages, platform overrides, and other platforms), **Buttons & Menus** section (see [Buttons & menus](#buttons--menus)) |
| 🖼️ `send_embed` — Send Embed | Sends a rich embed (title, description, color, fields, footer) with a live Discord preview. | `Embed` (embed builder), `ChannelOverride` (optional), **Buttons & Menus** section (see [Buttons & menus](#buttons--menus)) |
| ✉️ `send_dm` — Send DM | Direct-messages the triggering user (Discord). | `Content`, `Username` (defaults to `{username}`) |
| ✏️ `edit_message` — Edit Bot Message | Edits the bot's most recent sent message in the origin channel. | `NewContent` |
| 🗑️ `delete_message` — Delete Message | Deletes the triggering message or the bot's last sent message. | `Scope` = `trigger` / `last_bot` |
| ➕ `react` — React to Message | Adds an emoji reaction to the triggering or the bot's last message (Discord). | `Emoji`, `Scope` = `trigger` / `last_bot` |
| ⏳ `wait_for_response` — Wait for Response | Pauses until the triggering user posts another message (same channel); stored as `{response}`. A timeout takes the amber **Error** port. | `TimeoutSeconds` (default 30) |

**Actions — Roles**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 🏷️ `add_role` — Add Role | Gives the triggering user a role (Discord). | `Role`, `Username`, `Reason` |
| 🏷️ `remove_role` — Remove Role | Takes a role from the triggering user (Discord). | `Role`, `Username`, `Reason` |

**Actions — Threads & Channels**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 📁 `create_channel` — Create Channel | Creates a text/voice/forum channel in the origin guild (Discord). | `Name`, `Type` = Text/Voice/Forum, `Category` (optional) |
| 🧵 `create_thread` — Create Thread | Creates a thread from the bot's last message (Discord). | `Name`, `AutoArchiveMinutes` (default 60) |
| 🔒 `lock_channel` — Lock / Unlock Channel | Locks (deny @everyone SendMessages) or unlocks the origin channel (Discord). | `Mode` = Lock/Unlock, `Reason` |

**Actions — Variables & Math**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 💾 `set_variable` — Set Variable | Stores a value in an execution variable for later nodes (`{name}`). | `name`, `value` |
| 🎲 `random_number` — Random Number | Rolls a random integer between min and max; stored as `{random}` (or a custom name). | `min` (1), `max` (100), `store` |

**Actions — Economy**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 🪙 `add_currency` — Add Currency | Awards currency to a user via the existing economy system. | `username`, `amount` (10) |
| 🪙 `remove_currency` — Remove Currency | Deducts currency from a user via the existing economy system. | `username`, `amount` (10) |

**Actions — Moderation**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 🧩 `builtin` — Run Built-in Command | Runs a legacy built-in command (Commands-table engine) and stores its reply: cooldowns, permissions and variables all apply. | `Name` (built-in name without prefix), `Args` (default `{args}`), `OutputVariable` (default `builtin_response`) |
| 🔨 `moderate` — Moderate Member | Discord moderation action (kick, ban, unban, timeout, remove timeout, purge or slowmode); success flows to the next node. | `Action` (select), `User` (default `{username}`; unused by Purge/Slowmode), `Minutes` (10), `Count` (10), `Seconds` (10), `Reason` |

**Actions — Utility**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 🌐 `http_request` — HTTP Request | Calls an external API; the response body is stored as `{http_response}` and the status as `{http_status}`. | `Method` (GET/POST/PUT/DELETE), `Url`, `Headers` (`Key: Value` per line), `Body`, `SaveAs` (variable name, default `http_response`) |
| ⏱️ `wait` — Delay | Pauses the flow for a fixed duration. | `seconds` (default 5) |
| 📝 `log` — Log | Writes a message to the engine log (debugging). | `text` |
| 🗒️ `note` — Note | Writes a note in the tree: a canvas annotation that does nothing when the flow runs — it executes instantly, succeeds, and continues via **Next**. Its text is kept literally (no `{variables}` — it's documentation, not data). | `Text` |

**Actions — Loops**

| Block (`type`) | What it does | Key settings |
|---|---|---|
| 🔁 `loop` — Run a Loop | Runs the chain hanging off its **Loop # times** card once per iteration (1–25), then continues via the **After loop** card — both cards spawn attached to the block (see [Building a flow](#building-a-flow)). The body ends when it walks off the graph or routes back into the loop node. | `Count` (default 5; 1–25, supports `{variables}`) |
| ⏹️ `stop_loop` — Stop a Loop | Breaks out of the innermost active loop, ending every remaining iteration. Outside a loop it does nothing. | — |

**Conditions** (branch on their True/False ports — the Comparison Condition branches by its rules instead; flat list, no group headers)

| Block (`type`) | What it does | Key settings |
|---|---|---|
| ❓ `if` — If (Expression) | Branches on a guard expression: `{coins} > 100`, `{message} contains 'hello'`, and/or chains. | `Expression` (free expression or the structured rule builder) |
| ❓ `if_user_has_role` — If User Has Role | Branches on Discord role membership of the triggering user. | `Role`, `Username` |
| ❓ `if_message_contains` — If Message Contains | Branches on the trigger message text. | `Text`, `CaseSensitive` |
| ❓ `if_variable_equals` — If Variable Equals | Branches on a stored variable's value. | `Name`, `Value` |
| ❓ `if_time_between` — If Time Between | Branches on the current local hour (e.g. night mode). | `StartHour` (0–23), `EndHour` (0–23) |
| ❓ `if_user_is_bot` — If User Is Bot | Branches on whether the triggering user is a bot account. | — |
| 🎲 `chance` — Chance Condition | Branches on a random roll: **True** when the roll lands under the configured percentage (0% never passes, 100% always does). | `Percent` (0–100, default 50; supports `{variables}`) |
| ≥ `comparison` — Comparison Condition | A multi-rule brancher: the settings panel holds a **rules list** — **Left / Comparison Type / Right** per row, up to 10 rules (**+ Add Comparison**) — and the rules run **top-down** at run time: the **first matching rule** runs its own attached chain (one green **?** card per rule, titled like "10 is equal to 5"); no match runs the **Else** chain (the red **?** card). Every row picks from the 14 operators — **Less than**, **Less than or equal to**, **Greater than**, **Greater than or equal to**, **Equal to**, **Not equal to**, **Contains**, **Does not contain**, **Starts with**, **Ends with**, **Does not start with**, **Does not end with**, **Collection contains**, **Collection does not contain**. Ordering/equality operators compare numerically when **both** sides parse as numbers, otherwise as a case-insensitive string comparison; Contains / Starts / Ends (and their negations) are case-insensitive text checks; the **Collection** operators treat **Left** as a comma-separated list and look **Right** up in it. | `Comparisons` (the rules list; every row supports `{variables}`) |
| 🔒 `if_user_permission` — Permissions Condition | Branches on a Discord **server permission** of a user (e.g. `Administrator`, `ManageMessages`), resolved through `GuildPermissions`. | `Permission`, `Username` (defaults to `{username}`) |
| # `if_channel` — Channel Condition | Branches on the channel the command was used in: **True** when the origin channel matches any entry in a comma-separated list (leading `#` tolerated, case-insensitive). | `Channels` (e.g. `general, #support`) |
| 👤 `if_user` — User Condition | Branches on who used the command: **True** when the triggering user matches any entry in a comma-separated list (case-insensitive). | `Users` |

> **No Switch block.** Conditions are the only branching blocks — the old 🔀 `switch` was removed from the palette. Flows saved with a Switch still run and render (each case keeps its own port, unmatched takes the amber **Default**), but new ones can't be placed.
>
> Comparison flows saved by older builds keep working too: a legacy comparison (single **Left / Operator / Right** with green True/False branches) still runs exactly as before — the old operator symbols (`==`, `!=`, `>`, `<`, `>=`, `<=`) load and map onto the new operators, and the settings panel prefills one rule row from the stored values. The moment you edit the rules there, the flow migrates to the new look: rule cards plus an **Else** card appear under the node, and the old True/False wiring moves onto the first rule's card and the Else card. **All condition blocks behave this way**: their False branch auto-attaches an Else card (the grey False port drops a grey elbow to it), while True stays the direct green branch.

**Example — one Comparison Condition per button:** in a button sub-flow, put a **Comparison Condition** with the rule **Left** `{buttonId}` / **Contains** (or **Ends with**) / the button id `btn0` to detect the first button — `{buttonId}` carries the component's full id tail (`…:{nodeId}:btn0`, so a literal **Equal to** only works with that whole string). The matching rule's card takes the reply for that button; hang the fallback Send Message ("Please use the buttons above 👆") off the **Else** card so every other button lands on the retry path. Mind KI-46: the ids are positional, so reordering or removing buttons renumbers them and the comparisons must be updated.

### Flow variables

Every text field supports `{variables}`. Inside a flow run:

- **From the trigger**: `{username}`, `{channel}`, `{platform}`, `{message}`, `{args}`, `{command}`, `{guild}`, `{userRoles}`, `{isBot}`, `{isModerator}`.
- **From typed slash parameters** (Discord slash triggers only): one `{parameterName}` variable per authored parameter (also mirrored under its normalized name) — see [Discord specifics](#discord-specifics).
- **From component clicks** (Discord): `{customId}`, `{buttonId}`, `{values}` inside a button/menu sub-flow.
- **Produced by blocks**: `{response}` (Wait for Response), `{http_response}` / `{http_status}` (HTTP Request), `{random}` (Random Number — or a custom name via `store`), `{builtin_response}` (Run Built-in Command — or a custom name via `OutputVariable`).
- **Anything you define** with **Set Variable**.

### Runtime & execution

- The **entry node** is the block pinned by the trigger card's blue wire (`Workflow.EntryNodeId`) when set and present; otherwise the engine detects it — the first node nothing routes into. From there it follows the wires (a 200-step guard stops runaway graphs).
- **Loops run natively**: a `loop` node executes its body chain (the one hanging off its **Loop # times** card) once per iteration — the count substitutes `{variables}`, invalid/non-positive counts fall back to 1, and anything above 25 clamps to 25 — then continues via **After loop**. Body steps count toward the 200-step budget. A `stop_loop` inside the body ends every remaining iteration.
- **Multi-rule comparisons run top-down**: a `comparison` with a rules list evaluates its rules in order and the **first match** routes into that rule's attached chain; no match takes the **Else** chain (an unwired Else falls back to the legacy False branch or **Next**; an unwired rule just continues the chain). Legacy single-rule comparisons still output True/False.
- **Attachment cards are real pass-through blocks**: the loop/comparison cards (and the button/menu sub-cards) are ordinary nodes in the saved flow — executing one is an instant no-op and the walk simply continues through its blue dot. They appear in the node path and count toward the 200-step budget.
- **Errors route nearest-first**: a failing node takes its own wired amber error port when it has one. Without one, the workflow's **Error Handler** chain (see above) runs as a reaction — its steps count toward the budget and appear in History — and the run still ends **Failed** with the original error.
- Every run is recorded in **History** with its status, duration, and the exact node path.

### Discord specifics

- Flows with a **Slash Command** trigger auto-register as **real Discord `/` commands** in every guild the bot is in — re-registered on reconnect and on each save. Registration requires the bot to be online (KI-40).
- **Typed parameters register as typed slash options.** The COMMAND PARAMETERS entries (Text/Number/User/Channel/Role/Choice/Attachment) become one real Discord option each — text→String, Number→Integer, User/Channel/Role→their Discord types, Choice→Boolean — capped at Discord's 25 options per command, names normalized to Discord's identifier rules. Their values arrive in the flow as `{parameterName}` variables (also mirrored under the normalized name); User/Channel/Role options yield **names**, Choice yields `true`/`false`. A slash command with no parameters keeps the legacy single optional `args` text option.
- **Prefix commands don't see parameters** — `!command` triggers still pass everything through `{args}`; only slash triggers register typed options (KI-44).
- **Buttons and menus are real components** — the Buttons & Menus section (above) sends Discord action rows, and clicks dispatch to the component child flows. Discord only; other platforms just get the text/embed.
- Command options (cooldown, moderator requirement, channel restrictions) are enforced **before** a flow runs.
- The embed builder's JSON is sent as **real Discord embeds**.

The platform side (which bot registers what, and the online requirement) is in [Discord — Visual command builder](../platforms/discord.md#visual-command-builder-workflows).

## Overview

The **Automation** page (sidebar, Chat Features group) is Vyral's workflow engine — a local-first, per-profile automation system in the spirit of BotGhost, Streamer.bot and Node-RED. Workflows are stored as JSON (no generated code) in the per-profile `Workflows` table; every run is recorded in `WorkflowExecutions` with per-node timings.

## User Guide

1. **Create**: Automation → New Workflow. Name it, pick a **trigger**:
   - *Command* — a `!name` chat command on any connected platform (works alongside existing commands; only exact registered names are intercepted).
   - *Timer* — fires every N minutes.
   - *Event* — platform events: follow, subscribe, resub, giftsub, raid, cheer, host (Twitch/Kick), optionally filtered by platform.
   - *Manual* — runs only when you press RUN.
2. **Build steps**: ordered nodes — Send Message (with `{variables}`), Wait, **If** (expression like `{coins} > 100` or `{message} contains 'hello'`) with true/false branches, Set Variable, Random Number, Add/Remove Currency, Log. Add, reorder, remove; the live path preview shows the whole chain.
3. **Run & debug**: RUN executes immediately (even on disabled drafts). HISTORY shows the last runs: status, duration, the exact node path, and errors per run.
4. Toggling a workflow enabled (or saving) applies live — no restart.

> The **Visual Flow Editor** below is the drag-and-drop canvas alternative to this ordered list — it edits the same workflows.

## How It Works (Developer)

- New project `src/VyralBot.Workflows`: `WorkflowRuntime` (graph interpreter, 200-step cycle guard, cancellation-aware; native `loop` interpretation with `stop_loop`, multi-rule `comparison` routing (`case:{i}` first match / `else`), pass-through attachment-card nodes, workflow-level Error Handler chains, explicit `Workflow.EntryNodeId` pinning), `ExecutionContext` (execution-scoped variables + `{username}`/`{channel}`/`{platform}` fallbacks), `NodeRegistry` (extensible — plugins can register nodes), `ExpressionEvaluator` (comparisons + ` and `, numeric/string/contains semantics).
- Triggers live in `src/VyralBot.UI/Services/WorkflowTriggerService.cs`: command matching hooks the chat pipeline before the regular command engine; event triggers subscribe to the shared EventBus; a 60s timer tick drives timer triggers (last-run in memory V1).
- Output routing mirrors chat replies: Twitch/Kick/Discord per origin bot.
- Existing commands, timers, giveaways, polls, currency are untouched — workflows are additive (roadmap Phase 25).

## Files & Code

| File | Role |
|---|---|---|
| [VyralBot.Workflows](../../src/VyralBot.Workflows) | Engine project: runtime, nodes, expressions, JSON persistence |
| [WorkflowRuntime.cs](../../src/VyralBot.Workflows/Runtime/WorkflowRuntime.cs) | Graph interpreter + execution results |
| [WorkflowTriggerService.cs](../../src/VyralBot.UI/Services/WorkflowTriggerService.cs) | Command/event/timer triggers, manual runs |
| [FlowEditorView.xaml](../../src/VyralBot.UI/Views/FlowEditorView.xaml) | Visual Flow Editor: six-panel side rail, canvas, and settings panes |
| [FlowEditorViewModel.cs](../../src/VyralBot.UI/ViewModels/FlowEditorViewModel.cs) | Editor state: nodes/wires, undo/redo, drafts autosave, import/export, command adoption on save |
| [CommandFlowTemplates.cs](../../src/VyralBot.UI/Services/CommandFlowTemplates.cs) | Command row → editable flow conversion (seeds the editor from the Commands page — see [Commands](commands.md#from-command-to-flow)) |
| [BlockCatalog.cs](../../src/VyralBot.Workflows/Abstractions/BlockCatalog.cs) | The block library: palette entries, parameter fields, output ports |
| [AutomationView.xaml](../../src/VyralBot.UI/Views/AutomationView.xaml) | The builder page |

## Data & Settings

`Workflows` (Id, Name, Description, Enabled, Category, TriggerJson, GraphJson, timestamps) and `WorkflowExecutions` (run history with status/duration/path/error), both per-profile.

## Status & Known Issues

V1 (per the roadmap): the ordered-list editor renders one nesting level of If-branches; timer last-run is in-memory (fires fresh after restart); no trigger-condition editor (round-trips untouched). The Visual Flow Editor (above) is the canvas builder — button/select-menu triggers now receive clicks from the message components the Buttons & Menus section attaches (KI-39, resolved), and the purple tab's COMMAND PARAMETERS group registers typed slash options (KI-43, resolved; prefix commands still run on `{args}` — KI-44). Component button ids are positional and renumber on reorder (KI-46). The BotGhost music/voice/premium block suite (Create Music Player, voice-channel actions, IFTTT, transcripts, Components V2, Premium Check) is not implemented (KI-45), slash-command registration needs the bot online (KI-40). The plugin SDK, AI/scripting nodes, marketplace and breakpoint debugger are later phases. See the [known issues ledger](../reference/known-issues.md).
