# BotGhost Command Builder — Visual Reference (captured 2026-08-30)

> Source: 8 user-provided screenshots of the BotGhost command builder.
> Purpose: restyle Vyral's flow editor to match this look/structure. Analysis only —
> implementation starts after the user sends the remaining images.
> Image cache: `C:\Users\user\.zcode\cli\image-cache\sess_55a77d26-f673-4402-af94-704e41085189\`
> - image-8ae370b5010f2e28e33a2f8e2a58e389.png (full editor, wide)
> - image-6babaf23c29264f6938ad538a9d4146e.png (palette: Advanced/Reply Actions)
> - image-711d629dffd4ee20bc9c7221dd11e15e.png (palette: DM/Buttons/Select/Message Actions)
> - image-dcc0ae186a1cd61d58d1d2e61a5b9dcf.png (palette: Pin/Transcript/Variables/API)
> - image-9844bb8a4705efe8d3eca46f5142c366.png (palette: Loops/Music start)
> - image-fd0be6343ecb1ff92c2f8adb77e40da6.png (palette: Music controls)
> - image-5210c29b329a65d758fdc7d9534f600d.png (palette: Queue/Filters/Voice start)
> - image-f7d233c6513bf3620b93cc89f08835d6.png (palette: Voice actions/Role actions)

---

## 1. Overall layout (4 columns)

```
┌──┬──────────────┬──────────────────────────────────────┬──────────────────┐
│I │ BLOCKS PANEL │            CANVAS                    │ BLOCK SETTINGS   │
│C │ "Blocks ❓"  │  dark + dot grid                     │ "Block Settings" │
│O │ [hamburger]  │  nodes connected top→bottom          │ [red X close]    │
│N │ intro text   │                                      │ scrolling form   │
│  │ [3 tabs]     │                                      │                  │
│R │ [Search]     │                                      │                  │
│A │ group header │                                      │                  │
│I │ ⠿ ▣ Block    │                                      │                  │
│L │   description│                                      │                  │
└──┴──────────────┴──────────────────────────────────────┴──────────────────┘
```

- **Far-left icon rail** (very narrow, ~56px): vertical stack of colored square category
  icons at the top — blocks cube (indigo, active), `</>` code, ⚠️ red triangle, green
  circle, yellow clock, purple user icons. Reads as editor-section shortcuts.
- **Blocks panel** (~380-500px): header + intro + 3 tabs + search + grouped block list.
- **Canvas**: dark background, subtle dot grid. Main flow runs TOP → BOTTOM.
- **Block Settings panel** (~430px): header "Block Settings" + red square X (close) at
  top right; scrolling per-block form below.

## 2. Blocks panel anatomy

- **Header**: bold "Blocks" + grey ❓ help icon, right-aligned **indigo hamburger button**
  (rounded square).
- **Intro copy** (colour-coded): "Drag and drop <purple>Options</purple>,
  <blue>Actions</blue> and <green>Conditions</green> to add them to your command.
  **Connect the corresponding colors** to create your command flow."
  → COLOR CODING: **Options = purple, Actions = blue, Conditions = GREEN**
  (differs from our current palette: our conditions are red — BotGhost wires condition
  branches in green; wire colors match the category color of the port).
- **Tabs**: `Options | Actions | Conditions` — active tab = darker background + **red
  underline**; full-width segmented bar.
- **Search**: dark rounded rectangle, placeholder "Search".
- **Grouped list**: bold white group headers ("Advanced Message", "Reply Actions (Old)",
  "Message Actions", "Custom Variable Actions", "API Actions", "Loop Actions",
  "Music Actions", "Voice Actions", "Role Actions").
- **Block row**: `⠿ drag handle` + colored rounded-square icon (all Actions blocks are
  BLUE squares w/ white glyph) + bold white title + grey one-line description.
  Premium blocks: ⚡ lightning prefix, **red title text**, full-row subtle red-tinted
  background (e.g. Create Transcript, Add/Remove Roles to All Users).

## 3. Complete Actions inventory captured (exact titles + descriptions)

### Advanced Message
| Block | Description |
|---|---|
| Send or Edit a Message | Send or edit a message with optional buttons and select menus. |
| Edit a Button or Select Menu | Edit a button or select menu in a message |

### Reply Actions (Old)
| Block | Description |
|---|---|
| Plain Text Reply | Bot replies with a plain text response |
| Embed Reply | Bot replies with an embed response |
| Random Reply | Bot responds with a random response |
| Send a message to a Channel | Bot sends a message to a specific channel |

### (continuing the list, same Reply group)
| Block | Description |
|---|---|
| Direct Message a User | Bot sends a direct message to a member |
| Send a Message with attached Buttons | Add buttons and button actions |
| Send a Message with a Select Menu | Create a select menu with attached actions |

### Message Actions
| Block | Description |
|---|---|
| Send a Form | Send a form or modal and wait for a response |
| Delete a Message | Delete a message or reply |
| Publish a Message | Publish a message sent in an announcement channel |
| React to a Message | React to a message |
| Pin a Message | Pin a message or reply sent in this command |
| ⚡ Create Transcript (premium) | Generate an HTML transcript of channel messages |

### Custom Variable Actions
| Block | Description |
|---|---|
| Set Variable | Set the value of a custom variable |
| Run Equation on Variable | Run an equation on a custom variable |
| Delete Variable | Delete the value of a custom variable |

### API Actions
| Block | Description |
|---|---|
| Send an API Request | Send a HTTP request to an external API |
| Execute an IFTTT Trigger | Execute an IFTTT trigger and any associated applets |

### Loop Actions
| Block | Description |
|---|---|
| Run a Loop | Loop through actions and conditions a set amount of times |
| Stop a Loop | Break out of and stop any active loop |

### Music Actions
| Block | Description |
|---|---|
| Create Music Player | Create a music player for the server |
| Add to Queue | Add a track to the queue |
| Play Queue | Start playing the music queue |
| Pause Music | Pause the current music playback |
| Resume Music | Resume the paused music playback |
| Stop Music | Stop the current music playback |
| Disconnect from Voice | Disconnect from voice channel and optionally destroy the player |
| Skip Track | Skip to the next track or a specific position in the queue |
| Play Previous Track | Go back to a previously played track from history |
| Set Track Position / Seek | Set the track position (absolute or relative seek) |
| Set Volume | Change the playback volume (0-200+) |
| Set Loop Mode | Set loop mode (0=off, 1=track, 2=queue) |
| Set AutoLeave | Enable/disable auto-leave when queue ends |
| Replay Track | Restart the current track from the beginning |
| Remove from Queue | Remove track(s) from the queue |
| Shuffle Queue | Shuffle the music queue in a random order |
| Apply Audio Filter | Apply audio filters (bassboost, nightcore, vaporwave, 8d, etc.) |
| Clear Filters | Remove all active audio filters from the player |
| Search Tracks | Search for tracks without adding to queue |

### Voice Actions
| Block | Description |
|---|---|
| Join a Voice Channel | Join a voice channel |
| Leave a Voice Channel | Leave a voice channel |
| Voice Move a Member | Move a member to another voice channel |
| Voice Kick a Member | Kick a member from a voice channel |
| Mute or Unmute a Member | Mute or unmute a member in a voice channel |
| Deafen or Undeafen a Member | Deafen or undeafen a member in a voice channel |

### Role Actions
| Block | Description |
|---|---|
| Add Roles | Adds one or more roles to a user |
| Remove Roles | Removes one or more roles from a user |
| ⚡ Add Roles to All Users (premium) | Add one or more roles to all users in the server |
| ⚡ Remove Roles from All Users (premium) | Remove one or more roles from all users in the server |

### Role/Channel/Thread management (batch 2, Screenshot 2026-08-30 142551)
| Block | Description |
|---|---|
| Create a role | Creates a role in the server |
| Delete a role | Deletes a role in the server |
| Edit a Role | Edits a role in the server |
| **Channel Actions** (group) | |
| Create a Channel | Creates a channel in the server |
| Edit a Channel | Edits a channel in the server |
| Delete a Channel | Deletes a channel in the server |
| **Thread and Forum Actions** (group) | |
| Create a Thread | Create a public or private thread in the server |
| Edit a Thread | Edit a public or private thread in the server |
| Delete a Thread | Delete a public or private thread in the server |

### Server Actions (batch 2, Screenshot 2026-08-30 142616 + 142623)
| Block | Description |
|---|---|
| Kick a Member | Kick a member from the server |
| Ban a Member | Ban a member from the server |
| Timeout a Member | Timeout a member in the server |
| Change a Members Nickname | Change a member nickname in the server |
| Purge Messages | Purge messages in a channel |
| Create Server Invite | Create an invite for the server |
| Leave Server | Force the bot to leave a server |

### Other Actions (batch 2, Screenshot 2026-08-30 142623)
| Block | Description |
|---|---|
| Wait before running another action | Wait before executing another action |
| Manipulate some text | Manipulate and run functions on provided text |
| Send an error log message | Trigger an error message |
| Set a unique variable | Set a unique variable for every execution of this command or event. |
| Change the Bot's Status | Change the bot's status or presence |
| Note | **Write a note in the tree** — a canvas comment/annotation block |

(Options and Conditions tabs captured in batch 2 — see section 8.)

## 4. Canvas / node anatomy (from the wide screenshot)

- **Root node** at top: larger card, **yellow icon square**, title = command name
  ("/beta email-guide"), with the intro copy rendered inside it.
- **Error Handler**: separate parallel node (red ⚠️ icon) off to the side —
  "Handle errors that occur during the command execution", connected independently.
- **Action nodes**: dark card, blue rounded-square icon on the left, bold white title,
  grey description underneath. Blue circular **ports**: one on TOP (in), one on BOTTOM
  (out), connected by a straight blue line; a small ⊗ marker sits mid-edge on the node
  border where the line meets (connection point visual).
- **Component chips**: a message node can carry attachments — small dark chips labeled
  "Button" (bottom-left of node) and "Menu" (bottom-right), each with a ⊕ icon.
- **Button action sub-nodes**: clicking a Button chip spawns small linked nodes in the
  flow ("Start / Button", "Part 1 / Send an API Request", "Next / Button") — i.e. button
  clicks branch into their own sub-flows, rendered as small icon-chip cards.
- **Selection**: dashed indigo border around the selected node; solid indigo border on
  the Settings panel's selected option card.
- **Canvas controls** bottom-left: two small white square buttons (fit-to-view +
  auto-layout/shuffle icons).
- Main flow example captured: `/beta email-guide` → Send or Edit a Message →
  (Start Button sub-flow: Start → Part 1 Send an API Request → Delete a Message) →
  Send or Edit a Message (with Button/Menu chips) → Delete a Message.

## 5. Block Settings panel patterns (right side)

- Header: "Block Settings" + **red X** (close) — panel hides when nothing selected.
- Block title (bold, large) + grey description.
- **Builder Mode** radio-cards: two selectable cards stacked ("Message Builder" —
  indigo highlight + radio check when active, vs "Components V2") + footnote
  "• Message Builder Active".
- Section "Message Builder ✅" → big indigo CTA "💬 Open Message Builder →"
  (opens the full embed/message builder dialog — likely in a later image).
- **"Response type ❓"** dropdown ("Reply to the command").
- **"Optional Variable"**: "An optional variable to return the message id of this reply.
  You can use this variable in other blocks." + text field. → message-id capture
  pattern (our `wait_for_response`/message scope could adopt this).
- **"Allowed Mentions (Optional)"**: "Control which mentions are allowed to ping users…"
  (cut off).
- Bold section headers with grey ❓ help circles throughout.

## 6. Notable deltas vs Vyral's current flow editor

| Aspect | BotGhost | Vyral today | Action (pending user's go-ahead) |
|---|---|---|---|
| Icon rail | Left category rail | none | add? |
| Palette grouping | group headers inside tabs | flat list w/ 2 tabs | group headers per tab |
| Tabs | Options/Actions/Conditions, red underline | Actions/Conditions | add Options tab styling |
| Colors | Options=purple, Actions=blue, Conditions=green | conditions red | re-map wire/port colors |
| Drag handle | ⠿ per row | none | add |
| Premium marker | ⚡ red rows | n/a | skip (no premium tier) |
| Node look | icon chip + title + desc, top/bottom ports | similar already | restyle to match |
| Component chips | Button/Menu chips on message nodes | none (KI-39) | future |
| Error Handler | dedicated node | error port on nodes | compare |
| Settings panel | radio-cards, CTA buttons, ❓ helps, optional-variable capture | plain fields | restyle + optional-variable pattern |
| Blocks not in Vyral | music player suite, voice channel join/move, forms/modals, IFTTT, transcripts, loops, publish/pin, edit-button/menu, Components V2 | — | inventory for later phases |

## 7. Batch 2 — Options + Conditions tabs (5 screenshots, 2026-08-30)

Sources:
- `OneDrive\Pictures\Screenshots\Screenshot 2026-08-30 142738.png` (Conditions tab)
- `OneDrive\Pictures\Screenshots\Screenshot 2026-08-30 142733.png` (Options tab)
- `OneDrive\Pictures\Screenshots\Screenshot 2026-08-30 142623.png` (Other Actions)
- `OneDrive\Pictures\Screenshots\Screenshot 2026-08-30 142616.png` (Server/Thread Actions)
- `OneDrive\Pictures\Screenshots\Screenshot 2026-08-30 142551.png` (Role/Channel/Thread)

### Options tab — PURPLE blocks (command PARAMETERS, not constraints!)
All icons are purple rounded-squares. **These are slash-command argument definitions** —
each dropped block defines a typed parameter the command accepts:

| Block | Description |
|---|---|
| Text | A text option |
| Number | A number option |
| User | Select a member from the server |
| Channel | Select a channel from the server |
| Role | Select a role from the server |
| Choice | A True or False option |
| Attachment | An attachment option |
| Music Smart Search | Search and select music tracks |

⚠️ KEY INSIGHT for Vyral: BotGhost's "Options" = **typed command parameters**
(the equivalent of slash-command options). Vyral's current "Options…" popup
(cooldown/permission/channel restriction) is a DIFFERENT concept — ours are command
constraints. If we adopt the BotGhost layout, the purple Options tab should hold
typed argument definitions ({args} typed variants feeding {username}/{number} etc.),
and our cooldown/permission constraints stay in a command-settings area.
No group headers in this tab — flat list.

### Conditions tab — GREEN blocks
ALL condition icons are green rounded-squares (confirms the color code). Flat list,
no group headers:

| Block | Description |
|---|---|
| Comparison Condition | Run actions based on the difference between two values. (≥ icon) |
| Chance Condition | Run actions based on a random chance. (dice) |
| Permissions Condition | Run actions based on the server permissions of a user. (lock) |
| Role Condition | Run actions based on the roles of the user. |
| Channel Condition | Run actions based on the channel the command was used in. (#) |
| User Condition | Run actions based on who used the command. (person) |
| Premium Check Condition | Run actions based on whether the bot is premium or not ($ icon) |

Tab active-state styling note: Actions tab shows a darker background + RED underline;
in the Conditions screenshot the active tab shows a **white outline box** instead —
likely a hover/focus state. Treat "dark bg + colored underline (red)" as the base
pattern.

### Impact on the Vyral mapping
- Conditions→Vyral: Comparison ≈ If (Expression); Chance ≈ Random+If; Permissions ≈
  our `isModerator` check; Role Condition ≈ if_user_has_role; Channel Condition ≈
  channel restriction/if; User Condition ≈ user check; Premium Check — skip.
- Green wire color for condition True/False branches (both branches green in
  BotGhost; our True=green/False=red split is a difference to decide on).
- Options tab (purple) = typed args → candidate feature: typed slash options for
  Vyral slash commands (currently single text "args").

## 8. Status

- ✅ 13 images captured total (batch 1: 8 — full editor + Actions A–Z; batch 2: 5 —
  **Options tab (purple, typed command parameters)**, **Conditions tab (green, 7
  conditions)**, plus Server/Channel/Thread/Role/Other action groups).
- ⏳ Waiting for the user to say "done sending" before starting the restyle work.
  Possibly still coming: the Message Builder dialog, other settings panels
  (Response type, Allowed Mentions), and any additional tabs/panels.


## 9. Batch 3 — side panels + wiring semantics (2026-08-30, 7 screenshots)

### The 6 side-rail panels (left icon rail, top→bottom)
1. **Blocks** (blue cube) — the block palette (built).
2. **Variables** (pink `</>`) — search, "+ Create New Variable" (indigo CTA), rows: name + {BGVAR_token} + type badge (User Object) + N props.
3. **Error Logs** (red ⚠️) — "View error logs and debugging information for your command" + Refresh button; rows: ⚠️ title + relative time, message, "Module: <command>".
4. **Commands** (green) — tabs Commands|Events; "New Command" button; search; "CUSTOM COMMANDS" group; rows: /name + description + edit/toggle/delete buttons.
5. **Timed Events** (yellow clock) — intro; Timezone dropdown; Default Server dropdown; yellow "Add New Timed Event" CTA; Active Events (N) list.
6. **Block Templates** (purple block) — "Click and drag and drop a saved block template"; save via Shift-multi-select + "Save Template"; search; template rows (name + description, drag handles).

Hamburger (indigo ☰, panel top-right): collapses the ENTIRE left side (rail + panel);
a small ☰ button remains afterwards and reopens on click.

### Wiring semantics (user-confirmed, OVERRIDES earlier guesses)
- ONE colored dot per block: single input (top), single output (bottom).
- Main-flow lines are COLORED, not grey (user: "the command block doesn't need a gray
  line"). Grey lines are ONLY for things ATTACHED via a block's settings:
  buttons/menus sub-flows, the loop body ("Loop 5 times") and "After loop" cards,
  Error Handler.
- ALL blocks connect to a single next block. The ONLY branching is condition blocks
  (Comparison = if/else). Switch-style multi-outputs must not be in the palette.
- Example given: If "button 5 was pressed" then run response for button 5, else say
  "try again" — i.e. comparison on {buttonId} inside a button sub-flow.
- Comparison Type dropdown (full BotGhost list): Less than, Less than or equal to,
  Greater than, Greater than or equal to, Equal to, Not equal to, Contains,
  Does not contain, Starts with, Ends with, Does not start with, Does not end with,
  Collection contains, Collection does not contain.
