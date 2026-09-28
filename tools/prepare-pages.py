#!/usr/bin/env python3
"""Stage the actual generated Uno site, independent of SDK output nesting."""
from pathlib import Path
import json
import os
import re
import shutil
import sys

source=Path(sys.argv[1] if len(sys.argv)>1 else 'artifacts/browser')
target=Path(sys.argv[2] if len(sys.argv)>2 else 'artifacts/site')
indexes=sorted(source.rglob('index.html'),key=lambda p:len(p.parts))
if not indexes:
    raise SystemExit(f'No generated Uno index.html below {source}')
root=indexes[0].parent
if target.exists():
    shutil.rmtree(target)
shutil.copytree(root,target)
(target/'.nojekyll').touch()
(target/'build-info.json').write_text(json.dumps({'commit':os.environ.get('GITHUB_SHA','local'),'version':'0.1.0','application':'DesignSpace','runtime':'Uno WebAssembly'}),encoding='utf-8')
index=target/'index.html'
html=index.read_text(encoding='utf-8')
html=re.sub(r'<title>.*?</title>','<title>DesignSpace — XAML Designer</title>',html,flags=re.S)
index.write_text(html,encoding='utf-8')
if Path('docs/site').exists():
    shutil.copytree('docs/site',target/'docs',dirs_exist_ok=True)
print(f'Staged generated Uno application: {root} -> {target}')
