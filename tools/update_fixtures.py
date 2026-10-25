#!/usr/bin/env python3
"""Makes the zips tools/UpdateHarness.cs.txt checks (1.2.3 updater). Usage: update_fixtures.py DIR [release.zip]"""
import zipfile, os, sys, shutil
d = sys.argv[1]; os.makedirs(d, exist_ok=True); os.chdir(d)
exe = b'MZ' + bytes((i * 7919) % 251 for i in range(200 * 1024))
def mk(name, files, method=zipfile.ZIP_DEFLATED):
    with zipfile.ZipFile(name, 'w', method) as z:
        for n, data in files: z.writestr(n, data)
mk('good.zip', [('BotBeacons-9.9.9-win/BotBeacons.exe', exe), ('BotBeacons-9.9.9-win/README.md', b'hi' * 100), ('BotBeacons-9.9.9-win/source/BotBeacons.exe', b'MZsource')])
mk('stored.zip', [('BotBeacons.exe', exe)], zipfile.ZIP_STORED)
mk('noexe.zip', [('BotBeacons-9.9.9-win/README.md', b'hi')])
mk('onlysource.zip', [('x/source/BotBeacons.exe', exe)])
mk('notpe.zip', [('BotBeacons.exe', b'XX' + exe[2:])])
mk('small.zip', [('BotBeacons.exe', b'MZ' + b'0' * 100)])
mk('evil.zip', [('../BotBeacons.exe', exe)])
b = bytearray(open('stored.zip', 'rb').read()); b[100] ^= 0xFF; open('crc.zip', 'wb').write(b)
g = open('good.zip', 'rb').read(); open('trunc.zip', 'wb').write(g[:len(g) // 2])
open('notzip.zip', 'wb').write(b'<html>rate limited</html>')
open('exe.bin', 'wb').write(exe)
if len(sys.argv) > 2: shutil.copy(sys.argv[2], 'release.zip')
