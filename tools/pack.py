"""Pack build/pkg into build/NoitaCraft-<version>.zip with forward-slash paths."""
import os, sys, zipfile, hashlib
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ver = sys.argv[1]
pkg = os.path.join(root, "build", "pkg")
out = os.path.join(root, "build", f"NoitaCraft-{ver}.zip")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for d, _, fs in os.walk(pkg):
        for f in sorted(fs):
            p = os.path.join(d, f)
            z.write(p, os.path.relpath(p, pkg).replace(os.sep, "/"))
b = open(out, "rb").read()
print(out, len(b), hashlib.sha256(b).hexdigest())
for i in zipfile.ZipFile(out).infolist(): print(" ", i.filename, i.file_size)
