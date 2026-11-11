#!/usr/bin/env python3
"""Merge Cobertura reports from several runners into one SonarQube generic coverage file.

Usage: coverage-to-sonar.py OUTPUT REPORT [REPORT ...]

Each runner measures only the code that runs on its own operating system, so a line (and each
branch condition of a line) counts as covered when any report covers it. Paths are made relative to the repository ("src/Core/...").
"""
import sys
import xml.etree.ElementTree as ET


def relative_path(filename):
    path = filename.replace(chr(92), "/")
    return path[path.index("src/"):]


def branch_details(line):
    """Return (total, indices of covered conditions, covered count when no per-condition detail)."""
    nodes = line.findall("conditions/condition")
    if nodes:
        return len(nodes), {int(n.get("number")) for n in nodes if n.get("coverage") != "0%"}, 0
    condition = line.get("condition-coverage")
    covered, total = (int(x) for x in condition[condition.index("(") + 1:-1].split("/"))
    return total, set(), covered


def merge(reports):
    lines = {}
    for report in reports:
        for cls in ET.parse(report).getroot().iter("class"):
            path = relative_path(cls.get("filename"))
            for line in cls.iter("line"):
                key = (path, int(line.get("number")))
                hits = int(line.get("hits")) > 0
                total, indices, covered = (0, set(), 0)
                if line.get("branch") == "True":
                    total, indices, covered = branch_details(line)
                old = lines.get(key, (False, 0, set(), 0))
                # Conditions are united by number: the OS-specific branch taken on one runner
                # is not the one taken on another, so counts alone would lose coverage.
                lines[key] = (old[0] or hits, max(old[1], total), old[2] | indices, max(old[3], covered))
    return {key: (hit, total, max(len(indices), covered)) for key, (hit, total, indices, covered) in lines.items()}


def write(lines, output):
    root = ET.Element("coverage", version="1")
    files = {}
    for (path, number), (hit, branches, covered) in sorted(lines.items()):
        node = files.setdefault(path, ET.SubElement(root, "file", path=path))
        attributes = {"lineNumber": str(number), "covered": str(hit).lower()}
        if branches:
            attributes["branchesToCover"] = str(branches)
            attributes["coveredBranches"] = str(covered)
        ET.SubElement(node, "lineToCover", attributes)
    ET.ElementTree(root).write(output, encoding="utf-8", xml_declaration=True)


def main(arguments):
    if len(arguments) < 3:
        print(__doc__)
        return 2
    lines = merge(arguments[2:])
    write(lines, arguments[1])
    total = len(lines)
    hit = sum(1 for value in lines.values() if value[0])
    print(f"{hit}/{total} lines covered")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
