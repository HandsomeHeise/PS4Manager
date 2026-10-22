using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PS4Manager.Core.Mods
{
    public sealed class ModSignatureVerifier
    {
        private readonly IReadOnlyDictionary<string,string> keys;
        public ModSignatureVerifier(IReadOnlyDictionary<string,string> keys)
        {
            var copy=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var pair in keys) {
                if(KeyId(pair.Value)!=pair.Key)throw new CryptographicException("Mod key ID does not match its public key.");
                copy.Add(pair.Key,pair.Value);
            }
            this.keys=copy;
        }
        public static string Envelope(ModCatalogItem release)
        {
            if(release==null || release.PackageSize<1 || release.PackageSize>2147483648L)throw ModJson.Bad();
            if(release.Compatibility==null || release.Compatibility.Count==0)throw ModJson.Bad();
            var pairs=release.Compatibility.Select(p=>p.Key).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            if(pairs.Distinct(StringComparer.Ordinal).Count()!=pairs.Length)throw ModJson.Bad();
            if(release.ReleaseVersion==null || release.ReleaseVersion.Any(char.IsControl))throw ModJson.Bad();
            return "PS4MANAGER-MOD-RELEASE-V1\nreleaseId="+release.ReleaseId.ToString("D")+"\nmodId="+release.ModId.ToString("D")+
                "\nversion="+release.ReleaseVersion+"\npackageType="+release.PackageType+"\nsize="+release.PackageSize.ToString(CultureInfo.InvariantCulture)+
                "\nsha256="+release.Sha256+"\ncompatibility="+string.Join(",",pairs)+"\n";
        }
        public void VerifyMetadata(ModRelease release)
        {
            string xml;if(!keys.TryGetValue(release.SigningKeyId,out xml))throw new CryptographicException("Untrusted mod signing key.");
            var envelope=Envelope(release);
            if(envelope!=release.SignedEnvelope)throw new CryptographicException("Mod envelope does not match metadata.");
            using(var rsa=new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp=false; rsa.FromXmlString(xml);
                byte[] signature;try{signature=Convert.FromBase64String(release.Signature);}catch(FormatException){throw new CryptographicException("Invalid mod signature encoding.");}
                if(!rsa.VerifyData(Encoding.UTF8.GetBytes(envelope),CryptoConfig.MapNameToOID("SHA256"),signature))throw new CryptographicException("Invalid mod signature.");
            }
        }
        public void Verify(ModRelease release,Stream zip)
        {
            VerifyMetadata(release);
            long total=0;var buffer=new byte[65536];int count;
            using(var sha=SHA256.Create()) {
                while((count=zip.Read(buffer,0,buffer.Length))>0) {
                    total=checked(total+count);if(total>release.PackageSize)throw new InvalidDataException("Mod package size mismatch.");
                    sha.TransformBlock(buffer,0,count,null,0);
                }
                sha.TransformFinalBlock(new byte[0],0,0);
                if(total!=release.PackageSize || Hex(sha.Hash)!=release.Sha256)throw new InvalidDataException("Mod package hash or size mismatch.");
            }
        }
        public static string Hash(Stream stream){using(var sha=SHA256.Create())return Hex(sha.ComputeHash(stream));}
        public static string HashFile(string path){using(var input=File.OpenRead(path))return Hash(input);}
        internal static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
        public static string KeyId(string publicKeyXml)
        {
            using(var rsa=new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp=false; rsa.FromXmlString(publicKeyXml); var p=rsa.ExportParameters(false);
                var algorithm=new byte[]{0x30,0x0d,0x06,0x09,0x2a,0x86,0x48,0x86,0xf7,0x0d,0x01,0x01,0x01,0x05,0x00};
                var rsaKey=Der(0x30,Concat(Integer(p.Modulus),Integer(p.Exponent)));
                var spki=Der(0x30,Concat(algorithm,Der(0x03,Concat(new byte[]{0},rsaKey))));
                using(var sha=SHA256.Create())return Hex(sha.ComputeHash(spki)).Substring(0,16);
            }
        }
        private static byte[] Integer(byte[] bytes){return Der(2,(bytes[0]&128)!=0?Concat(new byte[]{0},bytes):bytes);}
        private static byte[] Der(byte tag,byte[] bytes)
        {
            var length=new List<byte>();int n=bytes.Length;
            if(n<128)length.Add((byte)n);else{while(n>0){length.Insert(0,(byte)(n&255));n>>=8;}length.Insert(0,(byte)(128|length.Count));}
            return Concat(new[]{tag},Concat(length.ToArray(),bytes));
        }
        private static byte[] Concat(byte[] a,byte[] b){var result=new byte[a.Length+b.Length];Buffer.BlockCopy(a,0,result,0,a.Length);Buffer.BlockCopy(b,0,result,a.Length,b.Length);return result;}
    }
}
