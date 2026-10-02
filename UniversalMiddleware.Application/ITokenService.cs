using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application.Services;

public interface ITokenService
{
    string GenerateToken(User user);
}