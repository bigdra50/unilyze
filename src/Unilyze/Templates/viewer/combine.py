#!/usr/bin/env python3
"""Combine viewer fragments into the single-file viewer.html the build embeds.

Inputs:  Templates/viewer/{index.html,styles.css,main.js}
Output:  $1 (combined viewer.html)
"""
import pathlib
import sys

here = pathlib.Path(__file__).parent
shell = (here / "index.html").read_text()
css = (here / "styles.css").read_text()
js = (here / "main.js").read_text()
out = shell.replace("/*__VIEWER_STYLES__*/", css).replace("//__VIEWER_MAIN_JS__", js)
pathlib.Path(sys.argv[1]).write_text(out)
