"""Usage: python build/check-vecturadb.py [--write]. The executables of ChromaDotNet.Local.VecturaDB against the GitHub release of
VecturaDB at the tag of Directory.Build.props: for each platform of src/ChromaDotNet.Local.VecturaDB/VecturaDB.props, the file of the
release and the SHA-256 digest GitHub gives it. Without arguments it checks the hashes of VecturaDB.props; with --write, for a new tag,
it writes the digests of the release into VecturaDB.props, which the pack puts into the package. With GH_TOKEN, as in the CI, the
requests to the API of GitHub have the limits of the token."""
import json, os, re, sys, urllib.request

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
props = os.path.join(root, "src", "ChromaDotNet.Local.VecturaDB", "VecturaDB.props")
write = sys.argv[1:] == ["--write"]
tag = re.search(r"<VecturaDBTag>(.*?)</VecturaDBTag>", open(os.path.join(root, "Directory.Build.props"), encoding="utf-8").read()).group(1)
headers = {"Accept": "application/vnd.github+json"}
if os.environ.get("GH_TOKEN"):
    headers["Authorization"] = "Bearer " + os.environ["GH_TOKEN"]
request = urllib.request.Request(f"https://api.github.com/repos/VecturaDB/VecturaDB/releases/tags/{tag}", headers=headers)
with urllib.request.urlopen(request) as response:
    release = json.load(response)
digests = {asset["name"]: (asset.get("digest") or "").removeprefix("sha256:") for asset in release["assets"]}
text = open(props, encoding="utf-8").read()
failures = 0
found = []


def check(match):
    global failures
    rid, asset, pinned = match.group("rid"), match.group("asset"), match.group("sha")
    found.append(rid)
    digest = digests.get(asset, "")
    if not re.fullmatch("[0-9a-f]{64}", digest):
        print(f"FAIL {rid}: no file {asset} with a SHA-256 digest in the release {tag}")
        failures += 1
    elif write:
        print(f"{rid}: {asset} {digest}")
        return match.group(0).replace(f'Sha256="{pinned}"', f'Sha256="{digest}"')
    elif digest == pinned:
        print(f"ok   {rid}: {asset} {digest}")
    else:
        print(f"FAIL {rid}: {asset} {digest} in the release, {pinned} in VecturaDB.props")
        failures += 1
    return match.group(0)


text = re.sub(r'<VecturaDBExecutable Include="(?P<rid>[^"]+)"[^>]*Asset="(?P<asset>[^"]+)" Sha256="(?P<sha>[0-9a-f]*)"', check, text)
# Each platform once, in the form above: an element written otherwise would be left out without a word.
expected = ["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"]
if sorted(found) != sorted(expected) or text.count("<VecturaDBExecutable ") != len(expected):
    print(f"FAIL VecturaDB.props has the platforms {', '.join(found) or 'none'} in the form Include, Asset, Sha256, not one each of {', '.join(expected)}")
    failures += 1
if write and not failures:
    with open(props, "w", encoding="utf-8", newline="") as file:
        file.write(text)
print(f"release {tag}, failures: {failures}")
sys.exit(1 if failures else 0)
