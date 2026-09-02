"""Seed Visual Builder test workflows into the Drip profile database.

Creates three slash commands (/hello, /panel, /roll) plus two button child
flows, then prints the resulting rows. Run with the app STOPPED.
"""
import datetime
import json
import sqlite3

DB = r"appdata/profiles/Drip/data/vyralbot.db"


def node(nid, ntype, config, nxt=None):
    n = {"id": nid, "type": ntype, "config": config, "x": 0, "y": 0}
    if nxt:
        n["next"] = nxt
    return n


def trigger(t, command):
    return json.dumps({"type": t, "commandName": command})


def graph(nodes, entry=None, parameters=None, component_key=None):
    doc = {"nodes": nodes, "options": []}
    if entry:
        doc["entryNodeId"] = entry
    if parameters:
        doc["parameters"] = parameters
    if component_key:
        doc["componentKey"] = component_key
    return json.dumps(doc)


def main():
    con = sqlite3.connect(DB)
    cur = con.cursor()
    now = datetime.datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")

    hello_text = "👋 **{username}** ran `/hello` — the Visual Builder → Discord pipeline is live! (args: `{args}`)"
    rows = [
        (
            "wf-hello",
            "Hello (VB test)",
            "Visual Builder test: greets the invoker and echoes args.",
            1,
            trigger("slash_command", "hello"),
            graph([node("n1", "send_message", {"text": hello_text})], entry="n1"),
            "Tests",
        ),
        (
            "wf-panel",
            "Control panel (VB test)",
            "Visual Builder test: posts a button panel wired to child flows.",
            1,
            trigger("slash_command", "panel"),
            graph(
                [
                    node(
                        "p1",
                        "send_message",
                        {
                            "text": "🎛️ **Control panel** for **{username}** — click a button:",
                            "Buttons": json.dumps(
                                [
                                    {"label": "Ping", "style": "Primary"},
                                    {"label": "About", "style": "Secondary"},
                                ]
                            ),
                        },
                    )
                ],
                entry="p1",
            ),
            "Tests",
        ),
        (
            "wf-panel-btn0",
            "Panel → Ping",
            None,
            1,
            trigger("button_click", "Panel → Ping"),
            graph(
                [node("b0", "send_message", {"text": "🏓 **Pong!** Buttons dispatch back into flows natively."})],
                entry="b0",
                component_key="vyralwf:wf-panel:p1:btn0",
            ),
            "Tests",
        ),
        (
            "wf-panel-btn1",
            "Panel → About",
            None,
            1,
            trigger("button_click", "Panel → About"),
            graph(
                [node("b1", "send_message", {"text": "ℹ️ This panel was built with the **Vyral Visual Builder** and runs natively on Discord."})],
                entry="b1",
                component_key="vyralwf:wf-panel:p1:btn1",
            ),
            "Tests",
        ),
        (
            "wf-roll",
            "Roll (VB test)",
            "Visual Builder test: typed parameter (number) echo.",
            1,
            trigger("slash_command", "roll"),
            graph(
                [
                    node(
                        "r1",
                        "send_message",
                        {"text": "🎲 **{username}** wants a d{sides} roll (args: `{args}`) — parameter routing works!"},
                    )
                ],
                entry="r1",
                parameters=[{"name": "sides", "type": "number", "description": "How many sides the die has", "required": False}],
            ),
            "Tests",
        ),
    ]

    cur.execute("DELETE FROM Workflows WHERE Id LIKE 'wf-%' OR Category='Tests'")
    for r in rows:
        cur.execute(
            "INSERT INTO Workflows (Id, Name, Description, Enabled, TriggerJson, GraphJson, Category, CreatedAt, UpdatedAt) "
            "VALUES (?,?,?,?,?,?,?,?,?)",
            (*r, now, now),
        )
    con.commit()
    print("inserted", len(rows), "workflows:")
    for row in cur.execute("SELECT Id, Name, Enabled FROM Workflows").fetchall():
        print(" ", row)


if __name__ == "__main__":
    main()
