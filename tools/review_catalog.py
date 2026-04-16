#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import sys
from dataclasses import asdict, dataclass
from typing import Any


ALLOWED_FIELDS = {
    "id",
    "category",
    "displayName",
    "matchTokens",
    "notes",
    "repoUrl",
    "repoJsonUrl",
    "repoJsonUrls",
    "description",
    "green",
    "yellow",
    "red",
}
LEGACY_ENTRY_FIELDS = {"ruleType", "relatedIds"}

REQUIRED_FIELDS = {"id", "displayName", "matchTokens"}
LIST_FIELDS = {"matchTokens", "repoJsonUrls", "green", "yellow", "red"}
TEXT_FIELDS = {"id", "category", "displayName", "notes", "repoUrl", "repoJsonUrl", "description"}
FIELD_ALIASES = {
    "id": "id",
    "category": "category",
    "displayname": "displayName",
    "matchtokens": "matchTokens",
    "notes": "notes",
    "repourl": "repoUrl",
    "repojsonurl": "repoJsonUrl",
    "repojsonurls": "repoJsonUrls",
    "description": "description",
    "green": "green",
    "yellow": "yellow",
    "red": "red",
    "ruletype": "ruleType",
    "relatedids": "relatedIds",
}


@dataclass
class EntryReview:
    entry_id: str
    row_kind: str
    recommendation: str
    reasons: list[str]
    changed_fields: list[str]
    decision: str | None = None


def load_manifest(path: pathlib.Path) -> list[dict[str, Any]]:
    raw = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(raw, list):
        entries = raw
    elif isinstance(raw, dict):
        raw_lower = {str(key).lower(): value for key, value in raw.items()}
        if isinstance(raw_lower.get("entries"), list):
            entries = raw_lower["entries"]
        elif isinstance(raw_lower.get("plugins"), list):
            entries = raw_lower["plugins"]
        else:
            raise ValueError(f"{path} does not contain an 'entries' or 'plugins' array.")
    else:
        raise ValueError(f"{path} must be a JSON object or array.")

    if not all(isinstance(entry, dict) for entry in entries):
        raise ValueError(f"{path} contains non-object catalog rows.")

    return [normalize_entry_keys(entry) for entry in entries]


def normalize_entry_keys(entry: dict[str, Any]) -> dict[str, Any]:
    normalized: dict[str, Any] = {}
    for key, value in entry.items():
        raw_key = str(key).strip()
        canonical_key = FIELD_ALIASES.get(raw_key.lower(), raw_key)
        normalized[canonical_key] = value
    return normalized


def normalize_text(value: Any) -> str:
    return str(value or "").strip()


def normalize_id(value: Any) -> str:
    return normalize_text(value).lower()


def normalize_id_list(values: Any) -> list[str]:
    if not isinstance(values, list):
        return []

    normalized: list[str] = []
    seen: set[str] = set()
    for value in values:
        normalized_value = normalize_id(value)
        if not normalized_value or normalized_value in seen:
            continue
        normalized.append(normalized_value)
        seen.add(normalized_value)
    return normalized


def detect_rotation_ids(entries: list[dict[str, Any]]) -> set[str]:
    rotation_ids: set[str] = set()
    for entry in entries:
        rule_type = normalize_text(entry.get("ruleType")).lower()
        if rule_type in {"rotation_conflict", "bossmod_pair"}:
            entry_id = normalize_id(entry.get("id"))
            if entry_id:
                rotation_ids.add(entry_id)
    return rotation_ids


def apply_legacy_rule_conversion(
    entry: dict[str, Any],
    rotation_ids: set[str],
    normalized: dict[str, Any],
) -> None:
    if normalized["green"] or normalized["yellow"] or normalized["red"]:
        return

    related_ids = normalize_id_list(entry.get("relatedIds"))
    rule_type = normalize_text(entry.get("ruleType")).lower()
    entry_id = normalize_id(entry.get("id"))

    if rule_type == "autoduty_conflict":
        normalized["red"] = ["autoduty"]
    elif rule_type == "paired_conflict_yellow":
        normalized["yellow"] = related_ids
    elif rule_type in {"paired_conflict_red", "direct_conflict_red"}:
        normalized["red"] = related_ids
    elif rule_type == "rotation_conflict":
        normalized["yellow"] = sorted(rotation_ids - {entry_id})
    elif rule_type == "bossmod_pair":
        normalized["red"] = related_ids
        normalized["yellow"] = sorted(rotation_ids - {entry_id} - set(related_ids))
    elif rule_type == "loaded_warning_yellow":
        normalized["yellow"] = [entry_id] if entry_id else []


def normalize_entry(entry: dict[str, Any], rotation_ids: set[str] | None = None) -> dict[str, Any]:
    normalized: dict[str, Any] = {}
    for field in ALLOWED_FIELDS:
        value = entry.get(field)
        if field in LIST_FIELDS:
            if value is None:
                normalized[field] = []
            else:
                normalized[field] = [normalize_text(item) for item in value if normalize_text(item)]
        elif value is None:
            normalized[field] = ""
        else:
            normalized[field] = normalize_text(value)

    apply_legacy_rule_conversion(entry, rotation_ids or set(), normalized)
    return normalized


def validate_entry(entry: dict[str, Any]) -> list[str]:
    reasons: list[str] = []

    unknown_fields = sorted(set(entry.keys()) - (ALLOWED_FIELDS | LEGACY_ENTRY_FIELDS))
    if unknown_fields:
        reasons.append(f"Unknown fields: {', '.join(unknown_fields)}")

    for field in REQUIRED_FIELDS:
        if not str(entry.get(field, "")).strip():
            reasons.append(f"Missing required field: {field_label(field)}")

    for field in LIST_FIELDS:
        value = entry.get(field)
        if value is None:
            continue
        if not isinstance(value, list):
            reasons.append(f"Field '{field_label(field)}' must be an array")

    if entry.get("relatedIds") is not None and not isinstance(entry.get("relatedIds"), list):
        reasons.append("Field 'relatedIds' must be an array")

    for field in TEXT_FIELDS:
        value = entry.get(field)
        if value is None:
            continue
        if not isinstance(value, str):
            reasons.append(f"Field '{field_label(field)}' must be a string")

    if entry.get("ruleType") is not None and not isinstance(entry.get("ruleType"), str):
        reasons.append("Field 'ruleType' must be a string")

    return reasons


def build_id_map(entries: list[dict[str, Any]]) -> tuple[dict[str, dict[str, Any]], list[str]]:
    id_map: dict[str, dict[str, Any]] = {}
    duplicate_ids: list[str] = []
    rotation_ids = detect_rotation_ids(entries)
    for entry in entries:
        entry_id = normalize_id(entry.get("id"))
        if not entry_id:
            continue
        if entry_id in id_map:
            duplicate_ids.append(entry_id)
        else:
            id_map[entry_id] = normalize_entry(entry, rotation_ids)
    return id_map, sorted(set(duplicate_ids))


def compare_entries(master: dict[str, Any], submission: dict[str, Any]) -> list[str]:
    changed_fields: list[str] = []
    for field in sorted(ALLOWED_FIELDS):
        if submission[field] != master[field]:
            changed_fields.append(field_label(field))
    return changed_fields


def field_label(field: str) -> str:
    return field


def review_submission(master_entries: list[dict[str, Any]], submission_entries: list[dict[str, Any]]) -> list[EntryReview]:
    reviews: list[EntryReview] = []
    master_map, _ = build_id_map(master_entries)
    _, duplicate_ids = build_id_map(submission_entries)
    submission_rotation_ids = detect_rotation_ids(submission_entries)

    for entry in submission_entries:
        raw_id = str(entry.get("id", "")).strip()
        entry_id = raw_id.lower() or "<missing>"
        reasons = validate_entry(entry)
        changed_fields: list[str] = []

        if raw_id.lower() in duplicate_ids:
            reasons.append("Duplicate id in submission")

        normalized_entry = normalize_entry(entry, submission_rotation_ids)
        if not reasons:
            master_entry = master_map.get(entry_id)
            if master_entry is None:
                reviews.append(EntryReview(raw_id, "additive row", "APPROVE", ["Schema-clean additive row"], []))
                continue

            changed_fields = compare_entries(master_entry, normalized_entry)
            if not changed_fields:
                continue

            reviews.append(EntryReview(raw_id, "override row", "APPROVE", ["Schema-clean override to existing row"], changed_fields))
            continue

        reviews.append(EntryReview(raw_id, "invalid row", "DENY", reasons, changed_fields))

    return reviews


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run an interactive local Botology catalog review against an approved master catalog.",
    )
    parser.add_argument("master_catalog", type=pathlib.Path, help="Path to the approved master catalog JSON")
    parser.add_argument("submission", type=pathlib.Path, help="Path to the local overlay/submission JSON to review")
    parser.add_argument("--output", type=pathlib.Path, help="Optional path for a machine-readable review report JSON")
    return parser.parse_args()


def build_report(reviews: list[EntryReview], skipped_count: int, completed: bool) -> dict[str, Any]:
    approved_count = sum(review.decision == "APPROVE" for review in reviews)
    denied_count = sum(review.decision == "DENY" for review in reviews)
    unreviewed_count = sum(review.decision is None for review in reviews)
    recommendation_approve_count = sum(review.recommendation == "APPROVE" for review in reviews)
    recommendation_deny_count = sum(review.recommendation == "DENY" for review in reviews)
    return {
        "summary": {
            "entries_reviewed": len(reviews),
            "approved": approved_count,
            "denied": denied_count,
            "unreviewed": unreviewed_count,
            "recommendation_approve": recommendation_approve_count,
            "recommendation_deny": recommendation_deny_count,
            "skipped_identical": skipped_count,
            "completed": completed,
        },
        "reviews": [asdict(review) for review in reviews],
    }


def write_report(path: pathlib.Path | None, reviews: list[EntryReview], skipped_count: int, completed: bool) -> None:
    if path is None:
        return

    report = build_report(reviews, skipped_count, completed)
    path.write_text(json.dumps(report, indent=2), encoding="utf-8")


def print_review_header(reviews: list[EntryReview], skipped_count: int) -> None:
    print("BOTOLOGY LOCAL REVIEW")
    print("Nothing was uploaded automatically.")
    print("Review the changed local rows below and choose APPROVE or DENY for each one.")
    print("Suggested decision: APPROVE means the row is structurally clean.")
    print("Suggested decision: DENY means validation problems were detected.")
    print(f"Changed local rows queued for review: {len(reviews)}")
    print(f"SKIPPED identical rows: {skipped_count}")
    print("")


def print_review_entry(review: EntryReview, index: int, total: int) -> None:
    print("=" * 72)
    print(f"[{index}/{total}] {review.entry_id}")
    print(f"Row kind: {review.row_kind}")
    print(f"Suggested decision: {review.recommendation}")
    for reason in review.reasons:
        print(f"  - {reason}")
    if review.changed_fields:
        print(f"  - Changed fields: {', '.join(review.changed_fields)}")
    print("")


def prompt_for_decision() -> str | None:
    while True:
        response = input("Decision [a]pprove / approve [aa]ll / [d]eny / [q]uit: ").strip().lower()
        if response in {"a", "approve"}:
            return "APPROVE"
        if response in {"aa", "approveall", "approve_all", "all"}:
            return "APPROVE_ALL"
        if response in {"d", "deny"}:
            return "DENY"
        if response in {"q", "quit"}:
            return None
        print("Please enter 'a', 'aa', 'd', or 'q'.")


def print_decision_summary(reviews: list[EntryReview], skipped_count: int, completed: bool) -> None:
    approved_count = sum(review.decision == "APPROVE" for review in reviews)
    denied_count = sum(review.decision == "DENY" for review in reviews)
    unreviewed_count = sum(review.decision is None for review in reviews)
    print("")
    print("Decision summary")
    print(f"APPROVE: {approved_count}  DENY: {denied_count}  UNREVIEWED: {unreviewed_count}")
    print(f"SKIPPED identical rows: {skipped_count}")
    print("Review completed." if completed else "Review stopped early.")
    print("Next step: inspect plugin-repository-links.json and review-report.json manually.")
    print("The command window stays open so you can run additional Python commands here if you want.")


def main() -> int:
    args = parse_args()

    try:
        master_entries = load_manifest(args.master_catalog)
        submission_entries = load_manifest(args.submission)
    except Exception as exc:  # noqa: BLE001
        print(f"Failed to load inputs: {exc}", file=sys.stderr)
        return 1

    reviews = review_submission(master_entries, submission_entries)
    skipped_count = len(submission_entries) - len(reviews)
    print_review_header(reviews, skipped_count)

    if not reviews:
        write_report(args.output, reviews, skipped_count, completed=True)
        print("No changed local rows need interactive review.")
        print("The command window stays open so you can run additional Python commands here if you want.")
        return 0

    for index, review in enumerate(reviews, start=1):
        print_review_entry(review, index, len(reviews))
        try:
            decision = prompt_for_decision()
        except EOFError:
            decision = None

        if decision is None:
            write_report(args.output, reviews, skipped_count, completed=False)
            print_decision_summary(reviews, skipped_count, completed=False)
            return 1

        if decision == "APPROVE_ALL":
            review.decision = "APPROVE"
            for remaining_review in reviews[index:]:
                remaining_review.decision = "APPROVE"
            write_report(args.output, reviews, skipped_count, completed=True)
            print("")
            print(f"Approved the current row and all {len(reviews) - index} remaining rows.")
            print_decision_summary(reviews, skipped_count, completed=True)
            return 0

        review.decision = decision
        write_report(args.output, reviews, skipped_count, completed=False)
        print("")

    write_report(args.output, reviews, skipped_count, completed=True)
    print_decision_summary(reviews, skipped_count, completed=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
