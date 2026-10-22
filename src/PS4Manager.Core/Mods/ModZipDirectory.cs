using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PS4Manager.Core.Mods
{
    /// <summary>Checks ZIP metadata not exposed by net48 ZipArchive before any extraction.</summary>
    internal static class ModZipDirectory
    {
        internal sealed class Entry
        {
            public string Name; public long Size,Compressed,Offset;public uint Crc;public ushort Flags,Method;public bool Directory;
        }
        public static IReadOnlyList<Entry> Read(Stream stream)
        {
            if(stream.Length<22 || stream.Length>2147483648L)throw ModJson.Bad();
            using(var reader=new BinaryReader(stream,Encoding.UTF8,true)) {
                stream.Position=Math.Max(0,stream.Length-65557);long tailStart=stream.Position;var tail=reader.ReadBytes((int)(stream.Length-stream.Position));
                int eocd=-1;for(int i=tail.Length-22;i>=0;i--)if(U32(tail,i)==0x06054b50 && i+22+U16(tail,i+20)==tail.Length){eocd=i;break;}
                if(eocd<0||U16(tail,eocd+4)!=0||U16(tail,eocd+6)!=0||U16(tail,eocd+8)!=U16(tail,eocd+10))throw ModJson.Bad();
                long count=U16(tail,eocd+10),centralSize=U32(tail,eocd+12),centralOffset=U32(tail,eocd+16),centralEnd=tailStart+eocd;
                if(count==65535 || centralSize==uint.MaxValue || centralOffset==uint.MaxValue) {
                    long locator=tailStart+eocd-20;if(locator<0)throw ModJson.Bad();stream.Position=locator;
                    if(reader.ReadUInt32()!=0x07064b50||reader.ReadUInt32()!=0)throw ModJson.Bad();long z=Checked(reader.ReadUInt64());if(reader.ReadUInt32()!=1||z<0||z+56>locator)throw ModJson.Bad();
                    stream.Position=z;if(reader.ReadUInt32()!=0x06064b50)throw ModJson.Bad();long size=Checked(reader.ReadUInt64());
                    if(size<44||z+12+size!=locator)throw ModJson.Bad();reader.ReadUInt16();reader.ReadUInt16();if(reader.ReadUInt32()!=0||reader.ReadUInt32()!=0)throw ModJson.Bad();
                    long diskCount=Checked(reader.ReadUInt64());count=Checked(reader.ReadUInt64());if(diskCount!=count)throw ModJson.Bad();
                    centralSize=Checked(reader.ReadUInt64());centralOffset=Checked(reader.ReadUInt64());centralEnd=z;
                }
                if(count<1||count>10000||centralOffset<0||centralSize<0||centralOffset+centralSize!=centralEnd)throw ModJson.Bad();
                var entries=new List<Entry>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0,compressed=0;stream.Position=centralOffset;
                for(long i=0;i<count;i++) {
                    if(stream.Position+46>centralEnd||reader.ReadUInt32()!=0x02014b50)throw ModJson.Bad();
                    var header=reader.ReadBytes(42);ushort flags=U16(header,4),method=U16(header,6),nameLength=U16(header,24),extraLength=U16(header,26),commentLength=U16(header,28);
                    if((flags&~0x080e)!=0 || (flags&1)!=0 || (method!=0&&method!=8)||U16(header,30)!=0)throw ModJson.Bad();
                    if(stream.Position+nameLength+extraLength+commentLength>centralEnd)throw ModJson.Bad();
                    var nameBytes=reader.ReadBytes(nameLength);var extra=reader.ReadBytes(extraLength);stream.Position+=commentLength;
                    var name=Decode(nameBytes,flags);bool directory=name.EndsWith("/",StringComparison.Ordinal);ModManifest.ValidatePath(directory?name.Substring(0,name.Length-1):name);
                    if(!names.Add(name.TrimEnd('/')))throw ModJson.Bad();
                    var mode=(U32(header,34)>>16)&0xf000;
                    if(mode!=0&&mode!=0x8000&&mode!=0x4000 || directory&&mode==0x8000 || !directory&&mode==0x4000)throw ModJson.Bad();
                    long size=U32(header,20),packed=U32(header,16),offset=U32(header,38);
                    ReadZip64(extra,ref size,ref packed,ref offset);
                    expanded=checked(expanded+size);compressed=checked(compressed+packed);
                    if(expanded>4294967296L||compressed>2147483648L||size>Math.Max(1,packed)*100||offset<0||offset>=centralOffset)throw ModJson.Bad();
                    if(directory&&(size!=0 || (name!="payload/"&&!name.StartsWith("payload/",StringComparison.Ordinal))))throw ModJson.Bad();
                    if(!directory && name!="manifest.json"&&!name.StartsWith("payload/",StringComparison.Ordinal))throw ModJson.Bad();
                    if(name=="manifest.json"&&size>1048576)throw ModJson.Bad();
                    entries.Add(new Entry{Name=name,Directory=directory,Size=size,Compressed=packed,Offset=offset,Crc=U32(header,12),Flags=flags,Method=method});
                }
                if(stream.Position!=centralEnd)throw ModJson.Bad();
                var fileNames=new HashSet<string>(entries.Where(x=>!x.Directory).Select(x=>x.Name),StringComparer.OrdinalIgnoreCase);ModManifest.NoPrefixes(fileNames);
                foreach(var entry in entries) {
                    stream.Position=entry.Offset;if(reader.ReadUInt32()!=0x04034b50)throw ModJson.Bad();var local=reader.ReadBytes(26);
                    if(local.Length!=26||U16(local,2)!=entry.Flags||U16(local,4)!=entry.Method)throw ModJson.Bad();
                    var localName=reader.ReadBytes(U16(local,22));var localExtra=reader.ReadBytes(U16(local,24));
                    if(Decode(localName,entry.Flags)!=entry.Name||stream.Position+entry.Compressed>centralOffset)throw ModJson.Bad();
                    if((entry.Flags&8)==0) {
                        long size=U32(local,18),packed=U32(local,14),offset=0;ReadZip64(localExtra,ref size,ref packed,ref offset);
                        if(size!=entry.Size||packed!=entry.Compressed||U32(local,10)!=entry.Crc)throw ModJson.Bad();
                    }
                }
                return entries.AsReadOnly();
            }
        }
        private static string Decode(byte[] bytes,ushort flags)
        {
            if((flags&2048)==0 && bytes.Any(x=>x>=128))throw new InvalidDataException("Non-UTF8 ZIP names are unsupported.");
            try{return new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){throw ModJson.Bad();}
        }
        private static void ReadZip64(byte[] extra,ref long size,ref long packed,ref long offset)
        {
            if(size!=uint.MaxValue&&packed!=uint.MaxValue&&offset!=uint.MaxValue)return;
            for(int i=0;i+4<=extra.Length;) {
                int id=U16(extra,i),length=U16(extra,i+2);i+=4;if(i+length>extra.Length)throw ModJson.Bad();
                if(id==1) {int end=i+length;if(size==uint.MaxValue)size=Next64(extra,ref i,end);if(packed==uint.MaxValue)packed=Next64(extra,ref i,end);if(offset==uint.MaxValue)offset=Next64(extra,ref i,end);return;}i+=length;
            }
            throw ModJson.Bad();
        }
        private static long Next64(byte[] b,ref int i,int end){if(i+8>end)throw ModJson.Bad();long n=Checked(BitConverter.ToUInt64(b,i));i+=8;return n;}
        private static long Checked(ulong n){if(n>long.MaxValue)throw ModJson.Bad();return (long)n;}
        private static ushort U16(byte[] b,int i){if(i<0||i+2>b.Length)throw ModJson.Bad();return BitConverter.ToUInt16(b,i);}
        private static uint U32(byte[] b,int i){if(i<0||i+4>b.Length)throw ModJson.Bad();return BitConverter.ToUInt32(b,i);}
    }
}
