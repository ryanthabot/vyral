"""Audit all features for gaps in code_entities, data_operations, and db_tables."""
import sqlite3

conn = sqlite3.connect("tools/context-db/vyralbot_context.db")
conn.row_factory = sqlite3.Row

print("=" * 80)
print("FEATURE GAP AUDIT")
print("=" * 80)

features = conn.execute("SELECT id, name, display_name, category FROM features ORDER BY category, name").fetchall()

for f in features:
    fid = f["id"]
    files = conn.execute("SELECT COUNT(*) as c FROM files WHERE feature_id = ?", (fid,)).fetchone()["c"]
    entities = conn.execute("SELECT COUNT(*) as c FROM code_entities WHERE feature_id = ?", (fid,)).fetchone()["c"]
    ops = conn.execute("SELECT COUNT(*) as c FROM data_operations WHERE feature_id = ?", (fid,)).fetchone()["c"]
    tables = conn.execute("SELECT COUNT(*) as c FROM db_tables WHERE feature_id = ?", (fid,)).fetchone()["c"]
    di = conn.execute("SELECT COUNT(*) as c FROM di_registrations WHERE feature_id = ?", (fid,)).fetchone()["c"]
    events = conn.execute("SELECT COUNT(*) as c FROM events WHERE feature_id = ?", (fid,)).fetchone()["c"]
    nav = conn.execute("SELECT COUNT(*) as c FROM navigation_map WHERE feature_id = ?", (fid,)).fetchone()["c"]

    gaps = []
    if entities == 0: gaps.append("NO ENTITIES")
    if files > 0 and entities == 0: gaps.append("has files but no entities")
    if tables == 0 and ops == 0: gaps.append("no tables/ops")

    gap_str = f"  *** {', '.join(gaps)}" if gaps else ""
    print(f"\n[{f['category']:12s}] {f['name']:30s} files={files:3d} entities={entities:3d} ops={ops:3d} tables={tables:2d} di={di:2d} events={events:2d} nav={nav:1d}{gap_str}")

print("\n" + "=" * 80)
print("CODE ENTITIES AUDIT - checking which file_ids are NULL")
print("=" * 80)
null_file = conn.execute("SELECT COUNT(*) as c FROM code_entities WHERE file_id IS NULL").fetchone()["c"]
total_ent = conn.execute("SELECT COUNT(*) as c FROM code_entities").fetchone()["c"]
print(f"  Total entities: {total_ent}, with NULL file_id: {null_file}")

# Show entities with their file paths
print("\n  Entities grouped by feature:")
rows = conn.execute("""
    SELECT ce.feature_id, f.name as feat_name, COUNT(*) as cnt,
           SUM(CASE WHEN ce.file_id IS NULL THEN 1 ELSE 0 END) as null_files
    FROM code_entities ce
    LEFT JOIN features f ON ce.feature_id = f.id
    GROUP BY ce.feature_id
    ORDER BY f.name
""").fetchall()
for r in rows:
    flag = "  *** NULL file_ids" if r["null_files"] > 0 else ""
    print(f"    {r['feat_name'] or 'NONE':30s} entities={r['cnt']:3d} null_files={r['null_files']:3d}{flag}")

print("\n" + "=" * 80)
print("FILE PATH VERIFICATION - checking paths used in fill_gaps.py")
print("=" * 80)

# Check which paths DON'T exist
test_paths = [
    "src/VyralBot.Data/DapperDataService.cs",
    "src/VyralBot.Data/IDataService.cs",
    "src/VyralBot.Core/Events/IEventBus.cs",
    "src/VyralBot.Core/Events/EventBus.cs",
    "src/VyralBot.Core/Triggers/TriggerEngine.cs",
    "src/VyralBot.Core/Triggers/ITriggerEngine.cs",
    "src/VyralBot.Core/Actions/ActionExecutor.cs",
    "src/VyralBot.Core/Actions/IActionExecutor.cs",
    "src/VyralBot.Core/Chat/IChatService.cs",
    "src/VyralBot.Integrations/Kick/KickChatService.cs",
    "src/VyralBot.Integrations/Twitch/TwitchChatService.cs",
    "src/VyralBot.UI/ViewModels/MainViewModel.cs",
    "src/VyralBot.UI/AppHost.cs",
    "src/VyralBot.Core/Variables/IVariableService.cs",
    "src/VyralBot.Core/Variables/VariableService.cs",
    "src/VyralBot.Core/Currency/ICurrencyService.cs",
    "src/VyralBot.Core/Currency/CurrencyService.cs",
    "src/VyralBot.Core/Giveaways/IGiveawayService.cs",
    "src/VyralBot.Core/Giveaways/GiveawayService.cs",
    "src/VyralBot.Core/Polls/IPollService.cs",
    "src/VyralBot.Core/Polls/PollService.cs",
    "src/VyralBot.Core/Watchtime/IWatchtimeService.cs",
    "src/VyralBot.Core/Watchtime/WatchtimeService.cs",
    "src/VyralBot.Core/Commands/ICommandService.cs",
    "src/VyralBot.Core/Commands/CommandService.cs",
    "src/VyralBot.Api/WebServer.cs",
    "src/VyralBot.Integrations/Kick/KickWebhookService.cs",
]

for p in test_paths:
    row = conn.execute("SELECT id FROM files WHERE path = ?", (p,)).fetchone()
    status = f"id={row['id']}" if row else "NOT FOUND"
    if not row:
        # Try to find similar
        basename = p.split("/")[-1]
        similar = conn.execute("SELECT path FROM files WHERE path LIKE ?", (f"%{basename}%",)).fetchall()
        if similar:
            status += f" -> actual: {', '.join(s['path'] for s in similar)}"
    print(f"  {'OK' if row else 'MISS':4s} {p:60s} {status}")

print("\n" + "=" * 80)
print("ACTUAL FILE PATHS BY FEATURE (for features with 0 entities)")
print("=" * 80)
for f in features:
    fid = f["id"]
    entities = conn.execute("SELECT COUNT(*) as c FROM code_entities WHERE feature_id = ?", (fid,)).fetchone()["c"]
    if entities == 0:
        files = conn.execute("SELECT path, type FROM files WHERE feature_id = ? ORDER BY type, path", (fid,)).fetchall()
        if files:
            print(f"\n  {f['name']} ({len(files)} files):")
            for fl in files:
                print(f"    [{fl['type']:10s}] {fl['path']}")

conn.close()
