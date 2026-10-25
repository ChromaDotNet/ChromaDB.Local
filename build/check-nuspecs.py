"""Usage: python build/check-nuspecs.py <directory of the packages>. Each package has the URL of the repository and of the project, so
that NuGet links its source, and dependencies whose versions nuget.org accepts: it reads a release label without a dot as one of
SemVer 1, which starts with a letter, and refuses the package otherwise, as for the bound 2.0.0-0."""
import glob, os, re, sys, zipfile

expected = 'https://github.com/ChromaDotNet/ChromaDB.Local'
failures = 0
packages = sorted(glob.glob(os.path.join(sys.argv[1], '*.nupkg')))
for path in packages:
    with zipfile.ZipFile(path) as package:
        name = next(n for n in package.namelist() if n.endswith('.nuspec'))
        nuspec = package.read(name).decode('utf-8')
    repository = re.search(r'<repository [^>]*url="([^"]*)"', nuspec)
    project = re.search(r'<projectUrl>(.*?)</projectUrl>', nuspec)
    ok = repository is not None and repository.group(1) == expected and project is not None and project.group(1) == expected
    # The bounds of the versions of the dependencies, as 2.14.0 or [1.5.9.1-preview.6, 2.0.0).
    bounds = [b.strip() for v in re.findall(r'<dependency [^>]*version="([^"]*)"', nuspec) for b in v.strip('[]()').split(',') if b.strip()]
    labels = [(b, b.split('-', 1)[1]) for b in bounds if '-' in b and '+' not in b]
    refused = [b for b, label in labels if '.' not in label and not re.fullmatch(r'[A-Za-z][0-9A-Za-z-]*', label)]
    ok = ok and not refused
    print(('ok   ' if ok else 'FAIL ') + os.path.basename(path) + (f': nuget.org refuses the versions {", ".join(refused)}' if refused else ''))
    failures += 0 if ok else 1
if not packages:
    print('FAIL no packages')
    failures = 1
sys.exit(failures)
