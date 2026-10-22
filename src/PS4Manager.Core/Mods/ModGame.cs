namespace PS4Manager.Core.Mods
{
    public enum ModRunningState { Unknown, NotRunning, Running }
    public sealed class ModGame
    {
        public string TitleId {get;internal set;}
        public string Name {get;internal set;}
        public string Version {get;internal set;}
        public string InvalidReason {get;internal set;}
        public ModRunningState RunningState {get;internal set;}
        public override string ToString(){return Name+" ("+TitleId+" / "+(Version??"unknown version")+")";}
    }
}
