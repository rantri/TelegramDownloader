"""Select pushed commits or the proposed PR merge for independent Windows checks."""

import json
import os
import re
import subprocess
from pathlib import Path


def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()


def sdk_version(sha):
    contents = subprocess.run(
        ["git", "show", f"{sha}:global.json"], capture_output=True, text=True
    )
    if contents.returncode:
        return None
    try:
        version = json.loads(contents.stdout)["sdk"]["version"]
    except (KeyError, TypeError, ValueError):
        return None
    if isinstance(version, str) and re.fullmatch(r"\d+\.\d+\.\d+(?:-[\w.-]+)?", version):
        return version
    return None


event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
tip = os.environ["GITHUB_SHA"]
commits = [tip]
if os.environ["GITHUB_EVENT_NAME"] == "push":
    reported = event.get("commits", [])
    # GitHub caps this array at 2,048. A new ref may have no comparison base.
    if 0 < len(reported) < 2048:
        commits = [item["id"] for item in reported]
    else:
        before = event["before"]
        span = tip if before == "0" * 40 else f"{before}..{tip}"
        # The discarded tip of a force push may no longer be fetchable.
        if before != "0" * 40 and subprocess.run(
            ["git", "cat-file", "-e", before], stderr=subprocess.DEVNULL
        ).returncode:
            span = tip
        commits = git("rev-list", "--reverse", "--topo-order", span).splitlines()

commits = list(dict.fromkeys([*commits, tip]))
if not all(re.fullmatch(r"[a-f0-9]{40}", sha) for sha in commits):
    raise ValueError("Expected full Git commit IDs")

# GitHub permits at most 256 jobs per matrix. A normal push gets one job per
# SHA. Larger pushes use sequential groups, still collecting every result.
group_size = (len(commits) + 255) // 256
matrix = []
for start in range(0, len(commits), group_size):
    group = commits[start : start + group_size]
    versions = {sha: sdk_version(sha) for sha in group}
    sdks = sorted({version for version in versions.values() if version})
    label = group[0][:12] if len(group) == 1 else f"{group[0][:12]} + {len(group)-1} more"
    matrix.append({
        "label": label,
        "revisions": group,
        "sdks": "\n".join(sdks),
        "invalid": [sha for sha, version in versions.items() if not version],
    })

with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
    print("matrix=" + json.dumps({"include": matrix}, separators=(",", ":")), file=output)
