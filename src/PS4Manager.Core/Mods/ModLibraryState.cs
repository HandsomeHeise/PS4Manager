using System;
using System.Collections.Generic;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.Mods
{
    public sealed class ModLibraryState
    {
        public IReadOnlyList<ModGame> Games {get;internal set;}=new ModGame[0];
        public ModGame Game {get;internal set;}
        public ModCatalogPage Catalog {get;internal set;}
        public ModDetail Detail {get;internal set;}
        public ModRelease Release {get;internal set;}
        public ModInstallPlan Plan {get;internal set;}
        public IReadOnlyList<ModReceipt> Receipts {get;internal set;}=new ModReceipt[0];
        public TransferJob Job {get;internal set;}
        public bool Busy {get;internal set;}
        public bool Connected {get;internal set;}=true;
        public string Status {get;internal set;}="Choose a console game.";
        public bool CanPrepare {get{return Connected&&!Busy&&Plan==null&&Game?.Version!=null&&Catalog!=null&&!Catalog.IsOffline&&Release?.State=="published"&&!JobActive;}}
        public bool CanInstall {get{return Connected&&!Busy&&Plan!=null&&!JobActive;}}
        public bool JobActive {get{return Job!=null&&(Job.State==JobState.Waiting||Job.State==JobState.Running);}}
        internal ModLibraryState Copy(){return (ModLibraryState)MemberwiseClone();}
    }
}
