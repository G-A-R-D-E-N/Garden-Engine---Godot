#!/usr/bin/env python3
"""Validate the Papyrus runtime evidence registry.

Usage: validate_evidence_registry.py <registry.json>

The registry is the machine-readable evidence record task B0 asks for. This check keeps it
well formed and keeps the provenance boundary honest:

* every row carries the full field set, a known status and a known source category;
* an UNPROVEN row never claims a probe that ran, and a deliberate engine policy is labelled as
  one;
* a row that is not public states its provenance boundary;
* no field carries an absolute local path, so corpus locations and machine paths never leak
  into the repository.

It is documentation data, not runtime behavior, so the check is a schema and boundary check
only. It never decides whether a conclusion is true; that is what the evidence rows are for.
"""
import json
import re
import sys
from pathlib import Path

FAMILIES = {"pex", "seq", "quest", "vm", "registration", "save", "cache"}
PROBE_STATUSES = {"run", "unrun", "not_applicable"}
REQUIRES_UNCERTAINTY = {"UNPROVEN", "APPROXIMATED"}
REQUIRES_NO_CONSUMER = {"UNPROVEN", "CONTRADICTED"}
KEY_PATTERN = re.compile(r"^[a-z0-9_]+(\.[a-z0-9_]+)+$")
EVENT_KEY_PATTERN = re.compile(r"^event(\.[A-Za-z0-9_]+)+$")  # event.<name> keeps the Papyrus handler case
ABSOLUTE_PATH = re.compile(r"((?<![A-Za-z0-9])[A-Za-z]:[\\/])|(/home/)|(/Users/)")
MAX_CONCLUSION = 400

ROW_FIELDS = {
    "key",
    "family",
    "status",
    "source_category",
    "source_detail",
    "game_version",
    "tool_version",
    "public",
    "factual_conclusion",
    "allowed_consumers",
    "unresolved_uncertainty",
    "provenance_boundary",
    "probe_status",
    "evidence_links",
}


def text_values(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, list):
        for item in value:
            yield from text_values(item)
    elif isinstance(value, dict):
        for item in value.values():
            yield from text_values(item)


def main():
    if len(sys.argv) != 2:
        print("usage: validate_evidence_registry.py <registry.json>")
        return 2

    path = Path(sys.argv[1])
    registry = json.loads(path.read_text(encoding="utf-8"))
    problems = []

    if registry.get("schema") != "garden.papyrus.evidence-registry":
        problems.append("schema must be garden.papyrus.evidence-registry")
    if not isinstance(registry.get("version"), int):
        problems.append("version must be an integer")
    if not re.match(r"^\d{4}-\d{2}-\d{2}$", str(registry.get("updated", ""))):
        problems.append("updated must be an ISO date")

    statuses = registry.get("statuses")
    categories = registry.get("source_categories")
    if not isinstance(statuses, dict) or not statuses:
        problems.append("statuses must be a non-empty object")
        statuses = {}
    if not isinstance(categories, dict) or not categories:
        problems.append("source_categories must be a non-empty object")
        categories = {}

    rows = registry.get("rows")
    if not isinstance(rows, list) or not rows:
        problems.append("rows must be a non-empty array")
        rows = []

    keys = []
    families = set()
    for index, row in enumerate(rows):
        where = f"rows[{index}]"
        if not isinstance(row, dict):
            problems.append(f"{where}: must be an object")
            continue
        key = row.get("key", where)
        missing = ROW_FIELDS - set(row)
        if missing:
            problems.append(f"{key}: missing {sorted(missing)}")
        unknown = set(row) - ROW_FIELDS
        if unknown:
            problems.append(f"{key}: unknown fields {sorted(unknown)}")
        keys.append(key)

        if not (KEY_PATTERN.match(str(key)) or EVENT_KEY_PATTERN.match(str(key))):
            problems.append(f"{key}: key must be dotted lower-case (event.<name> may keep handler case)")
        family = row.get("family")
        if family not in FAMILIES:
            problems.append(f"{key}: unknown family {family!r}")
        else:
            families.add(family)
        status = row.get("status")
        if status not in statuses:
            problems.append(f"{key}: unknown status {status!r}")
        if row.get("source_category") not in categories:
            problems.append(f"{key}: unknown source category {row.get('source_category')!r}")

        conclusion = row.get("factual_conclusion")
        if not isinstance(conclusion, str) or not conclusion.strip():
            problems.append(f"{key}: factual_conclusion must be non-empty")
        elif len(conclusion) > MAX_CONCLUSION:
            problems.append(f"{key}: factual_conclusion exceeds {MAX_CONCLUSION} characters")

        uncertainty = row.get("unresolved_uncertainty")
        if status in REQUIRES_UNCERTAINTY and not uncertainty:
            problems.append(f"{key}: {status} rows must state their unresolved uncertainty")

        probe = row.get("probe_status")
        if probe not in PROBE_STATUSES:
            problems.append(f"{key}: unknown probe_status {probe!r}")
        elif status == "UNPROVEN" and probe == "run":
            problems.append(f"{key}: an UNPROVEN row must not claim a probe that ran")
        elif status == "ENGINE_POLICY" and probe != "not_applicable":
            problems.append(f"{key}: an ENGINE_POLICY row is not a probe result")

        consumers = row.get("allowed_consumers")
        if not isinstance(consumers, list):
            problems.append(f"{key}: allowed_consumers must be an array")
        elif not consumers and status not in REQUIRES_NO_CONSUMER:
            problems.append(f"{key}: a {status} row must name at least one allowed consumer")

        if row.get("public") is False and not row.get("provenance_boundary"):
            problems.append(f"{key}: a non-public row must state its provenance boundary")
        if row.get("public") not in (True, False):
            problems.append(f"{key}: public must be true or false")

        links = row.get("evidence_links")
        if not isinstance(links, list) or not links:
            problems.append(f"{key}: evidence_links must be non-empty")

        for value in text_values(row):
            if ABSOLUTE_PATH.search(value):
                problems.append(f"{key}: absolute local path in {value!r}")

    if len(keys) != len(set(keys)):
        duplicates = sorted({key for key in keys if keys.count(key) > 1})
        problems.append(f"duplicate keys: {duplicates}")

    for problem in problems:
        print(f"error: {problem}")
    print(f"rows={len(rows)} families={sorted(families)} problems={len(problems)}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
