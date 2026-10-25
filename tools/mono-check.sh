#!/bin/sh
# Compile check for src/*.cs on Linux (for example a Grok Bot box) with Mono's mcs, before copying a change to Windows.
# It is only a syntax and type check: tools/MonoCngStub.cs.txt stubs the Windows-only CNG crypto classes, so the
# resulting exe is thrown away. The real build is build.bat on Windows.
# Needs mono (Debian/Ubuntu: apt-get install mono-mcs mono-devel).  Usage: sh tools/mono-check.sh
set -e
cd "$(dirname "$0")/.."
# 1.2.3: a Cursor on a RichTextBox/TextBox fights the edit control's own WM_SETCURSOR (the cursor flickers I-beam <-> arrow)
if grep -nE "new (Rich)?TextBox *\{[^}]*Cursor *=" src/*.cs; then echo "FAIL: don't set Cursor on a text box (see TabCard.cs)"; exit 1; fi
tmp=$(mktemp -d)
cp src/*.cs "$tmp"/ && cp tools/MonoCngStub.cs.txt "$tmp"/CngStub.cs
mcs -langversion:5 -target:winexe -out:"$tmp"/check.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Web.Extensions.dll -r:System.Security.dll -r:System.Core.dll \
  "$tmp"/*.cs && echo "OK: src compiles (C# 5)."
rm -rf "$tmp"
