using Opc.Ua;

namespace Quickstarts.ReferenceServer
{
    public interface ITokenValidator
    {
        IUserIdentity ValidateToken(IssuedIdentityToken issuedToken);
    }
}
