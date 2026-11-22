#!/usr/bin/env python3
"""Decide whether a local benchmark change is real, or whether the machine is just noisy.

A local A/B on a developer laptop cannot reuse `benchmarks/baseline.json`: that file was recorded
on a Linux CI runner, so a Windows or macOS run is not comparable to it, and a single run of the
harness on either machine is dominated by the JIT, by frequency scaling and by whatever else the
box was doing. The comparison that *is* possible locally is two revisions measured under the same
conditions, close together in time, with the run-to-run spread of each revision used as the noise
floor of the verdict.

Two ways to feed it:

    # the script builds both worktrees, alternates the runs, then compares
    tools/bench-compare.py run --a C:/src/text --b C:/src/text-feature --rounds 7

    # compare result files produced elsewhere with: --samples <file> --label <name>
    tools/bench-compare.py compare --a a1.json a2.json --b b1.json b2.json

Method, and why each part is there:

* A *round* is one process per revision. `run` alternates them A B, then B A, then A B... so the
  two revisions see the same thermal and load conditions, and the reversal cancels the "second
  process of the pair is slower" bias a fixed order would bake in.
* Per scenario and per run, the sample of iteration timings is reduced to its **median**. The
  harness also writes the minimum, but the minimum is the luckiest single iteration and it is not
  what a user experiences; the median is stable across runs and still responds to a real change.
* Effect = median over rounds of `candidate_median / baseline_median`, paired by round. A ratio
  above 1 means the candidate is slower.
* Noise floor = the median absolute relative difference between *adjacent* runs of the **same**
  revision. Adjacent because two consecutive runs of one revision are separated by the same slice
  of machine time as the two sides of a round, so their disagreement is the null distribution of
  exactly the comparison being made. That is the only honest source for it: identical code, this
  machine, this session.
* Uncertainty = percentile bootstrap over rounds on the paired ratio. With 7 rounds the interval
  is coarse, and it says so.
* Verdict is never a bare threshold. The interval must clear the noise band
  `[1 - noise, 1 + noise]` to name a regression or an improvement — a large effect is decided even
  on a noisy host, because the machine would have to drift further than the effect to explain it.
  Below that, if the resolution (noise, or the interval half-width) is wider than the budget, the
  answer is "refused"; if the interval contains 1, "no change at this resolution"; otherwise
  "inconclusive". Never a number the data cannot support.

Exit status: 0 no regression, 1 a regression was established, 2 nothing could be concluded for at
least one scenario (noise above `--max-noise`, too few rounds, mismatched inputs), 3 bad usage.
Standard library only, Python 3.9 or newer.
"""

from __future__ import annotations

import argparse
import json
import random
import statistics
import subprocess
import sys
import tempfile
from pathlib import Path

PROJECT = Path("benchmarks") / "Text.Typst.Benchmarks"
DEFAULT_ITERATIONS = 50
DEFAULT_WARMUP = 10
BOOTSTRAP_ROUNDS = 2000
MIN_ROUNDS = 3

EXIT_CLEAN = 0
EXIT_REGRESSION = 1
EXIT_INCONCLUSIVE = 2
EXIT_USAGE = 3


class UsageError(Exception):
    """A command-line or input problem, as opposed to a measurement problem."""


class Result:
    """The per-scenario numbers extracted from one `--samples` file."""

    def __init__(self, path: Path, document: dict) -> None:
        if document.get("Format") != 1:
            raise UsageError(f"{path}: unknown samples format {document.get('Format')!r}")
        scenarios = document.get("Scenarios")
        if not isinstance(scenarios, dict) or not scenarios:
            raise UsageError(f"{path}: no Scenarios")
        self.path = path
        self.label = document.get("Label") or path.stem
        self.machine = (
            document.get("OperatingSystem"),
            document.get("Architecture"),
            document.get("ProcessorCount"),
        )
        self.iterations = document.get("Iterations")
        self.scenarios: dict[str, list[float]] = {}
        for name, values in scenarios.items():
            samples = values.get("SamplesMs") if isinstance(values, dict) else None
            if not isinstance(samples, list) or not samples:
                raise UsageError(f"{path}: scenario {name!r} has no SamplesMs")
            self.scenarios[name] = [float(v) for v in samples]


def load_result(path: Path) -> Result:
    try:
        with path.open(encoding="utf-8") as handle:
            document = json.load(handle)
    except OSError as error:
        raise UsageError(f"cannot read {path}: {error}") from error
    except json.JSONDecodeError as error:
        raise UsageError(f"{path} is not JSON: {error}") from error
    return Result(path, document)


def centre(samples: list[float]) -> float:
    """The per-run statistic: the median, not the minimum (see the module docstring)."""
    return statistics.median(samples)


def relative_spread(values: list[float]) -> float:
    """Median absolute relative difference between *adjacent* runs of the same revision.

    Adjacent, not all pairs: two consecutive runs of one revision are separated by the same
    amount of machine time as the baseline and candidate of one round, so their disagreement is
    the null distribution of exactly the comparison being made. All pairs would also fold in the
    slow drift of a session — a real effect on the ratio, but one the pairing already removes,
    and charging it twice would refuse every campaign on a busy host.
    """
    differences = [
        abs(values[i + 1] / values[i] - 1.0)
        for i in range(len(values) - 1)
        if values[i] > 0
    ]
    return statistics.median(differences) if differences else float("nan")


def bootstrap_interval(
    ratios: list[float], rng: random.Random, confidence: float = 0.95,
) -> tuple[float, float]:
    """Percentile bootstrap on the median of the paired ratios."""
    if len(ratios) < 2:
        return float("nan"), float("nan")
    medians = []
    for _ in range(BOOTSTRAP_ROUNDS):
        draw = [ratios[rng.randrange(len(ratios))] for _ in ratios]
        medians.append(statistics.median(draw))
    medians.sort()
    tail = (1.0 - confidence) / 2.0
    low = medians[int(tail * (len(medians) - 1))]
    high = medians[int((1.0 - tail) * (len(medians) - 1))]
    return low, high


class Verdict:
    def __init__(
        self,
        scenario: str,
        ratio: float,
        interval: tuple[float, float],
        noise: float,
        status: str,
        reason: str,
    ) -> None:
        self.scenario = scenario
        self.ratio = ratio
        self.interval = interval
        self.noise = noise
        self.status = status
        self.reason = reason


def judge(
    scenario: str,
    baseline_runs: list[float],
    candidate_runs: list[float],
    max_noise: float,
    rng: random.Random,
) -> Verdict:
    paired = min(len(baseline_runs), len(candidate_runs))
    ratios = [candidate_runs[i] / baseline_runs[i] for i in range(paired) if baseline_runs[i] > 0]
    ratio = statistics.median(ratios) if ratios else float("nan")
    noise = max(relative_spread(baseline_runs), relative_spread(candidate_runs))

    if paired < MIN_ROUNDS:
        return Verdict(scenario, ratio, (float("nan"), float("nan")), noise, "REFUSED",
                       f"{paired} paired round(s); {MIN_ROUNDS} is the minimum")
    if noise != noise:  # NaN
        return Verdict(scenario, ratio, (float("nan"), float("nan")), noise, "REFUSED",
                       "no spread could be computed")
    low, high = bootstrap_interval(ratios, rng)
    half_width = (high - low) / 2.0
    band_low, band_high = 1.0 - noise, 1.0 + noise
    resolution = max(noise, half_width)
    budget = f"{max_noise * 100:.0f}%"

    # A large effect is decidable even on a noisy host: the machine would have to drift more than
    # the effect itself to explain it. This test comes first so that a real 2x regression is never
    # hidden behind "the machine is noisy".
    if low > band_high:
        return Verdict(scenario, ratio, (low, high), noise, "REGRESSION",
                       f"slower by {(low - 1) * 100:.1f}% at worst, {(high - 1) * 100:.1f}% at best")
    if high < band_low:
        return Verdict(scenario, ratio, (low, high), noise, "IMPROVEMENT",
                       f"faster by {(1 - high) * 100:.1f}% at worst, {(1 - low) * 100:.1f}% at best")

    # Below that, the question is whether this campaign can resolve the effect at the requested
    # budget at all. A machine drifting 25% between two runs of identical code cannot answer a
    # question about a 5% regression, whatever the point estimate says.
    if resolution > max_noise:
        return Verdict(scenario, ratio, (low, high), noise, "REFUSED",
                       f"resolution +/-{resolution * 100:.1f}% (noise {noise * 100:.1f}%, "
                       f"interval +/-{half_width * 100:.1f}%) is wider than the {budget} budget: "
                       "more rounds or a quieter machine")

    if low <= 1.0 <= high:
        return Verdict(scenario, ratio, (low, high), noise, "NO CHANGE",
                       f"the interval contains no change; resolution is +/-{resolution * 100:.1f}%")
    return Verdict(scenario, ratio, (low, high), noise, "INCONCLUSIVE",
                   f"the interval excludes 1 but is inside the +/-{noise * 100:.1f}% noise band: "
                   "the effect is smaller than this machine's run-to-run spread")


def describe_machine(results: list[Result]) -> list[str]:
    groups: dict[tuple, list[str]] = {}
    for result in results:
        groups.setdefault(result.machine, []).append(result.path.name)
    if len(groups) <= 1:
        return []
    lines = ["warning: the result files were not all recorded under the same conditions:"]
    for machine, names in groups.items():
        os_name, architecture, processors = machine
        lines.append(f"  {os_name} / {architecture} / {processors} cpu: {len(names)} file(s), e.g. {names[0]}")
    lines.append("a ratio across machines is not a measurement of the code.")
    return lines


def analyse(
    baseline: list[Result],
    candidate: list[Result],
    max_noise: float,
    seed: int | None,
) -> tuple[list[Verdict], list[str], int]:
    """Compare two groups of runs. Returns (verdicts, notes, exit status)."""
    notes = describe_machine(baseline + candidate)
    rng = random.Random(seed)

    baseline_centres: dict[str, list[float]] = {}
    candidate_centres: dict[str, list[float]] = {}
    for group, target in ((baseline, baseline_centres), (candidate, candidate_centres)):
        for result in group:
            for name, samples in result.scenarios.items():
                target.setdefault(name, []).append(centre(samples))

    names = [name for name in baseline_centres if name in candidate_centres]
    missing = sorted(
        set(baseline_centres) ^ set(candidate_centres)
    )
    if missing:
        notes.append(f"warning: scenario(s) measured on one side only, skipped: {', '.join(missing)}")
    if not names:
        raise UsageError("no scenario is present in both inputs")

    verdicts = [
        judge(name, baseline_centres[name], candidate_centres[name], max_noise, rng)
        for name in names
    ]

    # A regression outranks an inconclusive scenario: a developer must not miss a real one because
    # the machine could not answer a different question in the same campaign.
    if any(verdict.status == "REGRESSION" for verdict in verdicts):
        status = EXIT_REGRESSION
    elif any(verdict.status in ("REFUSED", "INCONCLUSIVE") for verdict in verdicts):
        status = EXIT_INCONCLUSIVE
    else:
        status = EXIT_CLEAN

    return verdicts, notes, status


def print_report(
    verdicts: list[Verdict], notes: list[str], baseline: list[Result], candidate: list[Result],
) -> None:
    base_label = baseline[0].label or "baseline"
    cand_label = candidate[0].label or "candidate"
    print(f"baseline : {base_label}  ({len(baseline)} run(s))")
    print(f"candidate: {cand_label}  ({len(candidate)} run(s))")
    for note in notes:
        print(note)
    print()
    header = f"{'scenario':<18}{'base ms':>9}{'cand ms':>9}{'ratio':>8}  {'95% CI':<18}{'noise':>7}  verdict"
    print(header)
    print("-" * len(header))
    for verdict in verdicts:
        name = verdict.scenario
        base = statistics.median([centre(r.scenarios[name]) for r in baseline if name in r.scenarios])
        cand = statistics.median([centre(r.scenarios[name]) for r in candidate if name in r.scenarios])
        if verdict.interval[0] == verdict.interval[0]:  # not NaN
            interval = f"[{verdict.interval[0]:.3f}, {verdict.interval[1]:.3f}]"
        else:
            interval = "-"
        print(
            f"{verdict.scenario:<18}{base:9.2f}{cand:9.2f}{verdict.ratio:8.3f}  "
            f"{interval:<18}{verdict.noise * 100:6.1f}%  {verdict.status}"
        )
        print(f"{'':<18}{verdict.reason}")
    print()
    print("ratio = candidate / baseline median per round; > 1 is slower.")
    print("A regression or an improvement needs the whole interval outside the noise band.")


def write_json(path: Path, verdicts: list[Verdict], status: int) -> None:
    payload = {
        "Status": status,
        "Verdicts": [
            {
                "Scenario": verdict.scenario,
                "Status": verdict.status,
                "Ratio": verdict.ratio,
                "IntervalLow": verdict.interval[0],
                "IntervalHigh": verdict.interval[1],
                "Noise": verdict.noise,
                "Reason": verdict.reason,
            }
            for verdict in verdicts
        ],
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)
        handle.write("\n")


def build_command(repo: Path, output: Path, no_restore: bool) -> list[str]:
    command = [
        "dotnet", "build", str(repo / PROJECT / "Text.Typst.Benchmarks.csproj"),
        "-c", "Release", "-o", str(output),
    ]
    if no_restore:
        command.append("--no-restore")
    return command


def run_harness(dll: Path, out_path: Path, label: str, args: argparse.Namespace) -> None:
    command = [
        "dotnet", str(dll), "--samples", str(out_path), "--label", label,
        "--iterations", str(args.iterations), "--warmup", str(args.warmup),
        "--min-iteration-ms", str(args.min_iteration_ms),
    ]
    completed = subprocess.run(command, capture_output=True, text=True)
    if completed.returncode != 0:
        raise UsageError(
            f"the harness failed for {label} (exit {completed.returncode}):\n"
            f"{completed.stdout}{completed.stderr}"
        )


def head_of(repo: Path) -> str:
    """The short commit of a checkout, for a label that survives the report."""
    completed = subprocess.run(
        ["git", "-C", str(repo), "rev-parse", "--short", "HEAD"],
        capture_output=True, text=True,
    )
    return completed.stdout.strip() if completed.returncode == 0 else "unknown"


def cmd_run(args: argparse.Namespace) -> int:
    repo_a = Path(args.a).resolve()
    repo_b = Path(args.b).resolve()
    for repo in (repo_a, repo_b):
        if not (repo / PROJECT / "Text.Typst.Benchmarks.csproj").is_file():
            raise UsageError(f"{repo} does not contain {PROJECT}")

    out_dir = Path(args.out).resolve() if args.out else Path(tempfile.mkdtemp(prefix="bench-ab-"))
    out_dir.mkdir(parents=True, exist_ok=True)
    build_dir = out_dir / "bin"
    results_dir = out_dir / "results"

    dlls = {}
    for name, repo in (("a", repo_a), ("b", repo_b)):
        target = build_dir / name
        print(f"building {name}: {repo}", file=sys.stderr)
        completed = subprocess.run(build_command(repo, target, args.no_restore), capture_output=True, text=True)
        if completed.returncode != 0:
            raise UsageError(f"build failed for {name}:\n{completed.stdout[-4000:]}{completed.stderr[-4000:]}")
        dlls[name] = target / "Text.Typst.Benchmarks.dll"

    label_a = args.label_a or f"a:{head_of(repo_a)}"
    label_b = args.label_b or f"b:{head_of(repo_b)}"

    files: dict[str, list[Path]] = {"a": [], "b": []}
    for round_index in range(args.rounds):
        # ABBA: the revision that goes second in one round goes first in the next, so the position
        # in the pair cannot be mistaken for the effect.
        order = ["a", "b"] if round_index % 2 == 0 else ["b", "a"]
        for name in order:
            path = results_dir / f"{name}-{round_index}.json"
            print(f"round {round_index + 1}/{args.rounds}: {name}", file=sys.stderr)
            run_harness(dlls[name], path, label_a if name == "a" else label_b, args)
            files[name].append(path)

    baseline = [load_result(path) for path in files["a"]]
    candidate = [load_result(path) for path in files["b"]]
    verdicts, notes, status = analyse(baseline, candidate, args.max_noise, args.seed)
    print_report(verdicts, notes, baseline, candidate)
    if args.json:
        write_json(Path(args.json), verdicts, status)
    print(f"\nresults kept in {results_dir}")
    return status


def cmd_compare(args: argparse.Namespace) -> int:
    baseline = [load_result(Path(path)) for path in args.a]
    candidate = [load_result(Path(path)) for path in args.b]
    verdicts, notes, status = analyse(baseline, candidate, args.max_noise, args.seed)
    print_report(verdicts, notes, baseline, candidate)
    if args.json:
        write_json(Path(args.json), verdicts, status)
    return status


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Compare two sets of benchmark runs and say whether the difference is real.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    def common(sub: argparse.ArgumentParser) -> None:
        sub.add_argument(
            "--max-noise", type=float, default=0.10,
            help="refuse to conclude when the run-to-run spread exceeds this fraction (default 0.10)",
        )
        sub.add_argument("--seed", type=int, default=None, help="bootstrap seed, for a repeatable report")
        sub.add_argument("--json", help="also write the verdicts to this file")

    run_parser = subparsers.add_parser("run", help="build two worktrees, alternate the runs, compare")
    run_parser.add_argument("--a", required=True, help="worktree or checkout of the baseline revision")
    run_parser.add_argument("--b", required=True, help="worktree or checkout of the candidate revision")
    run_parser.add_argument("--rounds", type=int, default=7, help="paired rounds, default 7")
    run_parser.add_argument("--iterations", type=int, default=DEFAULT_ITERATIONS, help="samples per round")
    run_parser.add_argument("--warmup", type=int, default=DEFAULT_WARMUP, help="warm-up iterations per round")
    run_parser.add_argument(
        "--min-iteration-ms", type=float, default=1.0,
        help="repeat a scenario body inside one timed iteration until it lasts this long "
             "(default 1.0 ms; 0 disables it). Sub-millisecond scenarios are otherwise timed by "
             "the scheduler as much as by the code.",
    )
    run_parser.add_argument("--label-a", help="name for the baseline in the report")
    run_parser.add_argument("--label-b", help="name for the candidate in the report")
    run_parser.add_argument("--out", help="directory for the built binaries and raw results")
    run_parser.add_argument("--no-restore", action="store_true", help="pass --no-restore to dotnet build")
    run_parser.set_defaults(handler=cmd_run)
    common(run_parser)

    compare_parser = subparsers.add_parser("compare", help="compare existing --samples files")
    compare_parser.add_argument("--a", required=True, nargs="+", help="baseline result file(s), one per round")
    compare_parser.add_argument("--b", required=True, nargs="+", help="candidate result file(s), one per round")
    compare_parser.set_defaults(handler=cmd_compare)
    common(compare_parser)

    args = parser.parse_args(argv)
    if args.max_noise <= 0 or args.max_noise >= 1:
        raise UsageError("--max-noise must be between 0 and 1")
    if getattr(args, "rounds", MIN_ROUNDS) < MIN_ROUNDS:
        raise UsageError(f"--rounds must be at least {MIN_ROUNDS}")
    return args


def main(argv: list[str]) -> int:
    try:
        args = parse_args(argv)
        return args.handler(args)
    except UsageError as error:
        sys.stderr.write(f"{error}\n")
        return EXIT_USAGE
    except KeyboardInterrupt:
        return EXIT_USAGE


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
