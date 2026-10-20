"""Usage: python build/check-nuspecs.py <directory of the packages>. Each package has the URL of the repository and of the project, so
that NuGet links its source."""
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
    print(('ok   ' if ok else 'FAIL ') + os.path.basename(path))
    failures += 0 if ok else 1
if not packages:
    print('FAIL no packages')
    failures = 1
sys.exit(failures)
