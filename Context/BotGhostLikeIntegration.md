# BotGhostLikeIntegration.md

# Visual Automation Engine Roadmap for Vyral

## Goal

Transform Vyral from a command-based chatbot into a visual automation platform similar to BotGhost, Streamer.bot, SAMMI and Node-RED while preserving Vyral's current architecture.

The new system should **extend** the existing EventBus, integrations, and Core services instead of replacing them.

The automation engine becomes the central execution system for every platform.

---

# Core Design Principles

- Local-first
- Offline capable
- Per-profile storage
- Multi-platform
- Multi-bot
- Plugin extensible
- Scriptable
- Event driven
- Visual
- Debuggable
- Backwards compatible

Nothing in the current application should stop working.

Existing commands should continue to work while workflows are gradually adopted.

---

# Current Architecture

Existing strengths:

UI
    WPF
    MVVM
    Material Design

Core
    EventBus
    Commands
    Currency
    Giveaways
    Polls
    Rules
    Timers
    Watchtime

Data
    SQLite
    Dapper
    Per Profile

Integrations

    Twitch
    Kick
    YouTube
    Discord

API

    Kestrel
    WebSockets
    Ngrok

This architecture is already ideal for an automation engine.

---

# Phase 1

## Create VyralBot.Workflows

New project

VyralBot.Workflows

Contains

Engine

Runtime

Node Registry

Trigger Registry

Scheduler

Expressions

Serialization

Execution Context

Debugger

History

---

# Phase 2

## Workflow Model

Everything is a workflow.

Workflow

- Id
- Name
- Description
- Enabled
- Category
- Trigger
- Variables
- Permissions
- Nodes
- Connections
- Metadata

Stored as JSON.

No generated C#.

No generated code.

---

# Phase 3

## Trigger System

Replace platform-specific command execution with trigger publishing.

Every integration becomes a trigger source.

Discord Message

↓

Workflow Trigger

Twitch Message

↓

Workflow Trigger

Kick Event

↓

Workflow Trigger

OBS Event

↓

Workflow Trigger

Timer

↓

Workflow Trigger

Webhook

↓

Workflow Trigger

Manual Run

↓

Workflow Trigger

Future integrations require only a trigger provider.

---

# Phase 4

## Workflow Runtime

Create a graph interpreter.

Execution flow

Trigger

↓

Load Workflow

↓

Create Execution Context

↓

Execute Node

↓

Determine Next Node

↓

Continue

↓

Complete

Every node executes independently.

---

# Phase 5

## Execution Context

ExecutionContext contains

Current Platform

Bot

Guild

Channel

Streamer

User

Message

Variables

Services

CancellationToken

Logger

ExecutionId

Every node receives the same context.

---

# Phase 6

## Node Registry

Create

INode

Every node registers itself.

Categories

Logic

Variables

Discord

Twitch

Kick

YouTube

OBS

AI

Timers

Economy

Giveaways

Polls

Moderation

Roles

Files

HTTP

Database

Math

Text

Random

Sound

Media

Plugins

Future plugins can register additional nodes.

---

# Phase 7

## Visual Workflow Editor

New UI

Automation

Features

Infinite Canvas

Zoom

Pan

Drag

Drop

Search

Comments

Groups

Undo

Redo

Copy

Paste

Auto Layout

Property Inspector

Mini Map

Execution Highlighting

The editor should resemble Unreal Blueprints or Node-RED.

---

# Phase 8

## Database

Add new tables

Workflows

WorkflowNodes

WorkflowVariables

WorkflowFolders

WorkflowExecutions

WorkflowSchedules

WorkflowTemplates

WorkflowMarketplace

Eventually these can be optimized into JSON storage if desired.

---

# Phase 9

## Variable System

Expand current variables.

Scopes

Global

Profile

Platform

Bot

Guild

Channel

User

Workflow

Execution

Temporary

Examples

{user.name}

{coins}

{watchtime}

{stream.title}

{discord.server}

{kick.viewerCount}

{obs.scene}

Variables continue using Vyral's existing template system.

---

# Phase 10

## Expression Engine

Current placeholders become expressions.

Examples

coins > 100

viewerCount > 50

user.roles contains Moderator

stream.live == true

Functions

Math

Random

Regex

Date

String

Collections

JSON

Variables

---

# Phase 11

## Scheduler

Long-running workflows.

Delay

Wait

Repeat

Cron

Cooldowns

Resume after restart

Execution persistence.

---

# Phase 12

## Existing Core Integration

Do not duplicate existing systems.

Instead expose them as nodes.

Currency

Giveaway

Poll

Watchtime

Rules

Commands

Timers

Sounds

Each existing Core service becomes callable from workflows.

---

# Phase 13

## Discord Integration

Replace DiscordModCommandHandler.

Discord only publishes

DiscordChatMessageEvent

WorkflowTriggerService subscribes.

Slash Commands

Message Commands

Button Clicks

Select Menus

Modals

Reactions

Threads

Voice

All become workflow triggers.

---

# Phase 14

## Twitch

Triggers

Message

Follow

Sub

Raid

Bits

Reward Redemption

Prediction

Poll

Goal

Everything executes workflows.

---

# Phase 15

## Kick

Triggers

Chat

Follow

Subscription

Gift

Webhook

Stream Online

Stream Offline

---

# Phase 16

## YouTube

Triggers

Chat

Super Chat

Member

Live Started

Live Ended

---

# Phase 17

## OBS

Expand beyond audio.

Triggers

Scene Changed

Source Visible

Recording Started

Recording Stopped

Replay Buffer

Streaming Started

Streaming Ended

Nodes

Switch Scene

Toggle Source

Mute

Volume

Filter

Replay Buffer

---

# Phase 18

## HTTP/API

Nodes

GET

POST

PUT

DELETE

Webhook Response

Download File

Upload File

JSON

Authentication

---

# Phase 19

## AI

Nodes

OpenAI

Claude

Gemini

Local LLM

Memory

Embeddings

Moderation

Summarization

Conversation

---

# Phase 20

## Plugin SDK

Plugins can register

Nodes

Triggers

Panels

Variables

Themes

Services

Settings Pages

Menus

This utilizes the existing VyralBot.Plugins project.

---

# Phase 21

## Scripting

Use existing VyralBot.Scripting.

Support

C#

JavaScript

Lua (optional)

Python (future)

Scripts become custom workflow nodes.

---

# Phase 22

## Marketplace

Export

Workflow

↓

Share

↓

Import

Workflow packages

Node packages

Theme packages

Plugin packages

---

# Phase 23

## Analytics

Track

Workflow executions

Execution time

Node timing

Failures

Retries

Usage

Platform statistics

Most-used nodes

Most-used triggers

---

# Phase 24

## Debugger

One of Vyral's biggest differentiators.

Run workflow

↓

Highlight node

↓

Display variables

↓

Display execution time

↓

Continue

↓

Pause

↓

Breakpoints

↓

Resume

↓

History

Much more powerful than BotGhost.

---

# Phase 25

## Migration

Existing command system remains operational.

Commands become wrappers around workflows.

Old

!hello

↓

Workflow

Start

↓

Send Message

Users should never lose functionality.

Migration should be automatic where possible.

---

# Final Vision

Vyral evolves from

"A multi-platform chatbot"

into

"A local-first visual automation platform for creators."

Everything becomes an event.

Every event starts a workflow.

Every workflow executes reusable nodes.

Platforms become interchangeable.

Discord, Twitch, Kick, YouTube, OBS, Webhooks, Timers, AI, Plugins, and future integrations all operate through the same automation engine.

The result is significantly more powerful than BotGhost because it is:

- Local-first
- Multi-platform
- Plugin-driven
- AI extensible
- Scriptable
- Offline capable
- Fully debuggable
- Open source
- Cross-platform automation rather than Discord-only automation.