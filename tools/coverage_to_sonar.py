#!/usr/bin/env python3
"""Merge Cobertura reports from several runners into one Sonar report."""
import sys
from html import escape
from html.parser import HTMLParser
from pathlib import Path

USAGE = """Usage: coverage_to_sonar.py OUTPUT REPORT [REPORT ...]

Each runner measures only the code that runs on its own operating system,
so a line (and each branch condition of a line) counts as covered when any
report covers it. Paths are made relative to the repository ("src/Core/...").
"""


def inside_cwd(name):
    """Return name as a path inside the current directory or raise."""
    return Path(name).resolve().relative_to(Path.cwd().resolve())


def relative_path(filename):
    """Return the repository relative path of a Cobertura file name."""
    path = filename.replace(chr(92), "/")
    return path[path.index("src/"):]


def condition_counts(condition):
    """Return (covered, total) from a value such as "50% (1/2)"."""
    counts = condition[condition.index("(") + 1:-1]
    covered, total = (int(x) for x in counts.split("/"))
    return covered, total


class CoberturaReader(HTMLParser):
    """Collect the lines of a Cobertura report with their conditions."""

    def __init__(self):
        """Start with no file and no lines."""
        super().__init__()
        self.path = ""
        self.lines = []

    def handle_starttag(self, tag, attrs):
        """Track the current file, line and condition elements."""
        values = dict(attrs)
        if tag == "class":
            self.path = relative_path(values.get("filename") or "")
        elif tag == "line":
            self.lines.append((self.path, values, []))
        elif tag == "condition":
            self.add_condition(values)

    def handle_startendtag(self, tag, attrs):
        """Treat self closing elements like opening ones."""
        self.handle_starttag(tag, attrs)

    def add_condition(self, values):
        """Attach a condition to the last line read."""
        if self.lines:
            self.lines[-1][2].append(values)


def read(report):
    """Return the lines found in one Cobertura report."""
    reader = CoberturaReader()
    reader.feed(Path(report).read_text(encoding="utf-8"))
    reader.close()
    return reader.lines


def covered_conditions(conditions):
    """Return the numbers of the conditions covered at least once."""
    return {int(c.get("number") or "0") for c in conditions
            if c.get("coverage") != "0%"}


def branch_details(values, conditions):
    """Return (total, covered condition numbers, covered count)."""
    if values.get("branch") != "True":
        return 0, set(), 0
    if conditions:
        return len(conditions), covered_conditions(conditions), 0
    coverage = values.get("condition-coverage") or "(0/0)"
    covered, total = condition_counts(coverage)
    return total, set(), covered


def combine(old, hits, details):
    """Unite the coverage of the same line seen in two reports."""
    total, indices, covered = details
    return (old[0] or hits, max(old[1], total), old[2] | indices,
            max(old[3], covered))


def add_line(lines, path, values, conditions):
    """Add one line of a report to the merged lines."""
    key = (path, int(values.get("number") or "0"))
    hits = int(values.get("hits") or "0") > 0
    old = lines.get(key, (False, 0, set(), 0))
    lines[key] = combine(old, hits, branch_details(values, conditions))


def merge(reports):
    """Merge reports into {(path, line): (hit, branches, covered branches)}."""
    lines = {}
    for report in reports:
        for path, values, conditions in read(report):
            add_line(lines, path, values, conditions)
    return {key: (hit, total, max(len(indices), covered))
            for key, (hit, total, indices, covered) in lines.items()}


def line_element(number, hit, branches, covered):
    """Return the lineToCover element of one line."""
    attributes = f'lineNumber="{number}" covered="{str(hit).lower()}"'
    if branches:
        attributes += (f' branchesToCover="{branches}"'
                       f' coveredBranches="{covered}"')
    return f"    <lineToCover {attributes}/>"


def write(lines, output):
    """Write the merged lines as a SonarQube generic coverage report."""
    rows = ["<?xml version='1.0' encoding='utf-8'?>", '<coverage version="1">']
    current = None
    for (path, number), (hit, branches, covered) in sorted(lines.items()):
        if path != current:
            if current is not None:
                rows.append("  </file>")
            rows.append(f'  <file path="{escape(path, quote=True)}">')
            current = path
        rows.append(line_element(number, hit, branches, covered))
    if current is not None:
        rows.append("  </file>")
    rows.append("</coverage>")
    Path(output).write_text("\n".join(rows) + "\n", encoding="utf-8")


def main(arguments):
    """Merge the reports named on the command line."""
    if len(arguments) < 3:
        print(USAGE)
        return 2
    output = inside_cwd(arguments[1])
    lines = merge([inside_cwd(report) for report in arguments[2:]])
    write(lines, output)
    hit = sum(1 for value in lines.values() if value[0])
    print(f"{hit}/{len(lines)} lines covered")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
