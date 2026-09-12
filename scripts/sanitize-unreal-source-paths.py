"""Redact source-location paths in unsigned native PE files, preserving layout."""
import re
import struct

SOURCE_PATH = re.compile(rb"[A-Za-z]:[\\/][\x20-\x7e]+?\.(?:cpp|c|h|hpp|inl)\x00")

def sections(data):
    if data[:2] != b'MZ':
        raise ValueError('Not a PE file')
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[pe:pe+4] != b'PE\0\0':
        raise ValueError('Invalid PE signature')
    count = struct.unpack_from('<H', data, pe+6)[0]
    opt = pe+24
    opt_size = struct.unpack_from('<H', data, pe+20)[0]
    if struct.unpack_from('<H', data, opt)[0] != 0x20b:
        raise ValueError('Only PE32+ supported')
    security = struct.unpack_from('<II', data, opt+112+4*8)
    if security != (0, 0):
        raise ValueError('Refusing to modify a signed image')
    result = []
    for index in range(count):
        entry = opt+opt_size+index*40
        name = data[entry:entry+8].rstrip(b'\0')
        length, start = struct.unpack_from('<II', data, entry+16)
        flags = struct.unpack_from('<I', data, entry+36)[0]
        if start+length > len(data):
            raise ValueError('Section outside file')
        result.append((name, start, length, flags))
    return result

def sanitize(data):
    layout = sections(data)
    updated = bytearray(data)
    changes = []
    for name, start, length, flags in layout:
        if name != b'.rdata':
            continue
        if flags & (0x20000000 | 0x80000000):
            raise ValueError('Source strings section is executable or writable')
        block = data[start:start+length]
        for match in SOURCE_PATH.finditer(block):
            original = match.group(0)[:-1]
            # Limit redaction to source-location literals, not arbitrary file paths.
            normalized = original.replace(b'\\', b'/')
            anchor = normalized.find(b'/Source/')
            if anchor < 0:
                continue
            prefix = original[:anchor]
            replacement = b'[build-root]'
            if len(prefix) < len(replacement):
                raise ValueError('Prefix too short to redact without moving data')
            replacement += b'_' * (len(prefix)-len(replacement))
            offset = start+match.start()
            updated[offset:offset+len(prefix)] = replacement
            changes.append({'offset':offset, 'length':len(prefix), 'source_name':normalized.rsplit(b'/',1)[-1].decode('ascii')})
    result = bytes(updated)
    assert len(result) == len(data)
    assert sections(result) == layout
    # All headers, executable code, import/export layout, resources and relocations
    # remain unchanged. Only fixed-length source-path prefixes in .rdata change.
    allowed = set()
    for change in changes:
        allowed.update(range(change['offset'],change['offset']+change['length']))
    assert all(a == z or index in allowed for index,(a,z) in enumerate(zip(data,result)))
    for name,start,length,flags in layout:
        if name != b'.rdata':
            assert data[start:start+length] == result[start:start+length]
    return result, changes

if __name__ == '__main__':
    import argparse
    import pathlib
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('files', nargs='+', type=pathlib.Path)
    args = parser.parse_args()
    for path in args.files:
        before = path.read_bytes()
        after, changes = sanitize(before)
        if changes:
            path.write_bytes(after)
        print(f'{path.name}: {len(changes)} diagnostic source paths redacted')
