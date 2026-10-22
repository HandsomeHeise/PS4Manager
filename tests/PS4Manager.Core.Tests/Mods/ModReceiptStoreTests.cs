using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModReceiptStoreTests:IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"mod-receipt-"+Guid.NewGuid().ToString("N"));
        [Fact]
        public void Canonical_aliases_cannot_appear_twice_in_a_receipt()
        {
            var receipt=Receipt();receipt.Files.Add(new ModReceiptFile{Target="/data/a"});receipt.Files.Add(new ModReceiptFile{Target="/user/data/a"});
            Assert.Throws<InvalidDataException>(()=>new ModReceiptStore(root).Save(receipt));
        }
        [Fact]
        public void Journal_is_atomic_and_profiles_are_isolated()
        {
            var store=new ModReceiptStore(root);var receipt=Receipt();store.Save(receipt);receipt.State=ModInstallState.Incomplete;store.Save(receipt);
            Assert.Equal(ModInstallState.Incomplete,Assert.Single(store.Load(receipt.ProfileId)).State);
            Assert.Empty(store.Load(Guid.NewGuid()));
            Assert.Equal(receipt.TransactionId,store.LoadTransaction(receipt.ProfileId,receipt.TransactionId).TransactionId);
        }
        [Fact]
        public void Backups_are_verified_before_recovery()
        {
            var store=new ModReceiptStore(root);var profile=Guid.NewGuid();var data=Encoding.UTF8.GetBytes("original");
            var hash=store.SaveBackup(profile,data);Assert.Equal(data,store.ReadBackup(profile,hash));
            File.WriteAllText(store.BackupPath(profile,hash),"tampered");
            Assert.Throws<InvalidDataException>(()=>store.ReadBackup(profile,hash));
            Assert.Throws<InvalidDataException>(()=>store.BackupPath(profile,"../../outside"));
        }
        [Fact]
        public void Reconnecting_requires_fresh_profile_confirmation()
        {
            var profile=new ModConsoleProfile(Guid.NewGuid(),"Living room");var first=new object();profile.ConfirmForSession(first);
            Assert.True(profile.IsConfirmedForSession(first));Assert.False(profile.IsConfirmedForSession(new object()));
        }
        [Fact]
        public void Unknown_schema_and_truncated_journal_are_rejected()
        {
            var store=new ModReceiptStore(root);var receipt=Receipt();store.Save(receipt);
            var path=store.JournalPath(receipt.ProfileId,receipt.TransactionId);var json=File.ReadAllText(path);
            File.WriteAllText(path,json.Replace("\"SchemaVersion\":1","\"SchemaVersion\":99"));
            Assert.Throws<InvalidDataException>(()=>store.Load(receipt.ProfileId));
            File.WriteAllText(path,json.Substring(0,json.Length/2));
            Assert.Throws<InvalidDataException>(()=>store.Load(receipt.ProfileId));
        }
        [Fact]
        public void Invalid_journal_is_reported_not_silently_ignored()
        {
            var store=new ModReceiptStore(root);var receipt=Receipt();store.Save(receipt);
            File.WriteAllText(store.JournalPath(receipt.ProfileId,receipt.TransactionId),"broken");
            Assert.Throws<InvalidDataException>(()=>store.Load(receipt.ProfileId));
        }
        internal static ModReceipt Receipt(){return new ModReceipt{ProfileId=Guid.NewGuid(),TransactionId=Guid.NewGuid(),ModId=Guid.NewGuid(),ReleaseId=Guid.NewGuid(),TitleId="CUSA00411",GameVersion="1.57",State=ModInstallState.Prepared,Files=new List<ModReceiptFile>()};}
        public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
