"""Language-aware static analysis dispatcher.
Parses JavaScript/TypeScript, Python, and C# to compute code quality metrics:
- loc (lines of code, non-comment, non-empty)
- cyclomatic (McCabe cyclomatic complexity)
- maintainability (Maintainability Index, 0-100)
- comment_ratio (comment lines / total lines)
- max_function_length (longest function LOC)
"""
from __future__ import annotations
import math
import os
import re
import subprocess
from abc import ABC, abstractmethod
from pathlib import Path


class StaticAnalyzer(ABC):
    @abstractmethod
    def analyze(self, code: str, file_path: Path | None = None) -> dict[str, float]:
        pass


def estimate_mi(cyclomatic: float, loc: float, comment_ratio: float) -> float:
    """Computes a standard Maintainability Index normalized to 0-100."""
    if loc <= 0:
        return 100.0
    # Halstead volume approximation: V approx loc * 15 * log2(15) ~ loc * 58.6
    v_approx = max(loc * 58.6, 1.0)
    raw_mi = 171.0 - 5.2 * math.log(v_approx) - 0.23 * cyclomatic - 16.2 * math.log(loc)
    # Add comment bonus if applicable (SEI formula)
    if comment_ratio > 0:
        raw_mi += 50.0 * math.sin(math.sqrt(2.4 * comment_ratio))
    # Normalize to 0-100 range
    normalized = max(0.0, min(100.0, (raw_mi * 100.0) / 171.0))
    return round(normalized, 2)


class PythonAnalyzer(StaticAnalyzer):
    def analyze(self, code: str, file_path: Path | None = None) -> dict[str, float]:
        # Try radon if installed
        try:
            import radon.complexity as cc
            import radon.metrics as mi

            blocks = cc.cc_visit(code)
            cyclo = sum(b.complexity for b in blocks) if blocks else 1
            mi_score = mi.mi_visit(code, multi=True)
            max_fn = max((b.endline - b.lineno for b in blocks), default=0)
            lines = code.splitlines()
            total = len(lines)
            comments = len([l for l in lines if l.strip().startswith("#")])
            non_empty = len([l for l in lines if l.strip() and not l.strip().startswith("#")])

            return {
                "loc": float(non_empty),
                "cyclomatic": float(cyclo),
                "maintainability": round(float(mi_score), 2),
                "comment_ratio": round(comments / max(total, 1), 4),
                "max_function_length": float(max_fn),
            }
        except Exception:
            return self._fallback_python(code)

    def _fallback_python(self, code: str) -> dict[str, float]:
        lines = code.splitlines()
        total = len(lines)
        comments = 0
        loc = 0
        in_multiline = False

        for line in lines:
            stripped = line.strip()
            if not stripped:
                continue
            if stripped.startswith('"""') or stripped.startswith("'''"):
                if stripped.count('"""') == 1 or stripped.count("'''") == 1:
                    in_multiline = not in_multiline
                comments += 1
                continue
            if in_multiline:
                comments += 1
                if '"""' in stripped or "'''" in stripped:
                    in_multiline = False
                continue
            if stripped.startswith("#"):
                comments += 1
                continue
            loc += 1

        # Decision points in Python
        decision_keywords = re.findall(r"\b(if|elif|for|while|except|and|or)\b", code)
        cyclo = len(decision_keywords) + 1

        # Estimate function lengths from def ... to next unindented line
        fn_matches = list(re.finditer(r"^[ \t]*def\s+[a-zA-Z0-9_]+\s*\(", code, re.MULTILINE))
        max_fn = 0
        if fn_matches:
            line_offsets = [code.count("\n", 0, m.start()) for m in fn_matches]
            line_offsets.append(total)
            for idx in range(len(line_offsets) - 1):
                length = line_offsets[idx + 1] - line_offsets[idx]
                if length > max_fn:
                    max_fn = length

        ratio = round(comments / max(total, 1), 4)
        mi_score = estimate_mi(cyclo, loc, ratio)

        return {
            "loc": float(loc),
            "cyclomatic": float(cyclo),
            "maintainability": mi_score,
            "comment_ratio": ratio,
            "max_function_length": float(max_fn),
        }


class JavaScriptAnalyzer(StaticAnalyzer):
    """Analyzes Node.js / JavaScript / TypeScript files for Harbour Notes and Express."""

    def analyze(self, code: str, file_path: Path | None = None) -> dict[str, float]:
        # If node and typhonjs-escomplex are available via bundled script, invoke it
        if file_path and file_path.exists():
            js_script = Path(__file__).resolve().parent / "js_analyzer.js"
            if js_script.exists():
                try:
                    res = subprocess.run(
                        ["node", str(js_script), str(file_path)],
                        capture_output=True,
                        text=True,
                        timeout=5,
                    )
                    if res.returncode == 0:
                        import json
                        data = json.loads(res.stdout)
                        return {
                            "loc": float(data.get("loc", 0)),
                            "cyclomatic": float(data.get("cyclomatic", 1)),
                            "maintainability": float(data.get("maintainability", 100)),
                            "comment_ratio": float(data.get("comment_ratio", 0)),
                            "max_function_length": float(data.get("max_function_length", 0)),
                        }
                except Exception:
                    pass

        return self._fallback_js(code)

    def _fallback_js(self, code: str) -> dict[str, float]:
        lines = code.splitlines()
        total = len(lines)
        comments = 0
        loc = 0
        in_block_comment = False

        for line in lines:
            stripped = line.strip()
            if not stripped:
                continue
            if stripped.startswith("/*"):
                in_block_comment = True
                comments += 1
                if "*/" in stripped:
                    in_block_comment = False
                continue
            if in_block_comment:
                comments += 1
                if "*/" in stripped:
                    in_block_comment = False
                continue
            if stripped.startswith("//"):
                comments += 1
                continue
            loc += 1

        # Cyclomatic decision branches in JS
        keywords = re.findall(r"\b(if|else\s+if|for|while|case|catch)\b|(&&|\|\||\?)", code)
        cyclo = len(keywords) + 1

        # Estimate function lengths
        fn_pattern = re.finditer(
            r"(?:function\s+[a-zA-Z0-9_]*\s*\(|(?:const|let|var)\s+[a-zA-Z0-9_]+\s*=\s*(?:async\s*)?\([^)]*\)\s*=>|\b[a-zA-Z0-9_]+\s*\([^)]*\)\s*\{)",
            code,
        )
        fn_positions = [m.start() for m in fn_pattern]
        max_fn = 0
        if fn_positions:
            line_offsets = [code.count("\n", 0, pos) for pos in fn_positions]
            line_offsets.append(total)
            for idx in range(len(line_offsets) - 1):
                length = line_offsets[idx + 1] - line_offsets[idx]
                if length > max_fn:
                    max_fn = length

        ratio = round(comments / max(total, 1), 4)
        mi_score = estimate_mi(cyclo, loc, ratio)

        return {
            "loc": float(loc),
            "cyclomatic": float(cyclo),
            "maintainability": mi_score,
            "comment_ratio": ratio,
            "max_function_length": float(max_fn),
        }


class CSharpAnalyzer(StaticAnalyzer):
    def analyze(self, code: str, file_path: Path | None = None) -> dict[str, float]:
        lines = code.splitlines()
        total = len(lines)
        comments = 0
        loc = 0
        in_block_comment = False

        for line in lines:
            stripped = line.strip()
            if not stripped:
                continue
            if stripped.startswith("/*"):
                in_block_comment = True
                comments += 1
                if "*/" in stripped:
                    in_block_comment = False
                continue
            if in_block_comment:
                comments += 1
                if "*/" in stripped:
                    in_block_comment = False
                continue
            if stripped.startswith("//"):
                comments += 1
                continue
            loc += 1

        keywords = re.findall(r"\b(if|else\s+if|for|foreach|while|case|catch)\b|(&&|\|\||\?\?)", code)
        cyclo = len(keywords) + 1

        # Methods regex
        fn_pattern = re.finditer(r"(?:public|private|protected|internal|static|async)\s+[\w\<\>\[\]]+\s+\w+\s*\(", code)
        fn_positions = [m.start() for m in fn_pattern]
        max_fn = 0
        if fn_positions:
            line_offsets = [code.count("\n", 0, pos) for pos in fn_positions]
            line_offsets.append(total)
            for idx in range(len(line_offsets) - 1):
                length = line_offsets[idx + 1] - line_offsets[idx]
                if length > max_fn:
                    max_fn = length

        ratio = round(comments / max(total, 1), 4)
        mi_score = estimate_mi(cyclo, loc, ratio)

        return {
            "loc": float(loc),
            "cyclomatic": float(cyclo),
            "maintainability": mi_score,
            "comment_ratio": ratio,
            "max_function_length": float(max_fn),
        }


ANALYZER_REGISTRY: dict[str, type[StaticAnalyzer]] = {
    ".py": PythonAnalyzer,
    ".js": JavaScriptAnalyzer,
    ".mjs": JavaScriptAnalyzer,
    ".ts": JavaScriptAnalyzer,
    ".cs": CSharpAnalyzer,
}


def get_analyzer(file_path: Path) -> StaticAnalyzer:
    ext = file_path.suffix.lower()
    analyzer_cls = ANALYZER_REGISTRY.get(ext)
    if analyzer_cls is None:
        return JavaScriptAnalyzer()
    return analyzer_cls()


def analyze_file(file_path: Path) -> dict[str, float]:
    if not file_path.exists() or not file_path.is_file():
        return {"loc": 0.0, "cyclomatic": 1.0, "maintainability": 100.0, "comment_ratio": 0.0, "max_function_length": 0.0}
    try:
        code = file_path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return {"loc": 0.0, "cyclomatic": 1.0, "maintainability": 100.0, "comment_ratio": 0.0, "max_function_length": 0.0}

    analyzer = get_analyzer(file_path)
    return analyzer.analyze(code, file_path)


def analyze_directory(dir_path: Path, touches: list[str] | None = None) -> dict[str, float]:
    """Analyzes either touched files or the entire source tree, aggregating metrics."""
    if not dir_path.exists():
        return {"loc": 0.0, "cyclomatic": 1.0, "maintainability": 100.0, "comment_ratio": 0.0, "max_function_length": 0.0}

    files_to_check: list[Path] = []
    if touches:
        for t in touches:
            p = dir_path / t
            if p.exists() and p.is_file():
                files_to_check.append(p)

    if not files_to_check:
        for ext in ANALYZER_REGISTRY.keys():
            files_to_check.extend(dir_path.rglob(f"*{ext}"))
            # Filter out node_modules, bin, obj, .git
            files_to_check = [
                f for f in files_to_check
                if not any(part in f.parts for part in ["node_modules", "bin", "obj", ".git", "__pycache__", "dist"])
            ]

    if not files_to_check:
        return {"loc": 0.0, "cyclomatic": 1.0, "maintainability": 100.0, "comment_ratio": 0.0, "max_function_length": 0.0}

    total_loc = 0.0
    total_cyclo = 0.0
    weighted_mi = 0.0
    total_comments_ratio = 0.0
    max_fn = 0.0

    for f in files_to_check:
        m = analyze_file(f)
        total_loc += m["loc"]
        total_cyclo += m["cyclomatic"]
        weighted_mi += m["maintainability"] * max(m["loc"], 1.0)
        total_comments_ratio += m["comment_ratio"]
        if m["max_function_length"] > max_fn:
            max_fn = m["max_function_length"]

    n = len(files_to_check)
    avg_mi = round(weighted_mi / max(total_loc, 1.0), 2)
    avg_comment = round(total_comments_ratio / n, 4)

    return {
        "loc": total_loc,
        "cyclomatic": total_cyclo,
        "maintainability": avg_mi,
        "comment_ratio": avg_comment,
        "max_function_length": max_fn,
    }
