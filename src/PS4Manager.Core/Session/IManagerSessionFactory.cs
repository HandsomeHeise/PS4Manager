namespace PS4Manager.Core.Session
{
    public interface IManagerSessionFactory
    {
        ManagerSession Create(ManagerEndpoint endpoint, bool expertMode);
    }
}
