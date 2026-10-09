#!/usr/bin/env python3
"""Prints which opcode each JNI entry of libpghw-lib.so sends.
Each Java_..._RemoteDevice_<name> loads its session's vtable from a GOT slot; the relocation names the class. A
GenericDeviceSession class carries its opcode in its name; a gen-1 session (GetDeviceNameSession, ...) passes cmd and
sub to PGSysEx::ComposePGSysEx, directly or through a helper that takes the sub byte.
Usage: scripts/jni-opcodes.py research/native/x86_64/libpghw-lib.so research/native/pghw-decompiled.c"""
import re, subprocess, sys

so, decompiled = sys.argv[1], sys.argv[2]
run = lambda *cmd: subprocess.run(cmd, capture_output=True, text=True, check=True).stdout
demangle = lambda text: subprocess.run(['c++filt'], input=text, capture_output=True, text=True, check=True).stdout

got = {int(a, 16): c for a, c in re.findall(r'^([0-9a-f]+)\s+\S+\s+R_X86_64_GLOB_DAT\s+\S+\s+vtable for (.+?) \+ 0$',
                                            demangle(run('readelf', '-rW', so)), re.M)}

# The GOT slots each JNI function reads.
refs, name = {}, None
for line in run('objdump', '-d', '--no-show-raw-insn', so).splitlines():
    if m := re.match(r'[0-9a-f]+ <(\S+?)(?:@@Base)?>:', line):
        name = m[1].split('RemoteDevice_')[1] if '_RemoteDevice_' in m[1] else None
        if name: refs[name] = []
    elif name:
        refs[name] += [got[a] for a in map(lambda x: int(x, 16), re.findall(r'# ([0-9a-f]+)', line)) if a in got]
        refs[name] += re.findall(r'call\s+[0-9a-f]+ <_ZN\d+(\w+Session)C[12]E', line)  # a constructor called on the stack

parts = re.split(r'^// ==== (\S+) @ [0-9a-f]+\n', open(decompiled).read(), flags=re.M)
bodies = {parts[i]: parts[i + 1] for i in range(1, len(parts), 2)}

escapes = {'\\0': 0, '\\t': 9, '\\n': 10}
char = lambda c: escapes[c] if c in escapes else int(c[2:], 16) if c.startswith('\\x') else ord(c)
compose = re.compile(r"ComposePGSysEx\([^,]+,\s*'(\\x[0-9a-f]{2}|\\.|.)',\s*(?:'(\\x[0-9a-f]{2}|\\.|.)'|\*param_1)")
helpers = {f: char(m[1]) for f, b in bodies.items() for m in compose.finditer(b) if not m[2]}

def sysex_ops(body):
    """cmd/sub pairs a gen-1 session's do_perform sends, directly or through a helper."""
    ops = [(char(c), char(s)) for c, s in compose.findall(body) if s]
    for helper, cmd in helpers.items():
        if helper + '(' in body:
            ops += [(cmd, int(v, 0)) for v in re.findall(
                r'local_\w+(?:\[0\] = |\s*=\s*CONCAT71\([^,]+,)(0x[0-9a-f]+|\d+)\)?;', body)]
    for f in dict.fromkeys(re.findall(r'PGSysExHelper::\w+', body)):
        ops += [(char(c), char(s)) for c, s in compose.findall(bodies.get(f, '')) if s]
    return ops

def ops_of(cls):
    if found := re.findall(r'SessionOperationCode<\(unsigned char\)(\d+), \(unsigned char\)(\d+)>', cls):
        return [tuple(map(int, found[0]))]
    return sysex_ops(bodies.get(f'{cls}::do_perform', ''))

for name, classes in refs.items():
    body = bodies.get(f'Java_com_positivegrid_spark_ble_RemoteDevice_{name}', '')
    ops = [op for c in dict.fromkeys(classes) for op in ops_of(c) if 'Session' in c]
    ops += [(int(a), int(b)) for a, b in re.findall(r'SessionOperationCodeILh(\d+)ELh(\d+)E', body)]  # inlined
    ops += [(int(v[2:], 16), int(v[:2], 16)) for v in re.findall(r'\(undefined2 \*\)\([^)]*\) = 0x([0-9a-f]{2}0[12]);', body)]
    sessions = [c for c in dict.fromkeys(classes) if 'Session' in c and '<' not in c]
    print(f'{name:55s} {" ".join(dict.fromkeys(f"{c:02x} {s:02x}" for c, s in ops)):12s} {" ".join(sessions)}'.rstrip())
