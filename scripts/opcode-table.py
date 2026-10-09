#!/usr/bin/env python3
"""Prints the Spark command table from libpghw-lib.so's symbols.
Every command is a pg::GenericDeviceSession<Op<cmd,sub>, Req, Op<cmd,sub>, Resp, expectsReply> instantiation.
Usage: scripts/opcode-table.py research/native/pghw-symbols.txt"""
import re, sys

pat = re.compile(r'GenericDeviceSession<pg::SessionOperationCode<\(unsigned char\)(\d+), \(unsigned char\)(\d+)>, ([^,]+), '
                 r'pg::SessionOperationCode<\(unsigned char\)(\d+), \(unsigned char\)(\d+)>, ([^,]+), (true|false)>')
rows = set()
for line in open(sys.argv[1]):
    for a, b, req, c, d, resp, ack in pat.findall(line):
        reply = f'{int(c):02x} {int(d):02x}' if (c, d) != ('0', '0') else '-    '
        rows.add(f'{int(a):02x} {int(b):02x}  {req.replace("pgmsgpack::", ""):28s} -> {reply}  {resp.replace("pgmsgpack::", ""):28s} {ack}')
print('\n'.join(sorted(rows)))
