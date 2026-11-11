"""Test degli script di tools/: unione della copertura per Sonar e maschera delle terre."""
import contextlib
import importlib.util
import io
import json
import os
import runpy
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest import mock

TOOLS = Path(__file__).resolve().parents[2] / "tools"


def load_coverage_tool():
    spec = importlib.util.spec_from_file_location("coverage_to_sonar", TOOLS / "coverage-to-sonar.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def cobertura(path, filename, lines):
    body = "".join(lines)
    Path(path).write_text(
        f'<coverage><packages><package><classes><class filename="{filename}"><lines>{body}</lines>'
        "</class></classes></package></packages></coverage>",
        encoding="utf-8",
    )


class CoverageToSonarTests(unittest.TestCase):
    def setUp(self):
        self.tool = load_coverage_tool()
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.root = Path(self.dir.name)

    def test_relative_path_handles_windows_separators(self):
        self.assertEqual(self.tool.relative_path(r"D:\a\repo\src\Core\X.cs"), "src/Core/X.cs")
        self.assertEqual(self.tool.relative_path("/home/r/repo/src/Core/X.cs"), "src/Core/X.cs")

    def test_merge_covers_a_line_when_any_report_covers_it(self):
        first, second = self.root / "a.xml", self.root / "b.xml"
        cobertura(first, "/r/src/A.cs", [
            '<line number="1" hits="0"/>',
            '<line number="2" hits="1" branch="True" condition-coverage="50% (1/2)"/>',
        ])
        cobertura(second, r"C:\r\src\A.cs", [
            '<line number="1" hits="3"/>',
            '<line number="2" hits="0" branch="True" condition-coverage="100% (2/2)"/>',
        ])
        merged = self.tool.merge([first, second])
        self.assertEqual(merged[("src/A.cs", 1)], (True, 0, 0))
        self.assertEqual(merged[("src/A.cs", 2)], (True, 2, 2))

    def test_write_emits_sonar_generic_coverage(self):
        output = self.root / "out.xml"
        self.tool.write({("src/A.cs", 1): (True, 0, 0), ("src/A.cs", 2): (False, 2, 1)}, output)
        nodes = ET.parse(output).getroot().find("file").findall("lineToCover")
        self.assertEqual([n.attrib for n in nodes], [
            {"lineNumber": "1", "covered": "true"},
            {"lineNumber": "2", "covered": "false", "branchesToCover": "2", "coveredBranches": "1"},
        ])

    def test_main_requires_output_and_reports(self):
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            self.assertEqual(self.tool.main(["tool", "only-output.xml"]), 2)
        self.assertIn("Usage:", stdout.getvalue())

    def test_script_exit_code_comes_from_main(self):
        report, output = self.root / "r.xml", self.root / "out.xml"
        cobertura(report, "/r/src/A.cs", ['<line number="1" hits="1"/>'])
        argv = ["coverage-to-sonar.py", str(output), str(report)]
        with mock.patch.object(sys, "argv", argv), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as raised:
                runpy.run_path(str(TOOLS / "coverage-to-sonar.py"), run_name="__main__")
        self.assertEqual(raised.exception.code, 0)

    def test_main_merges_and_prints_summary(self):
        report, output = self.root / "r.xml", self.root / "out.xml"
        cobertura(report, "/r/src/A.cs", ['<line number="1" hits="1"/>', '<line number="2" hits="0"/>'])
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            self.assertEqual(self.tool.main(["tool", str(output), str(report)]), 0)
        self.assertEqual(stdout.getvalue().strip(), "1/2 lines covered")
        self.assertTrue(output.exists())


class LandMaskTests(unittest.TestCase):
    def test_rasterizes_polygon_and_multipolygon_features(self):
        square = [[[-10, -10], [10, -10], [10, 10], [-10, 10], [-10, -10]]]
        far = [[[100, 40], [120, 40], [120, 60], [100, 60], [100, 40]]]
        geojson = {"features": [
            {"geometry": {"type": "Polygon", "coordinates": square}},
            {"geometry": {"type": "MultiPolygon", "coordinates": [far]}},
        ]}
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory).resolve()
            (work / "land.geojson").write_text(json.dumps(geojson), encoding="utf-8")
            previous = Path.cwd()
            os.chdir(work)
            try:
                with mock.patch.object(sys, "argv", ["gen-landmask.py", "land.geojson", "mask.bin"]):
                    runpy.run_path(str(TOOLS / "gen-landmask.py"), run_name="__main__")
            finally:
                os.chdir(previous)
            mask = (work / "mask.bin").read_bytes()

        def is_land(lat_row, lon_col):
            index = lat_row * 360 + lon_col
            return bool(mask[index >> 3] & (1 << (index & 7)))

        self.assertEqual(len(mask), 360 * 180 // 8)
        self.assertTrue(is_land(90, 180))    # 0.5S, 0.5E dentro il quadrato
        self.assertTrue(is_land(40, 290))    # 49.5N, 109.5E dentro il secondo poligono
        self.assertFalse(is_land(10, 10))    # oceano

    def test_rejects_paths_outside_the_working_directory(self):
        with tempfile.TemporaryDirectory() as directory, tempfile.TemporaryDirectory() as outside:
            previous = Path.cwd()
            os.chdir(directory)
            try:
                argv = ["gen-landmask.py", str(Path(outside) / "land.geojson"), "mask.bin"]
                with mock.patch.object(sys, "argv", argv), self.assertRaises(ValueError):
                    runpy.run_path(str(TOOLS / "gen-landmask.py"), run_name="__main__")
            finally:
                os.chdir(previous)

