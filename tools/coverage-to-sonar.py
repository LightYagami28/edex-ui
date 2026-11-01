#!/usr/bin/env python3
"""Merge Cobertura reports from several runners into one SonarQube generic coverage file.

Usage: coverage-to-sonar.py OUTPUT REPORT [REPORT ...]

Each runner measures only the code that runs on its own operating system, so a line counts as
covered when any report covers it. Paths are made relative to the repository ("src/Core/...").
"""
import sys
import xml.etree.ElementTree as ET


def relative_path(filename):
    path = filename.replace(chr(92), "/")
    return path[path.index("src/"):]


def merge(reports):
    lines = {}
    for report in reports:
        for cls in ET.parse(report).getroot().iter("class"):
            path = relative_path(cls.get("filename"))
            for line in cls.iter("line"):
                key = (path, int(line.get("number")))
                hits = int(line.get("hits")) > 0
                branches = 0
                covered = 0
                if line.get("branch") == "True":
                    condition = line.get("condition-coverage")
                    covered, branches = (int(x) for x in condition[condition.index("(") + 1:-1].split("/"))
                old = lines.get(key, (False, 0, 0))
                lines[key] = (old[0] or hits, max(old[1], branches), max(old[2], covered))
    return lines


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
