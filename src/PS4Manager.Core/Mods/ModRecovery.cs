using System;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.Mods
{
    public sealed class ModRecovery
    {
        private readonly ModInstallPlanner planner;
        public ModRecovery(ModInstallPlanner planner){this.planner=planner;}
        public IQueuedTransaction CreateResume(Guid id,bool gameClosed,bool approveZones){return planner.Create(id,"install",gameClosed,approveZones);}
        public IQueuedTransaction CreateRollback(Guid id,bool gameClosed,bool approveZones){return planner.Create(id,"rollback",gameClosed,approveZones);}
        public IQueuedTransaction CreateUninstall(Guid id,bool gameClosed,bool approveZones){return planner.Create(id,"uninstall",gameClosed,approveZones);}
    }
}
