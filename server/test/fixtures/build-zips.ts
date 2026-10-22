import { deflateRawSync, crc32 } from "node:zlib";
export interface TestEntry {
  name: string;
  content?: string;
  mode?: number;
  encrypted?: boolean;
  deflate?: boolean;
  badCrc?: boolean;
}
// Small ZIP fixture writer deliberately permits hostile metadata that normal
// archive writers reject, so tests exercise the validator against actual bytes.
export function buildZip(entries: TestEntry[]): Buffer {
  const local: Buffer[] = [],
    central: Buffer[] = [];
  let offset = 0;
  for (const entry of entries) {
    const name = Buffer.from(entry.name);
    const data = Buffer.from(entry.content ?? "x");
    const compressed = entry.deflate ? deflateRawSync(data) : data;
    const crc = entry.badCrc ? 42 : crc32(data);
    const flags = 0x800 | (entry.encrypted ? 1 : 0);
    const header = Buffer.alloc(30);
    header.writeUInt32LE(0x04034b50);
    header.writeUInt16LE(20, 4);
    header.writeUInt16LE(flags, 6);
    header.writeUInt16LE(entry.deflate ? 8 : 0, 8);
    header.writeUInt32LE(crc, 14);
    header.writeUInt32LE(compressed.length, 18);
    header.writeUInt32LE(data.length, 22);
    header.writeUInt16LE(name.length, 26);
    local.push(header, name, compressed);
    const cd = Buffer.alloc(46);
    cd.writeUInt32LE(0x02014b50);
    cd.writeUInt16LE(0x0314, 4);
    cd.writeUInt16LE(20, 6);
    cd.writeUInt16LE(flags, 8);
    cd.writeUInt16LE(entry.deflate ? 8 : 0, 10);
    cd.writeUInt32LE(crc, 16);
    cd.writeUInt32LE(compressed.length, 20);
    cd.writeUInt32LE(data.length, 24);
    cd.writeUInt16LE(name.length, 28);
    cd.writeUInt32LE(((entry.mode ?? 0o100644) << 16) >>> 0, 38);
    cd.writeUInt32LE(offset, 42);
    central.push(cd, name);
    offset += header.length + name.length + compressed.length;
  }
  const directory = Buffer.concat(central),
    end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...local, directory, end]);
}
