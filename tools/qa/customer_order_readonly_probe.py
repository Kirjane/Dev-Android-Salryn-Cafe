#!/usr/bin/env python3
"""Read-only evidence for one installed Customer Order. Never repairs source data.

Usage: py -3 tools/qa/customer_order_readonly_probe.py --db <path-to-existing-salryn.db>
Requires Python 3 stdlib only. Outputs no customer name, phone or private user data.
"""
import argparse
import json
import sqlite3
from pathlib import Path


def inspect(database: Path, order_number: str) -> dict:
    if not database.is_file():
        raise FileNotFoundError(f"Database file does not exist: {database}")
    # SQLite URI mode=ro and query_only prohibit modifying the installed file.
    conn = sqlite3.connect(database.resolve().as_uri() + "?mode=ro", uri=True, timeout=5)
    try:
        conn.execute("PRAGMA query_only = ON")
        conn.execute("BEGIN")  # single consistent read transaction
        parent = conn.execute(
            "SELECT id, status, total_cents FROM customer_orders WHERE order_number = ?",
            (order_number,),
        ).fetchone()
        if parent is None:
            return {"order_number": order_number, "found": False}
        order_id, status, total_cents = parent
        lines = conn.execute(
            """SELECT product_id, quantity_x1000, unit_price_cents, line_total_cents
               FROM customer_order_lines WHERE order_id = ? ORDER BY rowid""",
            (order_id,),
        ).fetchall()
        audit_count = conn.execute(
            """SELECT COUNT(*) FROM audit_logs
               WHERE entity_type = 'customer_orders' AND entity_id = ?
                 AND action = 'CUSTOMER_ORDER_CREATED'""",
            (order_id,),
        ).fetchone()[0]
        line_total = sum(row[3] for row in lines)
        line_checks = [
            bool(row[0]) and row[1] > 0 and row[2] >= 0 and row[3] >= 0
            and (row[1] * row[2] + 500) // 1000 == row[3]
            for row in lines
        ]
        integrity = (
            "MISSING_PERSISTED_LINES" if len(lines) == 0
            else "LINE_AMOUNT_MISMATCH" if not all(line_checks) or line_total != total_cents
            else "RECONCILED"
        )
        return {
            "order_number": order_number,
            "found": True,
            "status": status,
            "stored_total_cents": total_cents,
            "persisted_line_count": len(lines),
            "sum_persisted_line_cents": line_total,
            "creation_audit_count": audit_count,
            "lines": [
                {
                    "quantity_x1000": q,
                    "unit_price_cents": unit,
                    "line_total_cents": total,
                }
                for _, q, unit, total in lines
            ],
            "integrity": integrity,
        }
    finally:
        conn.close()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", type=Path, required=True, help="Existing QA1 SQLite database path")
    parser.add_argument("--order-number", default="CO-202611-0001")
    args = parser.parse_args()
    print(json.dumps(inspect(args.db, args.order_number), indent=2))


if __name__ == "__main__":
    main()
