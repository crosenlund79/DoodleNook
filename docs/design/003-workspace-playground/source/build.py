"""Wrap the artifact-form page (no html/head/body tags) into a standalone HTML document."""
import sys
src = open('playground.html').read()
i = src.index('<div class="lab"')
head, body = src[:i], src[i:]
out = ('<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n'
       '<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">\n'
       + head + '</head>\n<body>\n' + body + '\n</body>\n</html>\n')
open(sys.argv[1] if len(sys.argv) > 1 else 'index.html', 'w').write(out)
