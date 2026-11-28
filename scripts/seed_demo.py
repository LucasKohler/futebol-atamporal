"""Idempotently enrich the local SQLite demo database; never changes Pix tables."""
from __future__ import annotations

import json
import sqlite3
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

DB = Path(__file__).resolve().parents[1] / "apps/api/src/FutebolAtemporal.Api/futebol-atemporal.dev.db"
BACKUP = DB.with_name("futebol-atemporal.dev.pre-demo-seed.db")
ADMIN = "22222222-2222-2222-2222-222222222222"

def gid() -> str:
    return str(uuid.uuid4()).upper()

def remap(value, mapping):
    if isinstance(value, dict):
        return {k: remap(v, mapping) for k, v in value.items()}
    if isinstance(value, list):
        return [remap(v, mapping) for v in value]
    return mapping.get(value.upper(), value) if isinstance(value, str) else value

def insert_copy(db, table, row, changes):
    data = dict(row)
    data.update(changes)
    cols = list(data)
    db.execute(f"INSERT INTO {table} ({','.join(cols)}) VALUES ({','.join('?' for _ in cols)})", [data[c] for c in cols])

def main():
    if not DB.exists():
        raise SystemExit(f"Banco local não encontrado: {DB}")
    if not BACKUP.exists():
        src = sqlite3.connect(DB)
        dst = sqlite3.connect(BACKUP)
        src.backup(dst)
        dst.close(); src.close()

    db = sqlite3.connect(DB)
    db.row_factory = sqlite3.Row
    before_pix = [tuple(r) for r in db.execute("SELECT * FROM PixCharges ORDER BY Id")]
    profile_ids = {r["Id"].lower(): r["Id"] for r in db.execute("SELECT Id FROM PlayerProfiles")}
    db.execute("PRAGMA foreign_keys=ON")
    db.execute("BEGIN IMMEDIATE")
    try:
        completed = db.execute("SELECT * FROM Matches WHERE Status='Completed' ORDER BY StartsAt").fetchall()
        if len(completed) < 2:
            raise RuntimeError("São necessárias duas partidas concluídas existentes para gerar a temporada de demonstração.")

        # Complete historical sample: retain the two existing rounds and add eight Saturday rounds.
        saturday_dates = ["2026-09-19", "2026-09-26", "2026-10-03", "2026-10-10",
                          "2026-10-17", "2026-10-24", "2026-10-31", "2026-11-07"]
        originals = {r["Title"]: r for r in completed}
        for i, day in enumerate(saturday_dates, start=3):
            title = f"Pelada #{i} - Sábado (Rodada Encerrada)"
            if db.execute("SELECT 1 FROM Matches WHERE Title=?", (title,)).fetchone():
                continue
            source = completed[(i - 3) % len(completed)]
            mid = gid()
            insert_copy(db, "Matches", source, {
                "Id": mid, "Title": title, "StartsAt": f"{day} 19:00:00-03:00",
                "CreatedAt": f"{day}T12:00:00.0000000+00:00", "UpdatedAt": None,
                "Version": 1, "Notes": "Rodada fictícia para demonstração local.",
            })

            team_rows = db.execute("""SELECT t.* FROM MatchTeams t JOIN TeamDraws d ON d.Id=t.TeamDrawId
                                      WHERE d.MatchId=? ORDER BY t.SortOrder""", (source["Id"],)).fetchall()
            draw = db.execute("SELECT * FROM TeamDraws WHERE MatchId=? AND Status='Confirmed' ORDER BY CreatedAt DESC LIMIT 1", (source["Id"],)).fetchone()
            if not draw:
                raise RuntimeError(f"Partida-fonte sem sorteio confirmado: {source['Id']}")
            did = gid(); team_map = {}; member_map = {}
            insert_copy(db, "TeamDraws", draw, {"Id": did, "MatchId": mid, "Seed": f"demo-{day}", "CreatedAt": f"{day}T12:00:00.0000000+00:00", "ConfirmedAt": f"{day}T12:05:00.0000000+00:00"})
            new_teams = []
            for t in team_rows:
                tid = gid(); team_map[t["Id"].upper()] = tid
                insert_copy(db, "MatchTeams", t, {"Id": tid, "TeamDrawId": did})
                new_teams.append((t, tid))
            for old_team, new_team in new_teams:
                members = db.execute("SELECT * FROM MatchTeamMembers WHERE MatchTeamId=?", (old_team["Id"],)).fetchall()
                for member in members:
                    insert_copy(db, "MatchTeamMembers", member, {"Id": gid(), "MatchTeamId": new_team})

            for reg in db.execute("SELECT * FROM MatchRegistrations WHERE MatchId=?", (source["Id"],)).fetchall():
                insert_copy(db, "MatchRegistrations", reg, {"Id": gid(), "MatchId": mid, "CreatedAt": f"{day}T12:00:00.0000000+00:00", "UpdatedAt": f"{day}T12:05:00.0000000+00:00"})

            result = db.execute("SELECT * FROM MatchResults WHERE MatchId=?", (source["Id"],)).fetchone()
            if result:
                insert_copy(db, "MatchResults", result, {"Id": gid(), "MatchId": mid, "WinnerTeamId": team_map.get((result["WinnerTeamId"] or "").upper(), result["WinnerTeamId"]), "ClosedAt": f"{day}T21:00:00.0000000+00:00"})
            revision = db.execute("SELECT * FROM MatchResultRevisions WHERE MatchId=? AND IsCurrent=1", (source["Id"],)).fetchone()
            if not revision:
                raise RuntimeError(f"Partida-fonte sem súmula encerrada: {source['Id']}")
            sheet = remap(json.loads(revision["SheetJson"]), team_map)
            insert_copy(db, "MatchResultRevisions", revision, {"Id": gid(), "MatchId": mid, "Revision": 1, "IsCurrent": 1, "SheetJson": json.dumps(sheet, ensure_ascii=False, separators=(",", ":")), "Reason": None, "CreatedAt": f"{day}T21:00:00.0000000+00:00"})

            live = db.execute("SELECT * FROM MatchLiveSheets WHERE MatchId=?", (source["Id"],)).fetchone()
            if live:
                insert_copy(db, "MatchLiveSheets", live, {"Id": gid(), "MatchId": mid, "SheetJson": json.dumps(remap(json.loads(live["SheetJson"]), team_map), ensure_ascii=False, separators=(",", ":")), "Version": 1, "CreatedAt": f"{day}T12:00:00.0000000+00:00", "UpdatedAt": f"{day}T21:00:00.0000000+00:00"})
            for award in db.execute("SELECT * FROM PlayerAwards WHERE MatchId=?", (source["Id"],)).fetchall():
                insert_copy(db, "PlayerAwards", award, {"Id": gid(), "MatchId": mid})

        # Seed legacy per-player evaluations for all ten completed matches.
        completed = db.execute("SELECT Id,StartsAt FROM Matches WHERE Status='Completed' ORDER BY StartsAt").fetchall()
        for match in completed:
            rev = db.execute("SELECT SheetJson FROM MatchResultRevisions WHERE MatchId=? AND IsCurrent=1", (match["Id"],)).fetchone()
            if not rev:
                continue
            sheet = json.loads(rev["SheetJson"])
            day = match["StartsAt"][:10]
            for p in sheet["participants"]:
                if not p.get("present", True):
                    continue
                profile_id = profile_ids.get(p["playerProfileId"].lower())
                if not profile_id or db.execute("SELECT 1 FROM PlayerMatchRatings WHERE MatchId=? AND PlayerProfileId=?", (match["Id"], profile_id)).fetchone():
                    continue
                # Stable, varied demo scores; never claims these are real player assessments.
                score = 6.0 + ((int(p["playerProfileId"].replace("-", "")[:4], 16) + int(day[-2:])) % 41) / 10
                db.execute("INSERT INTO PlayerMatchRatings (Id,MatchId,PlayerProfileId,Score,RatedByUserId,Notes,CreatedAt) VALUES (?,?,?,?,?,?,?)",
                           (gid(), match["Id"], profile_id, round(score, 1), ADMIN, "Avaliação fictícia de demonstração.", f"{day}T21:00:00.0000000+00:00"))

        # Include cash/manual match-fee obligations for casual players on the next open match.
        future = db.execute("SELECT * FROM Matches WHERE Status='RegistrationOpen' ORDER BY StartsAt LIMIT 1").fetchone()
        if future:
            members = {r[0] for r in db.execute("SELECT PlayerProfileId FROM MonthlyMemberships WHERE GroupId=? AND Status='Active'", (future["GroupId"],))}
            confirmed = db.execute("SELECT PlayerProfileId FROM MatchRegistrations WHERE MatchId=? AND Status='Confirmed'", (future["Id"],)).fetchall()
            for (pid,) in confirmed:
                if pid not in members and not db.execute("SELECT 1 FROM MatchFeePayments WHERE MatchId=? AND PlayerProfileId=?", (future["Id"], pid)).fetchone():
                    db.execute("INSERT INTO MatchFeePayments (Id,GroupId,MatchId,PlayerProfileId,Amount,Status,PaymentSource,PaidAt,RecordedByUserId,Notes,CreatedAt,UpdatedAt) VALUES (?,?,?,?,?,'Pending','None',NULL,NULL,'Diária fictícia para demonstração.',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)", (gid(), future["GroupId"], future["Id"], pid, 40.0))

        db.commit()
        after_pix = [tuple(r) for r in db.execute("SELECT * FROM PixCharges ORDER BY Id")]
        if before_pix != after_pix:
            raise RuntimeError("A tabela Pix foi alterada; restaure o backup antes de usar esta base.")
    except Exception:
        db.rollback()
        raise
    finally:
        db.close()

    # Read-only coverage report after the committed seed.
    check = sqlite3.connect(DB); check.row_factory = sqlite3.Row
    integrity = check.execute("PRAGMA integrity_check").fetchone()[0]
    fk_errors = check.execute("PRAGMA foreign_key_check").fetchall()
    match_count = check.execute("SELECT COUNT(*) FROM Matches").fetchone()[0]
    history_count = check.execute("SELECT COUNT(*) FROM Matches WHERE Status='Completed'").fetchone()[0]
    evals = check.execute("SELECT COUNT(*) FROM PlayerMatchRatings").fetchone()[0]
    penalties = 0
    goals = 0
    for (raw,) in check.execute("SELECT SheetJson FROM MatchResultRevisions WHERE IsCurrent=1"):
        sheet=json.loads(raw); goals += len(sheet.get("goals", []))
        penalties += sum(1 for g in sheet.get("games", []) if g.get("penaltyWinnerTeamId"))
    print(json.dumps({"database": str(DB), "backup": str(BACKUP), "matches": match_count, "completed_history": history_count,
                      "evaluations": evals, "penalty_decisions_in_current_sheets": penalties, "recorded_goals": goals,
                      "match_fees": check.execute("SELECT COUNT(*) FROM MatchFeePayments").fetchone()[0],
                      "pix_charges_unchanged": len(before_pix), "push_campaigns": check.execute("SELECT COUNT(*) FROM NotificationCampaigns").fetchone()[0],
                      "in_app_notifications": check.execute("SELECT COUNT(*) FROM UserNotifications").fetchone()[0],
                      "integrity_check": integrity, "foreign_key_errors": len(fk_errors)}, ensure_ascii=False, indent=2))
    check.close()

if __name__ == "__main__":
    main()
