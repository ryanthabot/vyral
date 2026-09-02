"""
Fix data_operations feature_id — map each table to its owning feature.
Also fix db_tables feature_id where needed.
"""
import sqlite3
from pathlib import Path

DB = Path(__file__).parent / "vyralbot_context.db"

# Table name -> feature name mapping
TABLE_TO_FEATURE = {
    "Actions": "actions",
    "AutoReplyRules": "auto-reply",
    "ChatEventSettings": "auto-reply",  # chat event settings are auto-reply config
    "ChatMessages": "chat",
    "CommandCooldowns": "commands",
    "Commands": "commands",
    "Configs": "settings",
    "CurrencyRedemptions": "currency",
    "CurrencyRewards": "currency",
    "CurrencySettings": "currency",
    "CurrencyTransactions": "currency",
    "EventLogs": "event-history",
    "EventStatistics": "statistics",
    "GiveawayEntries": "giveaway",
    "Giveaways": "giveaway",
    "Platforms": "platforms-hub",
    "PollVotes": "poll",
    "Polls": "poll",
    "Profiles": "profiles",
    "Triggers": "triggers",
    "UserCurrency": "currency",
    "UserWatchtime": "watchtime",
    "Variables": "variables",
    "WebServerSettings": "webserver",
    "WebhookSubscriptions": "webserver",
}


def main():
    conn = sqlite3.connect(str(DB))

    # Build feature name -> id lookup
    features = {}
    for r in conn.execute("SELECT id, name FROM features").fetchall():
        features[r[1]] = r[0]

    # Build table name -> id lookup
    tables = {}
    for r in conn.execute("SELECT id, name FROM db_tables").fetchall():
        tables[r[1]] = r[0]

    # 1. Fix db_tables feature_id
    print("Fixing db_tables feature assignments...")
    for table_name, feat_name in TABLE_TO_FEATURE.items():
        feat_id = features.get(feat_name)
        if feat_id and table_name in tables:
            conn.execute("UPDATE db_tables SET feature_id = ? WHERE name = ?", (feat_id, table_name))
            print(f"  {table_name:30s} -> {feat_name} (id={feat_id})")
        else:
            print(f"  WARNING: table={table_name} or feature={feat_name} not found")

    # 2. Fix data_operations feature_id based on their table_id
    print("\nFixing data_operations feature assignments...")
    updated = 0
    ops = conn.execute("SELECT id, table_id FROM data_operations").fetchall()
    for op_id, table_id in ops:
        # Find the table name
        t = conn.execute("SELECT name FROM db_tables WHERE id = ?", (table_id,)).fetchone()
        if t:
            table_name = t[0]
            feat_name = TABLE_TO_FEATURE.get(table_name)
            if feat_name:
                feat_id = features.get(feat_name)
                if feat_id:
                    conn.execute("UPDATE data_operations SET feature_id = ? WHERE id = ?", (feat_id, op_id))
                    updated += 1

    conn.commit()
    print(f"  Updated {updated}/{len(ops)} data_operations\n")

    # 3. Verify
    print("Verification — data_operations per feature:")
    rows = conn.execute("""
        SELECT f.name, COUNT(do.id) as op_count
        FROM data_operations do
        JOIN features f ON do.feature_id = f.id
        GROUP BY f.name
        ORDER BY f.name
    """).fetchall()
    for r in rows:
        print(f"  {r[0]:30s} {r[1]:3d} ops")

    null_ops = conn.execute("SELECT COUNT(*) FROM data_operations WHERE feature_id IS NULL").fetchone()[0]
    if null_ops > 0:
        print(f"\n  WARNING: {null_ops} ops still have NULL feature_id")
    else:
        print(f"\n  All data_operations have a feature assigned!")

    conn.close()


if __name__ == "__main__":
    main()
