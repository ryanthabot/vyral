"""Check data_operations and map to features based on table names."""
import sqlite3
from pathlib import Path

DB = Path(__file__).parent / "vyralbot_context.db"
conn = sqlite3.connect(str(DB))

rows = conn.execute("""
    SELECT do.id, do.method_name, do.operation, do.feature_id,
           dt.name as table_name
    FROM data_operations do
    LEFT JOIN db_tables dt ON do.table_id = dt.id
    ORDER BY dt.name, do.method_name
""").fetchall()

print(f"Total data_operations: {len(rows)}\n")
for r in rows:
    feat = r[3] if r[3] else "NULL"
    print(f"  [{r[4] or 'NO-TABLE':25s}] {r[2]:8s} {r[1]:50s} feat={feat}")

# Also show DB tables and their assigned features
print("\n\nDB Tables -> Feature mapping:")
tables = conn.execute("""
    SELECT dt.name, dt.feature_id, f.name as feature_name
    FROM db_tables dt
    LEFT JOIN features f ON dt.feature_id = f.id
    ORDER BY dt.name
""").fetchall()
for t in tables:
    print(f"  {t[0]:30s} -> feature: {t[2] or 'NULL'} (id={t[1]})")

conn.close()
