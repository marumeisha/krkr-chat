using SecureChat.Core.Keys;

namespace SecureChat.ClientCore.Services;

public sealed class IdentityBootstrapService
{
    private readonly IdentityKeyService _identityKeyService;

    public IdentityBootstrapService(IdentityKeyService identityKeyService)
    {
        _identityKeyService = identityKeyService;
    }

    public void EnsureInitialized(string userId)
    {
        _identityKeyService.EnsureIdentityKeyPair(userId);
    }
}