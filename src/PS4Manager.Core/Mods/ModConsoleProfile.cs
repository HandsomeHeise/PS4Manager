using System;

namespace PS4Manager.Core.Mods
{
    public sealed class ModConsoleProfile
    {
        private object confirmedSession;
        public ModConsoleProfile(Guid id,string name){if(id==Guid.Empty)throw new ArgumentException("Profile ID required.");Id=id;Name=name??"Console";}
        public Guid Id {get;}
        public string Name {get;}
        public void ConfirmForSession(object session){confirmedSession=session??throw new ArgumentNullException(nameof(session));}
        public bool IsConfirmedForSession(object session){return session!=null && ReferenceEquals(confirmedSession,session);}
        public void ForgetSession(){confirmedSession=null;}
    }
}
