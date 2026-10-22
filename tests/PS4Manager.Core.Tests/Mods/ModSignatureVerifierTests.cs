using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModSignatureVerifierTests
    {
        [Fact]
        public void Accepts_independently_signed_envelope_and_bytes()
        {
            using(var fixture=new SignedFixture()) using(var stream=new MemoryStream(fixture.Bytes))
                fixture.Verifier.Verify(fixture.Release,stream);
        }
        [Fact]
        public void Accepts_fixture_signed_by_actual_node_server_signer()
        {
            var data=ModJson.Object(ModJson.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Mods","Fixtures","node-signature.json"))));
            using(var f=new SignedFixture()) {
                var input=ModJson.Object(ModJson.Get(data,"input"));
                f.Release.PackageSize=ModJson.Number(input,"size",1,1000);
                f.Release.Sha256=ModJson.Text(input,"sha256",64);
                f.Release.SigningKeyId=ModJson.Text(data,"keyId",16);
                f.Release.Signature=ModJson.Text(data,"signature",1024);
                f.Release.SignedEnvelope=ModJson.Text(data,"envelope",2000);
                var verifier=new ModSignatureVerifier(new Dictionary<string,string>{{f.Release.SigningKeyId,ModJson.Text(data,"publicKeyXml",2048)}});
                using(var stream=new MemoryStream(Convert.FromBase64String(ModJson.Text(data,"packageBase64",1000)))) verifier.Verify(f.Release,stream);
            }
        }
        [Theory]
        [InlineData("hash")][InlineData("size")][InlineData("version")][InlineData("mod")]
        [InlineData("release")][InlineData("kind")][InlineData("compatibility")][InlineData("envelope")]
        [InlineData("key")][InlineData("signature")]
        public void Rejects_substitution_of_every_signed_field(string field)
        {
            using(var f=new SignedFixture()) {
                switch(field) {
                    case "hash":f.Release.Sha256=new string('b',64);break;
                    case "size":f.Release.PackageSize++;break;
                    case "version":f.Release.ReleaseVersion="2.0.0";break;
                    case "mod":f.Release.ModId=Guid.NewGuid();break;
                    case "release":f.Release.ReleaseId=Guid.NewGuid();break;
                    case "kind":f.Release.PackageType="luma-script";break;
                    case "compatibility":f.Release.Compatibility=new[]{new ModCompatibility("CUSA03041","1.32")};break;
                    case "envelope":f.Release.SignedEnvelope="forged";break;
                    case "key":f.Release.SigningKeyId="0000000000000000";break;
                    case "signature":f.Release.Signature="AQID";break;
                }
                using(var stream=new MemoryStream(f.Bytes)) Assert.ThrowsAny<Exception>(()=>f.Verifier.Verify(f.Release,stream));
            }
        }
        [Fact]
        public void Truncated_or_changed_download_is_rejected()
        {
            using(var f=new SignedFixture()) {
                using(var shortStream=new MemoryStream(new byte[2]))Assert.Throws<InvalidDataException>(()=>f.Verifier.Verify(f.Release,shortStream));
                using(var wrongStream=new MemoryStream(new byte[f.Bytes.Length]))Assert.Throws<InvalidDataException>(()=>f.Verifier.Verify(f.Release,wrongStream));
            }
        }
        [Fact]
        public void Production_public_key_is_separate_from_test_trust()
        {
            Assert.Equal("503d131c6d8d979e",ModSignatureVerifier.KeyId(ModTrustKeys.Production["503d131c6d8d979e"]));
            using(var f=new SignedFixture()) using(var stream=new MemoryStream(f.Bytes))
                Assert.Throws<CryptographicException>(()=>new ModSignatureVerifier(ModTrustKeys.Production).Verify(f.Release,stream));
        }

        internal sealed class SignedFixture:IDisposable
        {
            private readonly RSACryptoServiceProvider rsa=new RSACryptoServiceProvider(2048);
            public byte[] Bytes=Encoding.ASCII.GetBytes("package-test");
            public ModRelease Release;
            public ModSignatureVerifier Verifier;
            public SignedFixture()
            {
                string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Bytes)).Replace("-","").ToLowerInvariant();
                var envelope="PS4MANAGER-MOD-RELEASE-V1\nreleaseId=aa6b9003-f0e2-4ee9-a8b5-f48cf3da73c2\nmodId=f8615e4a-cb7e-4cf7-8326-5464475f080e\nversion=1.0.0\npackageType=files\nsize=12\nsha256="+hash+"\ncompatibility=CUSA00411@1.57\n";
                Release=new ModRelease {ModId=Guid.Parse("f8615e4a-cb7e-4cf7-8326-5464475f080e"),ReleaseId=Guid.Parse("aa6b9003-f0e2-4ee9-a8b5-f48cf3da73c2"),ReleaseVersion="1.0.0",PackageType="files",PackageSize=12,Sha256=hash,Compatibility=new[]{new ModCompatibility("CUSA00411","1.57")},SignedEnvelope=envelope,State="published"};
                var xml=rsa.ToXmlString(false);Release.SigningKeyId=ModSignatureVerifier.KeyId(xml);
                Release.Signature=Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(envelope),CryptoConfig.MapNameToOID("SHA256")));
                Verifier=new ModSignatureVerifier(new Dictionary<string,string>{{Release.SigningKeyId,xml}});
            }
            public void Dispose(){rsa.Dispose();}
        }
    }
}
